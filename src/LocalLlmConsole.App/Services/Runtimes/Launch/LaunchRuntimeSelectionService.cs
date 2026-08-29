namespace LocalLlmConsole.Services;

public sealed record LaunchRuntimeSelectorState(
    IReadOnlyList<RuntimeRecord> Runtimes,
    string? MissingRuntimeId,
    string? SelectedRuntimeId);

public sealed class LaunchRuntimeSelectionService
{
    public LaunchRuntimeSelectorState BuildSelectorState(
        IReadOnlyList<RuntimeRecord> runtimes,
        string? selectedRuntimeId)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        var available = RuntimeAvailabilityService.AvailableRuntimes(runtimes);

        var requestedRuntimeId = (selectedRuntimeId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(requestedRuntimeId))
        {
            var match = available.FirstOrDefault(runtime => string.Equals(runtime.Id, requestedRuntimeId, StringComparison.OrdinalIgnoreCase));
            return match is null
                ? new LaunchRuntimeSelectorState(available, requestedRuntimeId, requestedRuntimeId)
                : new LaunchRuntimeSelectorState(available, null, match.Id);
        }

        return new LaunchRuntimeSelectorState(available, null, available.FirstOrDefault()?.Id);
    }

    public RuntimeRecord? Resolve(
        IReadOnlyList<RuntimeRecord> runtimes,
        string? runtimeId,
        RuntimeRecord? fallbackRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        if (!string.IsNullOrWhiteSpace(runtimeId))
            return runtimes.FirstOrDefault(runtime => string.Equals(runtime.Id, runtimeId, StringComparison.OrdinalIgnoreCase)
                && RuntimeAvailabilityService.IsAvailable(runtime));

        if (fallbackRuntime is not null && RuntimeAvailabilityService.IsAvailable(fallbackRuntime))
            return fallbackRuntime;
        return runtimes.FirstOrDefault(RuntimeAvailabilityService.IsAvailable);
    }

    public string MissingRuntimeStatus(IReadOnlyList<RuntimeRecord> runtimes, string? runtimeId)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        if (!string.IsNullOrWhiteSpace(runtimeId))
        {
            var registered = runtimes.FirstOrDefault(runtime => string.Equals(runtime.Id, runtimeId, StringComparison.OrdinalIgnoreCase));
            if (registered is not null)
            {
                var availability = RuntimeAvailabilityService.Inspect(registered);
                if (!availability.IsAvailable)
                    return $"Saved runtime '{registered.Name}' is unavailable. {availability.Reason} Repair or reinstall it, or choose another runtime and save the model profile.";
            }
        }

        if (!runtimes.Any(RuntimeAvailabilityService.IsAvailable))
            return "Register a llama.cpp runtime first.";

        if (!string.IsNullOrWhiteSpace(runtimeId))
            return $"Saved runtime '{runtimeId}' is missing. Choose another runtime and save the model profile.";

        return "Choose a llama.cpp runtime before loading the model.";
    }
}
