using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace LocalLlmConsole.Services;

public static class ApplicationUiScaleService
{
    private sealed record OriginalTransform(Transform Value);

    private static readonly ConditionalWeakTable<FrameworkElement, OriginalTransform> OriginalTransforms = new();
    private static int _currentPercent = AppSettings.DefaultUiScalePercent;
    private static int _windowLoadedHandlerRegistered;

    public static int CurrentPercent => Volatile.Read(ref _currentPercent);

    public static void Apply(int percent)
    {
        var normalized = AppPreferenceService.NormalizeUiScalePercent(percent);
        Volatile.Write(ref _currentPercent, normalized);
        EnsureWindowLoadedHandler();

        var application = WpfApplication.Current;
        if (application is null) return;
        foreach (Window window in application.Windows)
            ApplyToWindow(window, normalized);
    }

    internal static void ApplyToWindow(Window window, int percent)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Content is not FrameworkElement content) return;

        var original = OriginalTransforms.GetValue(
            content,
            element => new OriginalTransform(element.LayoutTransform ?? Transform.Identity)).Value;
        var normalized = AppPreferenceService.NormalizeUiScalePercent(percent);
        if (normalized == AppSettings.DefaultUiScalePercent)
        {
            content.LayoutTransform = original;
            return;
        }

        var scale = normalized / 100d;
        if (original.Value.IsIdentity)
        {
            content.LayoutTransform = new ScaleTransform(scale, scale);
            return;
        }

        var transforms = new TransformGroup();
        transforms.Children.Add(original);
        transforms.Children.Add(new ScaleTransform(scale, scale));
        content.LayoutTransform = transforms;
    }

    private static void EnsureWindowLoadedHandler()
    {
        if (Interlocked.Exchange(ref _windowLoadedHandlerRegistered, 1) != 0) return;
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                    ApplyToWindow(window, CurrentPercent);
            }),
            handledEventsToo: true);
    }
}
