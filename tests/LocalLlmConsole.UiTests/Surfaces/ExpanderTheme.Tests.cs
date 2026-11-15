using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace LocalLlmConsole.UiTests;

public sealed class WpfExpanderThemeTests : WpfUiTestBase
{
    [Fact]
    public async Task ExpanderHeaderFollowsThemeBrushesAndExpandsFromKeyboardAccessibleHeader()
    {
        await RunStaAsync(() =>
        {
            try
            {
                LocalLlmConsole.ApplicationThemeService.Apply("dark");
                AssertThemedHeader(CreateExpander());
                LocalLlmConsole.ApplicationThemeService.Apply("light");
                var (expander, header, headerText, arrow) = AssertThemedHeader(CreateExpander());
                var expandSite = Assert.IsType<ContentPresenter>(expander.Template.FindName("ExpandSite", expander));
                Assert.True(header.Focusable);
                Assert.Same(Application.Current.Resources["KeyboardFocusVisual"], header.FocusVisualStyle);
                Assert.Equal(Visibility.Collapsed, expandSite.Visibility);

                var toggle = (IToggleProvider)new ToggleButtonAutomationPeer(header).GetPattern(PatternInterface.Toggle)!;
                toggle.Toggle();
                Arrange(expander);
                Assert.True(expander.IsExpanded);
                Assert.Equal(Visibility.Visible, expandSite.Visibility);
                Assert.Equal(90, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);

                expander.IsEnabled = false;
                Arrange(expander);
                AssertBrush("DisabledControlForeground", headerText.Foreground);
                AssertBrush("DisabledControlForeground", arrow.Stroke);
            }
            finally
            {
                LocalLlmConsole.ApplicationThemeService.Apply("light");
            }
        });
    }

    [Fact]
    public async Task ExpanderHeaderUsesAnExplicitStatusForeground()
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.ApplicationThemeService.Apply("dark");
            try
            {
                var expander = new Expander
                {
                    Header = "Partial details",
                    Foreground = (Brush)Application.Current.Resources["Warning"],
                    Content = new TextBlock { Text = "Unavailable source" }
                };
                Arrange(expander);

                var header = Assert.IsType<ToggleButton>(expander.Template.FindName("HeaderSite", expander));
                var headerText = VisualDescendants<TextBlock>(header).Single(text => text.Text == "Partial details");
                AssertBrush("Warning", headerText.Foreground);
            }
            finally
            {
                LocalLlmConsole.ApplicationThemeService.Apply("light");
            }
        });
    }

    private static Expander CreateExpander()
        => new() { Header = "Details", Content = new TextBlock { Text = "Body" } };

    private static (Expander Expander, ToggleButton Header, TextBlock HeaderText, ShapePath Arrow) AssertThemedHeader(Expander expander)
    {
        Arrange(expander);
        var header = Assert.IsType<ToggleButton>(expander.Template.FindName("HeaderSite", expander));
        var headerText = VisualDescendants<TextBlock>(header).Single(text => text.Text == "Details");
        var arrow = Assert.Single(VisualDescendants<ShapePath>(header));
        AssertBrush("TextMain", headerText.Foreground);
        AssertBrush("TextSoft", arrow.Stroke);
        return (expander, header, headerText, arrow);
    }

    private static void AssertBrush(string key, Brush actual)
    {
        var expected = Assert.IsType<SolidColorBrush>(Application.Current.Resources[key]).Color;
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(actual).Color);
    }

    private static void Arrange(FrameworkElement element)
    {
        element.ApplyTemplate();
        element.Measure(new Size(320, 200));
        element.Arrange(new Rect(0, 0, 320, 200));
        element.UpdateLayout();
    }
}
