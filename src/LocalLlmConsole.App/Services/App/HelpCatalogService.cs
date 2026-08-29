namespace LocalLlmConsole.Services;

public sealed record HelpSectionDefinition(
    string Key,
    string Label,
    string Summary,
    string LabelKey,
    string SummaryKey);

public sealed record HelpActionDefinition(
    string Label,
    string Target);

public sealed record HelpArticleDefinition(
    string Id,
    string SectionKey,
    string Title,
    string Summary,
    IReadOnlyList<string> Details,
    IReadOnlyList<HelpActionDefinition> Actions,
    IReadOnlyList<string> Keywords);

public sealed record HelpSearchResult(
    string Query,
    HelpSectionDefinition ActiveSection,
    IReadOnlyList<HelpArticleDefinition> Articles)
{
    public bool IsSearch => !string.IsNullOrWhiteSpace(Query);
}

public sealed partial class HelpCatalogService
{
    public const string FirstSteps = "first-steps";

    private static readonly HelpSectionDefinition[] DefaultSections =
    [
        new(FirstSteps, "First Steps", "Install a runtime, add a model, and start serving.", "Help.FirstSteps.Label", "Help.FirstSteps.Summary"),
        new("overview", "Overview", "Loaded sessions, endpoints, metrics, and live status.", "Help.Overview.Label", "Help.Overview.Summary"),
        new("models", "Models", "GGUF files, profiles, groups, vision, and draft helpers.", "Help.Models.Label", "Help.Models.Summary"),
        new("runtimes", "Runtimes", "Install, register, build, and choose llama.cpp runtimes.", "Help.Runtimes.Label", "Help.Runtimes.Summary"),
        new("settings", "APIs & Settings", "Credentials, endpoints, gateway, network, and app behavior.", "Help.Settings.Label", "Help.Settings.Summary"),
        new("maintenance", "Troubleshooting", "Resolve load, authentication, download, build, and update problems.", "Help.Maintenance.Label", "Help.Maintenance.Summary")
    ];

