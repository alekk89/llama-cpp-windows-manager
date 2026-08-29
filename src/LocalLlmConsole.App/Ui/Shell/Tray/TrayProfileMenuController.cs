using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LocalLlmConsole;

public sealed record TrayProfileMenuControllerActions(
    Func<TrayProfileMenuEntry, Task> ExecuteProfileAsync,
    Func<Exception, Task> ReportErrorAsync,
    Action ShowWindow,
    Action Exit);

public sealed class TrayProfileMenuController : IDisposable
{
    private readonly TrayProfileMenuApplicationService _application;
    private readonly TrayProfileMenuControllerActions _actions;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private TrayProfileMenuView? _view;
    private TrayProfileMenuSnapshot? _snapshot;
    private bool _disposed;

    public TrayProfileMenuController(
        TrayProfileMenuApplicationService application,
        TrayProfileMenuControllerActions actions)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public async Task OpenAsync(FrameworkElement placementTarget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(placementTarget);
        try
        {
            var snapshot = await _application.BuildSnapshotAsync();
            Close();
            var view = TrayProfileMenuFactory.CreateView(
                snapshot,
                new TrayProfileMenuActions(ExecuteProfileAsync, _actions.ShowWindow, _actions.Exit));
            var menu = view.Menu;
            menu.Placement = PlacementMode.MousePoint;
            menu.PlacementTarget = placementTarget;
            menu.StaysOpen = false;
            menu.Opened += (_, _) => TrayPopupActivationService.ActivateForOutsideDismissal(menu);
            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_view, view))
                {
                    _view = null;
                    _snapshot = null;
                }
            };
            _view = view;
            _snapshot = snapshot;
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            await _actions.ReportErrorAsync(ex);
        }
    }

    public void Close()
    {
        if (_view is null) return;
        _view.Menu.IsOpen = false;
        _view = null;
        _snapshot = null;
    }

    public async Task RefreshIfOpenAsync()
    {
        var view = _view;
        if (view?.Menu.IsOpen != true) return;
        var snapshot = await _application.BuildSnapshotAsync();
        if (!ReferenceEquals(_view, view) || !view.Menu.IsOpen) return;
        _snapshot = snapshot;
        view.Refresh(snapshot);
    }

    private async Task ExecuteProfileAsync(TrayProfileMenuEntry entry)
    {
        if (!await _actionGate.WaitAsync(0)) return;
        try
        {
            await System.Windows.Threading.Dispatcher.Yield(
                System.Windows.Threading.DispatcherPriority.Background);
            ShowTransition(entry);
            await _actions.ExecuteProfileAsync(entry);
        }
        catch (Exception ex)
        {
            await _actions.ReportErrorAsync(ex);
        }
        finally
        {
            try
            {
                await RefreshIfOpenAsync();
            }
            catch (Exception ex)
            {
                await _actions.ReportErrorAsync(ex);
            }
            _actionGate.Release();
        }
    }

    private void ShowTransition(TrayProfileMenuEntry selected)
    {
        if (_snapshot is null || _view?.Menu.IsOpen != true) return;
        var transition = selected.Action == TrayProfileActionKind.Stop
            ? TrayProfileActionKind.Stopping
            : TrayProfileActionKind.Loading;
        TrayProfileMenuEntry Update(TrayProfileMenuEntry entry)
            => entry.Model.Id.Equals(selected.Model.Id, StringComparison.OrdinalIgnoreCase)
                ? entry with { Action = transition, CanExecute = false }
                : entry with { CanExecute = false };

        _snapshot = new TrayProfileMenuSnapshot(
            _snapshot.Favorites.Select(Update).ToArray(),
            _snapshot.Models
                .Select(model => model with { Profiles = model.Profiles.Select(Update).ToArray() })
                .ToArray());
        _view.Refresh(_snapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        _actionGate.Dispose();
    }
}
