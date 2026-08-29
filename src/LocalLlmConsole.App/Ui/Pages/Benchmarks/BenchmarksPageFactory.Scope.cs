using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace LocalLlmConsole;

public static partial class BenchmarksPageFactory
{
    private sealed record BenchmarkScopeControls(
        StackPanel Panel,
        ComboBox Model,
        ComboBox Profile,
        ComboBox Runtime,
        ComboBox ExecutionMode,
        DataGrid Profiles,
        Action<double> ResizeSelector);

    private static BenchmarkScopeControls CreateScopeControls(BenchmarksPageController controller)
    {
        var model = OverviewSizedCombo("Model", 240);
        var profile = OverviewSizedCombo("Profile", 220);
        var runtime = OverviewSizedCombo("Runtime", 220);
        var executionMode = Combo("Benchmark type");
        executionMode.ItemsSource = new[]
        {
            new BenchmarkModeItem(BenchmarkExecutionMode.ProfileServing, "Saved-profile server benchmark (recommended)"),
            new BenchmarkModeItem(BenchmarkExecutionMode.LlamaBench, "Direct llama-bench microbenchmark")
        };
        executionMode.SelectedIndex = 0;
        model.SelectionChanged += controller.SelectionChanged;

        var addProfile = Button("Add", controller.AddProfile);
        VisualRole.SetButtonRole(addProfile, VisualRole.Primary);
        OverviewPageResponsiveCoordinator.ConfigureLoadButton(addProfile);
        var clear = Button("Clear", controller.ClearProfiles);
        VisualRole.SetButtonRole(clear, VisualRole.Quiet);
        OverviewPageResponsiveCoordinator.ConfigureLoadButton(clear);
        var (profileSelector, resizeSelector) = OverviewStyleSelector(model, profile, runtime, addProfile, clear);

        var scopeProfiles = PageSectionFactory.GridFor(
            ("Model", nameof(BenchmarkScopeRow.Model), 1.15),
            ("Profile", nameof(BenchmarkScopeRow.Profile), 1.25),
            ("Runtime", nameof(BenchmarkScopeRow.Runtime), 1.25),
            ("Environment", nameof(BenchmarkScopeRow.Environment), .75));
        PageSectionFactory.AddButtonColumn(scopeProfiles, "", nameof(BenchmarkScopeRow.RemoveAction), nameof(BenchmarkScopeRow.CanRemove),
            controller.RemoveProfile, .65, tooltipBinding: nameof(BenchmarkScopeRow.RemoveToolTip), visualRole: VisualRole.Danger);
        scopeProfiles.MinHeight = 118;
        scopeProfiles.MaxHeight = 220;
        scopeProfiles.SelectionMode = DataGridSelectionMode.Extended;

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(profileSelector);
        panel.Children.Add(PageSectionFactory.GridSection("Selected profiles", scopeProfiles));
        return new BenchmarkScopeControls(panel, model, profile, runtime, executionMode, scopeProfiles, resizeSelector);
    }

    private static (Grid Bar, Action<double> Resize) OverviewStyleSelector(
        ComboBox model,
        ComboBox profile,
        ComboBox runtime,
        Button add,
        Button clear)
    {
        var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modelLabel = SelectorLabel(Loc.T("Overview.ModelLabel"));
        var profileLabel = SelectorLabel(Loc.T("ModelGroups.Column.LaunchProfile"));
        var runtimeLabel = SelectorLabel("Runtime");
        Grid.SetColumn(model, 1);
        Grid.SetColumn(profileLabel, 3);
        Grid.SetColumn(profile, 4);
        Grid.SetColumn(runtimeLabel, 6);
        Grid.SetColumn(runtime, 7);
        Grid.SetColumn(add, 9);
        Grid.SetColumn(clear, 10);
        bar.Children.Add(modelLabel);
        bar.Children.Add(model);
        bar.Children.Add(profileLabel);
        bar.Children.Add(profile);
        bar.Children.Add(runtimeLabel);
        bar.Children.Add(runtime);
        bar.Children.Add(add);
        bar.Children.Add(clear);
        var resize = ConfigureBenchmarkSelector(bar, modelLabel, model, profileLabel, profile, runtimeLabel, runtime, add, clear);
        return (bar, resize);
    }

