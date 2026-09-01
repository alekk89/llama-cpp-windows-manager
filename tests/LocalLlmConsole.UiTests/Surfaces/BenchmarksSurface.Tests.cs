using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed class WpfBenchmarksSurfaceTests : WpfUiTestBase
{
    [Fact]
    public async Task BenchmarksSurfaceRemainsUsableAtMinimumWindowSize()
    {
        await RunStaAsync(() =>
        {
            var planChanged = 0;
            var actions = new LocalLlmConsole.BenchmarksPageActions(
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => 0,
                _ => Task.CompletedTask,
                () => { },
                _ => { },
                () => { },
                () => planChanged++,
                action => action());
            var controls = LocalLlmConsole.BenchmarksPageFactory.Create(
                new LocalLlmConsole.BenchmarksPageController(actions));

            controls.Root.Measure(new Size(624, 504));
            controls.Root.Arrange(new Rect(0, 0, 624, 504));
            controls.Root.UpdateLayout();

            Assert.Equal(ScrollBarVisibility.Auto, controls.Root.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, controls.Root.HorizontalScrollBarVisibility);
            Assert.True(controls.Root.ExtentHeight > controls.Root.ViewportHeight);
            Assert.True(controls.RunButton.IsEnabled);
            Assert.False(controls.StopButton.IsEnabled);
            Assert.All(VisualDescendants<TextBox>(controls.Root), box =>
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(box))));
            Assert.Equal(28, controls.Name.Height);
            Assert.Equal(controls.Name.Height, controls.PromptSizes.Height);
            Assert.Equal(controls.Name.Height, controls.Preset.Height);
            Assert.False(string.IsNullOrWhiteSpace(controls.Model.ToolTip?.ToString()));
            Assert.All(new[] { controls.Model, controls.Profile, controls.Runtime }, combo =>
            {
                Assert.IsType<LocalLlmConsole.SearchableComboBox>(combo);
                Assert.False(combo.IsEditable);
                Assert.True(combo.StaysOpenOnEdit);
            });
            Assert.False(string.IsNullOrWhiteSpace(controls.PromptSizes.ToolTip?.ToString()));
            Assert.Contains("ignores both lists", controls.PromptSizes.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("standalone token-generation", controls.GenerationSizes.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("do not suppress", controls.PromptGenerationPairs.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("excluded from reported measurements", controls.Warmup.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("non-speculative profiles", controls.RequireSpeculativeMetrics.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(controls.FlashAttention.ToolTip?.ToString()));
            Assert.True(controls.FlashAttention.IsEditable);
            Assert.Contains("on", controls.FlashAttention.Items.Cast<string>());
            Assert.Contains("off", controls.KvOffload.Items.Cast<string>());
            var gpuModeLabels = new[] { "Single", "Layer", "Row", "Tensor" };
            for (var index = 0; index < gpuModeLabels.Length; index++)
            {
                controls.GpuConfigurations.Mode.SelectedIndex = index;
                Assert.Equal(gpuModeLabels[index], controls.GpuConfigurations.Mode.Text);
                Assert.Equal(gpuModeLabels[index], controls.GpuConfigurations.Mode.SelectedItem?.ToString());
            }
            controls.SpeculativeConfigurations.Type.SelectedIndex = 2;
            Assert.Equal("Draft MTP", controls.SpeculativeConfigurations.Type.Text);
            Assert.Equal("Draft MTP", controls.SpeculativeConfigurations.Type.SelectedItem?.ToString());
            controls.SpeculativeConfigurations.Head.SelectedIndex = 0;
            Assert.Equal("Profile head", controls.SpeculativeConfigurations.Head.Text);
            controls.GpuConfigurations.Mode.SelectedIndex = -1;
            Assert.Equal("GPU split", AutomationProperties.GetName(controls.GpuConfigurations.Distribution));
            Assert.False(controls.GpuConfigurations.AddButton.IsEnabled);
            controls.GpuConfigurations.Mode.SelectedIndex = 1;
            controls.GpuConfigurations.Distribution.Text = "1,";
            Assert.False(controls.GpuConfigurations.AddButton.IsEnabled);
            controls.GpuConfigurations.Distribution.Text = "3,2,1";
            Assert.True(controls.GpuConfigurations.AddButton.IsEnabled);
            controls.GpuConfigurations.Mode.SelectedIndex = -1;
            Assert.True(double.IsNaN(controls.Model.Width));
            Assert.True(double.IsNaN(controls.Profile.Width));
            Assert.True(double.IsNaN(controls.Runtime.Width));
            var addButtons = VisualDescendants<Button>(controls.Root).Where(button => Equals(button.Content, "Add")).ToArray();
            var addProfileButton = Assert.Single(addButtons);
            var clearButton = VisualDescendants<Button>(controls.Root).Single(button => Equals(button.Content, "Clear"));
            Assert.Equal(0, Grid.GetRow(controls.Model));
            Assert.Equal(1, Grid.GetRow(controls.Profile));
            Assert.Equal(2, Grid.GetRow(controls.Runtime));
            Assert.Equal(2, Grid.GetRow(addProfileButton));
            Assert.Equal(Grid.GetRow(addProfileButton), Grid.GetRow(clearButton));
            Assert.Equal(1, Grid.GetRowSpan(addProfileButton));
            Assert.Equal(30, addProfileButton.MinHeight);
            Assert.Equal(addProfileButton.MinHeight, clearButton.MinHeight);
            Assert.Equal(controls.Runtime.MinHeight, addProfileButton.MinHeight);
            Assert.Equal(5, controls.ScopeProfiles.Columns.Count);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(controls.ScopeProfiles.Columns[4]).MinWidth);
            Assert.DoesNotContain(VisualDescendants<TextBlock>(controls.Root), block => block.Text.StartsWith("Benchmarks\n", StringComparison.Ordinal));
            Assert.Contains(VisualDescendants<TextBlock>(controls.Root), block => block.Text == "1. Launch settings to test");
            Assert.Contains(VisualDescendants<TextBlock>(controls.Root), block => block.Text == "2. Choose the request workload");
            Assert.Contains(VisualDescendants<TextBlock>(controls.Root), block => block.Text == "3. Review and run");
            Assert.DoesNotContain(VisualDescendants<TextBlock>(controls.Root), block => block.Text is "Plan" or "Active run");
            var workloadDescription = VisualDescendants<TextBlock>(controls.Root).Single(block =>
                block.Text.Contains("Presets provide a starting point", StringComparison.Ordinal));
            Assert.Equal(TextWrapping.Wrap, workloadDescription.TextWrapping);
            foreach (var label in new[] { "Run name", "Preset", "Benchmark type", "Prompt targets", "Generation targets" })
            {
                var workloadLabel = VisualDescendants<TextBlock>(controls.Root).Single(block => block.Text == label);
                Assert.Equal(FontWeights.SemiBold, workloadLabel.FontWeight);
                Assert.Equal(TextWrapping.Wrap, workloadLabel.TextWrapping);
            }
            Assert.Equal(HorizontalAlignment.Stretch, controls.Name.HorizontalAlignment);
            Assert.Equal(HorizontalAlignment.Stretch, controls.Preset.HorizontalAlignment);
            Assert.True(controls.CompareContextSizes.IsChecked);
            Assert.Equal("65536", controls.ContextSizes.Text);
            Assert.True(controls.ContextSizes.IsEnabled);
            Assert.False(controls.CompareBatchSizes.IsChecked);
            Assert.True(controls.BatchSizes.IsEnabled);
            Assert.False(controls.CompareMicroBatchSizes.IsChecked);
            Assert.True(controls.MicroBatchSizes.IsEnabled);
            Assert.DoesNotContain(VisualDescendants<CheckBox>(controls.Root), checkBox =>
                checkBox.Content?.ToString()?.StartsWith("Compare", StringComparison.Ordinal) == true);
            Assert.Contains("saved context", controls.ContextSizes.ToolTip?.ToString(), StringComparison.OrdinalIgnoreCase);
            var comparisonDescription = VisualDescendants<TextBlock>(controls.Root).Single(block =>
                block.Text.Contains("Leave a row empty", StringComparison.Ordinal));
            Assert.Equal(TextWrapping.Wrap, comparisonDescription.TextWrapping);
            Assert.Contains("speculative type/head", comparisonDescription.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("262144", controls.ContextSizes.Items.Cast<string>());
            Assert.Contains("32768", controls.BatchSizes.Items.Cast<string>());
            Assert.Contains("8192", controls.MicroBatchSizes.Items.Cast<string>());
            Assert.DoesNotContain(VisualDescendants<TextBlock>(controls.Root), block =>
                block.Text.Equals("Speculative type", StringComparison.OrdinalIgnoreCase));

            controls.GpuConfigurations.Mode.SelectedItem = controls.GpuConfigurations.Mode.Items.Cast<object>()
                .Single(item => item.ToString()?.Contains("tensor", StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(controls.GpuConfigurations.AddButton.IsEnabled);
            controls.GpuConfigurations.Distribution.Text = "1,1";
            controls.GpuConfigurations.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            controls.GpuConfigurations.Mode.SelectedItem = controls.GpuConfigurations.Mode.Items.Cast<object>()
                .Single(item => item.ToString()?.Contains("layer", StringComparison.OrdinalIgnoreCase) == true);
            controls.GpuConfigurations.Distribution.Text = "1,1,1";
            controls.GpuConfigurations.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(controls.CompareGpuConfigurations.IsChecked);
            Assert.Equal(
                [new BenchmarkGpuConfiguration("tensor", "1,1"), new BenchmarkGpuConfiguration("layer", "1,1,1")],
                controls.GpuConfigurations.Values);
            Assert.Contains(VisualDescendants<Button>(controls.Root), button =>
                button.Tag is BenchmarkGpuConfiguration configuration
                && configuration == new BenchmarkGpuConfiguration("tensor", "1,1")
                && button.Content?.ToString()?.Contains("Tensor · 1,1", StringComparison.Ordinal) == true);

            controls.SpeculativeConfigurations.Type.SelectedItem = controls.SpeculativeConfigurations.Type.Items.Cast<object>()
                .Single(item => item.ToString()?.Equals("Draft simple", StringComparison.OrdinalIgnoreCase) == true);
            controls.SpeculativeConfigurations.Head.SelectedIndex = 0;
            controls.SpeculativeConfigurations.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            controls.SpeculativeConfigurations.Type.SelectedItem = controls.SpeculativeConfigurations.Type.Items.Cast<object>()
                .Single(item => item.ToString()?.Equals("Atomic MTP", StringComparison.OrdinalIgnoreCase) == true);
            controls.SpeculativeConfigurations.Head.SelectedIndex = 1;
            controls.SpeculativeConfigurations.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(controls.CompareSpeculativeConfigurations.IsChecked);
            Assert.Equal(
                [new BenchmarkSpeculativeConfiguration("draft-simple", "profile"), new BenchmarkSpeculativeConfiguration("atomic-mtp", "auto")],
                controls.SpeculativeConfigurations.Values);
            Assert.Contains(VisualDescendants<Button>(controls.Root), button =>
                button.Tag is BenchmarkSpeculativeConfiguration configuration
                && configuration == new BenchmarkSpeculativeConfiguration("atomic-mtp", "auto")
                && button.Content?.ToString()?.Contains("Atomic MTP · Automatic", StringComparison.Ordinal) == true);

            controls.Root.Measure(new Size(820, 650));
            controls.Root.Arrange(new Rect(0, 0, 820, 650));
            controls.Root.UpdateLayout();
            Assert.Equal(0, Grid.GetRow(controls.Model));
            Assert.Equal(0, Grid.GetRow(controls.Profile));
            Assert.Equal(1, Grid.GetRow(controls.Runtime));
            Assert.Equal(1, Grid.GetRow(addProfileButton));
            Assert.Equal(1, Grid.GetRow(clearButton));

            controls.Root.Measure(new Size(1180, 700));
            controls.Root.Arrange(new Rect(0, 0, 1180, 700));
            controls.Root.UpdateLayout();
            var wideModelWidth = controls.Model.ActualWidth;
            var wideProfileWidth = controls.Profile.ActualWidth;
            Assert.True(double.IsNaN(controls.Model.Width));
            Assert.True(double.IsNaN(controls.Profile.Width));
            Assert.Equal(220, controls.Runtime.Width);
            controls.Root.Measure(new Size(1480, 700));
            controls.Root.Arrange(new Rect(0, 0, 1480, 700));
            controls.Root.UpdateLayout();
            Assert.True(controls.Model.ActualWidth > wideModelWidth);
            Assert.True(controls.Profile.ActualWidth > wideProfileWidth);
            Assert.Equal(controls.GpuConfigurations.Mode.ActualWidth,
                controls.GpuConfigurations.Distribution.ActualWidth, 3);
            Assert.Equal(controls.GpuConfigurations.Mode.ActualHeight,
                controls.GpuConfigurations.Distribution.ActualHeight, 3);
            Assert.Equal(controls.GpuConfigurations.Mode.TranslatePoint(new Point(), controls.Root).Y,
                controls.GpuConfigurations.AddButton.TranslatePoint(new Point(), controls.Root).Y, 3);
            Assert.Equal(controls.SpeculativeConfigurations.Type.ActualWidth,
                controls.SpeculativeConfigurations.Head.ActualWidth, 3);
            Assert.Equal(controls.SpeculativeConfigurations.Type.TranslatePoint(new Point(), controls.Root).Y,
                controls.SpeculativeConfigurations.AddButton.TranslatePoint(new Point(), controls.Root).Y, 3);
            Assert.True(double.IsNaN(controls.Model.Width));
            Assert.True(double.IsNaN(controls.Profile.Width));
            Assert.Equal(220, controls.Runtime.Width);
            Assert.Equal(0, Grid.GetRow(controls.Profile));
            Assert.Equal(0, Grid.GetRow(controls.Runtime));
            Assert.Equal(0, Grid.GetRow(addProfileButton));
            Assert.Equal(0, Grid.GetRow(clearButton));
            controls.Root.Measure(new Size(1600, 800));
            controls.Root.Arrange(new Rect(0, 0, 1600, 800));
            controls.Root.UpdateLayout();
            var benchmarkContent = Assert.IsAssignableFrom<FrameworkElement>(controls.Root.Content);
            Assert.True(benchmarkContent.ActualWidth > 1180);
            controls.Root.Measure(new Size(624, 504));
            controls.Root.Arrange(new Rect(0, 0, 624, 504));
            controls.Root.UpdateLayout();
            Assert.Equal(0, Grid.GetRow(controls.Model));
            Assert.Equal(1, Grid.GetRow(controls.Profile));
            Assert.Equal(2, Grid.GetRow(controls.Runtime));
            Assert.Equal(2, Grid.GetRow(addProfileButton));
            Assert.True(benchmarkContent.ActualWidth <= controls.Root.ViewportWidth);
            Assert.Contains("recommended", controls.ExecutionMode.SelectedItem?.ToString(), StringComparison.OrdinalIgnoreCase);
            var directSettings = Assert.Single(
                VisualDescendants<Expander>(controls.Root),
                expander => Equals(expander.Header, "Direct llama-bench settings (optional)"));
            Assert.Equal(Visibility.Collapsed, directSettings.Visibility);
            controls.ExecutionMode.SelectedIndex = 1;
            Assert.Equal(Visibility.Visible, directSettings.Visibility);
            directSettings.IsExpanded = true;
            controls.Root.UpdateLayout();
            var directDescription = VisualDescendants<TextBlock>(directSettings).Single(block =>
                block.Text.Contains("low-level llama-bench-only matrix", StringComparison.Ordinal));
            Assert.Equal(TextWrapping.Wrap, directDescription.TextWrapping);
            foreach (var label in new[] { "Context depths", "CPU MoE layers", "Main GPUs", "Devices", "Load modes" })
            {
                var directLabel = VisualDescendants<TextBlock>(directSettings).Single(block => block.Text == label);
                Assert.Equal(FontWeights.SemiBold, directLabel.FontWeight);
                Assert.Equal(TextWrapping.Wrap, directLabel.TextWrapping);
            }
            Assert.True(controls.AdditionalArguments.IsEnabled);
            Assert.False(controls.CompareContextSizes.IsEnabled);
            Assert.False(controls.ContextSizes.IsEnabled);
            Assert.False(controls.RequireSpeculativeMetrics.IsEnabled);
            var automationSettings = Assert.Single(
                VisualDescendants<Expander>(controls.Root),
                expander => Equals(expander.Header, "Automation and low-level options (optional)"));
            automationSettings.IsExpanded = true;
            controls.Root.UpdateLayout();
            var automationDescription = VisualDescendants<TextBlock>(automationSettings).Single(block =>
                block.Text.Contains("Control how expanded benchmark items", StringComparison.Ordinal));
            Assert.Equal(TextWrapping.Wrap, automationDescription.TextWrapping);
            foreach (var label in new[] { "Failure policy", "Cooldown between items", "Equivalent profiles", "Additional arguments" })
            {
                var automationLabel = VisualDescendants<TextBlock>(automationSettings).Single(block => block.Text == label);
                Assert.Equal(FontWeights.SemiBold, automationLabel.FontWeight);
                Assert.Equal(TextWrapping.Wrap, automationLabel.TextWrapping);
            }
            Assert.True(controls.AdditionalArguments.MinHeight >= 100);
            controls.ExecutionMode.SelectedIndex = 0;
            Assert.Equal(Visibility.Collapsed, directSettings.Visibility);
            Assert.Equal(
                "2 context lengths × 3 batch sizes = 6 temporary launch configurations per profile.",
                LocalLlmConsole.BenchmarksPageFactory.VariableCombinationSummary(true, "8192,16384", true, "512,1024,2048"));

            Assert.Contains("q8_0", controls.CacheTypesK.Items.Cast<string>());
            controls.CacheTypesK.Input.SelectedItem = "q8_0";
            Assert.DoesNotContain("q8_0", controls.CacheTypesK.Items.Cast<string>());
            controls.CacheTypesK.Input.SelectedItem = "q4_0";
            Assert.DoesNotContain("q4_0", controls.CacheTypesK.Items.Cast<string>());
            Assert.True(controls.CompareCacheTypesK.IsChecked);
            Assert.Equal("q8_0,q4_0", controls.CacheTypesK.Text);
            Assert.Equal(2, controls.CacheTypesK.Values.Count);
            Assert.DoesNotContain(VisualDescendants<Button>(controls.CacheTypesK), button => Equals(button.Tag, "q8_0"));
            var removeQ8 = VisualDescendants<Button>(controls.Root).Single(button => Equals(button.Tag, "q8_0"));
            var removeQ4 = VisualDescendants<Button>(controls.Root).Single(button => Equals(button.Tag, "q4_0"));
            Assert.Contains("K/V cache type", removeQ8.Content?.ToString(), StringComparison.Ordinal);
            Assert.Same(VisualTreeHelper.GetParent(removeQ8), VisualTreeHelper.GetParent(removeQ4));
            removeQ8.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("q4_0", controls.CacheTypesK.Text);
            Assert.Contains("q8_0", controls.CacheTypesK.Items.Cast<string>());

            Assert.Equal(new[] { "Short", "Medium", "Long", "Custom" }, controls.Preset.Items.Cast<string>());
            Assert.Empty(controls.PromptSizes.Text);
            Assert.Empty(controls.GenerationSizes.Text);
            Assert.Equal("8192/512, 16384/512, 32768/1024", controls.PromptGenerationPairs.Text);
            Assert.Equal("65536", controls.ContextSizes.Text);
            Assert.Equal("1800", controls.RequestTimeoutSeconds.Text);

            controls.Preset.SelectedItem = "Short";
            Assert.Empty(controls.PromptSizes.Text);
            Assert.Empty(controls.GenerationSizes.Text);
            Assert.Equal("512/128, 2048/256, 4096/256", controls.PromptGenerationPairs.Text);
            Assert.Equal("8192", controls.ContextSizes.Text);
            Assert.Equal("5", controls.Repetitions.Text);

            controls.Preset.SelectedItem = "Long";
            Assert.Empty(controls.PromptSizes.Text);
            Assert.Empty(controls.GenerationSizes.Text);
            Assert.Equal("32768/1024, 65536/1024, 131072/1024", controls.PromptGenerationPairs.Text);
            Assert.Equal("262144", controls.ContextSizes.Text);
            Assert.Equal("1", controls.Concurrencies.Text);
            Assert.Equal("1200", controls.ReadyTimeoutSeconds.Text);
            Assert.Equal("3600", controls.RequestTimeoutSeconds.Text);
            Assert.Equal("Enabled", controls.Warmup.SelectedItem?.ToString());
            Assert.Equal(new[] { "Enabled", "Disabled" }, controls.Warmup.Items.Cast<object>().Select(item => item.ToString()));
            Assert.Equal(new[] { "Required", "Not required" }, controls.RequireSpeculativeMetrics.Items.Cast<object>().Select(item => item.ToString()));
            Assert.True(controls.CompareContextSizes.IsChecked);
            Assert.True(planChanged > 0);

            var state = new LocalLlmConsole.BenchmarksPageState();
            state.Apply(controls);
            var model = new ModelRecord("model-1", "Qwen test", "model.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
            var launchSettings = ModelLaunchSettings.FromAppSettings(
                AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-benchmarks-smoke"))) with
            { RuntimeId = "runtime-1" };
            var profiles = new[]
            {
                new NamedModelLaunchProfile("profile-default", model.Id, "Default", launchSettings, DateTimeOffset.UtcNow, true),
                new NamedModelLaunchProfile("profile-fast", model.Id, "Fast", launchSettings, DateTimeOffset.UtcNow)
            };
            var runtime = new RuntimeRecord("runtime-1", "CUDA runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
            state.SetCatalog([model], profiles, [runtime]);
            state.SetRunPolicies(stopActiveSessions: true, preventSystemSleep: false);
            state.AddSelectedProfile();
            Assert.Single(state.ScopeRows);
            var longPresetPlan = LocalLlmConsole.BenchmarksPagePlanService.Build(state, "");
            Assert.Empty(longPresetPlan.PromptSizes);
            Assert.Empty(longPresetPlan.GenerationSizes);
            Assert.Equal(
                [
                    new BenchmarkPromptGenerationPair(32768, 1024),
                    new BenchmarkPromptGenerationPair(65536, 1024),
                    new BenchmarkPromptGenerationPair(131072, 1024)
                ],
                longPresetPlan.PromptGenerationPairs);
            Assert.Equal([262144], longPresetPlan.Serving.ContextSizes);
            Assert.Equal(3, LocalLlmConsole.Services.BenchmarkPlanService.ServingWorkloads(longPresetPlan).Count);
            controls.ScopeProfiles.UpdateLayout();
            var removeButton = Assert.Single(
                VisualDescendants<Button>(controls.ScopeProfiles),
                button => button is LocalLlmConsole.ResponsiveActionButton { FullLabel: "Remove" });
            Assert.Contains(removeButton.Content, new object[] { "Remove", "×" });
            Assert.Equal(LocalLlmConsole.VisualRole.Danger, LocalLlmConsole.VisualRole.GetButtonRole(removeButton));
            state.AddAllProfilesForSelectedModel();
            Assert.Equal(2, state.ScopeRows.Count);
            Assert.Equal(2, controls.ScopeProfiles.Items.Count);
            var removedProfile = state.ScopeRows[0];
            Assert.Equal("Remove", removedProfile.RemoveAction);
            state.RemoveProfile(removedProfile);
            Assert.Single(state.ScopeRows);
            state.ClearScopeProfiles();
            Assert.Empty(state.ScopeRows);
            LocalLlmConsole.BenchmarksPagePlanService.Apply(state, new BenchmarkPlan
            {
                Name = "Imported suite",
                ExecutionMode = BenchmarkExecutionMode.ProfileServing,
                ModelIds = [model.Id],
                ProfileIds = [profiles[0].Id],
                RuntimeIds = [runtime.Id],
                UseProfileRuntime = false,
                PromptSizes = [1024],
                GenerationSizes = [],
                Depths = [0, 8192],
                Repetitions = 7,
                Warmup = false,
                StopActiveSessions = true,
                PreventSystemSleep = false,
                Serving = new BenchmarkServingOptions
                {
                    ContextSizes = [8192, 16384],
                    SpeculativeTypes = ["none", "draft-mtp"],
                    Concurrencies = [1]
                },
                Options = new BenchmarkOptionSet
                {
                    Threads = [6],
                    BatchSizes = [1024, 2048],
                    MicroBatchSizes = [256, 512],
                    FlashAttention = ["on", "off"],
                    CacheTypesKv = ["f16"],
                    GpuConfigurations =
                    [
                        new BenchmarkGpuConfiguration("tensor", "1,1"),
                        new BenchmarkGpuConfiguration("layer")
                    ],
                    Priorities = [2],
                    CpuStrict = ["0", "1"],
                    AdditionalArguments = ["--fake-option", "value"]
                }
            }, profiles);
            Assert.Equal("Imported suite", controls.Name.Text);
            Assert.Single(state.ScopeRows);
            Assert.Equal("Default", state.ScopeRows[0].Profile);
            Assert.Equal("1024", controls.PromptSizes.Text);
            Assert.Equal("8192,16384", controls.ContextSizes.Text);
            Assert.Equal("1024,2048", controls.BatchSizes.Text);
            Assert.True(controls.CompareContextSizes.IsChecked);
            Assert.True(controls.ContextSizes.IsEnabled);
            Assert.True(controls.CompareBatchSizes.IsChecked);
            Assert.True(controls.BatchSizes.IsEnabled);
            Assert.True(controls.CompareMicroBatchSizes.IsChecked);
            Assert.True(controls.MicroBatchSizes.IsEnabled);
            Assert.Equal("0,8192", controls.Depths.Text);
            Assert.Equal("--fake-option\r\nvalue", controls.AdditionalArguments.Text);
            Assert.Equal("Disabled", controls.Warmup.SelectedItem?.ToString());
            Assert.Equal(runtime.Id, state.ScopeRows[0].RuntimeId);
            Assert.Equal("on,off", controls.FlashAttention.Text);
            Assert.Equal("f16", controls.CacheTypesK.Text);
            Assert.Equal(
                [new BenchmarkGpuConfiguration("tensor", "1,1"), new BenchmarkGpuConfiguration("layer")],
                controls.GpuConfigurations.Values);
            Assert.Equal(
                [new BenchmarkSpeculativeConfiguration("none", "profile"), new BenchmarkSpeculativeConfiguration("draft-mtp", "profile")],
                controls.SpeculativeConfigurations.Values);
            Assert.Equal("2", controls.Priorities.Text);
            var rebuilt = LocalLlmConsole.BenchmarksPagePlanService.Build(state, "");
            Assert.Equal(["on", "off"], rebuilt.Options.FlashAttention);
            Assert.Equal([256, 512], rebuilt.Options.MicroBatchSizes);
            Assert.Empty(rebuilt.Serving.SpeculativeTypes);
            Assert.Equal(
                [new BenchmarkSpeculativeConfiguration("none", "profile"), new BenchmarkSpeculativeConfiguration("draft-mtp", "profile")],
                rebuilt.Serving.SpeculativeConfigurations);
            Assert.True(rebuilt.StopActiveSessions);
            Assert.False(rebuilt.PreventSystemSleep);
            Assert.False(rebuilt.UseProfileRuntime);
            Assert.Equal([runtime.Id], rebuilt.RuntimeIds);
            Assert.Equal(new BenchmarkScopeSelection(model.Id, profiles[0].Id, runtime.Id), Assert.Single(rebuilt.ScopeSelections));
            Assert.Equal(["f16"], rebuilt.Options.CacheTypesKv);
            Assert.Equal(
                [new BenchmarkGpuConfiguration("tensor", "1,1"), new BenchmarkGpuConfiguration("layer")],
                rebuilt.Options.GpuConfigurations);
            Assert.Empty(rebuilt.Options.SplitModes);
            Assert.Empty(rebuilt.Options.TensorSplits);
            Assert.Equal(["0", "1"], rebuilt.Options.CpuStrict);
            Assert.Equal([8192, 16384], rebuilt.Serving.ContextSizes);

            controls.ContextSizes.Text = "";
            controls.BatchSizes.Text = "";
            rebuilt = LocalLlmConsole.BenchmarksPagePlanService.Build(state, "");
            Assert.Empty(rebuilt.Serving.ContextSizes);
            Assert.Empty(rebuilt.Options.BatchSizes);

            var actionLabels = new[] { "Validate", "Start", "Stop" };
            Assert.Equal(actionLabels,
                VisualDescendants<Button>(controls.Root)
                    .Where(button => actionLabels.Contains(button.Content?.ToString(), StringComparer.Ordinal))
                    .Select(button => button.Content?.ToString())
                    .ToArray());
            Assert.DoesNotContain(VisualDescendants<Button>(controls.Root), button =>
                button.Content?.ToString() is "View report" or "Compare 2" or "More");
            Assert.Equal(6, controls.History.Columns.Count);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(controls.History.Columns[^1]).MinWidth);
            Assert.Equal(
                ["View report", "Compare selected", "Pause after current test", "Resume selected run", "Export results", "Clone selected plan", "Import plan", "Export current plan", "Open run log", "Refresh runs"],
                controls.History.ContextMenu!.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray());
            var completedRun = new LocalLlmConsole.BenchmarkRunRow("run", "now", "Completed", "1 item", "1/1", "Done");
            var runningRun = completedRun with { RunId = "active", Status = "Running" };
            Assert.Equal("Delete", completedRun.RemoveAction);
            Assert.True(completedRun.CanRemove);
            Assert.False(runningRun.CanRemove);
            var pager = Assert.IsType<StackPanel>(VisualTreeHelper.GetParent(controls.HistoryPrevious));
            Assert.Equal(Orientation.Horizontal, pager.Orientation);
            Assert.Equal(HorizontalAlignment.Right, pager.HorizontalAlignment);
            Assert.Equal(1, Grid.GetColumn(pager));
            Assert.Same(pager, VisualTreeHelper.GetParent(controls.HistoryNext));
        });
    }

}
