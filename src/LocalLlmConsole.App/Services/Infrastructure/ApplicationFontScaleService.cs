using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfControl = System.Windows.Controls.Control;

namespace LocalLlmConsole.Services;

public static class ApplicationFontScaleService
{
    private sealed record OriginalFontSize(double Value);

    private static readonly ConditionalWeakTable<FrameworkElement, OriginalFontSize> OriginalFontSizes = new();
    private static int _currentPercent = AppSettings.DefaultFontScalePercent;
    private static int _loadedHandlerRegistered;

    public static readonly DependencyProperty IsExcludedProperty = DependencyProperty.RegisterAttached(
        "IsExcluded",
        typeof(bool),
        typeof(ApplicationFontScaleService),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static int CurrentPercent => Volatile.Read(ref _currentPercent);

    public static bool GetIsExcluded(DependencyObject element)
        => (bool)element.GetValue(IsExcludedProperty);

    public static void SetIsExcluded(DependencyObject element, bool value)
        => element.SetValue(IsExcludedProperty, value);

    public static void Apply(int percent)
    {
        var normalized = AppPreferenceService.NormalizeFontScalePercent(percent);
        Volatile.Write(ref _currentPercent, normalized);
        EnsureLoadedHandler();

        var application = WpfApplication.Current;
        if (application is null) return;
        foreach (Window window in application.Windows)
            ApplyToWindow(window, normalized);
    }

    internal static void ApplyToWindow(Window window, int percent)
    {
        ArgumentNullException.ThrowIfNull(window);
        var normalized = AppPreferenceService.NormalizeFontScalePercent(percent);
        ApplyToElement(window, normalized, includeInherited: true);
        if (window.Content is FrameworkElement content)
        {
            ApplyToElement(content, normalized, includeInherited: false);
            ApplyToVisualDescendants(content, normalized);
        }
    }

    private static void ApplyToVisualDescendants(DependencyObject parent, int percent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element)
                ApplyToElement(element, percent, includeInherited: false);
            ApplyToVisualDescendants(child, percent);
        }
    }

    private static void ApplyToElement(FrameworkElement element, int percent, bool includeInherited)
    {
        if (GetIsExcluded(element)) return;
        var fontSizeProperty = element switch
        {
            WpfControl => WpfControl.FontSizeProperty,
            TextBlock => TextBlock.FontSizeProperty,
            _ => null
        };
        if (fontSizeProperty is null) return;

        var source = DependencyPropertyHelper.GetValueSource(element, fontSizeProperty);
        if (!includeInherited && source.BaseValueSource == BaseValueSource.Inherited) return;

        var original = OriginalFontSizes.GetValue(
            element,
            current => new OriginalFontSize((double)current.GetValue(fontSizeProperty))).Value;
        element.SetCurrentValue(fontSizeProperty, original * percent / 100d);
    }

    private static void EnsureLoadedHandler()
    {
        if (Interlocked.Exchange(ref _loadedHandlerRegistered, 1) != 0) return;
        var handler = new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window)
                ApplyToWindow(window, CurrentPercent);
            else if (sender is FrameworkElement element)
                ApplyToElement(element, CurrentPercent, includeInherited: false);
        });

        // Only controls and text blocks expose a font size that this service can scale.
        // Registering on every FrameworkElement also invoked the handler for layout-only
        // grids, borders, panels, and decorators throughout the application.
        EventManager.RegisterClassHandler(
            typeof(WpfControl),
            FrameworkElement.LoadedEvent,
            handler,
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.LoadedEvent,
            handler,
            handledEventsToo: true);
    }
}
