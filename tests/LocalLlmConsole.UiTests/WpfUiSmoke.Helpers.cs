using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed partial class WpfUiSmokeTests
{
    private static LoadedModelSessionSnapshot RunningSession(AppSettings settings)
        => new(
            "session-1",
            "model-1",
            "Qwen",
            "runtime-1",
            "Official CPU",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            "runtime.log",
            DateTimeOffset.UtcNow,
            "",
            123,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true,
            LaunchProfileId: "default:model-1",
            LaunchProfileName: "Default");

    private static ModelRecord RunningModel()
        => new(
            "model-1",
            "Qwen",
            Path.Combine(Path.GetTempPath(), "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static Border MetricCard(Grid metric)
        => Assert.IsType<Border>(Assert.IsType<StackPanel>(metric.Parent).Parent);

    private static void AssertContextMenu(DataGrid grid, object row, params string[] expectedHeaders)
    {
        grid.SelectedItem = row;
        grid.ContextMenu!.IsOpen = true;
        Assert.Equal(expectedHeaders, grid.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray());
        grid.ContextMenu.IsOpen = false;
    }

    private static void AssertGridActionButtonMatches(Button actual, DataGrid grid, string expectedPeerContent)
    {
        var expected = VisualDescendants<Button>(grid)
            .Single(button => Equals(button.Content, expectedPeerContent)
                              && ReferenceEquals(button.DataContext, actual.DataContext));
        Assert.True(double.IsNaN(actual.Height));
        Assert.Equal(expected.ActualHeight, actual.ActualHeight, precision: 1);
        Assert.Equal(expected.Padding, actual.Padding);
        Assert.Equal(expected.Margin, actual.Margin);
    }

    private static async Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        thread.Join(TimeSpan.FromSeconds(5));
    }
}
