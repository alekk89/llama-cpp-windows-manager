# Local control API and `llwmctl`

Last reviewed: 2026-09-01

The Manager exposes an authenticated, loopback-only control API for local agents and automation. This API controls the desktop application's managed state; it is separate from the OpenAI-compatible model-serving gateway and direct model ports.

Use `llwmctl` instead of calling the HTTP API directly. The CLI discovers the active Manager instance, decrypts its current-user DPAPI session token, sends the request, and prints structured JSON.

Portable releases also embed `llwmctl.exe`, `AGENTS.md`, `agent.md`, this reference, `LICENSE`, and third-party/.NET notices in the main executable. The Manager verifies and restores those sidecars beside itself at startup, so an executable-only update from an older release acquires the complete control surface and compliance notices automatically. `LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only` runs that bootstrap and exits without opening the UI.

## Operator interface and API logs

The Manager intentionally does not include a visual API command console. Use `llwmctl` so requests go to the running desktop application through authenticated loopback transport and receive the same validation, self-identification, self-preservation, and confirmation handling as any other control client.

Each routed request creates one bounded, redacted audit entry in `<workspace>/logs/control-api.log`. The in-app **Logs** page classifies this file as Type **Control API**. Entries contain only the HTTP method, route path without its query string, result status, and elapsed time. Request bodies, query values, bearer credentials, model-serving API keys, and response bodies are never written to this log. Its size follows the app's maximum log-file-size setting.

## Discovery and authentication

At startup the Manager writes a discovery document to:

```text
%LocalAppData%\llama.cpp Windows Manager\control.json
<workspace>\state\control.json
```

The document contains the current process ID, selected localhost port, workspace, and a DPAPI-protected per-process bearer token. The files are removed on normal shutdown. The control listener binds only to `127.0.0.1`, checks the `Host` and browser `Origin`, requires authentication for `/api/*` except health, limits JSON bodies to 1 MiB, and never uses the model-serving API key as its control credential.

Override discovery when necessary:

```powershell
llwmctl status --workspace D:\MyManagerWorkspace
llwmctl status --connection D:\MyManagerWorkspace\state\control.json
```

## Self-identification

```powershell
llwmctl self
llwmctl self --endpoint http://127.0.0.1:8087/v1
llwmctl self --model qwen3-30b-q4-k-m
llwmctl self --session model:qwen3-30b-q4-k-m
```

The CLI automatically forwards `LLWM_SESSION_ID`, `LLWM_MODEL_ID`, `LLWM_ENDPOINT`, `OPENCODE_MODEL`, `OPENAI_MODEL`, `LLM_MODEL`, `OPENAI_BASE_URL`, or `OPENAI_API_BASE` when present. The API matches a session ID, registered model alias/ID/name/filename, endpoint port, or process ID. With exactly one running model it can make a clearly labelled single-session inference. With multiple unmatched sessions it returns all candidates instead of guessing.

An agent should identify itself before unloading, restarting, or using `--unload-others`. Stopping its own model may terminate the response that initiated the command.

For destructive lifecycle commands, `llwmctl` performs this identity check automatically. It refuses to stop the identified current model unless `--allow-self-stop` is supplied. Agents should add that override only after an explicit user request and warning.

## Model lifecycle

List registered models and runtimes:

```powershell
llwmctl models list
llwmctl runtimes list
```

Load with a saved profile:

```powershell
llwmctl load "Qwen3 30B Q4_K_M" --profile CUDA --wait
```

Apply one-shot overrides:

```powershell
llwmctl load qwen3-30b-q4-k-m `
  --runtime llama-cpp-cuda `
  --set contextSize=65536 `
  --set gpuLayers=999 `
  --set gpuMode=row `
  --set gpuDevices=CUDA0,CUDA1 `
  --set gpuSplit=3,1 `
  --wait
