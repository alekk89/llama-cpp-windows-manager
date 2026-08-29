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
        => (await SnapshotAsync(activeSession, now, cancellationToken)).Summary;

    public async Task<HostHardwareSnapshot> SnapshotAsync(
        LoadedModelSessionSnapshot? activeSession,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(activeSession);
        return await _cache.GetOrCreateSnapshotAsync(
            cacheKey,
            now,
            async () => HostHardwareSnapshotParser.Parse(
                await ProbeSummaryAsync(activeSession, CancellationToken.None),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<string> PowerSummaryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
        => (await PowerSnapshotAsync(now, cancellationToken)).Summary;

    public async Task<HostHardwareSnapshot> PowerSnapshotAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetSnapshot("host", now, out var hostSnapshot)
            && hostSnapshot.Gpus.Any(gpu => gpu.PowerWatts is >= 0 and <= 2000))
            return hostSnapshot;
        return await _cache.GetOrCreateSnapshotAsync(
            "host-power",
            now,
            async () => HostHardwareSnapshotParser.Parse(
                await ProbePowerSummaryAsync(CancellationToken.None),
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task<string> ProbePowerSummaryAsync(CancellationToken cancellationToken)
    {
        var nvidiaProbe = _gpuStatus.NvidiaPowerAcceleratorsAsync(cancellationToken);
        var amdProbe = _gpuStatus.AmdAcceleratorsAsync(cancellationToken);
        var intelProbe = _gpuStatus.IntelAcceleratorsAsync(cancellationToken);
        var windowsProbe = _gpuStatus.WindowsAcceleratorsAsync(cancellationToken);
        await Task.WhenAll(nvidiaProbe, amdProbe, intelProbe, windowsProbe);
        return MergeHostAccelerators(
            await windowsProbe,
            await nvidiaProbe,
            await amdProbe,
            await intelProbe);
    }

    private async Task<string> ProbeSummaryAsync(
        LoadedModelSessionSnapshot? activeSession,
        CancellationToken cancellationToken)
    {
        if (activeSession is null)
            return await ProbeHostSummaryAsync(cancellationToken);

        var cpuProbe = _gpuStatus.CpuSummaryAsync(cancellationToken);
        var memoryProbe = _gpuStatus.SystemMemorySummaryAsync(cancellationToken);
        var processProbe = _gpuStatus.ProcessSummaryAsync(activeSession.ProcessId, cancellationToken);
        var usesAccelerator = activeSession.Backend is RuntimeBackend.Cuda
            or RuntimeBackend.Vulkan
            or RuntimeBackend.Metal
            or RuntimeBackend.Sycl;
        var acceleratorProbe = !usesAccelerator || activeSession.LaunchSettings.GpuLayers == 0
            ? HostAcceleratorSummaryAsync(cancellationToken)
            : AcceleratorSummaryAsync(activeSession, cancellationToken);

        await Task.WhenAll(cpuProbe, memoryProbe, acceleratorProbe, processProbe);
        return CombinedHardwareSummary(await cpuProbe, await memoryProbe, await acceleratorProbe, await processProbe);
    }

    private async Task<string> ProbeHostSummaryAsync(CancellationToken cancellationToken)
    {
        var windowsProbe = _gpuStatus.WindowsHostSummaryAsync(cancellationToken);
        var nvidiaProbe = _gpuStatus.NvidiaAcceleratorsAsync(cancellationToken);
        var amdProbe = _gpuStatus.AmdAcceleratorsAsync(cancellationToken);
        var intelProbe = _gpuStatus.IntelAcceleratorsAsync(cancellationToken);
        await Task.WhenAll(windowsProbe, nvidiaProbe, amdProbe, intelProbe);

        var windows = await windowsProbe;
        var accelerators = MergeHostAccelerators(
            windows.Accelerators,
            await nvidiaProbe,
            await amdProbe,
            await intelProbe);
        return CombinedHardwareSummary(
            windows.CpuSummary,
            windows.MemorySummary,
            accelerators,
            "Unavailable");
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
                await HostAcceleratorSummaryAsync(cancellationToken),
                activeSession.LaunchSettings);
        }

        if (activeSession.Backend == RuntimeBackend.Sycl)
            return FilterConfiguredAccelerators(
                await FirstAvailableAsync(
                [() => activeSession.Mode == RuntimeMode.Wsl
                        ? _gpuStatus.WslIntelArcSummaryAsync(_wslExe(), activeSession.LaunchSettings.WslDistro, cancellationToken)
                        : _gpuStatus.IntelXpuSmiSummaryAsync(cancellationToken),
                    () => _gpuStatus.WindowsSummaryAsync(cancellationToken),
                    () => _gpuStatus.WindowsIntelArcSummaryAsync(cancellationToken)]),
                activeSession.LaunchSettings);

        return FilterConfiguredAccelerators(
            await HostAcceleratorSummaryAsync(cancellationToken),
            activeSession.LaunchSettings);
    }

    private async Task<string> HostAcceleratorSummaryAsync(CancellationToken cancellationToken)
    {
        var nvidiaProbe = _gpuStatus.NvidiaAcceleratorsAsync(cancellationToken);
        var amdProbe = _gpuStatus.AmdAcceleratorsAsync(cancellationToken);
        var intelProbe = _gpuStatus.IntelAcceleratorsAsync(cancellationToken);
        var windowsProbe = _gpuStatus.WindowsAcceleratorsAsync(cancellationToken);
        await Task.WhenAll(nvidiaProbe, amdProbe, intelProbe, windowsProbe);
        return MergeHostAccelerators(
            await windowsProbe,
            await nvidiaProbe,
            await amdProbe,
            await intelProbe);
    }

    private static string MergeHostAccelerators(
        AcceleratorProbeSummary windowsSummary,
        params AcceleratorProbeSummary[] vendorSummaries)
    {
        var rich = vendorSummaries.Where(summary => summary.IsAvailable)
            .SelectMany(summary => summary.Devices)
            .ToList();
        if (rich.Count == 0) return windowsSummary.DisplayText;

        var merged = new List<(int Index, AcceleratorProbeDevice Device)>();
        var usedIndices = new HashSet<int>();
        foreach (var device in rich)
        {
            var index = device.Index;
            while (usedIndices.Contains(index)) index++;
            merged.Add((index, device));
            usedIndices.Add(index);
        }

        if (!windowsSummary.IsAvailable)
            return JoinAcceleratorLines(merged);

        var matchedRich = new bool[merged.Count];
        foreach (var windows in windowsSummary.Devices)
        {
            var richIndex = -1;
            for (var candidateIndex = 0; candidateIndex < matchedRich.Length; candidateIndex++)
            {
                if (matchedRich[candidateIndex]
                    || !AcceleratorsMatch(windows, merged[candidateIndex].Device))
                    continue;
                richIndex = candidateIndex;
                break;
            }
            if (richIndex >= 0)
            {
                var selected = merged[richIndex];
                merged[richIndex] = (selected.Index, selected.Device with
                {
                    Name = windows.Name,
                    NameKey = windows.NameKey,
                    Vendor = windows.Vendor,
                    DisplayLine = WithAcceleratorIdentity(selected.Device.DisplayLine, windows.Name)
                });
                matchedRich[richIndex] = true;
                continue;
            }

            var nextIndex = usedIndices.Count == 0 ? 0 : usedIndices.Max() + 1;
            merged.Add((nextIndex, windows));
            usedIndices.Add(nextIndex);
        }

        return JoinAcceleratorLines(merged);
    }

    private static string JoinAcceleratorLines(IEnumerable<(int Index, AcceleratorProbeDevice Device)> devices)
        => string.Join(Environment.NewLine, devices.OrderBy(item => item.Index)
            .Select(item => WithAcceleratorIndex(item.Device.DisplayLine, item.Index)));

    private static string WithAcceleratorIndex(string line, int index)
        => Regex.Replace(line, @"^GPU\s+\d+\s*:", $"GPU {index}:",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool AcceleratorsMatch(AcceleratorProbeDevice host, AcceleratorProbeDevice candidate)
        => string.Equals(host.NameKey, candidate.NameKey, StringComparison.Ordinal)
           || (host.Index == candidate.Index
               && !string.IsNullOrWhiteSpace(host.Vendor)
               && string.Equals(host.Vendor, candidate.Vendor, StringComparison.Ordinal));

    private static string WithAcceleratorIdentity(string richLine, string hostName)
        => Regex.Replace(richLine, @"^(GPU\s+\d+\s*:)\s*[^|]+", $"$1 {hostName} ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    private static string CombinedHardwareSummary(string cpu, string memory, string accelerator, string process)
    {
        var lines = new List<string>();
        if (!IsUnavailable(cpu))
            lines.AddRange(cpu.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        if (!IsUnavailable(memory))
            lines.AddRange(memory.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(1));
        if (!IsUnavailable(process))
            lines.AddRange(process.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(1));
        if (!IsUnavailable(accelerator))
            lines.AddRange(accelerator
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select((line, index) => Regex.IsMatch(line, @"^GPU\s+\d+\s*:",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    ? line
                    : $"GPU {index}: {line.Trim()}"));
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
