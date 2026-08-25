namespace LocalLlmConsole.Services;

public sealed partial class GpuStatusProbeService
{
    private static readonly string[] NvidiaBaseFields =
    [
        "index",
        "name",
        "utilization.gpu",
        "temperature.gpu",
        "memory.used",
        "memory.total",
        "power.draw",
        "clocks.gr"
    ];
    private static readonly string[] NvidiaExtendedFields =
    [
        "clocks.mem",
        "utilization.memory",
        "fan.speed",
        "power.limit",
        "clocks_throttle_reasons.active",
        "temperature.memory"
    ];
    private readonly SemaphoreSlim _nvidiaCapabilityGate = new(1, 1);
    private string[]? _supportedNvidiaExtendedFields;

    public async Task<string> NvidiaPowerSummaryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                NvidiaSmiStartInfo(
                    "--query-gpu=index,name,power.draw",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (result.ExitCode != 0) return "Unavailable";

            var lines = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(GpuStatusService.FormatNvidiaPowerCsvLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            return lines.Length == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceInformation($"NVIDIA power summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    private async Task<string> FullNvidiaSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var extendedFields = await SupportedNvidiaExtendedFieldsAsync(cancellationToken);
            var requestedFields = NvidiaBaseFields.Concat(extendedFields).ToArray();
            var result = await RunNvidiaSummaryQueryAsync(requestedFields, cancellationToken);
            if (result.ExitCode != 0
                || !TrySplitNvidiaSummary(result.Output, extendedFields, out var baseCsv, out var extendedCsv))
            {
                result = await RunNvidiaSummaryQueryAsync(NvidiaBaseFields, cancellationToken);
                if (result.ExitCode != 0) return "Unavailable";
                baseCsv = result.Output;
                extendedCsv = await NvidiaExtendedSummaryForFieldsAsync(extendedFields, cancellationToken);
            }

            var lines = baseCsv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(GpuStatusService.FormatNvidiaSmiCsvLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            if (lines.Length == 0) return "Unavailable";
            return string.IsNullOrWhiteSpace(extendedCsv)
                ? string.Join(Environment.NewLine, lines)
                : GpuStatusService.MergeNvidiaExtendedSummary(lines, extendedCsv);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"NVIDIA GPU summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    private async Task<string[]> SupportedNvidiaExtendedFieldsAsync(CancellationToken cancellationToken)
    {
        if (_supportedNvidiaExtendedFields is { } cached) return cached;
        await _nvidiaCapabilityGate.WaitAsync(cancellationToken);
        try
        {
            if (_supportedNvidiaExtendedFields is { } loaded) return loaded;
            var combined = await RunNvidiaSummaryQueryAsync(
                ["index", .. NvidiaExtendedFields],
                cancellationToken);
            if (combined.ExitCode == 0
                && HasNvidiaColumns(combined.Output, NvidiaExtendedFields.Length + 1))
                return _supportedNvidiaExtendedFields = NvidiaExtendedFields;
            if (combined.ExitCode == 0)
                return _supportedNvidiaExtendedFields = [];

            Trace.TraceInformation("Combined NVIDIA optional-sensor query was rejected; probing supported fields independently.");
            var probes = NvidiaExtendedFields
                .Select((field, position) => ProbeNvidiaExtendedFieldAsync(field, position, cancellationToken))
                .ToArray();
            var results = await Task.WhenAll(probes);
            return _supportedNvidiaExtendedFields = results
                .Where(result => result.Values.Count > 0)
                .OrderBy(result => result.Position)
                .Select(result => NvidiaExtendedFields[result.Position])
                .ToArray();
        }
        finally
        {
            _nvidiaCapabilityGate.Release();
        }
    }

    private Task<ProcessRunResult> RunNvidiaSummaryQueryAsync(
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
        => _processRunner.RunAsync(
            NvidiaSmiStartInfo(
                $"--query-gpu={string.Join(',', fields)}",
                "--format=csv,noheader,nounits"),
            TimeSpan.FromSeconds(2),
            cancellationToken);

    private async Task<string> NvidiaExtendedSummaryForFieldsAsync(
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken)
    {
        if (fields.Count == 0) return "";
        var probes = fields
            .Select(field => ProbeNvidiaExtendedFieldAsync(
                field,
                Array.IndexOf(NvidiaExtendedFields, field),
                cancellationToken))
            .ToArray();
        return FormatNvidiaExtendedFieldResults(await Task.WhenAll(probes));
    }

    private static bool TrySplitNvidiaSummary(
        string output,
        IReadOnlyList<string> extendedFields,
        out string baseCsv,
        out string extendedCsv)
    {
        if (extendedFields.Count == 0)
        {
            baseCsv = output;
            extendedCsv = "";
            return true;
        }

        var baseRows = new List<string>();
        var extendedRows = new List<string>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(',').Select(part => part.Trim()).ToArray();
            if (parts.Length < NvidiaBaseFields.Length + extendedFields.Count)
            {
                baseCsv = "";
                extendedCsv = "";
                return false;
            }
            baseRows.Add(string.Join(", ", parts.Take(NvidiaBaseFields.Length)));
            var values = Enumerable.Repeat("N/A", NvidiaExtendedFields.Length).ToArray();
            for (var index = 0; index < extendedFields.Count; index++)
            {
                var target = Array.IndexOf(NvidiaExtendedFields, extendedFields[index]);
                if (target >= 0) values[target] = parts[NvidiaBaseFields.Length + index];
            }
            extendedRows.Add($"{parts[0]}, {string.Join(", ", values)}");
        }
        baseCsv = string.Join(Environment.NewLine, baseRows);
        extendedCsv = string.Join(Environment.NewLine, extendedRows);
        return baseRows.Count > 0;
    }

    private static bool HasNvidiaColumns(string output, int minimumColumns)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Split(',').Length >= minimumColumns);

    private static string FormatNvidiaExtendedFieldResults(IReadOnlyList<NvidiaExtendedFieldResult> results)
    {
        var rows = new SortedDictionary<int, string[]>();
        foreach (var result in results)
        {
            foreach (var (index, value) in result.Values)
            {
                if (!rows.TryGetValue(index, out var values))
                {
                    values = Enumerable.Repeat("N/A", NvidiaExtendedFields.Length).ToArray();
                    rows[index] = values;
                }
                values[result.Position] = value;
            }
        }

        return string.Join(Environment.NewLine,
            rows.Select(row => $"{row.Key}, {string.Join(", ", row.Value)}"));
    }

    private async Task<NvidiaExtendedFieldResult> ProbeNvidiaExtendedFieldAsync(
        string field,
        int position,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                NvidiaSmiStartInfo(
                    $"--query-gpu=index,{field}",
                    "--format=csv,noheader,nounits"),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            if (result.ExitCode != 0) return new(position, []);

            var values = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(',', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2
                                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(parts => (int.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]))
                .ToArray();
            return new(position, values);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceInformation($"NVIDIA optional sensor '{field}' unavailable: {ex.Message}");
            return new(position, []);
        }
    }

    private sealed record NvidiaExtendedFieldResult(int Position, IReadOnlyList<(int Index, string Value)> Values);
}
