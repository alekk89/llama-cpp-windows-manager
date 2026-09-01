using System.Windows;
using System.Windows.Automation;

namespace LocalLlmConsole.UiTests;

public sealed class WpfResponsiveActionButtonTests : WpfUiTestBase
{
    [Fact]
    public async Task DestructiveActionLabelCompactsAndExpandsWithoutLosingAccessibilityText()
    {
        await RunStaAsync(() =>
        {
            var button = new LocalLlmConsole.ResponsiveActionButton
            {
                FullLabel = "Remove",
                CompactLabel = "×",
                Padding = new Thickness(7, 1, 7, 2),
                FontSize = 12.5,
                ToolTip = "Remove this row"
            };
            AutomationProperties.SetName(button, "Remove");

            Arrange(button, 38);
            Assert.Equal("×", button.Content);
            Assert.Equal("Remove", AutomationProperties.GetName(button));
            Assert.Equal("Remove this row", button.ToolTip);

            Arrange(button, 120);
            Assert.Equal("Remove", button.Content);
            Assert.Equal("Remove", AutomationProperties.GetName(button));

            var column = new LocalLlmConsole.ResponsiveActionDataGridColumn { MinWidth = 90 };
            Assert.Equal(LocalLlmConsole.ResponsiveActionDataGridColumn.CompactMinWidth, column.MinWidth);
        });
    }

    [Fact]
    public async Task OrdinaryColumnsCanShrinkFromEitherSharedBoundary()
    {
        await RunStaAsync(() =>
        {
            var grid = LocalLlmConsole.PageSectionFactory.GridFor(
                ("Left", "Left", 1d),
                ("Right", "Right", 1d));
            grid.ItemsSource = new[] { new { Left = "Alpha", Right = "Beta" } };
            grid.Measure(new Size(280, 120));
            grid.Arrange(new Rect(0, 0, 280, 120));
            grid.UpdateLayout();

            var leftColumn = Assert.IsType<LocalLlmConsole.FlexibleTextDataGridColumn>(grid.Columns[0]);
            var rightColumn = Assert.IsType<LocalLlmConsole.FlexibleTextDataGridColumn>(grid.Columns[1]);
            Assert.Equal(LocalLlmConsole.FlexibleTextDataGridColumn.CompactMinWidth, leftColumn.MinWidth);
            Assert.Equal(LocalLlmConsole.FlexibleTextDataGridColumn.CompactMinWidth, rightColumn.MinWidth);

            leftColumn.Width = new System.Windows.Controls.DataGridLength(48);
            rightColumn.Width = new System.Windows.Controls.DataGridLength(180);
            grid.UpdateLayout();
            Assert.Equal(48, leftColumn.ActualWidth, precision: 1);

            leftColumn.Width = new System.Windows.Controls.DataGridLength(180);
            rightColumn.Width = new System.Windows.Controls.DataGridLength(48);
            grid.UpdateLayout();
            Assert.Equal(48, rightColumn.ActualWidth, precision: 1);

        });
    }

    private static void Arrange(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, 28));
        element.Arrange(new Rect(0, 0, width, 28));
        element.UpdateLayout();
    }
}