```

Persist the effective overrides into the selected profile:

```powershell
llwmctl restart qwen3-30b-q4-k-m --profile CUDA --set contextSize=131072 --save-profile=CUDA-128K --wait
```

Without `--save-profile`, overrides never modify the saved profile. If a model is already running, `load` returns its current session; use `restart` to apply different settings.

Other lifecycle commands:

```powershell
llwmctl unload <model>
llwmctl models scan
llwmctl models import --folder D:\ExternalModels\ModelFolder
llwmctl models delete <model> --confirm
```

App-owned deletion removes the Manager-owned model directory. Imported/external models are unregistered without deleting the external model folder.

## Model groups, retention, and eviction priority

Groups are optional retention-policy containers assigned to launch profiles. They do not select a runtime, sampling settings, a default profile, or request priority. An ungrouped launch profile inherits the global `autoUnloadIdleMinutes` setting, and different profiles of the same model can belong to different groups.

```powershell
llwmctl groups list
llwmctl groups create --name "Interactive" --retention pinned --priority high
llwmctl groups create --name "Batch" --retention idle-timeout --idle-minutes 15 --priority low
llwmctl groups get "Batch"
llwmctl groups update "Batch" --name "Background batch"
llwmctl groups update "Batch" --idle-minutes 30 --priority normal
llwmctl groups assign <model> <profile-id-or-name> --group "Batch"
llwmctl groups unassign <model> <profile-id-or-name>
llwmctl groups delete "Batch"
```

Retention modes:

- `inherit` uses the global idle timeout. Its group eviction priority still applies.
- `pinned` prevents automatic idle unload.
- `idle-timeout` uses the group's own `idleMinutes` value from 1 through 10080, including when the global timeout is `0` (disabled).

Eviction priority is ordered `low`, `normal`, then `high`. When multiple non-processing sessions become idle-eligible together, the Manager unloads one lowest-priority candidate per telemetry refresh. It never uses this value to reorder inference requests, and active slots are not idle candidates. Pinned retention does not block an explicit unload/delete/shutdown/update or the gateway's **Single active** policy; those operations keep their existing confirmation and self-preservation behavior.

Editing or renaming a group keeps its stable ID and launch-profile assignments. Deleting a group only removes its assignments. Model registrations, GGUF files, running sessions, and launch profiles are kept. In the UI, use the compact **Models > Groups…** table to create, edit, or delete a group and open **Profiles…** for multi-select add, move, or removal. In **Saved Launch Profiles**, use inline **Add** when ungrouped or click the assigned group name for **Change group…** and **Remove from group**; right-clicking a profile opens the general assignment flow.

Overview lists groups in the Model selector. Clicking **Load** for a group validates every assigned runtime and port and performs an aggregate VRAM preflight before starting anything. Multiple profiles backed by the same physical model are allowed and are counted as independent allocations; they must use distinct ports. The VRAM check retains a 1 GiB safety reserve and does not treat an existing same-model profile as reclaimable because it remains loaded. If telemetry is unavailable for a GPU-backed group or the full set does not fit, no group member is started. CPU-only groups do not require VRAM telemetry. This Overview batch action does not change the group's retention meaning or add request-routing priority; control clients can inspect `profileIds` from `groups get` and load the same saved profiles explicitly.

The corresponding HTTP routes are:

- `GET|POST /api/v1/model-groups`
- `GET|PATCH|DELETE /api/v1/model-groups/{group}`
- `GET|PUT|DELETE /api/v1/models/{model}/profiles/{profile}/group`

`{profile}` accepts a profile ID or name scoped to `{model}`. `PUT` takes `{ "group": "<group-id-or-name>" }`. `GET /api/v1/models`, `GET /api/v1/models/{model}`, and profile-list responses include `group` and `effectivePolicy` on every launch-profile object. Group objects expose `profileCount` and `profileIds`.

For compatibility with early v2.2 development builds, `/api/v1/models/{model}/group` still maps to that model's default launch profile, but it is not advertised by `capabilities`; new clients should use the profile-scoped route.

## Profiles and all launch settings

```powershell
llwmctl profiles list --model <model>
llwmctl profiles create --model <model> --name "CUDA 128K" --set runtimeId=<runtime> --set contextSize=131072
llwmctl profiles update --model <model> --id <profile-id> --set temperature=0.7 --set topP=0.9
llwmctl profiles delete --model <model> --id <profile-id>
```

`llwmctl capabilities` returns the live schema for every `ModelLaunchSettings` and `AppSettings` property. Repeated `--set name=value` accepts booleans, integers, decimal numbers, strings, JSON arrays/objects, and `null`. For a large patch:

```powershell
llwmctl profiles update --model <model> --id <profile-id> --settings-file profile-patch.json
```

The full profile surface includes runtime, host, and port, context, GPU layers/mode/devices/split, parallelism, batches, threads, flash attention, K/V cache types and offload, prompt cache, checkpoints, continuous batching, reasoning mode/format/effort/budget/budget-message/preservation controls, template options, vision image tokens, mmap/mlock, sampling and penalties, RoPE, speculative mode and draft controls, vision/MTP/draft paths, metrics, and validated custom parameters. A non-loopback profile host is honored only when direct-model LAN access is enabled. Reasoning effort is passed to the llama.cpp chat template; non-default levels only affect models and templates that support them.

## Shared model-serving gateway

The OpenAI-compatible gateway is separate from the control API. When enabled, query its model catalog with `GET http://127.0.0.1:<gateway-port>/v1/models`. Every saved launch profile is returned as a separate model entry:

