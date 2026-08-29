namespace LocalLlmConsole.Services;

public sealed record RuntimePackageUpdateCheckRequest(
    RuntimePackagePreset Preset,
    RuntimePackageInventory Inventory,
    string CudaPackagePreference,
    DateTimeOffset CheckedAt,
    CancellationToken CancellationToken = default);

public sealed record RuntimePackageUpdateCheckOutcome(
    RuntimePackageCheckResult Result,
    RuntimePackageRelease? Release,
    bool AssetUnavailable);

public sealed class RuntimePackageUpdateCheckService
{
    private readonly HttpClient _client;
    private readonly RuntimePackageStatusService _status;

    public RuntimePackageUpdateCheckService(HttpClient client, RuntimePackageStatusService status)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public async Task<RuntimePackageUpdateCheckOutcome> CheckAsync(RuntimePackageUpdateCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        RuntimePackageRelease? release = null;
        try
        {
            release = await RuntimePackageReleaseClient.FetchLatestReleaseAsync(_client, request.Preset, request.CancellationToken);
            var selection = RuntimePackageAssetSelector.SelectAssets(request.Preset, release, request.CudaPackagePreference);
            var result = _status.EvaluateAvailableRelease(request.Inventory, release, selection, request.CheckedAt);
            return new RuntimePackageUpdateCheckOutcome(result, release, AssetUnavailable: false);
        }
        catch (RuntimePackageAssetUnavailableException ex)
        {
            var result = _status.EvaluateUnavailableRelease(request.Inventory, release, ex.Message, request.CheckedAt);
            return new RuntimePackageUpdateCheckOutcome(result, release, AssetUnavailable: true);
        }
    }
}
