using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed record TrayProfileMenuActions(
    Func<TrayProfileMenuEntry, Task> ExecuteProfileAsync,
    Action ShowWindow,
    Action Exit);

public sealed class TrayProfileMenuView
{
    private readonly Action<TrayProfileMenuSnapshot> _refresh;

    internal TrayProfileMenuView(ContextMenu menu, Action<TrayProfileMenuSnapshot> refresh)
    {
        Menu = menu;
        _refresh = refresh;
    }

    public ContextMenu Menu { get; }

    public void Refresh(TrayProfileMenuSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _refresh(snapshot);
    }
}
