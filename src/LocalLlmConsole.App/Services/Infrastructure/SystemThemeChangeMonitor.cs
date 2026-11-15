namespace LocalLlmConsole;

/// <summary>
/// Watches Windows user-preference changes and requests a theme reapply when the
/// app follows the System theme and the Windows app light/dark mode has changed.
/// </summary>
internal sealed class SystemThemeChangeMonitor : IDisposable
{
    private readonly Func<string> _selectedMode;
    private readonly Func<bool> _readSystemDark;
    private readonly Action _reapply;
    private readonly Action<UserPreferenceChangedEventHandler> _unsubscribe;
    private readonly UserPreferenceChangedEventHandler _handler;
    private readonly object _gate = new();
    private bool? _lastSystemDark;
    private bool _disposed;

    public SystemThemeChangeMonitor(
        Func<string> selectedMode,
        Func<bool> readSystemDark,
        Action reapply,
        Action<UserPreferenceChangedEventHandler> subscribe,
        Action<UserPreferenceChangedEventHandler> unsubscribe)
    {
        _selectedMode = selectedMode;
        _readSystemDark = readSystemDark;
        _reapply = reapply;
        _unsubscribe = unsubscribe;
        _handler = (_, args) => OnPreferenceChanged(args.Category);
        subscribe(_handler);
    }

    public static SystemThemeChangeMonitor ForWindows(
        Func<string> selectedMode,
        Func<bool> readSystemDark,
        Action reapply)
        => new(
            selectedMode,
            readSystemDark,
            reapply,
            handler => SystemEvents.UserPreferenceChanged += handler,
            handler => SystemEvents.UserPreferenceChanged -= handler);

    /// <summary>Records the Windows light/dark value the current theme was applied with.</summary>
    public void Observe(bool systemDark)
    {
        lock (_gate)
            _lastSystemDark = systemDark;
    }

    private void OnPreferenceChanged(UserPreferenceCategory category)
    {
        if (category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
        if (!string.Equals(AppPreferenceService.ThemeMode(_selectedMode()), "system", StringComparison.Ordinal)) return;

        var systemDark = _readSystemDark();
        lock (_gate)
        {
            if (_disposed || _lastSystemDark == systemDark) return;
            _lastSystemDark = systemDark;
        }
        _reapply();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _unsubscribe(_handler);
    }
}
