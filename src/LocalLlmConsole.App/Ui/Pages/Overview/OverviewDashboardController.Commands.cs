using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private void ConfigureCommands()
    {
        var add = QuietButton(Loc.T("Dashboard.AddCard"));
        add.Click += async (_, _) => await AddCardAsync();
        _sizeLockButton = QuietButton(Loc.T("Dashboard.Lock"));
        _sizeLockButton.Click += async (_, _) => await ToggleCardSizeLockAsync();
        var reset = QuietButton(Loc.T("Dashboard.Reset"));
        reset.Click += async (_, _) => await ResetLayoutAsync();
        _dashboardActions.Children.Add(add);
        _dashboardActions.Children.Add(_sizeLockButton);
        _dashboardActions.Children.Add(reset);
    }

    private void UpdateSizeLockButton()
    {
        if (_sizeLockButton is null) return;
        _sizeLockButton.Content = Loc.T(_layout.CardSizesLocked ? "Dashboard.Unlock" : "Dashboard.Lock");
        _sizeLockButton.ToolTip = Loc.T(_layout.CardSizesLocked
            ? "Dashboard.UnlockTooltip"
            : "Dashboard.LockTooltip");
        _sizeLockButton.IsEnabled = _layout.Cards.Count > 0;
    }

    private async Task ToggleCardSizeLockAsync()
    {
        var surfaceWidth = _dashboard.ActualWidth;
        if (!double.IsFinite(surfaceWidth) || surfaceWidth < 1) return;
        var renderedBounds = _cardViews.Values
            .Where(view => view.Root.Visibility == Visibility.Visible)
            .ToDictionary(
                view => view.Layout.Id,
                view => RenderedBounds(view, surfaceWidth),
                StringComparer.OrdinalIgnoreCase);
        await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetCardSizesLocked(
            layout,
            !layout.CardSizesLocked,
            surfaceWidth,
            renderedBounds));
    }

    private ContextMenu DashboardContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(AsyncItem(menu, Loc.T("Dashboard.AddCard"), AddCardAsync));
        menu.Items.Add(new Separator());
        menu.Items.Add(AsyncItem(menu, Loc.T("Dashboard.Reset"), ResetLayoutAsync));
        return menu;
    }

    private ContextMenu CardContextMenu(OverviewDashboardCardLayout card)
    {
        var menu = new ContextMenu();
        Populate();
        menu.Opened += (_, _) => Populate();
        return menu;

        void Populate()
        {
            menu.Items.Clear();
            var current = _layout.Cards.FirstOrDefault(item =>
                string.Equals(item.Id, card.Id, StringComparison.OrdinalIgnoreCase)) ?? card;
            menu.Items.Add(AsyncItem(menu, Loc.T("Dashboard.AddMetrics"), () => AddMetricsAsync(current)));
            menu.Items.Add(AsyncItem(menu, Loc.T("Dashboard.CardTitle"), () => EditCardTitleAsync(current)));
            var removeMetric = Submenu(Loc.T("Dashboard.RemoveMetric"));
            foreach (var metricId in current.MetricIds)
            {
                var definition = _registry.Definition(metricId);
                removeMetric.Items.Add(AsyncItem(menu, definition.DisplayName,
                    async () =>
                    {
                        await MutateAsync(layout => OverviewDashboardLayoutPolicy.RemoveMetric(layout, current.Id, metricId));
                        if (_layout.Cards.All(item => !string.Equals(item.Id, current.Id, StringComparison.Ordinal)))
                            menu.IsOpen = false;
                    },
                    keepOpen: true,
                    disableAfterClick: true));
            }
            menu.Items.Add(removeMetric);
            menu.Items.Add(AsyncItem(menu, Loc.T("Dashboard.Reorder"),
                () => BeginMetricReorderAsync(current.Id), current.MetricIds.Count > 1));
            menu.Items.Add(ChartMenu(menu, current));
            menu.Items.Add(new Separator());
            menu.Items.Add(AsyncItem(menu, Loc.T("Dashboard.RemoveCard"),
                () => MutateAsync(layout => OverviewDashboardLayoutPolicy.RemoveCard(layout, current.Id))));
        }
    }

    private MenuItem ChartMenu(ContextMenu owner, OverviewDashboardCardLayout card)
    {
        var menu = Submenu(Loc.T("Dashboard.Chart"));
        Populate();
        menu.SubmenuOpened += (_, _) => Populate();
        return menu;

        void Populate()
        {
            menu.Items.Clear();
            var current = _layout.Cards.FirstOrDefault(item =>
                string.Equals(item.Id, card.Id, StringComparison.OrdinalIgnoreCase)) ?? card;
            var availableCharts = current.MetricIds.Select(_registry.Definition)
                .Where(item => item.Chartable && IsDefinitionAvailable(item))
                .ToArray();
            var selectedCharts = (current.ChartMetricIds ?? [])
                .Intersect(availableCharts.Select(item => item.Id), StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);
            var chartItems = new List<MenuItem>();
            MenuItem noneItem = null!;
            noneItem = AsyncCheckItem(owner, Loc.T("Dashboard.ChartNone"), selectedCharts.Count == 0,
                async _ =>
                {
                    selectedCharts.Clear();
                    noneItem.IsChecked = true;
                    foreach (var chartItem in chartItems)
                        chartItem.IsChecked = false;
                    await MutateAsync(layout => OverviewDashboardLayoutPolicy.ClearCharts(layout, current.Id));
                });
            menu.Items.Add(noneItem);
            menu.Items.Add(new Separator());
            foreach (var definition in availableCharts)
            {
                var selected = selectedCharts.Contains(definition.Id);
                var metricId = definition.Id;
                var chartItem = AsyncCheckItem(owner, definition.DisplayName, selected,
                    async isChecked =>
                    {
                        if (isChecked)
                            selectedCharts.Add(metricId);
                        else
                            selectedCharts.Remove(metricId);
                        noneItem.IsChecked = selectedCharts.Count == 0;
                        await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetChartVisibility(
                            layout, current.Id, metricId, isChecked));
                    });
                chartItems.Add(chartItem);
                menu.Items.Add(chartItem);
            }
            menu.IsEnabled = menu.Items.Count > 2;
        }
    }

    private async Task AddCardAsync()
    {
        var usedMetricIds = _layout.Cards.SelectMany(card => card.MetricIds).ToArray();
        var selected = OverviewDashboardMetricPicker.Show(
            Loc.T("Dashboard.AddCardTitle"),
            AvailableDefinitions(usedMetricIds),
            usedMetricIds);
        if (selected.Count > 0)
            await MutateAsync(layout => OverviewDashboardLayoutPolicy.AddCard(layout, selected));
    }

    private async Task AddMetricsAsync(OverviewDashboardCardLayout card)
    {
        var usedMetricIds = _layout.Cards.SelectMany(item => item.MetricIds).ToArray();
        var selected = OverviewDashboardMetricPicker.Show(
            Loc.T("Dashboard.AddMetricsTitle"),
            AvailableDefinitions(usedMetricIds),
            usedMetricIds);
        if (selected.Count > 0)
            await MutateAsync(layout => OverviewDashboardLayoutPolicy.AddMetrics(layout, card.Id, selected));
    }

    private async Task EditCardTitleAsync(OverviewDashboardCardLayout card)
    {
        var title = OverviewDashboardCardTitleDialog.Show(card.Title);
        if (title is not null)
            await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetCardTitle(layout, card.Id, title));
    }

    private async Task ResetLayoutAsync()
    {
        var result = System.Windows.MessageBox.Show(
            Loc.T("Dashboard.ResetConfirmation"),
            Loc.T("Dashboard.Reset"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            await MutateAsync(_ => OverviewDashboardLayoutPolicy.Reset());
    }

    private async Task MutateAsync(Func<OverviewDashboardLayout, OverviewDashboardLayout> mutation)
    {
        await _actions.RunEventAsync(async () =>
        {
            await _mutationGate.WaitAsync();
            var previous = _layout;
            try
            {
                var next = OverviewDashboardLayoutPolicy.Normalize(mutation(_layout));
                _layout = next;
                RebuildDashboard();
                try
                {
                    await _actions.PersistLayoutAsync(next);
                }
                catch
                {
                    _layout = previous;
                    RebuildDashboard();
                    throw;
                }
            }
            finally
            {
                _mutationGate.Release();
            }
        });
    }

    private MenuItem AsyncItem(
        ContextMenu owner,
        string header,
        Func<Task> action,
        bool enabled = true,
        bool keepOpen = false,
        bool disableAfterClick = false)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            StaysOpenOnClick = keepOpen
        };
        item.Click += (_, args) =>
        {
            args.Handled = true;
            if (!keepOpen)
                owner.IsOpen = false;
            if (disableAfterClick)
                item.IsEnabled = false;
            DispatchMenuAction(owner, action);
        };
        return item;
    }

    private MenuItem AsyncCheckItem(ContextMenu owner, string header, bool selected, Func<bool, Task> action)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = selected,
            StaysOpenOnClick = true
        };
        item.Click += (_, args) =>
        {
            args.Handled = true;
            DispatchMenuAction(owner, () => action(item.IsChecked));
        };
        return item;
    }

    private void DispatchMenuAction(ContextMenu owner, Func<Task> action)
    {
        if (_actions.DispatchMenuActionAsync is { } dispatch)
        {
            _ = dispatch(() => _actions.RunEventAsync(action));
            return;
        }
        // Let the popup finish its input route before a mutation replaces the
        // card and its context menu. This also keeps modal picker failures on
        // the application's normal foreground-event error path.
        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(async () => await _actions.RunEventAsync(action)));
    }

    private static MenuItem Submenu(string header)
        => new() { Header = header };
}
