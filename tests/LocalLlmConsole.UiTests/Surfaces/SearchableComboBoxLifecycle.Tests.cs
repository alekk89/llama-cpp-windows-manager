using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LocalLlmConsole.UiTests;

public sealed class SearchableComboBoxLifecycleTests : WpfUiTestBase
{
    [Fact]
    public async Task DeferredFavoriteDecorationIgnoresContainersWhoseTemplateWasRemoved()
    {
        await RunStaAsync(() =>
        {
            var combo = new LocalLlmConsole.SearchableComboBox { ItemsSource = new[] { "Alpha", "Beta" } };
            var window = new Window { Content = combo, Width = 400, Height = 200, ShowInTaskbar = false };
            try
            {
                window.Show(); combo.IsDropDownOpen = true; combo.UpdateLayout();
                var container = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromIndex(0));
                container.Template = null;
                Assert.Null(container.Template);
                // Process the callback queued by PrepareContainerForItemOverride after its template is gone.
                combo.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Assert.Null(container.Template);
            }
            finally { combo.IsDropDownOpen = false; window.Close(); }
        });
    }
}
