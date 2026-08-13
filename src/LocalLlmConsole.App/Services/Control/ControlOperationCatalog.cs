namespace LocalLlmConsole.Services;

public sealed record LocalControlOperation(
    string Name,
    string Description,
    bool RequiresConfirmation = false,
    bool MayStopApplication = false,
    IReadOnlyList<LocalControlOperationParameter>? Parameters = null);

public sealed record LocalControlOperationParameter(
    string Name,
    string Description,
    bool Required = false,
    string Type = "string",
    string DefaultValue = "");

public static class ControlOperationCatalog
{
    private static readonly LocalControlOperation[] Operations =
    [
        new("app.refresh", "Refresh every application view and managed catalog."),
        new("app.shutdown", "Close llama.cpp Windows Manager.", true, true),
        new("ui.navigate", "Show an application page allowed by the control surface.", Parameters:
        [
            P("page", "Page name: overview, models, runtimes, windows, wsl, settings, lifetime, logs, updates, or help.", required: true)
        ]),
        new("gateway.restart", "Restart the configured model auto-load gateway."),
        new("gateway.stop", "Stop the model auto-load gateway."),
        new("cache.plan", "Inspect whether the application cache can be cleared."),
        new("cache.clear", "Delete safe disposable application cache contents.", true),
        new("logs.delete", "Delete one inactive application/runtime log.", true, Parameters:
        [
            P("file", "Inactive log file name.", required: true)
        ]),
        new("logs.delete-all", "Delete all inactive application/runtime logs.", true),
        new("lifetime.list", "List persisted per-model lifetime token totals."),
        new("lifetime.delete", "Delete lifetime token totals for one model.", true, Parameters:
        [
            P("model", "Model id or name.", required: true)
        ]),
        new("lifetime.delete-all", "Delete all lifetime token totals.", true),
        new("downloads.delete", "Delete a model-download history job and eligible partial files.", true, Parameters:
        [
            P("job", "Download job id.", required: true)
        ]),
        new("runtime.catalog", "List registered runtimes, package presets, source presets, and downloaded sources."),
        new("runtime-repository.add", "Add a safe HTTPS custom runtime source repository.", Parameters:
        [
            P("label", "Display label.", required: true),
            P("repo", "HTTPS Git repository URL.", required: true),
            P("branch", "Optional branch or tag."),
            P("backend", "Backend: CPU, CUDA, Vulkan, or SYCL.", required: true)
        ]),
        new("runtime.delete", "Delete a runtime registration or Manager-owned runtime files.", true, true, Id("runtime", "Runtime id or name.")),
        new("runtime-package.install", "Install a prebuilt runtime package.", true, Parameters: Id("preset", "Runtime package preset id or label.")),
        new("runtime-package.check", "Check a prebuilt runtime package for updates.", Parameters: Id("preset", "Runtime package preset id or label.")),
        new("runtime-package.delete", "Delete installed copies of a prebuilt runtime package.", true, Parameters: Id("preset", "Runtime package preset id or label.")),
        new("runtime-source.download", "Download a llama.cpp runtime source repository.", true, Parameters: Id("preset", "Runtime source preset id or label.")),
        new("runtime-source.check", "Check a downloaded runtime source repository for updates.", Parameters: Id("preset", "Runtime source preset id or label.")),
        new("runtime-source.delete", "Delete one downloaded runtime source tree.", true, Parameters: Id("source", "Source directory, preset id, or label.")),
        new("runtime-build.start", "Build or update a managed llama.cpp runtime.", true, Parameters:
        [
            P("preset", "Runtime build preset id or label.", required: true),
            P("update", "Update source before building.", type: "boolean", defaultValue: "false"),
            P("source", "Optional existing source directory, preset id, or label.")
        ]),
        new("runtime-build.delete", "Delete all sources/builds for a runtime preset.", true, Parameters: Id("preset", "Runtime build preset id or label.")),
        new("runtime-job.cancel", "Cancel an active runtime package/source/build job.", true, Parameters: Id("job", "Runtime job id.")),
        new("runtime-job.retry", "Retry an eligible runtime build job.", true, Parameters: Id("job", "Runtime job id.")),
        new("runtime-job.clear", "Clear a completed runtime job and its log.", true, Parameters: Id("job", "Runtime job id.")),
        new("windows.status", "Detect native Windows llama.cpp build tools."),
        new("windows.setup", "Launch a Windows CPU, CUDA, Vulkan, or SYCL tool setup.", true, Parameters: Id("action", "Setup action: CPU, CUDA, Vulkan, or SYCL.")),
        new("wsl.status", "Detect WSL distributions and tools for the selected Ubuntu distro."),
        new("wsl.select", "Select the Ubuntu WSL distribution used by the Manager.", Parameters: Id("distro", "Installed WSL distribution name.")),
        new("wsl.setup", "Launch a supported WSL/Ubuntu install, update, or removal action.", true, true,
        [
            P("action", "Action returned by capabilities.", required: true),
            P("distro", "Optional WSL distribution name.")
        ]),
        new("updates.check", "Check the configured GitHub release feed for an app update."),
        new("updates.install", "Stage and install the latest available app update.", true, true)
    ];

    public static IReadOnlyList<LocalControlOperation> All => Operations;

    public static LocalControlOperation Resolve(string name)
        => Operations.FirstOrDefault(operation => operation.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Control operation '{name}' was not found.");

    private static LocalControlOperationParameter P(
        string name,
        string description,
        bool required = false,
        string type = "string",
        string defaultValue = "")
        => new(name, description, required, type, defaultValue);

    private static IReadOnlyList<LocalControlOperationParameter> Id(string name, string description)
        => [P(name, description, required: true)];
}
