namespace LocalLlmConsole;

public static class OverviewResponsiveLayout
{
    public const double ThreeColumnMinimumWidth = 1140;
    public const double TwoColumnMinimumWidth = 620;

    public static int MetricColumnCount(double availableWidth, int visibleCardCount)
    {
        var requestedColumns = availableWidth >= ThreeColumnMinimumWidth
            ? 3
            : availableWidth >= TwoColumnMinimumWidth
                ? 2
                : 1;
        return Math.Min(requestedColumns, Math.Max(visibleCardCount, 1));
    }
}