The gateway authenticates this catalog and all inference requests. Direct
llama.cpp inference also requires the configured model API key, although some
upstream builds expose health or model-catalog metadata without authentication.

- The default profile uses the model id.
- A named profile uses `<model-id>--<profile-id>`, with the profile segment normalized for use in a URL/model field. If two stored ids normalize to the same value, each route receives a deterministic hash suffix.

Send the returned id in the `model` field of an OpenAI-compatible request. The gateway loads the selected profile automatically and proxies to its direct llama.cpp port. Under **Prefer keeping loaded models**, different profiles of the same GGUF can stay loaded concurrently on distinct saved ports. Under **Single active model**, a different-profile request waits for active upstream responses before the gateway stops other sessions and starts the requested profile. This contract is client-neutral; the Manager does not discover or edit third-party harness configuration.

## Vision, draft, and MTP heads

List every eligible companion in the model's exact folder rather than only the
first auto-detected file:

```powershell
llwmctl models companions <model>
```

Select an external vision head:

```powershell
llwmctl profiles update --model <model> --id <profile-id> --set visionProjectorPath=D:\Models\mmproj-f16.gguf
```

Use embedded vision:

```powershell
llwmctl profiles update --model <model> --id <profile-id> --set visionProjectorPath=embedded
```

Configure upstream draft MTP or an Atomic MTP head:

```powershell
llwmctl profiles update --model <model> --id <profile-id> `
  --set speculativeType=draft-mtp `
  --set specDraftModelPath=D:\Models\mtp-assistant.gguf

llwmctl profiles update --model <model> --id <profile-id> `
  --set speculativeType=atomic-mtp `
  --set mtpHeadPath=D:\Models\mtp-head.gguf
```

Use an empty string for automatic discovery. The response separates `mtpHeads`,
`dflashHeads`, `dsparkHeads`, `eagle3Heads`, and `simpleDraftModels`, and reports
`autoDiscoveryScope` plus the `draftMtpAutoPrecedence` rule. The legacy
`draftAndMtpHeads` aggregate remains for compatibility.

For `draft-mtp`, an empty draft-model path first checks the main GGUF for a
positive `*.nextn_predict_layers` metadata value. When present, the Manager uses
the embedded MTP tensors and deliberately omits `--model-draft`. Otherwise it
searches the exact model folder for an MTP sidecar. Every other `draft-*` mode
searches only its own sidecar type, so DSpark, DFlash, Eagle3, MTP, and ordinary
draft models are never substituted for one another. Recognizable conflicting
family/version or target-size candidates are rejected; `draft-simple` permits a
smaller draft size. Explicit paths still take precedence and may be outside the
model folder.

