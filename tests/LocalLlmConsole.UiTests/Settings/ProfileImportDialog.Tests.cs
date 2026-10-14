using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class ProfileImportDialogTests : WpfUiTestBase
{
    [Fact]
    public async Task LargeBundleUsesAProfilePickerAndPreservesEditsAcrossSelections()
    {
        await RunStaAsync(() =>
        {
            var owner = new Window { Width = 200, Height = 100, ShowInTaskbar = false }; owner.Show();
            var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(TestWorkspace));
            var model = new ModelRecord("target", "Target", "target.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var profiles = Enumerable.Range(0, 100).Select(index => new PortableLaunchProfile($"Profile {index}", ProfileBundleCodec.PortableSettings(settings))).ToArray();
            var bundle = new ProfileBundle(1, new("Source", "source.gguf"), profiles);
            var preview = new ProfileImportPreview(1, model.Id, profiles.Select((profile, index) => new ProfileImportRow(index, profile.Name, null, settings, null, [], [])).ToArray());
            IReadOnlyList<ProfileImportChoice>? reviewed = null;
            Exception? failure = null;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Owner == owner);
                try
                {
                    dialog.UpdateLayout();
                    var picker = VisualDescendants<ComboBox>(dialog).Single(combo => combo.Items.Count == 100);
                    picker.SelectedIndex = 99; dialog.UpdateLayout();
                    VisualDescendants<TextBox>(dialog).Single(box => box.Text == "Profile 99").Text = "Renamed last";
                    picker.SelectedIndex = 0; picker.SelectedIndex = 99; dialog.UpdateLayout();
                    Assert.Contains(VisualDescendants<TextBox>(dialog), box => box.Text == "Renamed last");
                    VisualDescendants<Button>(dialog).Single(button => Equals(button.Content, "Review changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            LocalLlmConsole.ProfileImportDialog.Show(owner, model, bundle, preview, [], choices => { reviewed = choices; return Task.FromResult(preview); });
            owner.Close(); if (failure is not null) throw failure;
            Assert.NotNull(reviewed); Assert.Equal(100, reviewed.Count); Assert.Equal("Renamed last", reviewed[99].Name);
        });
    }

    [Fact]
    public async Task ImportRequiresReviewAndEditingInvalidatesTheReviewedPlan()
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.ApplicationThemeService.Apply("dark");
            var owner = new Window { Width = 200, Height = 100, ShowInTaskbar = false };
            owner.Show();
            var defaults = AppSettings.CreateDefault(TestWorkspace);
            var settings = ModelLaunchSettings.FromAppSettings(defaults, "cpu");
            var model = new ModelRecord("target", "Target", "target.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var source = new PortableLaunchProfile("Long context", ProfileBundleCodec.PortableSettings(settings));
            var bundle = new ProfileBundle(1, new("Source", "source.gguf"), [source]);
            var initial = new ProfileImportPreview(1, model.Id, [new(0, source.Name, null, settings, null, [], [])]);
            var reviews = 0;
            Exception? failure = null;
            owner.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Title == "Import profiles into Target");
                try
                {
                    dialog.UpdateLayout();
                    var buttons = VisualDescendants<Button>(dialog).ToArray();
                    var review = buttons.Single(button => Equals(button.Content, "Review changes"));
                    var apply = buttons.Single(button => Equals(button.Content, "Import profiles"));
                    Assert.False(apply.IsEnabled);
                    review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(apply.IsEnabled); Assert.Equal(1, reviews);
                    var name = VisualDescendants<TextBox>(dialog).Single(box => box.Text == source.Name);
                    name.Text = "Changed";
                    Assert.False(apply.IsEnabled);
                    review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(apply.IsEnabled); Assert.Equal(2, reviews);
                    apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            var result = LocalLlmConsole.ProfileImportDialog.Show(owner, model, bundle, initial, [], choices =>
            {
                reviews++;
                return Task.FromResult(initial with { Rows = [initial.Rows[0] with { Name = choices[0].Name! }] });
            });
            owner.Close();
            if (failure is not null) throw failure;
            Assert.NotNull(result);
            Assert.Equal("Changed", result.Choices[0].Name);
            Assert.Equal(ProfileConflictAction.KeepBoth, result.Choices[0].Action);
        });
    }
}
