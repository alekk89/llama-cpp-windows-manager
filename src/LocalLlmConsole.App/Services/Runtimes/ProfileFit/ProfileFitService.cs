using System.Globalization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public interface IProfileFitService
{
    Task<ProfileFitResult> FitAsync(ProfileFitRequest request, CancellationToken cancellationToken = default);
}

public sealed class ProfileFitService : IProfileFitService
{
    private static readonly TimeSpan FitTimeout = TimeSpan.FromMinutes(20);
    private readonly IProcessRunner _processRunner;
    private readonly ProfileFitCapabilityService _capabilities;

    public ProfileFitService(IProcessRunner processRunner, ProfileFitCapabilityService capabilities)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public async Task<ProfileFitResult> FitAsync(ProfileFitRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var capability = await _capabilities.ProbeAsync(request.Runtime, request.WslDistro, cancellationToken);
        if (!capability.SupportsFitParams)
            return new ProfileFitResult(false, null, [], [], "", "", capability.Error);

        var arguments = BuildArguments(request);
        var start = BenchmarkRuntimeToolAdapter.CreateStartInfo(
            request.Runtime,
            request.WslDistro,
            capability.FitParamsExecutablePath,
            arguments,
            "");
        var result = await _processRunner.RunAsync(start, FitTimeout, cancellationToken);
        if (result.ExitCode != 0)
            return new ProfileFitResult(false, null, [], [], "", result.Error,
                $"llama-fit-params exited with code {result.ExitCode}: {CommandLineService.FirstNonBlankLine(result.Error)}");
        return ProfileFitOutputParser.Parse(request, result.Output, result.Error);
    }

    internal static IReadOnlyList<string> BuildArguments(ProfileFitRequest request)
    {
        var profile = request.CurrentProfile;
        var reserve = string.Join(',', request.ReservedVramMiBPerGpu.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        var arguments = new List<string>
        {
            "--model", BenchmarkRuntimeToolAdapter.RuntimeVisiblePath(request.Runtime.Mode, request.ModelPath),
            "--fit-target", reserve,
            "--fit-ctx", request.MinimumContext.ToString(CultureInfo.InvariantCulture),
            "--parallel", profile.ParallelSlots.ToString(CultureInfo.InvariantCulture),
            "--batch-size", profile.BatchSize.ToString(CultureInfo.InvariantCulture),
            "--ubatch-size", profile.MicroBatchSize.ToString(CultureInfo.InvariantCulture),
            "--cache-type-k", profile.CacheTypeK,
            "--cache-type-v", profile.CacheTypeV,
            "--flash-attn", profile.FlashAttention
        };
        if (profile.Threads > 0) arguments.AddRange(["--threads", profile.Threads.ToString(CultureInfo.InvariantCulture)]);
        if (!string.Equals(profile.GpuMode, AppSettings.DefaultGpuMode, StringComparison.OrdinalIgnoreCase))
            arguments.AddRange(["--split-mode", LaunchSettingMetadataService.LlamaSplitModeArgument(profile.GpuMode)]);
        if (!string.IsNullOrWhiteSpace(profile.GpuDevices))
            arguments.AddRange(["--device", LaunchSettingMetadataService.NormalizeGpuCsv(profile.GpuDevices)]);
        return arguments;
    }

    private static void Validate(ProfileFitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelPath);
        ArgumentNullException.ThrowIfNull(request.Runtime);
        ArgumentNullException.ThrowIfNull(request.CurrentProfile);
        if (request.MinimumContext < 1 || request.DesiredMaximumContext < request.MinimumContext)
            throw new InvalidOperationException("The maximum context must be at least the minimum context.");
        if (request.ReservedVramMiBPerGpu.Count == 0 || request.ReservedVramMiBPerGpu.Any(value => value < 0))
            throw new InvalidOperationException("Specify a non-negative VRAM reserve for at least one GPU.");
        if (request.AllowedAdjustments != ProfileFitAdjustment.All)
            throw new NotSupportedException("The first fitting release adjusts context, GPU layers, tensor split, and tensor buffers together.");
    }
}
