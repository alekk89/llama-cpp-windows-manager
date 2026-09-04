using System.Globalization;

namespace LocalLlmConsole;

public static partial class EndpointInspectionDialogFactory
{
    private static string Empty(string value, string? fallback = null)
        => string.IsNullOrWhiteSpace(value) ? fallback ?? "—" : value;

    private static string Number(int? value)
        => value?.ToString("N0", CultureInfo.InvariantCulture) ?? Loc.T("EndpointInspection.NotReported");

    private static string Number(double? value)
        => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? Loc.T("EndpointInspection.NotReported");

    private static string Boolean(bool? value)
        => value.HasValue
            ? value.Value ? Loc.T("Pref.Yes") : Loc.T("Pref.No")
            : Loc.T("EndpointInspection.NotReported");

    private static string OutputLimit(int? value)
        => value switch
        {
            null => Loc.T("EndpointInspection.NotReportedRequestControlled"),
            < 0 => Loc.T("EndpointInspection.UnlimitedRequestControlled"),
            _ => Tokens(value.Value)
        };

    private static string Tokens(long value)
        => Loc.T("EndpointInspection.Tokens", value.ToString("N0", CultureInfo.InvariantCulture));

    private static string CompactCount(long value)
        => value >= 1_000_000_000
            ? $"{value / 1_000_000_000d:0.##}B"
            : value >= 1_000_000
                ? $"{value / 1_000_000d:0.##}M"
                : value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Sampling(EndpointInspectionSlot slot)
        => $"T {Number(slot.Temperature)} · K {Number(slot.TopK)} · P {Number(slot.TopP)} · Min P {Number(slot.MinP)}";

    private static string SlotState(EndpointInspectionSlot slot)
    {
        var state = slot.IsProcessing ? Loc.T("EndpointInspection.Processing") : Loc.T("EndpointInspection.Idle");
        return string.IsNullOrWhiteSpace(slot.ReasoningFormat)
            ? state
            : Loc.T("EndpointInspection.StateReasoning", state, slot.ReasoningFormat);
    }

    private static string SlotOutput(EndpointInspectionSlot slot)
    {
        var parts = new List<string> { OutputLimit(slot.MaximumOutputTokens) };
        if (slot.DecodedTokens is { } decoded)
            parts.Add(Loc.T("EndpointInspection.Decoded", decoded.ToString("N0", CultureInfo.InvariantCulture)));
        if (slot.RemainingTokens is >= 0 and { } remaining)
            parts.Add(Loc.T("EndpointInspection.Remaining", remaining.ToString("N0", CultureInfo.InvariantCulture)));
        return string.Join(" · ", parts);
    }

    private static string FriendlyCapability(string key)
        => key.Replace("supports_", "", StringComparison.OrdinalIgnoreCase).Replace('_', ' ');

}
