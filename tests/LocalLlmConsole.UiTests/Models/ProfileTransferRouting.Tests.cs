using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class ProfileTransferRoutingTests : WpfUiTestBase
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task CopyThenSwitchModelAndPasteUsesTheDestinationEvenOutsideTheTables(int focus, bool emptyDestination)
    {
        await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(TestWorkspace);
            var source = new ModelRecord("source", "Source", "source.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var target = source with { Id = "target", Name = "Target" };
            var profile = new NamedModelLaunchProfile("profile", source.Id, "Long context",
                ModelLaunchSettings.FromAppSettings(settings), DateTimeOffset.UtcNow);
            var targetProfile = profile with { Id = "target-profile", ModelId = target.Id, Name = "Default", IsDefault = true };
            var viewModel = new MainWindowViewModel();
            viewModel.Models.ReplaceModels([source, target], _ => false,
                emptyDestination ? [profile] : [profile, targetProfile], new Dictionary<string, string>());
            var settingsPanel = new StackPanel();
            var settingsButton = new Button { Content = "Advanced settings" };
            var text = new TextBox { Text = "normal text" };
            settingsPanel.Children.Add(settingsButton); settingsPanel.Children.Add(text);
            string? copied = null;
            string? pastedInto = null;
            var pasteCount = 0;
            var page = LocalLlmConsole.ModelsPageFactory.Create(new(viewModel, settings.ModelsRoot, settingsPanel,
                new(() => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask, () => { },
                    () => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
                    _ => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask, () => { },
                    (selected, _) =>
                    {
                        if (selected.SelectedItem is ModelGridRow { LaunchProfile: null } row)
                            viewModel.Models.ShowLaunchProfilesForModel(row.Model.Id);
                    }, (_, _) => { }, (_, _) => { }, () => Task.CompletedTask, () => Task.CompletedTask, _ => { },
                    new((model, id, clipboard) =>
                    {
                        Assert.True(clipboard); Assert.Equal(source.Id, model.Id); Assert.Equal(profile.Id, id);
                        copied = ProfileBundleCodec.Write(new(1, new(source.Name, "source.gguf"),
                            [new(profile.Name, ProfileBundleCodec.PortableSettings(profile.Settings), Annotations: new("Source notes"))]));
                        return Task.CompletedTask;
                    }, (model, clipboard) =>
                    {
                        Assert.True(clipboard);
                        Assert.Equal("Source notes", ProfileBundleCodec.Read(copied!).Profiles[0].Annotations!.Notes);
                        pastedInto = model.Id; pasteCount++;
                        return Task.CompletedTask;
                    }, (_, _) => throw new InvalidOperationException("Use the shared clipboard import.")))));
            // Disable native cell copying so a regression cannot overwrite the user's clipboard.
            page.ModelVariantsGrid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
            var window = new Window { Content = page.Root, Width = 1100, Height = 720, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                page.ModelsGrid.SelectedItem = viewModel.Models.Rows.Single(row => row.Model.Id == source.Id);
                page.ModelVariantsGrid.SelectedItem = Assert.Single(viewModel.Models.VariantRows);
                window.UpdateLayout();
                Assert.True(ApplicationCommands.Copy.CanExecute(null, page.ModelVariantsGrid));
                ApplicationCommands.Copy.Execute(null, page.ModelVariantsGrid);
                Assert.NotNull(copied);

                page.ModelsGrid.SelectedItem = viewModel.Models.Rows.Single(row => row.Model.Id == target.Id);
                if (emptyDestination) Assert.Empty(viewModel.Models.VariantRows);
                else page.ModelVariantsGrid.SelectedItem = Assert.Single(viewModel.Models.VariantRows);
                window.UpdateLayout();
                IInputElement destination = focus switch { 0 => page.ModelVariantsGrid, 1 => settingsButton, _ => window };
                Keyboard.Focus(destination);
                Assert.True(ApplicationCommands.Paste.CanExecute(null, destination));
                ApplicationCommands.Paste.Execute(null, destination);
                Assert.Equal(target.Id, pastedInto); Assert.Equal(1, pasteCount);

                var textPastes = 0;
                text.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
                    (_, _) => textPastes++, (_, args) => args.CanExecute = true));
                ApplicationCommands.Paste.Execute(null, text);
                Assert.Equal(1, textPastes); Assert.Equal(1, pasteCount);

                window.Content = new Button { Content = "Another page" };
                window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert.False(ApplicationCommands.Paste.CanExecute(null, window));
            }
            finally { window.Close(); }
        });
    }
}
