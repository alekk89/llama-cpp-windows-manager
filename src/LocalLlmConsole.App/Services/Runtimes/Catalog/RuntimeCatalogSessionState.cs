namespace LocalLlmConsole.Services;

public sealed class RuntimeCatalogSessionState
{
    private readonly HashSet<string> _autoScannedRuntimeRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimePackageUpdateState> _runtimePackageUpdateStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeUpdateState> _runtimeUpdateStates = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, RuntimeUpdateState> RuntimeUpdateStates => _runtimeUpdateStates;

    public IReadOnlyDictionary<string, RuntimePackageUpdateState> RuntimePackageUpdateStates => _runtimePackageUpdateStates;

    public bool TryMarkRuntimeRootScanned(string runtimeRoot, out string fullPath)
    {
        fullPath = Path.GetFullPath(runtimeRoot);
        return _autoScannedRuntimeRoots.Add(fullPath);
    }

    public void MarkRuntimeRootScanned(string runtimeRoot)
    {
        TryMarkRuntimeRootScanned(runtimeRoot, out _);
    }

    public RuntimeUpdateState SetRuntimeUpdateState(string presetId, RuntimeUpdateState state)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            throw new ArgumentException("Preset id is required.", nameof(presetId));

        _runtimeUpdateStates[presetId] = state;
        return state;
    }

    public RuntimePackageUpdateState SetRuntimePackageUpdateState(string presetId, RuntimePackageUpdateState state)
    {
        if (string.IsNullOrWhiteSpace(presetId))
            throw new ArgumentException("Preset id is required.", nameof(presetId));

        _runtimePackageUpdateStates[presetId] = state;
        return state;
    }

    public void ClearRuntimePackageUpdateStates()
    {
        _runtimePackageUpdateStates.Clear();
    }

    public bool RemoveRuntimePackageUpdateState(string presetId)
        => !string.IsNullOrWhiteSpace(presetId) && _runtimePackageUpdateStates.Remove(presetId);
}
