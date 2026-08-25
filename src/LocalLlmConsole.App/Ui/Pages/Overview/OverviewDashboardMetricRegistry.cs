namespace LocalLlmConsole;

public enum OverviewDashboardMetricPresentation
{
    Status,
    Hardware,
    Rate,
    TokenCount,
    Count,
    Percentage,
    Text,
    Raw
}

public sealed record OverviewDashboardMetricDefinition(
    string Id,
    string DisplayName,
    string Category,
    bool Chartable,
    string PrimaryBrushKey = "AccentBlue",
    string SecondaryBrushKey = "Accent",
    double? FixedMaximum = null,
    OverviewDashboardMetricPresentation Presentation = OverviewDashboardMetricPresentation.Text,
    bool RequiresObservedValue = false,
    string Tooltip = "",
    bool PickerVisible = true);

public sealed record OverviewDashboardMetricReading(
    string MetricId,
    string Value,
    string RuntimeKey = "",
    double? Primary = null,
    double? Secondary = null,
    DateTimeOffset? LastKnownCapturedAt = null,
    string Unit = "",
    string Detail = "");

public sealed partial class OverviewDashboardMetricRegistry
{
    private readonly Dictionary<string, OverviewDashboardMetricDefinition> _hardwareDefinitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OverviewDashboardMetricDefinition> _runtimeDefinitions = new(StringComparer.Ordinal);

