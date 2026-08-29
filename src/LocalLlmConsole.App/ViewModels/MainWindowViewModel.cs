namespace LocalLlmConsole.ViewModels;

public sealed class MainWindowViewModel : ObservableViewModel
{
    private string _currentPage = "Overview";
    private string _statusText = Localization.Loc.T("Status.Starting");
    private bool _isBusy;

    public OverviewPageViewModel Overview { get; } = new();
    public ModelsPageViewModel Models { get; } = new();
    public RuntimesPageViewModel Runtimes { get; } = new();
    public RuntimePackagesPageViewModel RuntimePackages { get; } = new();
    public RuntimeBuildsPageViewModel RuntimeBuilds { get; } = new();
    public LifetimeMetricsViewModel LifetimeMetrics { get; } = new();
    public WindowsPageViewModel Windows { get; } = new();
    public WslLinuxPageViewModel WslLinux { get; } = new();
    public HuggingFacePageViewModel HuggingFace { get; } = new();
    public LogsViewModel Logs { get; } = new();
    public RuntimeMetricsViewModel RuntimeMetrics { get; } = new();
    public SettingsPageViewModel Settings { get; } = new();
    public LaunchSettingsViewModel LaunchSettings { get; } = new();
    public UpdatesPageViewModel Updates { get; } = new();

    public string CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
                OnPropertyChanged(nameof(DisplayStatusText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string DisplayStatusText => string.IsNullOrWhiteSpace(StatusText) ? Localization.Loc.T("Status.Ready") : StatusText;

    public void SetStatus(string text) => StatusText = text ?? "";

    public bool TryBeginBusy(out string busyMessage)
    {
        busyMessage = "";
        if (IsBusy)
        {
            busyMessage = string.IsNullOrWhiteSpace(StatusText)
                ? Localization.Loc.T("Status.PleaseWait")
                : Localization.Loc.T("Status.PleaseWaitFor", StatusText);
            return false;
        }

        IsBusy = true;
        return true;
    }

    public bool EndBusy()
    {
        if (!IsBusy) return false;
        IsBusy = false;
        return true;
    }
}
