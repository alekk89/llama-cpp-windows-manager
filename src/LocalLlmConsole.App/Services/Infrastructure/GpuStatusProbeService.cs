namespace LocalLlmConsole.Services;

public sealed class GpuStatusProbeService
{
    private const string WindowsCpuStatusProbeScript = """
        $ErrorActionPreference = 'SilentlyContinue'

        $processors = @()
        try {
            $processors = @(Get-CimInstance Win32_Processor | Select-Object Name, LoadPercentage, NumberOfCores, NumberOfLogicalProcessors)
        } catch {}

        $readings = @()
        try {
            $samples = Get-CimInstance -Namespace root\wmi -ClassName MSAcpi_ThermalZoneTemperature
            foreach ($sample in $samples) {
                if ($null -eq $sample.CurrentTemperature) { continue }
                $celsius = ([double]$sample.CurrentTemperature / 10.0) - 273.15
                if ($celsius -gt -20 -and $celsius -lt 125) {
                    $readings += [Math]::Round($celsius, 1)
                }
            }
        } catch {}

        if ($processors.Count -eq 0 -and $readings.Count -eq 0) {
            '{}'
        } else {
            $loadSamples = @($processors | Where-Object { $null -ne $_.LoadPercentage } | ForEach-Object { [double]$_.LoadPercentage })
            $physicalCores = @($processors | Where-Object { $null -ne $_.NumberOfCores } | Measure-Object NumberOfCores -Sum).Sum
            $logicalProcessors = @($processors | Where-Object { $null -ne $_.NumberOfLogicalProcessors } | Measure-Object NumberOfLogicalProcessors -Sum).Sum
            [pscustomobject]@{
                Name = if ($processors.Count -gt 0) { [string]$processors[0].Name } else { '' }
                Utilization = if ($loadSamples.Count -gt 0) { [Math]::Round(($loadSamples | Measure-Object -Average).Average, 1) } else { $null }
                PhysicalCores = if ($physicalCores -gt 0) { [int]$physicalCores } else { $null }
                LogicalProcessors = if ($logicalProcessors -gt 0) { [int]$logicalProcessors } else { $null }
                TemperatureCelsius = if ($readings.Count -gt 0) { ($readings | Measure-Object -Maximum).Maximum } else { $null }
                TemperatureSource = if ($readings.Count -gt 0) { 'ACPI thermal zone' } else { '' }
            } | ConvertTo-Json -Compress
        }
        """;

    private const string WindowsGpuProbeScript = """
        $ErrorActionPreference = 'SilentlyContinue'

        function Get-PhysIndex([string]$Name) {
            if ($Name -match 'phys_(\d+)') { return [int]$Matches[1] }
            return $null
        }

        function Add-Sum([hashtable]$Map, [int]$Key, [double]$Value) {
            if (-not $Map.ContainsKey($Key)) { $Map[$Key] = 0.0 }
            $Map[$Key] = [double]$Map[$Key] + $Value
        }

        $controllers = @(Get-CimInstance Win32_VideoController |
            Where-Object {
                $_.Name -and
                $_.Name -notmatch 'Microsoft Basic|Remote Display|Virtual Display|Parsec|RDP|Citrix'
            } |
            Select-Object -First 4 Name, AdapterRAM)

        $utilization = @{}
        $memoryUsed = @{}

        try {
            $samples = Get-CimInstance -Namespace root\CIMV2 -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine
            foreach ($sample in $samples) {
                $index = Get-PhysIndex $sample.Name
                if ($null -ne $index) { Add-Sum $utilization $index ([double]$sample.UtilizationPercentage) }
            }
        } catch {}

        try {
            $samples = Get-CimInstance -Namespace root\CIMV2 -ClassName Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory
            foreach ($sample in $samples) {
                $index = Get-PhysIndex $sample.Name
                if ($null -ne $index) { Add-Sum $memoryUsed $index ([double]$sample.DedicatedUsage) }
            }
        } catch {}

        $rows = @(
            for ($i = 0; $i -lt $controllers.Count; $i++) {
                $adapterRam = 0.0
                if ($null -ne $controllers[$i].AdapterRAM) { $adapterRam = [double]$controllers[$i].AdapterRAM }
                $total = if ($adapterRam -gt 0 -and $adapterRam -lt 4000000000.0) { $adapterRam } else { $null }

                [pscustomobject]@{
                    Index = $i
                    Name = [string]$controllers[$i].Name
                    Utilization = if ($utilization.ContainsKey($i)) { [Math]::Min(100.0, [Math]::Round([double]$utilization[$i], 1)) } else { $null }
                    MemoryUsedBytes = if ($memoryUsed.ContainsKey($i)) { [double]$memoryUsed[$i] } else { $null }
                    MemoryTotalBytes = $total
                }
            }
        )

        if ($rows.Count -eq 0) { '[]' } else { $rows | ConvertTo-Json -Compress }
        """;

    private readonly IProcessRunner _processRunner;
    private readonly Func<string> _findWindowsSyclLs;
    private readonly Func<string> _findNvidiaSmi;
    private readonly Func<string> _findWindowsPowerShell;

    public GpuStatusProbeService(
        IProcessRunner processRunner,
        Func<string>? findWindowsSyclLs = null,
        Func<string>? findNvidiaSmi = null,
        Func<string>? findWindowsPowerShell = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _findWindowsSyclLs = findWindowsSyclLs ?? FindWindowsSyclLs;
        _findNvidiaSmi = findNvidiaSmi ?? HostExecutableResolver.NvidiaSmiExe;
        _findWindowsPowerShell = findWindowsPowerShell ?? HostExecutableResolver.WindowsPowerShellExe;
    }

