namespace LocalLlmConsole;

public static class SelectorFavoriteContextAction
{
    public static DataGridRowContextAction Create<T>(
        Func<T, bool> isFavorite,
        Func<T, bool> canExecute,
        Func<T, Task> executeAsync)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(isFavorite);
        ArgumentNullException.ThrowIfNull(canExecute);
        ArgumentNullException.ThrowIfNull(executeAsync);
        return new DataGridRowContextAction(
            row => isFavorite((T)row)
                ? Loc.T("Selector.RemoveFavoriteTooltip")
                : Loc.T("Selector.AddFavoriteTooltip"),
            row => row is T typed && canExecute(typed),
            row => executeAsync((T)row),
            IsVisible: row => row is T typed && canExecute(typed));
    }
}
