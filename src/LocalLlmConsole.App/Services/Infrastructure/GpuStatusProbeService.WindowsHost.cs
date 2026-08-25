namespace LocalLlmConsole.Services;

public sealed record WindowsHostProbeSummary(
    string CpuSummary,
    string MemorySummary,
    string GpuSummary);

public sealed partial class GpuStatusProbeService
{
    private static readonly string WindowsHostProbeScript = BuildWindowsHostProbeScript();

    public async Task<WindowsHostProbeSummary> WindowsHostSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                WindowsPowerShellStartInfo(WindowsHostProbeScript),
                TimeSpan.FromSeconds(4),
                cancellationToken);
            if (result.ExitCode != 0) return await WindowsHostSummaryFallbackAsync(cancellationToken);

            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            var cpuJson = JsonString(root, "Cpu");
            var memoryJson = JsonString(root, "Memory");
            var gpuJson = JsonString(root, "Gpu");
            return new WindowsHostProbeSummary(
                Normalize(GpuStatusService.FormatWindowsCpuStatusJson(cpuJson)),
                Normalize(GpuStatusService.FormatWindowsMemoryStatusJson(memoryJson)),
                Join(GpuStatusService.FormatWindowsGpuStatusJson(gpuJson)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceInformation($"Combined Windows hardware summary unavailable: {ex.Message}");
            return await WindowsHostSummaryFallbackAsync(cancellationToken);
        }
    }

    private async Task<WindowsHostProbeSummary> WindowsHostSummaryFallbackAsync(
        CancellationToken cancellationToken)
    {
        var cpu = CpuSummaryAsync(cancellationToken);
        var memory = SystemMemorySummaryAsync(cancellationToken);
        var gpu = WindowsSummaryAsync(cancellationToken);
        await Task.WhenAll(cpu, memory, gpu);
        return new WindowsHostProbeSummary(await cpu, await memory, await gpu);
    }

    private static string BuildWindowsHostProbeScript()
        => $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $scripts = @{
            Cpu = @'
            {{WindowsCpuStatusProbeScript}}
            '@
            Memory = @'
            {{WindowsMemoryProbeScript}}
            '@
            Gpu = @'
            {{WindowsGpuProbeScript}}
            '@
            }
            $pool = [runspacefactory]::CreateRunspacePool(1, 3)
            $pool.Open()
            $jobs = @($scripts.GetEnumerator() | ForEach-Object {
                $shell = [PowerShell]::Create()
                $shell.RunspacePool = $pool
                [void]$shell.AddScript($_.Value)
                [pscustomobject]@{ Name = $_.Key; Shell = $shell; Async = $shell.BeginInvoke() }
            })
            $results = @{}
            foreach ($job in $jobs) {
                try { $results[$job.Name] = @($job.Shell.EndInvoke($job.Async)) -join [Environment]::NewLine }
                finally { $job.Shell.Dispose() }
            }
            $pool.Dispose()
            [pscustomobject]@{
                Cpu = ([string]$results.Cpu).Trim()
                Memory = ([string]$results.Memory).Trim()
                Gpu = ([string]$results.Gpu).Trim()
            } | ConvertTo-Json -Compress
            """;

    private static string JsonString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;

    private static string Join(IEnumerable<string> lines)
    {
        var values = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        return values.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, values);
    }

}