Upstream llama.cpp normally uses a separate mmproj file for vision. Automatic
vision discovery therefore selects a compatible mmproj/projector from the exact
model folder. The `embedded` token is explicit rather than inferred and is only
for compatible forks or specially packaged models; it omits `--mmproj`.

## Metrics and logs

```powershell
llwmctl sessions list
llwmctl sessions inspect <session-or-model>
llwmctl gateway inspect
llwmctl sessions metrics <session-or-model>
llwmctl sessions logs <session-or-model> --tail 32000
llwmctl metrics
llwmctl metrics usage --range 30d
llwmctl metrics usage --range 90d --model <model-id> --profile <profile-id> --runtime <runtime-id>
llwmctl logs list
llwmctl logs tail llama-server-example.log --tail 80000
```

`sessions inspect` asks the Manager to probe the selected model's `/health`,
`/v1/models`, `/props`, and `/slots` endpoints with the stored model-serving API
key. `gateway inspect` does the same for the shared gateway's `/health`,
`/v1/models`, and `/running` endpoints. Both return normalized reports without
exposing that key. Metrics responses
include raw parsed Prometheus samples, metric type/help metadata, endpoint
responsiveness, and the current `/slots` snapshot. Log responses are bounded and
redact the configured model API key and common bearer/command-line secret patterns.

`llwmctl metrics` returns the current raw live metrics. `llwmctl metrics usage`
queries persisted usage history through `GET /api/v1/metrics/usage`. Accepted
ranges are `7d`, `30d`, `90d`, and `all`; optional `model`, `profile`, `runtime`,
and `timeZone` query parameters narrow or regroup the result. The response
contains selected-period and tracked totals, local-day buckets, model
breakdowns, available filter dimensions, the daily-tracking start time, and a
flag when all-time totals include preserved usage from before daily tracking.

Input tokens are evaluated prompt tokens plus prompt tokens reused from cache.
Cache hit rate uses only periods where the runtime exposed its cumulative cache
counter. Optional counters are reported as unavailable rather than zero. Daily
history starts at upgrade; pre-existing lifetime totals remain visible but are
not assigned to synthetic dates.

## Hugging Face downloads and jobs

```powershell
llwmctl hf search "Qwen Q4_K_M"
llwmctl hf download --repo owner/repository --file model-Q4_K_M.gguf --dry-run
llwmctl hf download --repo owner/repository --file model-Q4_K_M.gguf --revision main
llwmctl jobs list
llwmctl jobs pause <job-id>
llwmctl jobs resume <job-id>
llwmctl jobs cancel <job-id>
```

The existing Manager download pipeline remains responsible for filename safety, byte-count/SHA-256 validation, resumable partials, companion projector discovery, registration, launch-profile suggestions, and UI/job updates. Generic `jobs pause|resume|cancel` commands apply only to Hugging Face download jobs. Runtime-build, runtime-package, and unknown job kinds return HTTP `409` without changing stored job state; use their matching runtime operations instead.

## Benchmarks

Benchmark automation uses a versioned plan contract. Discover that contract,
the built-in presets, and the selected runtime's exact capabilities before
creating or editing a plan:

```powershell
llwmctl benchmarks schema
llwmctl benchmarks presets
llwmctl benchmarks capabilities <runtime> --wsl-distro Ubuntu-24.04
llwmctl benchmarks validate --plan benchmark-plan.json
```

Validation expands the plan and reports capability, profile, port, workload,
memory, and session consequences without starting a process. A dry run performs
the same admission checks through the run route. Starting a benchmark applies
sustained load and therefore requires explicit confirmation:

```powershell
llwmctl self
llwmctl sessions list
llwmctl benchmarks run --plan benchmark-plan.json --dry-run
llwmctl benchmarks run --plan benchmark-plan.json --confirm --wait
```

When `benchmarkStopActiveSessions` is enabled, a confirmed plan may stop loaded
sessions before taking the exclusive compute lease. The CLI's normal
self-identification protection still applies; do not use `--allow-self-stop`
unless the user explicitly accepts that the current response may terminate.
Profile-serving runs snapshot and launch the exact saved profile. Direct
`llama-bench` runs use the selected runtime's advertised capabilities. Both run
under Manager process supervision and persist plans, checkpoints, results, and
logs without persisting generated model text.

