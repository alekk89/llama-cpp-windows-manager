namespace LocalLlmConsole.Services;

public sealed class RuntimeGpuSummaryApplicationService
{
    private readonly GpuStatusProbeService _gpuStatus;
    private readonly GpuSummaryCache _cache;
    private readonly Func<string> _wslExe;

    public RuntimeGpuSummaryApplicationService(
        GpuStatusProbeService gpuStatus,
        GpuSummaryCache cache,
        Func<string> wslExe)
    {
        _gpuStatus = gpuStatus ?? throw new ArgumentNullException(nameof(gpuStatus));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _wslExe = wslExe ?? throw new ArgumentNullException(nameof(wslExe));
    }

    public async Task<string> SummaryAsync(
        LoadedModelSessionSnapshot? activeSession,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(activeSession);
        if (_cache.TryGet(cacheKey, now, out var cachedSummary))
            return cachedSummary;

        var summary = await ProbeSummaryAsync(activeSession, cancellationToken);
        return _cache.Store(cacheKey, summary, now);
    }

    private async Task<string> ProbeSummaryAsync(
        LoadedModelSessionSnapshot? activeSession,
        CancellationToken cancellationToken)
    {
        if (activeSession is null)
            return "No loaded model";

        var usesAccelerator = activeSession.Backend is RuntimeBackend.Cuda
            or RuntimeBackend.Vulkan
            or RuntimeBackend.Metal
            or RuntimeBackend.Sycl;
        if (!usesAccelerator || activeSession.LaunchSettings.GpuLayers == 0)
            return await _gpuStatus.CpuSummaryAsync(cancellationToken);

        var acceleratorSummary = await AcceleratorSummaryAsync(activeSession, cancellationToken);
        if (activeSession.LaunchSettings.GpuLayers < 0
            || activeSession.LaunchSettings.GpuLayers >= AppSettings.DefaultGpuLayers)
            return acceleratorSummary;

        var cpuSummary = await _gpuStatus.CpuSummaryAsync(cancellationToken);
        return CombinedHardwareSummary(cpuSummary, acceleratorSummary);
    }

    private async Task<string> AcceleratorSummaryAsync(
        LoadedModelSessionSnapshot activeSession,
        CancellationToken cancellationToken)
    {
        if (activeSession.Backend == RuntimeBackend.Cuda)
        {
            var processSummary = await _gpuStatus.SummaryForProcessAsync(activeSession.ProcessId, cancellationToken);
            if (!IsUnavailable(processSummary)) return processSummary;

            return FilterConfiguredAccelerators(
                await FirstAvailableAsync(
                [() => _gpuStatus.SummaryAsync(cancellationToken),
                    () => _gpuStatus.WindowsSummaryAsync(cancellationToken)]),
                activeSession.LaunchSettings);
        }

        if (activeSession.Backend == RuntimeBackend.Sycl)
            return FilterConfiguredAccelerators(
                await FirstAvailableAsync(
                [() => _gpuStatus.WindowsSummaryAsync(cancellationToken),
                    () => activeSession.Mode == RuntimeMode.Wsl
                        ? _gpuStatus.WslIntelArcSummaryAsync(_wslExe(), activeSession.LaunchSettings.WslDistro, cancellationToken)
                        : _gpuStatus.WindowsIntelArcSummaryAsync(cancellationToken)]),
                activeSession.LaunchSettings);

        return FilterConfiguredAccelerators(
            await FirstAvailableAsync(
                [() => _gpuStatus.WindowsSummaryAsync(cancellationToken),
                    () => _gpuStatus.SummaryAsync(cancellationToken)]),
            activeSession.LaunchSettings);
    }

    private static async Task<string> FirstAvailableAsync(IReadOnlyList<Func<Task<string>>> probes)
    {
        foreach (var probe in probes)
        {
            var summary = await probe();
            if (!IsUnavailable(summary)) return summary;
        }

        return "Unavailable";
    }

    private static bool IsUnavailable(string summary)
        => string.IsNullOrWhiteSpace(summary)
           || string.Equals(summary.Trim(), "Unavailable", StringComparison.OrdinalIgnoreCase);

    private static string CombinedHardwareSummary(string cpu, string accelerator)
    {
        var lines = new List<string>();
        if (!IsUnavailable(cpu))
            lines.AddRange(cpu.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(2));
        if (!IsUnavailable(accelerator))
            lines.AddRange(accelerator.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(2));
        return lines.Count == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
    }

    private static string FilterConfiguredAccelerators(string summary, AppSettings settings)
    {
        if (IsUnavailable(summary)) return "Unavailable";

        var selectedIndices = ConfiguredGpuIndices(settings);
        if (selectedIndices is null) return summary;

        var lines = summary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var indexedLines = lines
            .Select(line => (Line: line, Match: Regex.Match(line, @"^GPU\s+(\d+)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .Where(item => item.Match.Success
                           && int.TryParse(item.Match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                           && selectedIndices.Contains(index))
            .Select(item => item.Line)
            .ToArray();
        if (indexedLines.Length > 0) return string.Join(Environment.NewLine, indexedLines);

        return lines.Length == 1 && selectedIndices.Count == 1 ? lines[0] : "Unavailable";
    }

    private static HashSet<int>? ConfiguredGpuIndices(AppSettings settings)
    {
        var devices = LaunchSettingMetadataService.NormalizeGpuCsv(settings.GpuDevices)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var deviceIndices = devices
            .Select(DeviceIndex)
            .ToArray();
        var split = LaunchSettingMetadataService.NormalizeGpuCsv(settings.GpuSplit)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (devices.Length > 0 && deviceIndices.All(index => index is not null))
        {
            var selected = new HashSet<int>();
            for (var i = 0; i < deviceIndices.Length; i++)
            {
                if (split.Length == deviceIndices.Length
                    && (!double.TryParse(split[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var share) || share <= 0))
                    continue;
                selected.Add(deviceIndices[i]!.Value);
            }
            return selected;
        }

        if (devices.Length == 0 && split.Length > 0)
        {
            return split
                .Select((value, index) => (value, index))
                .Where(item => double.TryParse(item.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var share) && share > 0)
                .Select(item => item.index)
                .ToHashSet();
        }

        return LaunchSettingMetadataService.NormalizeGpuMode(settings.GpuMode) == "single"
            ? [0]
            : null;
    }

    private static int? DeviceIndex(string device)
    {
        var match = Regex.Match(device, @"(\d+)$", RegexOptions.CultureInvariant);
        return match.Success
               && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
    }

    private static string CacheKey(LoadedModelSessionSnapshot? activeSession)
        => activeSession is null
            ? "host"
            : $"{activeSession.SessionId}|{activeSession.ProcessId}|{activeSession.Mode}|{activeSession.Backend}|{activeSession.LaunchSettings.WslDistro}|{activeSession.LaunchSettings.GpuLayers}|{activeSession.LaunchSettings.GpuMode}|{activeSession.LaunchSettings.GpuDevices}|{activeSession.LaunchSettings.GpuSplit}";
}