    public IReadOnlyList<OverviewDashboardMetricDefinition> Definitions(IEnumerable<string>? configuredMetricIds = null)
    {
        var definitions = BuiltInDefinitions()
            .Concat(_hardwareDefinitions.Values)
            .Concat(_runtimeDefinitions.Values)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var metricId in configuredMetricIds ?? [])
        {
            if (definitions.ContainsKey(metricId)) continue;
            if (OverviewDashboardMetricIds.TryParseGpu(metricId, out var gpuIndex))
                definitions[metricId] = GpuDefinition(metricId, gpuIndex);
            else if (OverviewDashboardMetricIds.TryParseGpuVram(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.VramUsed");
            else if (OverviewDashboardMetricIds.TryParseGpuPower(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.PowerDraw");
            else if (OverviewDashboardMetricIds.TryParseGpuCoreClock(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.CoreClock");
            else if (OverviewDashboardMetricIds.TryParseGpuTemperature(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(
                    metricId, gpuIndex, Loc.T("EndpointInspection.Temperature"), 125);
            else if (OverviewDashboardMetricIds.TryParseGpuVramTemperature(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(
                    metricId, gpuIndex, "Dashboard.Metric.VramTemperature", 125);
            else if (OverviewDashboardMetricIds.TryParseGpuMemoryClock(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.MemoryClock");
            else if (OverviewDashboardMetricIds.TryParseGpuMemoryActivity(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.MemoryActivity", 100);
            else if (OverviewDashboardMetricIds.TryParseGpuFanSpeed(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.FanSpeed", 100);
            else if (OverviewDashboardMetricIds.TryParseGpuPowerLimit(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.PowerLimit");
            else if (OverviewDashboardMetricIds.TryParseGpuThrottling(metricId, out gpuIndex))
                definitions[metricId] = GpuSensorDefinition(metricId, gpuIndex, "Dashboard.Metric.Throttling", 1, chartable: false);
            else if (string.Equals(metricId, OverviewDashboardMetricIds.ObservedGpuEnergyTotal, StringComparison.Ordinal))
                definitions[metricId] = ObservedEnergyTotalDefinition();
            else if (OverviewDashboardMetricIds.TryParseObservedGpuEnergy(metricId, out gpuIndex))
                definitions[metricId] = ObservedEnergyDeviceDefinition(metricId, gpuIndex);
            else if (string.Equals(metricId, OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal, StringComparison.Ordinal))
                definitions[metricId] = ObservedElectricityCostTotalDefinition();
            else if (OverviewDashboardMetricIds.TryParseObservedGpuElectricityCost(metricId, out gpuIndex))
                definitions[metricId] = ObservedElectricityCostDeviceDefinition(metricId, gpuIndex);
            else if (OverviewDashboardMetricIds.TryParsePrometheus(metricId, out var name, out var labels))
                definitions[metricId] = RawDefinition(metricId, name, labels, type: "", help: "");
        }

        return definitions.Values
            .OrderBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public OverviewDashboardMetricDefinition Definition(string metricId)
        => Definitions([metricId]).First(item => string.Equals(item.Id, metricId, StringComparison.Ordinal));

    public IReadOnlyList<OverviewDashboardMetricReading> ObserveHardware(string summary)
    {
        _hardwareDefinitions.Clear();
        var lines = (summary ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var readings = new List<OverviewDashboardMetricReading>();
        var cpuLine = lines.FirstOrDefault(line => line.StartsWith("CPU:", StringComparison.OrdinalIgnoreCase));
        var telemetry = lines.Where(line => line.StartsWith("Telemetry:", StringComparison.OrdinalIgnoreCase))
            .Select(StripMetricPrefix).ToArray();
        if (!string.IsNullOrWhiteSpace(cpuLine) || telemetry.Length > 0)
        {
            var rawTelemetry = string.Join(" | ", telemetry);
            var percentage = FirstPercentage(rawTelemetry);
            var cpuName = StripMetricPrefix(cpuLine ?? "");
            readings.Add(new OverviewDashboardMetricReading(
                OverviewDashboardMetricIds.Cpu,
                PercentageValue(percentage),
                Primary: percentage,
                Unit: percentage is null ? "" : "%",
                Detail: JoinDetails(cpuName, CpuIdentityDetail(rawTelemetry))));
            AddSensorReading(readings, OverviewDashboardMetricIds.CpuTemperature,
                FirstNumber(rawTelemetry, @"(\d+(?:\.\d+)?)\s*°\s*C"), "0.#", "°C", cpuName);
            AddSensorReading(readings, OverviewDashboardMetricIds.CpuCoreClock,
                FirstNumber(rawTelemetry, @"(\d+(?:\.\d+)?)\s*MHz(?:\s+core)?"), "0", "MHz", cpuName);
        }

        var ramLine = lines.FirstOrDefault(line => line.StartsWith("RAM:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(ramLine))
        {
            var raw = StripMetricPrefix(ramLine);
            var percentage = FirstPercentage(raw);
            var memory = FirstMemoryPair(raw);
            readings.Add(new OverviewDashboardMetricReading(
                OverviewDashboardMetricIds.Ram,
                PercentageValue(percentage),
                Primary: percentage,
                Unit: percentage is null ? "" : "%",
                Detail: memory is { } pair ? $"{pair.Used:0.0}/{pair.Total:0.0} GiB" : DetailWithoutPercentage(raw)));
            if (memory is { } ram)
                readings.Add(new OverviewDashboardMetricReading(
                    OverviewDashboardMetricIds.RamUsed,
                    ram.Used.ToString("0.0", CultureInfo.CurrentCulture),
                    Primary: ram.Used,
                    Secondary: ram.Total,
                    Unit: "GiB",
                    Detail: $"of {ram.Total:0.0} GiB"));
            AddSensorReading(readings, OverviewDashboardMetricIds.RamClock,
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*MHz"), "0", "MHz");
        }

        var processLine = lines.FirstOrDefault(line => line.StartsWith("Process:", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(processLine))
        {
            var raw = StripMetricPrefix(processLine);
            AddSensorReading(readings, OverviewDashboardMetricIds.ServerProcessCpu,
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*%\s*CPU"), "0.#", "%", "llama-server");
            AddSensorReading(readings, OverviewDashboardMetricIds.ServerProcessMemory,
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*GiB\s*(?:private\s*)?RAM"), "0.00", "GiB", "llama-server");
        }

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^GPU\s+(\d+)\s*:\s*(.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                continue;
            var id = OverviewDashboardMetricIds.Gpu(index);
            _hardwareDefinitions[id] = GpuDefinition(id, index);
            var raw = match.Groups[2].Value.Trim();
            var gpuName = raw.Split('|', StringSplitOptions.TrimEntries)[0];
            var percentage = FirstPercentage(raw);
            readings.Add(new OverviewDashboardMetricReading(
                id,
                PercentageValue(percentage),
                Primary: percentage,
                Unit: percentage is null ? "" : "%",
                Detail: gpuName));

            var memory = FirstMemoryPair(raw);
            if (memory is { } vram)
                AddGpuReading(readings, GpuSensorDefinition(
                        OverviewDashboardMetricIds.GpuVram(index), index, "Dashboard.Metric.VramUsed"),
                    vram.Used, "0.0", "GiB", vram.Total, $"of {vram.Total:0.0} GiB · {gpuName}");
            var powerWatts = FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*W(?:\s|$)");
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuPower(index), index, "Dashboard.Metric.PowerDraw"),
                powerWatts, "0.#", "W", detail: gpuName);
            if (powerWatts is not null)
            {
                var energyId = OverviewDashboardMetricIds.ObservedGpuEnergy(index);
                _hardwareDefinitions[energyId] = ObservedEnergyDeviceDefinition(energyId, index, gpuName);
                var costId = OverviewDashboardMetricIds.ObservedGpuElectricityCost(index);
                _hardwareDefinitions[costId] = ObservedElectricityCostDeviceDefinition(costId, index, gpuName);
            }
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuCoreClock(index), index, "Dashboard.Metric.CoreClock"),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*MHz(?:\s+core)?"), "0", "MHz", detail: gpuName);
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuTemperature(index), index,
                    Loc.T("EndpointInspection.Temperature"), 125),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*°\s*C(?:\s*(?:\||$))"), "0.#", "°C", detail: gpuName);
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuVramTemperature(index), index,
                    "Dashboard.Metric.VramTemperature", 125),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*°\s*C\s+(?:memory|VRAM)"),
                "0.#", "°C", detail: gpuName);
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuMemoryClock(index), index, "Dashboard.Metric.MemoryClock"),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*MHz\s+memory"), "0", "MHz", detail: gpuName);
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuMemoryActivity(index), index, "Dashboard.Metric.MemoryActivity", 100),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*%\s+memory"), "0.#", "%", detail: gpuName);
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuFanSpeed(index), index, "Dashboard.Metric.FanSpeed", 100),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*%\s+fan"), "0.#", "%", detail: gpuName);
            AddGpuReading(readings, GpuSensorDefinition(
                    OverviewDashboardMetricIds.GpuPowerLimit(index), index, "Dashboard.Metric.PowerLimit", chartable: false),
                FirstNumber(raw, @"(\d+(?:\.\d+)?)\s*W\s+limit"), "0.#", "W", detail: gpuName);
            var throttle = Regex.Match(raw, @"throttle\s+(active|none)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (throttle.Success)
            {
                var active = throttle.Groups[1].Value.Equals("active", StringComparison.OrdinalIgnoreCase);
                var definition = GpuSensorDefinition(OverviewDashboardMetricIds.GpuThrottling(index), index,
                    "Dashboard.Metric.Throttling", 1, chartable: false);
                _hardwareDefinitions[definition.Id] = definition;
                readings.Add(new(definition.Id, active ? Loc.T("Dashboard.Value.Active") : Loc.T("Dashboard.Value.None"),
                    Primary: active ? 1 : 0, Detail: gpuName));
            }
        }

        return readings;
    }

    public IReadOnlyList<OverviewDashboardMetricReading> Observe(IReadOnlyList<PrometheusSample> samples, string runtimeKey)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _runtimeDefinitions.Clear();
        var readings = new List<OverviewDashboardMetricReading>(samples.Count);
        foreach (var sample in samples)
        {
            var id = OverviewDashboardMetricIds.Prometheus(sample.Name, sample.Labels);
            _runtimeDefinitions[id] = RawDefinition(id, sample.Name, sample.Labels, sample.Type, sample.Help);
            readings.Add(new OverviewDashboardMetricReading(
                id,
                string.IsNullOrWhiteSpace(sample.RawValue)
                    ? DisplayFormatService.MetricNumber(sample.Value)
                    : sample.RawValue,
                runtimeKey,
                string.Equals(sample.Type, "gauge", StringComparison.OrdinalIgnoreCase) ? sample.Value : null));
        }
        return readings;
    }

    private void AddGpuReading(
        List<OverviewDashboardMetricReading> readings,
        OverviewDashboardMetricDefinition definition,
        double? value,
        string format,
        string unit,
        double? secondary = null,
        string detail = "")
    {
        if (value is null) return;
        _hardwareDefinitions[definition.Id] = definition;
        readings.Add(new OverviewDashboardMetricReading(
            definition.Id,
            value.Value.ToString(format, CultureInfo.CurrentCulture),
            Primary: value,
            Secondary: secondary,
            Unit: unit,
            Detail: detail));
    }

    private static string StripMetricPrefix(string value)
    {
        var separator = value.IndexOf(':');
        return separator >= 0 ? value[(separator + 1)..].Trim() : value.Trim();
    }

    private static double? FirstPercentage(string value)
    {
        var match = Regex.Match(value, @"(?<![\d.])(\d+(?:\.\d+)?)\s*%", RegexOptions.CultureInvariant);
        return match.Success
               && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? Math.Clamp(result, 0, 100)
            : null;
    }

    private static double? FirstNumber(string value, string pattern)
    {
        var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
               && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static (double Used, double Total)? FirstMemoryPair(string value)
    {
        var match = Regex.Match(value, @"(\d+(?:\.\d+)?)/(\d+(?:\.\d+)?)\s*GiB",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success
               && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var used)
               && double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
            ? (used, total)
            : null;
    }

    private static void AddSensorReading(
        List<OverviewDashboardMetricReading> readings,
        string metricId,
        double? value,
        string format,
        string unit,
        string detail = "")
    {
        if (value is null) return;
        readings.Add(new OverviewDashboardMetricReading(
            metricId,
            value.Value.ToString(format, CultureInfo.CurrentCulture),
            Primary: value,
            Unit: unit,
            Detail: detail));
    }

    private static string PercentageValue(double? value)
        => value is { } percentage
            ? percentage.ToString("0.#", CultureInfo.CurrentCulture)
            : Loc.T("Dashboard.ValueUnavailable");

    private static string DetailWithoutPercentage(string value)
        => string.Join(" · ", value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => FirstPercentage(segment) is null));

    private static string CpuIdentityDetail(string value)
        => string.Join(" · ", value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => FirstPercentage(segment) is null
                              && FirstNumber(segment, @"(\d+(?:\.\d+)?)\s*°\s*C") is null
                              && FirstNumber(segment, @"(\d+(?:\.\d+)?)\s*MHz") is null));

    private static string JoinDetails(params string[] values)
        => string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static OverviewDashboardMetricDefinition Definition(
        string id,
        string displayName,
        string category,
        bool chartable,
        string primaryBrushKey = "AccentBlue",
        string secondaryBrushKey = "Accent",
        double? FixedMaximum = null,
        OverviewDashboardMetricPresentation presentation = OverviewDashboardMetricPresentation.Text,
        bool requiresObservedValue = false,
        bool pickerVisible = true)
        => new(
            id,
            displayName,
            category,
            chartable,
            primaryBrushKey,
            secondaryBrushKey,
            FixedMaximum,
            presentation,
            requiresObservedValue,
            OverviewDashboardMetricNaming.BuiltInTooltip(displayName, id),
            pickerVisible);

    private static OverviewDashboardMetricDefinition RawDefinition(
        string id,
        string name,
        string labels,
        string type,
        string help)
    {
        var labelSuffix = OverviewDashboardMetricNaming.FriendlyLabels(labels);
        return new OverviewDashboardMetricDefinition(
            id,
            $"{OverviewDashboardMetricNaming.FriendlyRawName(name)}{labelSuffix}",
            Loc.T("Dashboard.Category.RawMetrics"),
            false,
            Presentation: OverviewDashboardMetricPresentation.Raw,
            RequiresObservedValue: true,
            Tooltip: OverviewDashboardMetricNaming.RawTooltip(name, labels, type, help));
    }
}
