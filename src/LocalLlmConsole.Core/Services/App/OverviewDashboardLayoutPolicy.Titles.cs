namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    public const int CardTitleLayoutVersion = 9;
    public const int MaximumCardTitleLength = 80;

    public static OverviewDashboardLayout SetCardTitle(
        OverviewDashboardLayout layout,
        string cardId,
        string? title)
        => UpdateCard(layout, cardId, card => card with { Title = NormalizeCardTitle(title) });

    private static string NormalizeCardTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var normalized = new string(title
            .Where(character => !char.IsControl(character))
            .ToArray())
            .Trim();
        return normalized.Length <= MaximumCardTitleLength
            ? normalized
            : normalized[..MaximumCardTitleLength].TrimEnd();
    }
}
