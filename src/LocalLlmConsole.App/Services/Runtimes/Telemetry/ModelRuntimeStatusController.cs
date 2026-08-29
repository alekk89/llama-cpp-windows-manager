namespace LocalLlmConsole.Services;

public sealed class ModelRuntimeStatusController
{
    private readonly ModelRuntimeStatusTracker _tracker;
    private readonly IUiTimerFactory _timerFactory;
    private IUiTimer? _loadingTimer;
    private IUiTimer? _loadedStatusTimer;

    public ModelRuntimeStatusController(
        ModelRuntimeStatusTracker tracker,
        IUiTimerFactory timerFactory)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
    }

    public bool HasLoadedStatusTimer => _loadedStatusTimer is not null;

    public void StartLoading(
        string modelId,
        string modelName,
        string endpointDisplay,
        DateTimeOffset now,
        Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        StopLoadedStatusTimer();
        StopLoadingTimer();
        _tracker.StartLoading(modelId, modelName, endpointDisplay, now);
        _loadingTimer = _timerFactory.Create(TimeSpan.FromSeconds(1));
        _loadingTimer.Tick += (_, _) => tick();
        tick();
        _loadingTimer.Start();
    }

    public ModelRuntimeStatusDisplay? LoadingStatusFor(string? selectedModelId, DateTimeOffset now)
        => _tracker.LoadingStatusFor(selectedModelId, now);

    public ModelRuntimeStatusDisplay StatusFor(string? selectedModelId, string fallbackModelStatus, DateTimeOffset now)
        => _tracker.StatusFor(selectedModelId, fallbackModelStatus, now);

    public bool IsLoadingModel(string modelId)
        => _tracker.IsLoadingModel(modelId);

    public ModelRuntimeStatusDisplay? LoadedStatusFor(string? selectedModelId)
        => _tracker.LoadedStatusFor(selectedModelId);

    public ModelRuntimeStatusDisplay? StopLoading(
        bool showLoadedDuration,
        string loadedModelName,
        DateTimeOffset now)
    {
        var hadLoadingStatus = _tracker.HasLoadingStatus;
        if (hadLoadingStatus)
            StopLoadedStatusTimer();
        StopLoadingTimer();
        if (!hadLoadingStatus)
            return null;

        return _tracker.StopLoading(showLoadedDuration, loadedModelName, now);
    }

    public void StartLoadedStatusTimer(Func<Task> expiredAsync)
    {
        ArgumentNullException.ThrowIfNull(expiredAsync);

        StopLoadedStatusTimerCore(clearLoadedStatus: false);
        _loadedStatusTimer = _timerFactory.Create(TimeSpan.FromSeconds(4));
        _loadedStatusTimer.Tick += async (_, _) => await expiredAsync();
        _loadedStatusTimer.Start();
    }

    public void StopLoadedStatusTimer(bool clearLoadedStatus = true)
        => StopLoadedStatusTimerCore(clearLoadedStatus);

    private void StopLoadingTimer()
    {
        _loadingTimer?.Stop();
        _loadingTimer = null;
    }

    private void StopLoadedStatusTimerCore(bool clearLoadedStatus)
    {
        _loadedStatusTimer?.Stop();
        _loadedStatusTimer = null;
        if (clearLoadedStatus)
            _tracker.ClearLoadedStatus();
    }
}
