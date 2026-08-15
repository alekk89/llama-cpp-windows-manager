namespace LocalLlmConsole.Services;

public sealed record WindowWorkAreaLayout(
    double MinimumWidth,
    double MinimumHeight,
    double Width,
    double Height,
    double Left,
    double Top);

public static class WindowWorkAreaSizingService
{
    public static WindowWorkAreaLayout Fit(
        double desiredWidth,
        double desiredHeight,
        double requestedMinimumWidth,
        double requestedMinimumHeight,
        double workAreaLeft,
        double workAreaTop,
        double workAreaWidth,
        double workAreaHeight,
        double margin = 16)
    {
        var safeMargin = Math.Max(0, margin);
        var availableWidth = Math.Max(1, workAreaWidth - (safeMargin * 2));
        var availableHeight = Math.Max(1, workAreaHeight - (safeMargin * 2));
        var minimumWidth = Math.Min(Math.Max(1, requestedMinimumWidth), availableWidth);
        var minimumHeight = Math.Min(Math.Max(1, requestedMinimumHeight), availableHeight);
        var width = Math.Clamp(desiredWidth, minimumWidth, availableWidth);
        var height = Math.Clamp(desiredHeight, minimumHeight, availableHeight);
        return new WindowWorkAreaLayout(
            minimumWidth,
            minimumHeight,
            width,
            height,
            workAreaLeft + ((workAreaWidth - width) / 2),
            workAreaTop + ((workAreaHeight - height) / 2));
    }
}
