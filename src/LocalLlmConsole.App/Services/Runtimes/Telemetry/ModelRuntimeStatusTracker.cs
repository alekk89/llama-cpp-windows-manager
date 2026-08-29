namespace LocalLlmConsole.Services;

public enum ModelRuntimeStatusKind
{
    Fallback,
    Loading,
    Loaded
}

public sealed record ModelRuntimeStatusDisplay(
    string ModelId,
    string MetricText,
    ModelRuntimeStatusKind Kind,
    string? StatusText = null);

public sealed class ModelRuntimeStatusTracker
{
    private DateTimeOffset _loadingStartedAt;
    private string _loadingModelId = "";
    private string _loadingModelName = "";
    private string _loadingEndpoint = "";
    private string _loadedStatusModelId = "";
    private string _loadedStatusText = "";

    public bool HasLoadingStatus => !string.IsNullOrWhiteSpace(_loadingModelName);

    public void StartLoading(string modelId, string modelName, string endpointDisplay, DateTimeOffset now)
    {
        ClearLoadedStatus();
        _loadingModelId = modelId ?? "";
        _loadingModelName = modelName ?? "";
        _loadingEndpoint = endpointDisplay ?? "";
        _loadingStartedAt = now;
    }

    public ModelRuntimeStatusDisplay? LoadingStatusFor(string? selectedModelId, DateTimeOffset now)
    {
        if (!HasLoadingStatus || !AppliesToSelectedModel(selectedModelId, _loadingModelId))
            return null;

        var elapsed = DisplayFormatService.Elapsed(now - _loadingStartedAt);
        return new ModelRuntimeStatusDisplay(
            _loadingModelId,
            $"Loading Model: {_loadingModelName}\nLoading Time: {elapsed}",
            ModelRuntimeStatusKind.Loading,
            $"Loading {_loadingModelName} at {_loadingEndpoint}.");
    }

    public ModelRuntimeStatusDisplay? StopLoading(
        bool showLoadedDuration,
        string loadedModelName,
        DateTimeOffset now)
    {
        var hadLoadingStatus = HasLoadingStatus;
        if (!hadLoadingStatus)
            return null;

        ClearLoadedStatus();

        var elapsed = now - _loadingStartedAt;
        var modelId = _loadingModelId;
        var modelName = string.IsNullOrWhiteSpace(loadedModelName) ? _loadingModelName : loadedModelName;

        _loadingModelId = "";
        _loadingModelName = "";
        _loadingEndpoint = "";

        if (!showLoadedDuration || !hadLoadingStatus || string.IsNullOrWhiteSpace(modelName))
            return null;

        _loadedStatusModelId = modelId;
        _loadedStatusText = $"Loaded Model: {modelName}\nLoading Time: {DisplayFormatService.Elapsed(elapsed)}";
        return new ModelRuntimeStatusDisplay(_loadedStatusModelId, _loadedStatusText, ModelRuntimeStatusKind.Loaded);
    }

    public bool IsLoadingModel(string modelId)
        => HasLoadingStatus && string.Equals(_loadingModelId, modelId, StringComparison.OrdinalIgnoreCase);

    public ModelRuntimeStatusDisplay? LoadedStatusFor(string? selectedModelId)
    {
        if (string.IsNullOrWhiteSpace(_loadedStatusText)
            || !AppliesToSelectedModel(selectedModelId, _loadedStatusModelId))
            return null;

        return new ModelRuntimeStatusDisplay(_loadedStatusModelId, _loadedStatusText, ModelRuntimeStatusKind.Loaded);
    }

    public ModelRuntimeStatusDisplay StatusFor(string? selectedModelId, string fallbackModelStatus, DateTimeOffset now)
        => LoadingStatusFor(selectedModelId, now)
            ?? LoadedStatusFor(selectedModelId)
            ?? new ModelRuntimeStatusDisplay(selectedModelId ?? "", fallbackModelStatus, ModelRuntimeStatusKind.Fallback);

    public void ClearLoadedStatus()
    {
        _loadedStatusModelId = "";
        _loadedStatusText = "";
    }

    private static bool AppliesToSelectedModel(string? selectedModelId, string statusModelId)
        => string.IsNullOrWhiteSpace(selectedModelId)
            || string.Equals(selectedModelId, statusModelId, StringComparison.OrdinalIgnoreCase);
}
