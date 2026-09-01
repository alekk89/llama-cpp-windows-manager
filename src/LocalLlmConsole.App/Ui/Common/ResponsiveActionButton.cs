using System.Windows;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfDataGridColumn = System.Windows.Controls.DataGridColumn;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfDataGridTemplateColumn = System.Windows.Controls.DataGridTemplateColumn;

namespace LocalLlmConsole;

public sealed class FlexibleTextDataGridColumn : WpfDataGridTextColumn
{
    public const double CompactMinWidth = 48;

    static FlexibleTextDataGridColumn()
    {
        WpfDataGridColumn.MinWidthProperty.OverrideMetadata(
            typeof(FlexibleTextDataGridColumn),
            new FrameworkPropertyMetadata(
                CompactMinWidth,
                null,
                (_, _) => CompactMinWidth));
    }
}

public sealed class FlexibleActionDataGridColumn : WpfDataGridTemplateColumn
{
    public const double CompactMinWidth = 48;

    static FlexibleActionDataGridColumn()
    {
        WpfDataGridColumn.MinWidthProperty.OverrideMetadata(
            typeof(FlexibleActionDataGridColumn),
            new FrameworkPropertyMetadata(
                CompactMinWidth,
                null,
                (_, _) => CompactMinWidth));
    }
}

public sealed class ResponsiveActionDataGridColumn : WpfDataGridTemplateColumn
{
    public const double CompactMinWidth = 36;

    static ResponsiveActionDataGridColumn()
    {
        WpfDataGridColumn.MinWidthProperty.OverrideMetadata(
            typeof(ResponsiveActionDataGridColumn),
            new FrameworkPropertyMetadata(
                CompactMinWidth,
                null,
                (_, _) => CompactMinWidth));
    }
}

public sealed class ResponsiveActionButton : WpfButton
{
    public static readonly DependencyProperty FullLabelProperty = DependencyProperty.Register(
        nameof(FullLabel),
        typeof(string),
        typeof(ResponsiveActionButton),
        new FrameworkPropertyMetadata("", LabelChanged));

    public static readonly DependencyProperty CompactLabelProperty = DependencyProperty.Register(
        nameof(CompactLabel),
        typeof(string),
        typeof(ResponsiveActionButton),
        new FrameworkPropertyMetadata("×", LabelChanged));

    public ResponsiveActionButton()
    {
        Loaded += (_, _) => UpdateLabel();
        SizeChanged += (_, _) => UpdateLabel();
    }

    public string FullLabel
    {
        get => (string)GetValue(FullLabelProperty);
        set => SetValue(FullLabelProperty, value);
    }

    public string CompactLabel
    {
        get => (string)GetValue(CompactLabelProperty);
        set => SetValue(CompactLabelProperty, value);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontFamilyProperty
            || e.Property == FontSizeProperty
            || e.Property == FontStretchProperty
            || e.Property == FontStyleProperty
            || e.Property == FontWeightProperty
            || e.Property == FlowDirectionProperty
            || e.Property == PaddingProperty
            || e.Property == BorderThicknessProperty)
            UpdateLabel();
    }

    private static void LabelChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
        => ((ResponsiveActionButton)source).UpdateLabel();

    private void UpdateLabel()
    {
        var fullLabel = FullLabel ?? "";
        if (ActualWidth <= 0 || string.IsNullOrWhiteSpace(fullLabel))
        {
            SetCurrentValue(ContentProperty, fullLabel);
            return;
        }

        var text = new FormattedText(
            fullLabel,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            Foreground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var available = ActualWidth
                        - Padding.Left - Padding.Right
                        - BorderThickness.Left - BorderThickness.Right;
        SetCurrentValue(ContentProperty, available + .5 < text.WidthIncludingTrailingWhitespace
            ? CompactLabel
            : fullLabel);
    }
}