    public async Task<VramMemorySnapshot?> MemoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-gpu=memory.free,memory.total",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (result.ExitCode != 0) return null;

            var snapshots = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(GpuStatusService.ParseMemoryLine)
                .Where(snapshot => snapshot is not null)
                .Select(snapshot => snapshot!)
                .ToArray();
            return snapshots.Length == 0
                ? null
                : new VramMemorySnapshot(
                    snapshots.Sum(snapshot => snapshot.FreeGiB),
                    snapshots.Sum(snapshot => snapshot.TotalGiB));
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"NVIDIA memory probe unavailable: {ex.Message}");
            return null;
        }
    }

    public async Task<string> SummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (result.ExitCode != 0) return "Unavailable";

            var lines = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(GpuStatusService.FormatNvidiaSmiCsvLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(4)
                .ToArray();
            return lines.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"NVIDIA GPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> SummaryForProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        if (processId <= 0) return "Unavailable";

        try
        {
            var processResult = await _processRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-compute-apps=gpu_uuid,pid",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (processResult.ExitCode != 0) return "Unavailable";

            var usedGpuUuids = processResult.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(',').Select(part => part.Trim()).ToArray())
                .Where(parts => parts.Length >= 2
                                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                                && pid == processId)
                .Select(parts => parts[0])
                .Where(uuid => !string.IsNullOrWhiteSpace(uuid))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (usedGpuUuids.Count == 0) return "Unavailable";

            var gpuResult = await _processRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-gpu=uuid,index,name,utilization.gpu,temperature.gpu,memory.used,memory.total",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (gpuResult.ExitCode != 0) return "Unavailable";

            var lines = gpuResult.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(',').Select(part => part.Trim()).ToArray())
                .Where(parts => parts.Length >= 7 && usedGpuUuids.Contains(parts[0]))
                .Select(parts => GpuStatusService.FormatNvidiaSmiCsvLine(string.Join(",", parts.Skip(1))))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(4)
                .ToArray();
            return lines.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"NVIDIA process GPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> WindowsSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                WindowsPowerShellStartInfo(WindowsGpuProbeScript),
                TimeSpan.FromSeconds(3),
                cancellationToken);
            if (result.ExitCode != 0) return "Unavailable";

            var lines = GpuStatusService.FormatWindowsGpuStatusJson(result.Output)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(4)
                .ToArray();
            return lines.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Windows GPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> CpuTemperatureAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                WindowsPowerShellStartInfo(WindowsCpuStatusProbeScript),
                TimeSpan.FromSeconds(3),
                cancellationToken);
            if (result.ExitCode != 0) return "Unavailable";

            var summary = GpuStatusService.FormatWindowsCpuTemperatureJson(result.Output);
            return string.IsNullOrWhiteSpace(summary) ? "Unavailable" : summary;
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Windows CPU temperature unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> CpuSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                WindowsPowerShellStartInfo(WindowsCpuStatusProbeScript),
                TimeSpan.FromSeconds(3),
                cancellationToken);
            if (result.ExitCode != 0) return "Unavailable";

            var summary = GpuStatusService.FormatWindowsCpuStatusJson(result.Output);
            return string.IsNullOrWhiteSpace(summary) ? "Unavailable" : summary;
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Windows CPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> WindowsIntelArcSummaryAsync(CancellationToken cancellationToken = default)
    {
        var syclLs = _findWindowsSyclLs();
        if (string.IsNullOrWhiteSpace(syclLs)) return "Unavailable";
        try
        {
            var output = await RunProcessOutputAsync(syclLs, [], TimeSpan.FromSeconds(3), cancellationToken);
            var line = GpuStatusService.FirstSyclGpuLine(output);
            return string.IsNullOrWhiteSpace(line) ? "Unavailable" : GpuStatusService.FormatIntelArcStatus(line);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Windows Intel Arc summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> WslIntelArcSummaryAsync(string wslExe, string wslDistro, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wslExe) || string.IsNullOrWhiteSpace(wslDistro)) return "Unavailable";
        try
        {
            var output = await RunProcessOutputAsync(
                wslExe,
                ["-d", wslDistro, "--", "bash", "-lc", "source /opt/intel/oneapi/setvars.sh --force >/dev/null 2>&1 || true; sycl-ls 2>/dev/null | grep -i 'level_zero.*gpu' | head -n 1"],
                TimeSpan.FromSeconds(3),
                cancellationToken);
            var line = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            return string.IsNullOrWhiteSpace(line) ? "Unavailable" : GpuStatusService.FormatIntelArcStatus(line);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"WSL Intel Arc summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    private async Task<string> RunProcessOutputAsync(
        string fileName,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(fileName);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var result = await _processRunner.RunAsync(psi, timeout, cancellationToken);
        return result.ExitCode == 0 ? result.Output : "";
    }

    private ProcessStartInfo WindowsPowerShellStartInfo(string script)
    {
        var psi = new ProcessStartInfo(_findWindowsPowerShell());
        foreach (var arg in new[]
        {
            "-NoProfile",
            "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
        })
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private ProcessStartInfo NvidiaSmiStartInfo(params string[] args)
    {
        var psi = new ProcessStartInfo(_findNvidiaSmi());
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    private static string FindWindowsSyclLs()
    {
        foreach (var directory in WindowsEnvironmentService.OneApiPathEntries().Concat(PathEntries()))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, "sycl-ls.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return "";
    }

    private static IEnumerable<string> PathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded) || !Directory.Exists(expanded)) continue;
            yield return Path.GetFullPath(expanded);
        }
    }
}