    private static Action<double> ConfigureBenchmarkSelector(
        Grid bar,
        TextBlock modelLabel,
        ComboBox model,
        TextBlock profileLabel,
        ComboBox profile,
        TextBlock runtimeLabel,
        ComboBox runtime,
        Button add,
        Button clear)
    {
        var layout = -1;
        void Apply(double width)
        {
            var nextLayout = width >= 980 ? 2 : width >= 720 ? 1 : 0;
            if (nextLayout == layout && width > 0) return;
            layout = nextLayout;
            if (layout == 2)
            {
                SetBenchmarkColumns(bar, GridLength.Auto, new GridLength(240), new GridLength(16), GridLength.Auto,
                    new GridLength(220), new GridLength(16), GridLength.Auto, new GridLength(220),
                    new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
                PlaceBenchmarkControl(modelLabel, 0, 0);
                PlaceBenchmarkControl(model, 0, 1);
                PlaceBenchmarkControl(profileLabel, 0, 3);
                PlaceBenchmarkControl(profile, 0, 4);
                PlaceBenchmarkControl(runtimeLabel, 0, 6);
                PlaceBenchmarkControl(runtime, 0, 7);
                PlaceBenchmarkControl(add, 0, 9);
                PlaceBenchmarkControl(clear, 0, 10);
                Grid.SetColumnSpan(model, 1);
                Grid.SetColumnSpan(profile, 1);
                Grid.SetColumnSpan(runtime, 1);
                model.Width = 240;
                profile.Width = runtime.Width = 220;
                profileLabel.Margin = runtimeLabel.Margin = new Thickness(0, 0, 8, 0);
                profile.Margin = runtime.Margin = new Thickness(0);
                add.MinWidth = clear.MinWidth = 94;
                add.Margin = new Thickness(0);
                clear.Margin = new Thickness(8, 0, 0, 0);
                return;
            }

            if (layout == 1)
            {
                SetBenchmarkColumns(bar, GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(12),
                    GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(0), new GridLength(0),
                    new GridLength(0), new GridLength(8), GridLength.Auto, GridLength.Auto);
                PlaceBenchmarkControl(modelLabel, 0, 0);
                PlaceBenchmarkControl(model, 0, 1);
                PlaceBenchmarkControl(profileLabel, 0, 3);
                PlaceBenchmarkControl(profile, 0, 4);
                PlaceBenchmarkControl(runtimeLabel, 1, 0);
                PlaceBenchmarkControl(runtime, 1, 1);
                PlaceBenchmarkControl(add, 1, 9);
                PlaceBenchmarkControl(clear, 1, 10);
                Grid.SetColumnSpan(model, 1);
                Grid.SetColumnSpan(profile, 7);
                Grid.SetColumnSpan(runtime, 7);
                model.Width = profile.Width = runtime.Width = double.NaN;
                profileLabel.Margin = new Thickness(0, 0, 8, 0);
                runtimeLabel.Margin = new Thickness(0, 6, 8, 0);
                profile.Margin = new Thickness(0);
                runtime.Margin = new Thickness(0, 6, 0, 0);
                add.MinWidth = clear.MinWidth = 94;
                add.Margin = new Thickness(0, 6, 0, 0);
                clear.Margin = new Thickness(8, 6, 0, 0);
                return;
            }

            SetBenchmarkColumns(bar, GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(0),
                new GridLength(0), new GridLength(0), new GridLength(0), new GridLength(0), new GridLength(0),
                new GridLength(8), GridLength.Auto, GridLength.Auto);
            PlaceBenchmarkControl(modelLabel, 0, 0);
            PlaceBenchmarkControl(model, 0, 1);
            PlaceBenchmarkControl(profileLabel, 1, 0);
            PlaceBenchmarkControl(profile, 1, 1);
            PlaceBenchmarkControl(runtimeLabel, 2, 0);
            PlaceBenchmarkControl(runtime, 2, 1);
            PlaceBenchmarkControl(add, 2, 9);
            PlaceBenchmarkControl(clear, 2, 10);
            Grid.SetColumnSpan(model, 10);
            Grid.SetColumnSpan(profile, 10);
            Grid.SetColumnSpan(runtime, 7);
            model.Width = profile.Width = runtime.Width = double.NaN;
            profileLabel.Margin = runtimeLabel.Margin = new Thickness(0, 6, 8, 0);
            profile.Margin = runtime.Margin = new Thickness(0, 6, 0, 0);
            add.MinWidth = clear.MinWidth = 94;
            add.Margin = new Thickness(0, 6, 0, 0);
            clear.Margin = new Thickness(8, 6, 0, 0);
        }

        Apply(0);
        return Apply;
    }

    private static void SetBenchmarkColumns(Grid grid, params GridLength[] widths)
    {
        for (var index = 0; index < widths.Length; index++)
            grid.ColumnDefinitions[index].Width = widths[index];
    }

    private static void PlaceBenchmarkControl(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }

    private static TextBlock SelectorLabel(string text)
        => new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

    private static ComboBox OverviewSizedCombo(string name, double width)
    {
        var combo = Combo(name);
        combo.Width = width;
        combo.MinHeight = 30;
        combo.Height = double.NaN;
        combo.Margin = new Thickness(0);
        combo.HorizontalAlignment = WpfHorizontalAlignment.Stretch;
        combo.ToolTip = name == "Model" ? Loc.T("Tooltip.OverviewModelCombo") : Loc.T("Overview.LaunchProfileTooltip");
        return combo;
    }

}
