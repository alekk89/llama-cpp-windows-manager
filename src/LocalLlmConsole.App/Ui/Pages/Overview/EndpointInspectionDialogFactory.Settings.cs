namespace LocalLlmConsole;

public static partial class EndpointInspectionDialogFactory
{
    private static List<(string Label, string Value)> SettingsFields(EndpointInspectionReport report)
    {
        var fields = new List<(string Label, string Value)>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !value.StartsWith("Not reported", StringComparison.OrdinalIgnoreCase))
                fields.Add((Loc.T(key), value));
        }

        if (report.Kind == EndpointInspectionKind.Gateway)
        {
            Add("EndpointInspection.Policy", report.GatewayPolicy);
            Add("EndpointInspection.Exposure", report.GatewayExposure);
            return fields;
        }

        var defaults = report.Defaults;
        if (report.Models.Count == 0 && defaults?.ContextSize is { } context)
            Add("EndpointInspection.ContextSize", Tokens(context));
        if (report.Slots.Count > 0)
            Add("EndpointInspection.ParallelSlots", Loc.T("EndpointInspection.SlotSummary",
                report.Slots.Count(slot => slot.IsProcessing), report.Slots.Count));
        else if (defaults?.ParallelSlots is { } slots)
            Add("EndpointInspection.ParallelSlots", Number(slots));
        if (defaults is null) return fields;

        if (defaults.MaximumOutputTokens is { } output)
            Add("EndpointInspection.DefaultMaxOutput", OutputLimit(output));
        Add("EndpointInspection.Reasoning", defaults.Reasoning);
        Add("EndpointInspection.ReasoningFormat", defaults.ReasoningFormat);
        Add("EndpointInspection.Vision", defaults.Vision);
        if (defaults.Speculative.HasValue)
            Add("EndpointInspection.Speculative", Boolean(defaults.Speculative));
        if (defaults.Temperature.HasValue) Add("EndpointInspection.Temperature", Number(defaults.Temperature));
        if (defaults.TopK.HasValue) Add("EndpointInspection.TopK", Number(defaults.TopK));
        if (defaults.TopP.HasValue) Add("EndpointInspection.TopP", Number(defaults.TopP));
        if (defaults.MinP.HasValue) Add("EndpointInspection.MinP", Number(defaults.MinP));
        if (defaults.Sleeping == true) Add("EndpointInspection.Sleeping", Boolean(true));
        Add("EndpointInspection.ChatCapabilities", string.Join(", ", defaults.ChatCapabilities
            .Where(pair => pair.Value).Select(pair => FriendlyCapability(pair.Key))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
        return fields;
    }
}