Inspect and control persisted runs with:

```powershell
llwmctl benchmarks list
llwmctl benchmarks inspect <run>
llwmctl benchmarks wait <run>
llwmctl benchmarks pause <run>
llwmctl benchmarks resume <run>
llwmctl benchmarks cancel <run>
llwmctl benchmarks plan <run>
llwmctl benchmarks clone <run>
llwmctl benchmarks results <run>
llwmctl benchmarks export <run> --format csv
llwmctl benchmarks log <run>
llwmctl benchmarks compare <baseline-run> <candidate-run>
llwmctl benchmarks delete <run> --confirm
```

Comparison requires workload-compatible run identities unless the returned
validation explicitly says otherwise. Deleting a benchmark also deletes its
persisted results and requires explicit authorization.

## Application settings

```powershell
llwmctl settings get
llwmctl settings set --set autoUnloadIdleMinutes=30 --set autoLoadGatewayPolicy=singleActive
llwmctl settings set --set showOverviewLiveRuntimeLog=false --set showOverviewAllMetrics=false
llwmctl settings set --set uiScalePercent=125
llwmctl settings set --set fontScalePercent=125
llwmctl settings rotate-key
```

Settings changes are persisted through the Manager, applied to the live UI, and update Start with Windows. The gateway restarts only when a gateway, access, authentication, or API-key field changes. API-key material is redacted. The running process workspace root is immutable; model API keys can only be rotated, not retrieved or injected through a general patch.

`uiScalePercent` accepts a bounded percentage from `75` through `175`; the UI
offers a slider in 1% steps and displays its current percentage. Slider changes
apply synchronously as the slider moves and persist once on pointer or key
release. The value is an additional application-only multiplier on top of
Windows Per-Monitor-V2 DPI scaling and applies to current and newly opened
Manager windows.

`fontScalePercent` has the same `75` through `175` range and live slider/persist
behavior, but scales application text only. Control dimensions, spacing, and
window chrome are unchanged. The visible Settings label is **Text scale**; the
control field deliberately retains `fontScalePercent` for compatibility.

The **UI** settings are independent booleans: `showOverviewModelStatus`,
`showOverviewHardware`, `showOverviewSlots`, `showOverviewTokens`,
`showOverviewMtpTokens`, `showOverviewKvCache`,
`showOverviewLiveRuntimeLog`, `showOverviewAllMetrics`, and
`showModelsHuggingFace`. They immediately show or collapse the corresponding
Overview or Models surface and persist across restarts. They do not disable
collection, downloads, or control-API access to logs and metrics.

| Setting | Visible target | Default |
| --- | --- | --- |
| `showOverviewModelStatus` | Model status card | `true` |
| `showOverviewHardware` | Hardware card | `true` |
| `showOverviewSlots` | Slots card | `true` |
| `showOverviewTokens` | Tokens card | `true` |
| `showOverviewMtpTokens` | Speculative tokens card | `true` |
| `showOverviewKvCache` | KV cache card | `true` |
| `showOverviewLiveRuntimeLog` | Live Runtime Log section | `true` |
| `showOverviewAllMetrics` | All llama.cpp Metrics table | `false` |
| `showModelsHuggingFace` | Hugging Face search and download history on Models | `false` |

The response from `settings set` reports the persisted settings result. Follow
with `settings get` when an automation needs an explicit read-after-write
check. When every status card is false the whole Model Status card area is
collapsed. When the log, raw metrics, or Hugging Face section is false, its grid
row and splitter are collapsed as well. Workspaces created by an older version
have no stored value for these fields; missing values use the defaults in the
table above.

### UI-managed preferences

Favorite model/profile/runtime choices, **Load profiles on startup** selections,
and remembered main-window/table/splitter layouts are persisted Manager state but
are not currently exposed as `AppSettings` patch fields or dedicated control
routes. Agents must not edit their SQLite tables or automate the WPF interface.
Use saved profiles directly with `llwmctl load ... --profile ... --wait` when an
automation needs deterministic lifecycle behavior.

