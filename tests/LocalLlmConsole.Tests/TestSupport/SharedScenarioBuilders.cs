using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public abstract partial class ManagerRegressionTestBase
{
    private static readonly IReadOnlyDictionary<ushort, OpCode> SharedIlOpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => unchecked((ushort)opCode.Value));

    protected static LoadedModelSessionManager CreateLoadedModelSessionManager(Func<DateTimeOffset>? utcNow = null)
        => new(CreateTestLlamaSupervisor, utcNow);

    protected static LlamaProcessSupervisor CreateTestLlamaSupervisor()
        => new(
            new WslRuntimeStopService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))),
            new NativeRuntimeStopService());

    protected static AppUpdateService CreateAppUpdateService(HttpClient http)
        => new(http, _ => { }, signatureVerifier: new AcceptingUpdateSignatureVerifier());

    protected static WslEnvironmentReport ReadyWslReport(
        string distroName = "Ubuntu-24.04",
        string version = "2")
        => new(
            WslExeFound: true,
            WslWorking: true,
            Status: "ready",
            Details: "",
            DefaultDistro: distroName,
            RecommendedDistro: distroName,
            RecommendedAction: "",
            Distros: [new WslDistroInfo(distroName, "Running", version, IsDefault: true, IsUbuntu: true)]);

    protected static WindowsToolSnapshot WindowsBuildTools(
        bool cpuReady = true,
        bool cudaReady = true,
        bool vulkanReady = true,
        bool syclReady = true)
        => new(
            GitInstalled: cpuReady,
            GitPath: cpuReady ? "git.exe" : "",
            CMakeInstalled: cpuReady,
            CMakePath: cpuReady ? "cmake.exe" : "",
            MsvcInstalled: cpuReady,
            MsvcDetails: cpuReady ? "MSVC ready" : "MSVC missing",
            NvidiaDriverVisible: false,
            NvidiaSmiPath: "",
            CudaToolsInstalled: cudaReady,
            CudaDetails: cudaReady ? "CUDA ready" : "nvcc.exe missing",
            VulkanToolsInstalled: vulkanReady,
            VulkanDetails: vulkanReady ? "Vulkan ready" : "VULKAN_SDK missing",
            SyclToolsInstalled: syclReady,
            SyclDetails: syclReady ? "oneAPI ready" : "oneAPI missing");

    protected static LoadedModelSessionSnapshot RuntimeMetricSession(string root, AppSettings settings)
        => RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true);

    protected static LoadedModelSessionSnapshot RuntimeSession(
        string root,
        AppSettings settings,
        LoadedModelSessionStatus status,
        bool isRunning)
        => new(
            "session-1",
            "model-1",
            "Qwen",
            "runtime-1",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            Path.Combine(root, "runtime.log"),
            DateTimeOffset.UtcNow,
            "",
            0,
            status,
            IsRunning: isRunning,
            IsSelected: true);

    protected static StringContent JsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    protected static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    protected static LoadedModelSessionSnapshot Session(
        ModelRecord model,
        RuntimeRecord runtime,
        AppSettings settings,
        string profileId)
        => new(
            $"session:{model.Id}",
            model.Id,
            model.Name,
            runtime.Id,
            runtime.Name,
            runtime.Mode,
            runtime.Backend,
            settings,
            "runtime.log",
            DateTimeOffset.UtcNow,
            "",
            1,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: false,
            LaunchProfileId: profileId,
            LaunchProfileName: $"Old {model.Name}");

    protected static ModelGroupSnapshot Snapshot(
        ModelGroupRecord group,
        params NamedModelLaunchProfile[] profiles)
        => new(
            [group],
            profiles.ToDictionary(
                profile => profile.Id,
                profile => new ModelGroupAssignment(profile.Id, group.Id, DateTimeOffset.UtcNow),
                StringComparer.OrdinalIgnoreCase));

    protected static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    protected static void CreateTarGzipArchive(string archivePath, params TarEntry[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? ".");
        using var file = File.Create(archivePath);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new TarWriter(gzip, leaveOpen: false);
        foreach (var entry in entries)
        {
            try
            {
                writer.WriteEntry(entry);
            }
            finally
            {
                entry.DataStream?.Dispose();
            }
        }
    }

    protected static void WriteMinimalGguf(
        string path,
        string architecture,
        params (string Key, uint Value)[] numericMetadata)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)(1 + numericMetadata.Length));
        WriteGgufString(writer, "general.architecture");
        writer.Write((uint)8);
        WriteGgufString(writer, architecture);
        foreach (var (key, value) in numericMetadata)
        {
            WriteGgufString(writer, key);
            writer.Write((uint)4);
            writer.Write(value);
        }
    }

    protected static IEnumerable<MemberInfo> ReferencedMembers(MethodBase method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes is null) yield break;
        for (var offset = 0; offset < bytes.Length;)
        {
            var first = bytes[offset++];
            var value = first == 0xfe ? unchecked((ushort)(0xfe00 | bytes[offset++])) : first;
            if (!SharedIlOpCodes.TryGetValue(value, out var opCode)) yield break;
            var operandStart = offset;
            offset += SharedOperandSize(opCode.OperandType, bytes, operandStart);
            if (opCode.OperandType is not (OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType))
                continue;
            var token = BitConverter.ToInt32(bytes, operandStart);
            MemberInfo? member = null;
            try
            {
                member = method.Module.ResolveMember(
                    token,
                    method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            catch (ArgumentException)
            {
                // Invalid metadata would fail the build; ignore unresolved optional tokens here.
            }
            if (member is not null) yield return member;
        }
    }

    protected static IEnumerable<MemberInfo> ReferencedMembers(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(ReferencedMembers);

    private static int SharedOperandSize(OperandType type, byte[] bytes, int offset)
        => type switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(bytes, offset) * 4),
            _ => 4
        };
}
