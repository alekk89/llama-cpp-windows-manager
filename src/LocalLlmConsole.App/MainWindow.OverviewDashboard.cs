namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task PersistOverviewDashboardLayoutAsync(OverviewDashboardLayout layout)
    {
        var updated = OverviewDashboardLayoutPolicy.WithLayout(_settings, layout);
        var settingsApplication = AppServices.SettingsApplication;
        Require(settingsApplication);
        _settings = await settingsApplication!.PersistAsync(updated);
        SetStatus(Loc.T("Dashboard.SavedStatus"));
    }
}
