using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfModelsLayoutTests : WpfUiTestBase
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task RestoredSplitterLayoutRespectsHuggingFaceVisibility(bool savedVisible, bool currentVisible)
    {
        await RunStaAsync(async () =>
        {
            var settings = AppSettings.CreateDefault(TestWorkspace);
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"models-layout-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();

            var first = BuildModels(settings with { ShowModelsHuggingFace = savedVisible });
            try
            {
                first.Window.Show();
                var persistence = new UiLayoutPersistenceService(store);
                await persistence.AttachShellAsync(first.Window, first.Host, () => "Models");
                if (savedVisible) first.Page.Root.RowDefinitions[3].Height = new GridLength(310);
                await persistence.SaveShellAsync();
            }
            finally { first.Window.Close(); }

            var second = BuildModels(settings with { ShowModelsHuggingFace = currentVisible });
            try
            {
                second.Window.Show();
                var persistence = new UiLayoutPersistenceService(store);
                await persistence.AttachShellAsync(second.Window, second.Host, () => "Models");
                second.Window.UpdateLayout();

                var expectedHeight = currentVisible ? savedVisible ? 310 : 230 : 0;
                Assert.Equal(expectedHeight, second.Page.Root.RowDefinitions[3].Height.Value);
                Assert.InRange(second.Page.Root.RowDefinitions[3].ActualHeight, expectedHeight - 1, expectedHeight + 1);
                Assert.Equal(currentVisible ? Visibility.Visible : Visibility.Collapsed, second.Page.HuggingFaceSection.Visibility);
                Assert.Equal(currentVisible ? Visibility.Visible : Visibility.Collapsed, second.Page.HuggingFaceSplitter.Visibility);

                // Repeated toggles must give all available height back to the model lists and settings.
                for (var toggle = 0; toggle < 2; toggle++)
                {
                    second.State.ApplyUiPreferences(settings with { ShowModelsHuggingFace = true });
                    second.Window.UpdateLayout();
                    var bodyHeight = second.Page.Root.RowDefinitions[1].ActualHeight;
                    var sectionHeight = second.Page.Root.RowDefinitions[3].ActualHeight;
                    var splitterHeight = second.Page.Root.RowDefinitions[2].ActualHeight;
                    Assert.True(sectionHeight >= 120);

                    second.State.ApplyUiPreferences(settings with { ShowModelsHuggingFace = false });
                    second.Window.UpdateLayout();
                    Assert.Equal(0, second.Page.Root.RowDefinitions[2].ActualHeight);
                    Assert.Equal(0, second.Page.Root.RowDefinitions[3].ActualHeight);
                    Assert.Equal(bodyHeight + sectionHeight + splitterHeight,
                        second.Page.Root.RowDefinitions[1].ActualHeight, precision: 1);
                }
            }
            finally { second.Window.Close(); }
        });
    }

    private static (Window Window, ContentControl Host, ModelsPageControls Page, ModelsPageState State) BuildModels(AppSettings settings)
    {
        var page = ModelsPageFactory.Create(new ModelsPageRequest(
            new MainWindowViewModel(), settings.ModelsRoot, new Grid(),
            new ModelsPageActions(
                () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask,
                () => { }, () => Task.CompletedTask,
                (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
                _ => Task.CompletedTask, (_, _) => Task.CompletedTask,
                (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
                () => { }, (_, _) => { }, (_, _) => { }, (_, _) => { },
                () => Task.CompletedTask, () => Task.CompletedTask, _ => { })));
        var state = new ModelsPageState();
        state.Apply(page);
        state.ApplyUiPreferences(settings);
        var host = new ContentControl { Content = page.Root };
        var window = new Window { Content = host, Width = 1100, Height = 850, ShowInTaskbar = false };
        return (window, host, page, state);
    }
}
