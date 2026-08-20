using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    private static RuntimeLaunchRequest ValidLaunchRequest() => new()
    {
        Mode = RuntimeMode.Native,
        Backend = RuntimeBackend.Cpu,
        ExecutablePath = "llama-server.exe",
        ModelPath = "model.gguf",
        Host = "127.0.0.1",
        ApiKey = new string('a', 32),
        RequireApiKeyAuth = true,
        Port = 8081
    };

    private static void WriteMinimalGguf(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)1);
        writer.Write((ulong)4);
        WriteGgufString(writer, "general.architecture");
        writer.Write((uint)8);
        WriteGgufString(writer, "qwen3");
        WriteGgufString(writer, "qwen3.context_length");
        writer.Write((uint)4);
        writer.Write((uint)32768);
        WriteGgufString(writer, "tokenizer.chat_template");
        writer.Write((uint)8);
        WriteGgufString(writer, "{{ bos_token }}");
        WriteGgufString(writer, "tokenizer.ggml.scores");
        writer.Write((uint)9);
        writer.Write((uint)0);
        writer.Write((ulong)100_001);
        writer.Write(new byte[100_001]);
        WriteGgufString(writer, "weights");
        writer.Write((uint)1);
        writer.Write((ulong)7_000_000_000);
        writer.Write((uint)0);
        writer.Write((ulong)0);
    }

    private static void WriteGgufString(BinaryWriter writer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalLlmConsole.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateRuntimeExecutable(string root, params string[] segments)
    {
        var relativeSegments = segments.Length == 0 ? ["llama-server.exe"] : segments;
        var path = Path.Combine(new[] { root }.Concat(relativeSegments).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            if (segments.Length > 0 && (segments.Contains("Services", StringComparer.OrdinalIgnoreCase)
                || segments.Contains("Ui", StringComparer.OrdinalIgnoreCase)))
            {
                var moduleRoot = Path.Combine(
                    directory.FullName,
                    "src",
                    "LocalLlmConsole.App",
                    segments.Contains("Services", StringComparer.OrdinalIgnoreCase) ? "Services" : "Ui");
                if (Directory.Exists(moduleRoot))
                {
                    var fileName = segments[^1];
                    var movedCandidates = Directory
                        .EnumerateFiles(moduleRoot, fileName, SearchOption.AllDirectories)
                        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (movedCandidates.Length > 1)
                    {
                        throw new InvalidOperationException(
                            $"Repository file lookup is ambiguous for {fileName}: {string.Join(", ", movedCandidates)}");
                    }
                    if (movedCandidates.Length == 1) return movedCandidates[0];
                }
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(segments)}");
    }

    private static string ReadMainWindowSources()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string ReadApplicationResourceSources()
    {
        var appXamlPath = FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml");
        var appRoot = Path.GetDirectoryName(appXamlPath)!;
        var themeRoot = Path.Combine(appRoot, "Themes");
        return string.Join(
            Environment.NewLine,
            new[] { appXamlPath }.Concat(
                Directory.EnumerateFiles(themeRoot, "*.xaml", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
    }

    private static string ReadLocalControlApiSources()
    {
        var hostPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Control", "LocalControlApi.cs");
        var controlRoot = Path.GetDirectoryName(hostPath)!;
        return string.Join(
            Environment.NewLine,
            new[] { hostPath }
                .Concat(Directory.EnumerateFiles(controlRoot, "Control*Endpoints.cs", SearchOption.TopDirectoryOnly))
                .Append(Path.Combine(controlRoot, "ControlEndpointHandler.cs"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string ReadAppServiceFactorySources()
    {
        var factoryPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "AppServiceFactory.cs");
        var servicesRoot = Path.GetDirectoryName(factoryPath)!;
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(servicesRoot, "AppServiceFactory*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string ReadAppServiceFactoryFileNames()
    {
        var factoryPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "AppServiceFactory.cs");
        var servicesRoot = Path.GetDirectoryName(factoryPath)!;
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(servicesRoot, "AppServiceFactory*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
    }

    private static string ReadLaunchSettingsPanelFactorySources()
    {
        var factoryPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPanelFactory.cs");
        var factoryRoot = Path.GetDirectoryName(factoryPath)!;
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(factoryRoot, "LaunchSettingsPanelFactory*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static string ReadServicePartialSources(string prefix)
    {
        var sourcePath = FindRepositoryFile("src", "LocalLlmConsole.App", "Services", $"{prefix}.cs");
        var sourceRoot = Path.GetDirectoryName(sourcePath)!;
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceRoot, $"{prefix}*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
    }

    private static void AssertServicePartials(string appRoot, string folder, string prefix, int maxLines, params string[] requiredFiles)
    {
        var root = Path.Combine(appRoot, folder);
        var files = Directory.EnumerateFiles(root, $"{prefix}*.cs", SearchOption.AllDirectories)
            .Select(path => new { Name = Path.GetFileName(path), Lines = File.ReadAllLines(path).Length })
            .ToArray();
        var names = files.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oversized = files
            .Where(file => file.Lines > maxLines)
            .Select(file => $"{file.Name}:{file.Lines}")
            .ToArray();

        Assert.Empty(oversized);
        foreach (var requiredFile in requiredFiles)
            Assert.Contains(requiredFile, names);
    }

}
