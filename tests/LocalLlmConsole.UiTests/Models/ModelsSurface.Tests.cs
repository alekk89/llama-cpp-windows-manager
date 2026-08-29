using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfModelsSurfaceTests : WpfUiTestBase
{
    [Fact]
    public async Task ModelsSurfaceRendersProfilesGroupsAndVisibilityIndependently()
    {
        await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-models-smoke"));
            var viewModel = new MainWindowViewModel();
            var alternateSettings = ModelLaunchSettings.FromAppSettings(settings with { Port = 8099 });
            var alternateProfile = new NamedModelLaunchProfile(
                "profile:model-1:alternate",
                "model-1",
                "Alternate",
                alternateSettings,
                DateTimeOffset.UtcNow);
            var ungroupedProfile = alternateProfile with
            {
                Id = "profile:model-1:ungrouped",
                Name = "Ungrouped",
                Settings = alternateSettings with { Port = 8100 }
            };
            var modelRow = new ModelGridRow { Name = "Qwen", Quant = "Q4_K_M", Size = "4 GiB", Model = RunningModel() };
            var profileRow = new ModelGridRow
            {
                Name = alternateProfile.Name,
                Quant = "Profile",
                Size = "4 GiB",
                Model = modelRow.Model,
                LaunchProfile = alternateProfile
            };
            var modelGrid = new DataGrid { ItemsSource = new[] { modelRow } };
            var profileGrid = new DataGrid { ItemsSource = new[] { profileRow } };
            var modelsState = new LocalLlmConsole.ModelsPageState();
            modelsState.Apply(new LocalLlmConsole.ModelsPageControls(
                new Grid(),
                new TextBlock(),
                modelGrid,
                profileGrid,
                new Grid(),
                new GridSplitter(),
                new TextBox(),
                new DataGrid()));
            modelGrid.SelectedItem = modelRow;
            profileGrid.SelectedItem = profileRow;

            Assert.Equal(modelRow.Model.Id, modelsState.SelectedModel?.Id);
            Assert.Equal(alternateProfile.Id, modelsState.SelectedLaunchProfileId);
            Assert.Equal(8099, modelsState.SelectedLaunchProfile?.Settings.Port);
            modelsState.ReleaseView();
            Assert.True(modelsState.SelectedModel is null && !modelsState.HasHuggingFaceGrid);

            viewModel.Models.ReplaceModels(
                [modelRow.Model],
                _ => false,
                [ungroupedProfile, alternateProfile],
                new Dictionary<string, string> { [modelRow.Model.Id] = "4 GiB" },
                new Dictionary<string, ModelGroupRecord>
                {
                    [alternateProfile.Id] = new(
                        "group:interactive", "Interactive", ModelGroupRetentionMode.Pinned, 30,
                        ModelGroupEvictionPriority.High, DateTimeOffset.UtcNow)
                },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { alternateProfile.Id });
            var launchSettingsPanel = new Grid();
            var modelPage = LocalLlmConsole.ModelsPageFactory.Create(new LocalLlmConsole.ModelsPageRequest(
                viewModel,
                settings.ModelsRoot,
                launchSettingsPanel,
                new LocalLlmConsole.ModelsPageActions(
                    () => Task.CompletedTask, () => Task.CompletedTask,
                    () => Task.CompletedTask,
                    () => { },
                    () => Task.CompletedTask,
                    (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask,
                    () => { },
                    (_, _) => { },
                    (_, _) => { },
                    (_, _) => { },
                    () => Task.CompletedTask,
                    () => Task.CompletedTask,
                    _ => { })));
            modelPage.Root.Measure(new Size(1024, 680));
            modelPage.Root.Arrange(new Rect(0, 0, 1024, 680));
            modelPage.Root.UpdateLayout();
            var modelSettingsWidthBeforeResize = launchSettingsPanel.ActualWidth;
            var modelSettingsSplitter = Assert.Single(VisualDescendants<GridSplitter>(modelPage.Root),
                splitter => splitter.ResizeDirection == GridResizeDirection.Columns);
            modelSettingsSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent
            });
            modelSettingsSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-120, 0)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent
            });
            modelSettingsSplitter.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-120, 0, false)
            {
                RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent
            });
            modelPage.Root.UpdateLayout();
            Assert.True(launchSettingsPanel.ActualWidth > modelSettingsWidthBeforeResize + 80,
                $"Model Settings width remained {launchSettingsPanel.ActualWidth:0.#} after starting at {modelSettingsWidthBeforeResize:0.#}.");
            var horizontalFallbacks = VisualDescendants<ScrollViewer>(modelPage.Root)
                .Where(scroll => scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto)
                .ToArray();
            Assert.True(horizontalFallbacks.Length >= 2);
            modelPage.Root.Measure(new Size(624, 680));
            modelPage.Root.Arrange(new Rect(0, 0, 624, 680));
            modelPage.Root.UpdateLayout();
            Assert.Contains(horizontalFallbacks, scroll => scroll.ExtentWidth > scroll.ViewportWidth);
            var liveModelsState = new LocalLlmConsole.ModelsPageState();
            liveModelsState.Apply(modelPage);
            liveModelsState.ApplyUiPreferences(settings);

            Assert.False(settings.ShowModelsHuggingFace);
            Assert.Equal(Visibility.Collapsed, modelPage.HuggingFaceSection.Visibility);
            Assert.Equal(Visibility.Collapsed, modelPage.HuggingFaceSplitter.Visibility);
            Assert.Equal(0, modelPage.Root.RowDefinitions[3].Height.Value);
            liveModelsState.ApplyUiPreferences(settings with { ShowModelsHuggingFace = true });
            Assert.Equal(Visibility.Visible, modelPage.HuggingFaceSection.Visibility);
            Assert.Equal(Visibility.Visible, modelPage.HuggingFaceSplitter.Visibility);
            Assert.Equal(230, modelPage.Root.RowDefinitions[3].Height.Value);
            Assert.Equal("Add", viewModel.Models.VariantRows[0].GroupAction);
            Assert.True(viewModel.Models.VariantRows[0].CanAssignGroup);
            Assert.Equal("Interactive", viewModel.Models.VariantRows[1].Group);
            Assert.Equal("", viewModel.Models.VariantRows[1].GroupAction);
            Assert.Equal("Click Interactive to change or remove this group assignment.", viewModel.Models.VariantRows[1].GroupToolTip);
            Assert.False(viewModel.Models.VariantRows[1].CanAssignGroup);
            Assert.False(viewModel.Models.VariantRows[0].IsTrayFavorite);
            Assert.True(viewModel.Models.VariantRows[1].IsTrayFavorite);
            Assert.DoesNotContain(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Group"));
            Assert.Contains(modelPage.ModelVariantsGrid.Columns, column => Equals(column.Header, "Group"));
            Assert.Contains(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Open Folder"));
            Assert.DoesNotContain(modelPage.ModelVariantsGrid.Columns, column => Equals(column.Header, "Open Folder"));
            AssertContextMenu(modelPage.ModelsGrid, viewModel.Models.Rows[0], "Open Folder", "Save New Profile", "Delete");
            AssertContextMenu(modelPage.ModelVariantsGrid, viewModel.Models.VariantRows[0], "Load", "Add to tray favourites", "Assign to group…", "Remove from group", "Remove");
            Assert.Contains(VisualDescendants<Button>(modelPage.Root), button => Equals(button.Content, "Groups…"));
            var inlineGroupButtons = VisualDescendants<Button>(modelPage.ModelVariantsGrid)
                .Where(button => Equals(button.Content, "Add"))
                .ToArray();
            Assert.Single(inlineGroupButtons);
            Assert.Equal(Visibility.Visible, inlineGroupButtons[0].Visibility);
            AssertGridActionButtonMatches(inlineGroupButtons[0], modelPage.ModelVariantsGrid, "Remove");
            var addGroupButtonWidth = inlineGroupButtons[0].ActualWidth;
            modelPage.ModelVariantsGrid.ScrollIntoView(viewModel.Models.VariantRows[1]);
            modelPage.ModelVariantsGrid.UpdateLayout();
            var variantButtons = VisualDescendants<Button>(modelPage.ModelVariantsGrid).ToArray();
            var groupNameButtons = variantButtons
                .Where(button => button.Visibility == Visibility.Visible
                                 && LocalLlmConsole.VisualRole.GetButtonRole(button) == LocalLlmConsole.VisualRole.Quiet)
                .ToArray();
            Assert.True(groupNameButtons.Length == 1, string.Join(" | ", variantButtons.Select(button =>
                $"{button.Content ?? "<null>"}:{button.Visibility}:{LocalLlmConsole.VisualRole.GetButtonRole(button)}:{(button.DataContext as ModelGridRow)?.Group}")));
            var groupNameButton = groupNameButtons[0];
            Assert.Equal(addGroupButtonWidth, groupNameButton.ActualWidth, precision: 1);
            Assert.Equal(LocalLlmConsole.VisualRole.Quiet, LocalLlmConsole.VisualRole.GetButtonRole(groupNameButton));
            Assert.Equal("Interactive", Assert.IsType<ModelGridRow>(groupNameButton.DataContext).Group);
            Assert.Contains(VisualDescendants<TextBlock>(groupNameButton), text => text.Text == "Interactive");
            groupNameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(
                ["Change group…", "Remove from group"],
                groupNameButton.ContextMenu!.Items.Cast<MenuItem>().Select(item => item.Header).ToArray());
            groupNameButton.ContextMenu.IsOpen = false;
        });
    }
}
