using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBinding = System.Windows.Data.Binding;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static partial class LaunchSettingsPanelFactory
{
    private static DataTemplate RuntimeNameTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(RuntimeChoice.DisplayName)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.ForegroundProperty, (WpfBrush)WpfApplication.Current.Resources["TextMain"]);
        text.SetValue(TextBlock.FontFamilyProperty, new WpfFontFamily("Segoe UI"));
        text.SetValue(TextBlock.FontSizeProperty, 12d);
        return new DataTemplate(typeof(RuntimeChoice)) { VisualTree = text };
    }

    private static T CrispCompactControl<T>(T control) where T : System.Windows.Controls.Control
    {
        control.FontFamily = new WpfFontFamily("Segoe UI");
        control.FontSize = 12;
        control.FontWeight = FontWeights.Normal;
        control.SnapsToDevicePixels = true;
        control.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(control, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(control, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(control, TextHintingMode.Fixed);
        return control;
    }

    private static Grid LaunchSettingsSearchHost(Action searchChanged, out WpfTextBox searchBox)
    {
        var input = CrispCompactControl(new WpfTextBox
        {
            Height = 28,
            MinHeight = 28,
            MinWidth = 150,
            Margin = new Thickness(0),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            Background = (WpfBrush)WpfApplication.Current.Resources["InputBack"],
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            ToolTip = Loc.T("Tooltip.LaunchSettingsSearch")
        });
        var hint = new TextBlock
        {
            Text = Loc.T("Launch.Search.Placeholder"),
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        TextOptions.SetTextHintingMode(hint, TextHintingMode.Fixed);
        input.TextChanged += (_, _) =>
        {
            hint.Visibility = string.IsNullOrEmpty(input.Text) ? Visibility.Visible : Visibility.Collapsed;
            searchChanged();
        };
        searchBox = input;
        var host = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        host.Children.Add(input);
        host.Children.Add(hint);
        return host;
    }
}
