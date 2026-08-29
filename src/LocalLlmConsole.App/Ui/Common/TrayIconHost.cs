using Forms = System.Windows.Forms;

namespace LocalLlmConsole;

public sealed class TrayIconHost : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;
    private readonly string _appDisplayName;

    private TrayIconHost(
        Forms.NotifyIcon icon,
        System.Drawing.Icon? ownedIcon,
        string appDisplayName)
    {
        _icon = icon;
        _ownedIcon = ownedIcon;
        _appDisplayName = appDisplayName;
    }

    public bool Visible
    {
        get => _icon.Visible;
        set => _icon.Visible = value;
    }

    public static TrayIconHost Create(
        string appDisplayName,
        Action show,
        Action showMenu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDisplayName);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(showMenu);

        var (icon, ownedIcon) = CreateIcon();
        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = appDisplayName,
            Visible = false
        };
        notifyIcon.DoubleClick += (_, _) => show();
        notifyIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Right)
                showMenu();
        };
        return new TrayIconHost(notifyIcon, ownedIcon, appDisplayName);
    }

    public void ShowStillRunningHint(string message)
        => _icon.ShowBalloonTip(
            1800,
            _appDisplayName,
            message,
            Forms.ToolTipIcon.Info);

    public void ShowNotification(string message, bool error = false)
        => _icon.ShowBalloonTip(
            error ? 5000 : 2500,
            _appDisplayName,
            message,
            error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon?.Dispose();
    }

    private static (System.Drawing.Icon Icon, System.Drawing.Icon? OwnedIcon) CreateIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(executable);
                if (icon is not null) return (icon, icon);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not extract tray icon from the executable: {ex.Message}");
        }

        return (System.Drawing.SystemIcons.Application, null);
    }
}