    private static readonly HelpArticleDefinition[] DefaultArticles =
    [
        Article(
            "quick-start",
            FirstSteps,
            "Start a model in four steps",
            "Install a runtime, add a GGUF, save a profile, then load it from Overview.",
            [
                "Install a prebuilt Windows or WSL runtime from Runtimes.",
                "Download a GGUF from Hugging Face or scan/import one you already have.",
                "Select the model and runtime, adjust its launch settings, and save a named profile.",
                "Open Overview, select the model and profile, then choose Load."
            ],
            [Action("Open Runtimes", "runtime-download"), Action("Open Models", "model-download"), Action("Open Overview", "overview-load")],
            ["setup", "install", "first model", "getting started"]),

        Article(
            "manual-folders",
            FirstSteps,
            "Add models and runtimes manually",
            "Copy files into the configured folders, then let the Manager scan and register them.",
            [
                "Models: copy a .gguf anywhere under the configured models folder, then choose Scan Models Folder.",
                "Runtimes: put a folder containing llama-server.exe or llama-server under the configured runtimes folder, then scan it.",
                "Models kept elsewhere can be selected with Add model file without moving them. Valid ambiguous files require one explicit confirmation that persists across future scans; external runtimes can also be registered in place."
            ],
            [Action("Open Models", "models"), Action("Open Runtimes", "runtime-download")],
            ["copy gguf", "manual model", "manual runtime", "scan folder", "register folder"]),

        Article(
            "prebuilt-first",
            FirstSteps,
            "Prefer a prebuilt runtime",
            "Prebuilt packages are the shortest and most reliable route to a working endpoint.",
            [
                "Choose Windows or Linux/WSL, then the backend that matches your hardware.",
                "Use source builds only for a custom fork, branch, patch, or unsupported package combination."
            ],
            [Action("Open Runtimes", "runtime-download")],
            ["official package", "source build", "beginner"]),

        Article(
            "loaded-sessions",
            "overview",
            "Loaded sessions and endpoints",
            "Overview lists the shared gateway and every running direct model endpoint.",
            [
                "Each model session uses the port saved in its launch profile.",
                "Select a session row to show that model's status and metrics.",
                "Use Unload on the session row to stop that model explicitly."
            ],
            [Action("Open Overview", "loaded-sessions")],
            ["running model", "unload", "direct port", "session"]),

        Article(
            "endpoint-inspection",
            "overview",
            "Inspect an endpoint without generating text",
            "Open a compact report for health, model metadata, defaults, capabilities, and slots.",
            [
                "Double-click a loaded-session or gateway row, or select its endpoint link. Report text is selectable, and the toolbar copies the endpoint, the safe report, or the API key separately.",
                "Direct inspection reads /health, /v1/models, /props, and /slots.",
                "Gateway inspection reads /health, /v1/models, and /running."
            ],
            [Action("Open Overview", "loaded-sessions")],
            ["health", "props", "slots", "endpoint report", "inspect", "copy", "api key"]),

        Article(
            "metrics",
            "overview",
            "Read live metrics",
            "Customizable cards combine live status, runtime summaries, and available raw metrics.",
            [
                "Right-click a card to add or remove metrics, set an optional title, manage charts, remove it, or restore the default layout; drag or resize its visible border directly. Choose Lock beside Add card to keep the current card dimensions while resizing the window; cards wrap before shrinking and border resizing returns when you unlock them.",
                "Cards keep a minimum gap, and matching top or bottom edges align when adjacent cards are resized. Overview retains its cards, charts, and latest readings while you visit another page, so returning does not wait for a full dashboard rebuild.",
                "Charts are offered for compatible averages, totals, and raw gauges. Optional sensors such as VRAM temperature appear only when the GPU driver reports them. Unreliable per-poll generation, prompt, and speculative live rates are intentionally omitted.",
                "If loading stalls or throughput collapses, inspect the live runtime log first. Settings can place its latest entry at the top or bottom without changing the stored log."
            ],
            [Action("Open Overview", "overview"), Action("Open Logs", "logs")],
            ["tokens per second", "gpu", "kv cache", "slow", "telemetry"]),

        Article(
            "usage-history",
            "overview",
            "Understand usage metrics",
            "Metrics shows daily token activity, cache reuse, throughput, request counts, and combined historical GPU energy and estimated cost.",
            [
                "Input is evaluated prompt tokens plus prompt tokens reused from cache; output is generated tokens.",
                "Cache hit rate is cached input divided by all tracked input. Prompt and generation speeds use llama.cpp active-processing time. Request counts appear only when a runtime exposes compatible counters; unsupported values remain unavailable.",
                "Daily history starts after this feature is installed. Older lifetime totals are preserved but are not assigned to invented dates.",
                "Day boxes stay fixed in size while resizing the window reveals up to 24 calendar months. Choose Total, Input, Output, Cached, Requests, or GPU energy, then click tracked days to filter. Metrics shows combined historical GPU energy and its estimated cost; per-device history remains available to automation. Configure currency, day/night prices per kWh, and local night times in Settings. Optional Overview rows can be added before loading a model and show cumulative energy and estimated cost observed during the selected live runtime session. Cost covers measured GPU board energy only."
            ],
            [Action("Open Metrics", "lifetime")],
            ["lifetime", "daily usage", "token history", "cache hit", "cached prompt", "gpu energy", "electricity cost", "day rate", "night rate", "watts", "kwh", "metrics"]),

        Article(
            "add-models",
            "models",
            "Download, scan, or import a model",
            "The Models page supports Manager-owned downloads and external model registrations.",
            [
                "Hugging Face downloads are verified and registered automatically.",
                "Scan Models Folder reads GGUF metadata before narrow filename conventions and explains every ambiguous or invalid file it skips.",
                "Add model file selects a model anywhere, keeps it in place, and can persistently confirm a valid ambiguous main model; removing an external registration does not delete the file.",
                "Deleting a Manager-owned download can remove its managed model folder."
            ],
            [Action("Open Models", "model-download")],
            ["hugging face", "external model", "delete model", "ownership", "gguf"]),

        Article(
            "profiles-and-groups",
            "models",
            "Use profiles and groups",
            "Profiles save launch variants; groups apply retention policy to selected profiles.",
            [
                "Keep separate profiles for low memory, long context, vision, or different runtimes; right-click profile rows to add tray favourites, then right-click the tray icon to start, stop, or switch them.",
                "A group can pin sessions, inherit the global idle timeout, or use its own idle timeout.",
                "Group priority controls automatic idle-eviction order, not inference scheduling.",
                "A group load validates runtimes, ports, duplicate models, and aggregate VRAM before starting anything."
            ],
            [Action("Open Models", "launch-settings")],
            ["launch variant", "tray", "favourite", "quick start", "retention", "pinned", "idle timeout", "eviction", "multi model"]),

        Article(
            "vision-and-draft",
            "models",
            "Vision, draft, and MTP companions",
            "Companion auto-detection is deliberately limited to the main model's exact folder.",
            [
                "Vision normally uses a matching external mmproj/projector GGUF.",
                "Use Embedded vision only when the runtime and model package explicitly support it.",
                "Upstream draft-* modes use Draft model; compatible Atomic --mtp-head forks use MTP head.",
                "Embedded NextN/MTP metadata takes precedence over an external draft sidecar."
            ],
            [Action("Open Models", "launch-settings")],
            ["mmproj", "projector", "multimodal", "speculative", "nextn", "eagle3", "dflash", "dspark"]),

        Article(
            "install-runtime",
            "runtimes",
            "Install or register a runtime",
            "A runtime is a folder containing a compatible llama-server executable and its libraries.",
            [
                "Install an official prebuilt package when one matches your platform and backend.",
                "For manual Windows runtimes, keep llama-server.exe and companion DLLs together.",
                "For WSL runtimes, keep the Linux llama-server binary and shared libraries in their supplied layout.",
                "External runtime folders can be registered, but deletion protection remains stricter."
            ],
            [Action("Open Runtimes", "runtime-download")],
            ["llama-server.exe", "dll", "shared library", "custom runtime"]),

        Article(
            "choose-backend",
            "runtimes",
            "Choose a backend",
            "Use CPU for compatibility or select the GPU backend supported by your hardware and drivers.",
            [
                "CUDA targets NVIDIA GPUs, Vulkan commonly targets AMD or other Vulkan-capable GPUs, and SYCL targets Intel Arc.",
                "Windows and WSL packages are separate and are not interchangeable.",
                "If a GPU runtime cannot start, try the CPU package to separate model problems from driver/backend problems."
            ],
            [Action("Open Runtimes", "runtime-download")],
            ["cuda", "nvidia", "vulkan", "amd", "sycl", "intel arc", "cpu"]),

        Article(
            "runtime-trust",
            "runtimes",
            "Verify a runtime installation",
            "Managed runtimes record provenance and installed-file hashes; custom runtimes remain explicitly user-trusted.",
            [
                "Select an installed runtime to see its provider, repository, release, assets, checksum and signature status, install time, backend, and version.",
                "Use Verify to re-hash managed runtime files and detect changed or missing files.",
                "A legacy managed runtime needs to be reinstalled once before it has a file manifest.",
                "An unverified custom runtime was supplied manually and cannot be authenticated by the Manager."
            ],
            [Action("Open Runtimes", "runtime-download")],
            ["trust", "verify runtime", "hash", "sha256", "modified files", "missing files", "provenance", "custom runtime"]),

        Article(
            "source-builds",
            "runtimes",
            "Build a runtime from source",
            "Source work follows Check, Download, then Build and runs as a supervised job.",
            [
                "Use Tools > Windows or Tools > WSL Linux to verify the selected backend's prerequisites.",
                "A successful table build removes its downloaded source and resets the row to Check.",
                "On failure, inspect the job log before retrying or changing toolchains."
            ],
            [Action("Open Windows tools", "windows-tools"), Action("Open WSL tools", "wsl-tools"), Action("Open Logs", "logs")],
            ["cmake", "compiler", "toolchain", "build job", "source download", "cuda", "vulkan", "sycl"]),

        Article(
            "two-apis",
            "settings",
            "Understand the two APIs",
            "The Manager control API and model-serving API are separate surfaces with separate credentials.",
            [
                "llwmctl uses the authenticated loopback Manager control API at /api/v1/*.",
                "The control token is generated per Manager process, discovered automatically, and is not a user setting.",
                "The gateway and direct model endpoints expose OpenAI-compatible /v1/* routes.",
                "Model-serving routes use the model API key configured in Settings unless authentication is explicitly disabled in Local-only mode."
            ],
            [Action("Open Settings", "settings")],
            ["control api", "llwmctl", "model api", "credential", "token", "difference"]),

        Article(
            "connect-client",
            "settings",
            "Connect an OpenAI-compatible client",
            "Use the shared gateway for one stable address or a loaded model's direct endpoint.",
            [
                "Read GET /v1/models from the gateway, send the returned profile route as the model id, and use context_length plus meta to discover its configured context and available GGUF details.",
                "When API key auth is enabled, send the Settings model API key as Authorization: Bearer <key>; the gateway also accepts x-api-key. Omit credentials only for an explicitly unauthenticated Local-only endpoint.",
                "A direct endpoint serves only its loaded model; the gateway can load the requested saved profile on demand."
            ],
            [Action("Open Settings", "gateway-settings"), Action("Open Overview", "loaded-sessions")],
            ["openai", "base url", "bearer", "x-api-key", "v1 models", "client"]),

        Article(
            "network-and-key",
            "settings",
            "API key and LAN exposure",
            "LAN serving always requires a strong API key; Local only can explicitly allow unauthenticated access.",
            [
                "Set authentication to Disable for local browser or client testing. LAN exposure changes to Local only, the active key becomes empty, and the preserved key is restored when authentication is re-enabled.",
                "Local only binds serving endpoints to 127.0.0.1 and is the only mode that permits authentication to be disabled.",
                "Gateway LAN, Direct models LAN, and Gateway + direct LAN require authentication and expose only the selected model-serving surfaces.",
                "The Manager control API remains loopback-only."
            ],
            [Action("Open Settings", "settings")],
            ["401", "unauthorized", "lan", "0.0.0.0", "localhost", "api key", "authentication"]),

        Article(
            "ui-and-data",
            "settings",
            "App behavior and data",
            "Settings apply automatically and the workspace remains fixed for the running process.",
            [
                "The customizable Overview dashboard and UI switches change presentation without disabling telemetry, logs, metrics, or downloads.",
                "Use a card's right-click menu for metrics, an optional title, charts, and removal; resize from any visible side or corner, or use the dashboard Lock button to preserve every card's current dimensions. Hardware rows include independent CPU, RAM, VRAM, power, clock, core-temperature, and VRAM-temperature sensors when Windows or the GPU driver exposes them.",
                "Portable installs normally keep models, runtimes, state, cache, and logs under data beside the executable.",
                "Start with Windows and minimize behavior apply to the current Windows user."
            ],
            [Action("Open Settings", "settings")],
            ["workspace", "portable data", "hide metrics", "dashboard", "vram", "power", "temperature", "startup", "tray"]),

        Article(
            "model-will-not-load",
            "maintenance",
            "A model will not load",
            "The final runtime-log lines usually identify an invalid flag, missing file, port conflict, or memory failure.",
            [
                "Confirm the selected runtime still exists and matches Windows or WSL mode.",
                "Set experimental options back to Auto, None, Off, 0, -1, or blank as appropriate.",
                "For GPU memory errors, reduce context, GPU layers, batch size, or micro-batch size.",
                "Confirm every simultaneously loaded profile uses a unique direct port."
            ],
            [Action("Open Logs", "logs"), Action("Open Models", "launch-settings")],
            ["failed to load", "out of memory", "oom", "unsupported flag", "port conflict", "missing file"]),

        Article(
            "authentication-errors",
            "maintenance",
            "401, connection, or gateway errors",
            "Check the endpoint, credential type, gateway state, and requested model route in that order.",
            [
                "With API key auth enabled, a 401 from /v1/* means the model API key is missing or invalid. In Local-only unauthenticated mode, clients omit the credential and readiness verifies that the open endpoint responds as configured.",
                "Do not use the llwmctl control token as the model API key; they are different credentials.",
                "Use the exact model id returned by the gateway's GET /v1/models response.",
                "If the port refuses connections, confirm the gateway or direct session is listed on Overview."
            ],
            [Action("Open Settings", "gateway-settings"), Action("Open Overview", "loaded-sessions"), Action("Open Logs", "logs")],
            ["unauthorized", "connection refused", "404", "model not found", "wrong key", "gateway"]),

        Article(
            "download-build-failures",
            "maintenance",
            "Downloads or builds fail",
            "Use the job or application log to identify network, checksum, disk, toolchain, or permission failures.",
            [
                "Keep failed job logs until you have identified the cause.",
                "Check free disk space, proxy/VPN behavior, antivirus quarantine, and write permission to the workspace.",
                "For source builds, verify the exact Windows or WSL backend prerequisites before retrying."
            ],
            [Action("Open Logs", "logs"), Action("Open Runtimes", "runtime-download")],
            ["checksum", "disk full", "permission denied", "network", "download interrupted", "job"]),

        Article(
            "updates-and-recovery",
            "maintenance",
            "Updates, logs, and recovery",
            "Updates preserve application data and verify staged artifacts before replacement.",
            [
                "Installer update, repair, and normal uninstall preserve data unless removal is explicitly selected.",
                "A portable update requires its matching SHA-256 companion and rolls back incomplete replacement.",
                "Use Logs for app, runtime, job, and bounded redacted Control API activity.",
                "Create Diagnostics Bundle collects safe inventory, environment details, runtime trust state, and sanitized log tails. Review the ZIP before sharing it."
            ],
            [Action("Check Updates", "updates"), Action("Open Logs", "logs"), Action("Open Metrics", "lifetime")],
            ["update failed", "sha256", "rollback", "preserve data", "control api log"])
    ];

}