## Complete application operations

The operation registry exposes the Manager functions available to local automation:

```powershell
llwmctl operations list
llwmctl operations run runtime.catalog
llwmctl operations run runtime-package.install --set preset=official-prebuilt-windows-cuda --dry-run
llwmctl operations run runtime-package.install --set preset=official-prebuilt-windows-cuda --confirm
llwmctl operations run windows.status
llwmctl operations run wsl.setup --set action=InstallUbuntuCudaToolkit --set distro=Ubuntu-24.04 --dry-run
```

Source downloads are check-gated. Discover the exact schemas with `operations
list`, then use the same staged flow as the Runtimes table:

```powershell
llwmctl operations run runtime-source.check --set preset=official-windows-cuda
llwmctl operations run runtime-source.download --set preset=official-windows-cuda --confirm
llwmctl operations run runtime-build.start --set preset=official-windows-cuda --confirm
```

Use the downloaded source identifier returned by the live catalog when the
operation schema requires one; live capabilities are authoritative.

The registry covers runtime packages, custom repositories, source downloads/builds, runtime job controls, Windows/WSL detection and setup, gateway lifecycle, cache/log/download-history maintenance, lifetime metrics, UI navigation, update checks/installation, refresh, and shutdown. `llwmctl capabilities` and `llwmctl operations list` are authoritative.

For prebuilt packages, run `runtime-package.check` before installation. The
check resolves the newest compatible release and exact platform/backend asset;
feed ordering is not an installed-version contract. Installation remains a
confirmed operation and fails closed when the selected asset cannot be verified.

Operations marked `requiresConfirmation` reject execution without `confirm=true`. Every operation accepts `dryRun=true`; dry-run validates the action and target and returns its planned consequence without mutation. Machine setup launches can open elevated or interactive terminals and report `Started`, not installation completion.

## HTTP route summary

The complete live route and settings list is returned by `GET /api/v1/capabilities`. Principal routes include:

- `GET /api/v1/status`, `/capabilities`, `/self`
- `GET /api/v1/models`, `/runtimes`, `/sessions`, `/metrics`, `/logs`, `/jobs`
- `GET /api/v1/sessions/{session}/inspect|metrics|logs`
- `GET /api/v1/gateway/inspect`
- `POST /api/v1/models/{model}/load|restart|unload`
- `GET|POST /api/v1/models/{model}/profiles`
- `PUT|DELETE /api/v1/models/{model}/profiles/{profile}`
- `GET /api/v1/models/{model}/companions`
- `GET|PATCH /api/v1/settings`
- `GET /api/v1/huggingface/search` and `POST /api/v1/huggingface/download`
- `POST /api/v1/jobs/{job}/pause|resume|cancel` for Hugging Face downloads only
- `GET /api/v1/benchmarks`, `/benchmarks/schema`, `/benchmarks/presets`, and `/benchmarks/capabilities`
- `POST /api/v1/benchmarks/validate`, `/benchmarks/run`, and `/benchmarks/compare`
- `GET /api/v1/benchmarks/{run}` plus `/wait`, `/plan`, `/results`, `/export`, and `/log`
- `POST /api/v1/benchmarks/{run}/pause|resume|cancel`
- `DELETE /api/v1/benchmarks/{run}?confirm=true`
- `GET /api/v1/operations` and `POST /api/v1/operations/{operation}`

For uncommon or future routes, use the raw client while retaining discovery and authentication:

```powershell
llwmctl request GET /api/v1/capabilities
llwmctl request POST /api/v1/models/MODEL/load --body '{"waitForReady":true}'
```

Raw requests sent through `llwmctl request` still enforce the CLI's current-session protection for model unload/restart/delete, `unloadOthers`, and operations that can stop the active runtime or Manager. `--allow-self-stop` is required for the same explicitly authorized cases as the named commands; CLI raw mode is not a safety bypass.
