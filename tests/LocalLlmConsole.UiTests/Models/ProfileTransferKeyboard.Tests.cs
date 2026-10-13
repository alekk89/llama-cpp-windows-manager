using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class ProfileTransferKeyboardTests : WpfUiTestBase
{
    [Fact]
    public async Task CopyAndPasteTransferAProfileToTheFocusedDestinationAndLeaveTextEditingAlone()
    {
        await RunStaAsync(async () =>
        {
            var source = new ModelRecord("source", "Source", "source.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var target = source with { Id = "target", Name = "Target" };
            var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(TestWorkspace)) with { CustomParameters = "--no-context-shift" };
            var profile = new NamedModelLaunchProfile("profile", source.Id, "Long context", settings, DateTimeOffset.UtcNow);
            var models = new DataGrid { ItemsSource = new[] { new ModelGridRow { Size = "Test", Quant = "Q4", Name = "Test", Model = target } } };
            var profiles = new DataGrid { ItemsSource = new[] { new ModelGridRow { Size = "Test", Quant = "Q4", Name = "Test", Model = source, LaunchProfile = profile } } };
            models.SelectedIndex = profiles.SelectedIndex = 0;
            string? clipboard = null;
            string? pastedInto = null;
            var exported = 0;
            var controller = new LocalLlmConsole.ProfileTransferKeyboardController(models, profiles,
                new LocalLlmConsole.ProfileTransferUiActions((model, id, useClipboard) =>
                {
                    Assert.True(useClipboard); Assert.Equal(source.Id, model.Id); Assert.Equal(profile.Id, id);
                    clipboard = ProfileBundleCodec.Write(new(1, new(source.Name, "source.gguf"),
                        [new(profile.Name, ProfileBundleCodec.PortableSettings(settings), Annotations: new("Copied notes"))]));
                    exported++;
                    return Task.CompletedTask;
                }, (model, useClipboard) =>
                {
                    Assert.True(useClipboard);
                    var copied = ProfileBundleCodec.Read(clipboard!);
                    Assert.Equal("--no-context-shift", copied.Profiles[0].Settings["customParameters"]!.GetValue<string>());
                    Assert.Equal("Copied notes", copied.Profiles[0].Annotations!.Notes);
                    pastedInto = model.Id;
                    return Task.CompletedTask;
                }, (_, _) => throw new InvalidOperationException("Keyboard paste must use the shared import preview.")));
            var handled = false;
            Assert.True(await controller.HandleAsync(Key.C, ModifierKeys.Control, profiles, true, () => handled = true));
            Assert.True(handled); Assert.Equal(1, exported);
            Assert.True(await controller.HandleAsync(Key.V, ModifierKeys.Control, models, false, () => { }));
            Assert.Equal(target.Id, pastedInto);
            foreach (var key in new[] { Key.C, Key.V })
                Assert.False(await controller.HandleAsync(key, ModifierKeys.Control, new TextBox(), true,
                    () => throw new InvalidOperationException("Text shortcuts must not be handled.")));
            Assert.False(await controller.HandleAsync(Key.C, ModifierKeys.Control, models, false, () => { }));
            Assert.False(await controller.HandleAsync(Key.V, ModifierKeys.Control | ModifierKeys.Shift, models, false, () => { }));
            models.SelectedItem = profiles.SelectedItem = null;
            Assert.False(await controller.HandleAsync(Key.V, ModifierKeys.Control, models, false, () => { }));
            Assert.Equal(1, exported);
        });
    }

    [Fact]
    public async Task KeyIsConsumedBeforeAwaitingAndRepeatedCopyDoesNotOverlap()
    {
        await RunStaAsync(async () =>
        {
            var model = new ModelRecord("m", "Model", "m.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(TestWorkspace));
            var row = new ModelGridRow { Size = "Test", Quant = "Q4", Name = "Test", Model = model, LaunchProfile = new("p", model.Id, "Profile", settings, DateTimeOffset.UtcNow) };
            var grid = new DataGrid { ItemsSource = new[] { row }, SelectedItem = row };
            var pending = new TaskCompletionSource();
            var count = 0;
            var controller = new LocalLlmConsole.ProfileTransferKeyboardController(new DataGrid(), grid,
                new((_, _, _) => { count++; return pending.Task; }, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask));
            var handled = false;
            var copy = controller.HandleAsync(Key.C, ModifierKeys.Control, grid, true, () => handled = true);
            Assert.True(handled); Assert.False(copy.IsCompleted);
            Assert.True(await controller.HandleAsync(Key.C, ModifierKeys.Control, grid, true, () => { }));
            Assert.Equal(1, count);
            pending.SetResult();
            Assert.True(await copy);
        });
    }
}
