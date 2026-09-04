using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfEndpointModelCopyTests : WpfUiTestBase
{
    [Theory]
    [InlineData(EndpointInspectionKind.Gateway)]
    [InlineData(EndpointInspectionKind.DirectModel)]
    public async Task ModelRowsExposeSelectableIdentityAndCopyExactIds(EndpointInspectionKind kind)
        => await RunStaAsync(() =>
        {
            var ids = new[] { "owner/Qwen-27B@iq3_xxs:2", "model-with-a-long-identifier-0123456789abcdef0123456789abcdef" };
            var copied = new List<string>();
            var report = Report(kind, ids.Select((id, index) => new EndpointInspectionModel(
                id, $"Friendly model {index}", "manager", $"CUDA {index}", null, 32768, 131072, 7_000_000_000, 4_000_000_000)).ToArray());
            var owner = new Window();
            var dialog = EndpointInspectionDialogFactory.Create(owner, report, "secret-api-key", copied.Add);
            try
            {
                var content = Layout(dialog);
                var table = Assert.Single(VisualDescendants<DataGrid>(content), grid => AutomationProperties.GetAutomationId(grid) == "EndpointModelsTable");
                Assert.Equal(DataGridClipboardCopyMode.ExcludeHeader, table.ClipboardCopyMode);
                Assert.True(table.IsReadOnly);
                foreach (var id in ids)
                {
                    var idText = Assert.Single(VisualDescendants<TextBox>(table), text => text.Text == id);
                    Assert.True(idText.IsReadOnly);
                    Assert.Same(System.Windows.Application.Current.FindResource("TextMain"), idText.Foreground);
                    Assert.Equal(FlowDirection.LeftToRight, idText.FlowDirection);
                    idText.SelectAll();
                    Assert.Equal(id, idText.SelectedText);
                    var button = Assert.Single(VisualDescendants<Button>(table), candidate =>
                        AutomationProperties.GetAutomationId(candidate) == "EndpointModelCopyIdButton"
                        && ReferenceEquals(candidate.DataContext, idText.DataContext));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(id, copied[^1]);
                    Assert.Equal(id, table.Columns[0].OnCopyingCellClipboardContent(idText.DataContext));
                }
                var name = Assert.Single(VisualDescendants<TextBox>(table), text => text.Text == "Friendly model 0");
                name.Select(0, 8);
                Assert.Equal("Friendly", name.SelectedText);
                Assert.True(name.IsReadOnly);
                Assert.Same(System.Windows.Application.Current.FindResource("TextSoft"), name.Foreground);
                Assert.Equal(ids, copied);
                Assert.DoesNotContain(copied, value => value.Contains("secret-api-key", StringComparison.Ordinal));
            }
            finally
            {
                dialog.Close();
                owner.Close();
            }
        });

    [Fact]
    public async Task MissingIdsCannotBeCopiedAndClipboardFailuresAreReported()
        => await RunStaAsync(() =>
        {
            var owner = new Window();
            var dialog = EndpointInspectionDialogFactory.Create(owner, Report(EndpointInspectionKind.Gateway,
                [new("", "No ID", "", "", null, null, null, null, null), new("valid-alias", "Valid", "", "", null, null, null, null, null)]),
                copyToClipboard: _ => throw new InvalidOperationException("Clipboard busy"));
            try
            {
                var content = Layout(dialog);
                var buttons = VisualDescendants<Button>(content).Where(button => AutomationProperties.GetAutomationId(button) == "EndpointModelCopyIdButton").ToArray();
                Assert.Equal(2, buttons.Length);
                Assert.Single(buttons, button => !button.IsEnabled);
                Assert.Single(buttons, button => button.IsEnabled).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var status = Assert.Single(VisualDescendants<TextBlock>(content), text => AutomationProperties.GetAutomationId(text) == "EndpointModelCopyStatus");
                Assert.Equal(LocalLlmConsole.Localization.Loc.T("EndpointInspection.CopyFailed"), status.Text);
            }
            finally
            {
                dialog.Close();
                owner.Close();
            }
        });

    [Theory]
    [InlineData("en", 760)]
    [InlineData("de", 760)]
    [InlineData("en", 620)]
    [InlineData("de", 620)]
    public async Task MetadataHeadersRemainReadableWithTheCopyAction(string language, double width)
        => await RunStaAsync(() =>
        {
            LocalLlmConsole.Localization.Loc.LoadLanguage(language);
            var owner = new Window();
            var dialog = EndpointInspectionDialogFactory.Create(owner, Report(EndpointInspectionKind.Gateway,
                [new("alias", "Model", "manager", "CUDA", null, 32768, 131072, 7_000_000_000, 4_000_000_000)]));
            try
            {
                var content = Assert.IsAssignableFrom<FrameworkElement>(dialog.Content);
                content.Measure(new Size(width, 560));
                content.Arrange(new Rect(0, 0, width, 560));
                content.UpdateLayout();
                var table = Assert.Single(VisualDescendants<DataGrid>(content), grid => AutomationProperties.GetAutomationId(grid) == "EndpointModelsTable");
                foreach (var column in table.Columns.Take(6))
                {
                    var header = Assert.Single(VisualDescendants<System.Windows.Controls.Primitives.DataGridColumnHeader>(table), item => item.Column == column);
                    var label = new TextBlock { Text = column.Header?.ToString(), FontSize = header.FontSize, FontFamily = header.FontFamily, FontWeight = header.FontWeight };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var required = label.DesiredSize.Width + header.Padding.Left + header.Padding.Right;
                    Assert.True(column.ActualWidth + 1 >= required, $"{language}: {column.Header} was clipped ({column.ActualWidth} < {required})");
                }
                var button = Assert.Single(VisualDescendants<Button>(table), item => AutomationProperties.GetAutomationId(item) == "EndpointModelCopyIdButton");
                Assert.Equal(LocalLlmConsole.Localization.Loc.T("EndpointInspection.CopyModelId"), AutomationProperties.GetName(button));
                Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(table));
            }
            finally { dialog.Close(); owner.Close(); LocalLlmConsole.Localization.Loc.LoadLanguage("en"); }
        });

    private static FrameworkElement Layout(Window dialog)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(dialog.Content);
        content.Measure(new Size(760, 560));
        content.Arrange(new Rect(0, 0, 760, 560));
        content.UpdateLayout();
        return content;
    }

    private static EndpointInspectionReport Report(EndpointInspectionKind kind, IReadOnlyList<EndpointInspectionModel> models)
        => new(kind, "Endpoint", "http://127.0.0.1:8082/v1", "Ready", DateTimeOffset.UtcNow,
            models, null, [], [], "Keep loaded", "Loopback", []);
}
