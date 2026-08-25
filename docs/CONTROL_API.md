# Local control API and `llwmctl`

Last reviewed: 2026-08-25

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
llwmctl models import --file D:\ExternalModels\FutureName.gguf
# Only after reviewing scan/import classification diagnostics:
llwmctl models import --file D:\ExternalModels\Ambiguous.gguf --confirm-role
llwmctl models delete <model> --confirm
```

App-owned deletion removes the Manager-owned model directory. Imported/external models are unregistered without deleting the external model folder.

`models scan` reads GGUF role metadata before considering narrow filename
conventions and returns `discovered`, `registered`, `companions`, `ambiguous`,
`invalid`, and a per-file `files` array with `role`, `confidence`, and `reason`.
Generic text embedded in a future main-model filename is not sufficient to
exclude it. `models import --file` validates the selected GGUF and imports a
main model from any folder. If its detected role is a companion or remains
ambiguous, the request fails unless `--confirm-role` is supplied; that explicit
decision is persisted for the observed file identity and honored by later scans.
Replacing the GGUF at that path invalidates the confirmation and causes its role
to be classified again. Invalid or unreadable GGUFs cannot be overridden.
`--folder` remains supported for compatibility and uses the automatic classifier.

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

Overview lists groups in the Model selector. Clicking **Load** for a group validates every assigned runtime and port, rejects multiple profiles for the same physical model, and performs an aggregate VRAM preflight before starting anything. The VRAM check includes reclaimable memory from a running profile that will be replaced and retains a 1 GiB safety reserve. If telemetry is unavailable for a GPU-backed group or the full set does not fit, no group member is started. CPU-only groups do not require VRAM telemetry. This Overview batch action does not change the group's retention meaning or add request-routing priority; control clients can inspect `profileIds` from `groups get` and load the same saved profiles explicitly.

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

The full profile surface includes runtime and port, context, GPU layers/mode/devices/split, parallelism, batches, threads, flash attention, K/V cache types and offload, prompt cache, checkpoints, continuous batching, reasoning mode/format/effort/budget/budget-message/preservation controls, template options, vision image tokens, mmap/mlock, sampling and penalties, RoPE, speculative mode and draft controls, vision/MTP/draft paths, metrics, and validated custom parameters. Reasoning effort is passed to the llama.cpp chat template; non-default levels only affect models and templates that support them.

## Shared model-serving gateway

The OpenAI-compatible gateway is separate from the control API. When enabled, query its model catalog with `GET http://127.0.0.1:<gateway-port>/v1/models`. Every saved launch profile is returned as a separate model entry:

```json
{
  "id": "qwen--128k",
  "object": "model",
  "name": "Qwen",
  "profile_name": "128K",
  "context_length": 131072,
  "meta": {
    "n_ctx_train": 262144,
    "n_params": 27000000000,
    "size": 18000000000
  }
}
```

`context_length` is the context size configured on that saved profile, so
profiles for the same GGUF may report different values. A value of `0` preserves
the profile's automatic context setting; it is not an inferred model-training
limit or a measurement of currently available KV cache.

The optional `meta` values describe the underlying GGUF rather than the launch
profile: `n_ctx_train` is its declared training context, `n_params` is its GGUF
parameter count, and `size` is its current file size in bytes. They remain
`null` when the file or metadata is unavailable and are never inferred from its
name. Multiple profiles for one GGUF therefore share `meta` while retaining
their own `context_length`.

The gateway and direct llama.cpp inference require the configured model API key
by default. In Local-only mode, `requireApiKeyAuth=false` explicitly makes the
active key empty for unauthenticated browser or client testing. LAN exposure
always requires authentication. Some upstream builds expose health or
model-catalog metadata without authentication even while inference is protected.

- The default profile uses the model id.
- A named profile uses `<model-id>--<profile-id>`, with the profile segment normalized for use in a URL/model field. If two stored ids normalize to the same value, each route receives a deterministic hash suffix.

Send the returned id in the `model` field of an OpenAI-compatible request. The gateway loads the selected profile automatically, proxies to its direct llama.cpp port, and restarts an already-running copy of the same GGUF when a different profile is requested. Concurrent requests for one profile remain concurrent, while a different-profile request waits for their upstream responses to complete before switching. This contract is client-neutral; the Manager does not discover or edit third-party harness configuration.

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
llwmctl metrics usage --range month
llwmctl metrics usage --range 90d --model <model-id> --profile <profile-id> --runtime <runtime-id>
llwmctl metrics usage --date 2026-08-18 --date 2026-08-20
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
ranges are `1d`, `7d`, `month`, `30d`, `90d`, and `all`; `1d` uses the current
local calendar day, while `month` returns the complete
current calendar month while `30d` remains a rolling window. Optional `model`, `profile`, `runtime`,
and `timeZone` query parameters narrow or regroup the result. Repeat `--date
YYYY-MM-DD` (or pass a comma-separated `dates` query value) to aggregate up to
366 exact local dates; exact dates take precedence over the range for totals.
The response contains selected-period and tracked totals, selected local-day
buckets, a separate rolling 24-month `calendarDays` surface with `isTracked`
availability, model breakdowns and tracked-token share, active-day and peak-day
insights, available filter dimensions, the daily-tracking start time, and a flag
when all-time totals include preserved usage from before daily tracking. It also
returns host-wide `gpuEnergy` and per-day `gpuEnergy` objects with `wattHours`,
`kilowattHours`, `sampledSeconds`, `powerObserved`, `completeCoverage`, and
observed/detected GPU counts. These energy values follow the date/range window but
intentionally ignore model/profile/runtime filters because host power cannot be
attributed safely to one model.
The response also includes `gpuEnergyDevices`, ordered by GPU index, with
`sensorKey`, `gpuIndex`, `gpuName`, `wattHours`, `kilowattHours`, and
`sampledSeconds` for each power-reporting adapter. Per-device history starts at
first observation after device-level tracking is available; older combined
energy is never guessed or distributed across adapters.
`gpuElectricityCost` contains `amount`, `currencyCode`, `dayRatePerKwh`, and
`nightRatePerKwh`. It is recalculated from the selected measured hourly energy
using the current application tariff; it is not a persisted charge ledger.

Input tokens are evaluated prompt tokens plus prompt tokens reused from cache.
Cache hit rate uses only periods where the runtime exposed its cumulative cache
counter. Average prompt and generation throughput uses cumulative active
processing seconds reported by llama.cpp rather than wall time. Request totals
and success rates appear only when a compatible runtime request counter is
available. Optional counters are reported as unavailable rather than zero.
Daily history starts at upgrade; pre-existing lifetime totals remain visible
but are not assigned to synthetic dates.
GPU energy history begins with the first pair of valid power samples. By default,
a dedicated sampler integrates 10-second samples only while at least one model
session is active, rejects
intervals longer than 30 seconds or a changed
sensor set, and never fills app downtime with an estimate. With no active session,
historical persistence stops and idle detection backs off to five minutes.
Set `trackGpuEnergyWhileIdle=true` to retain continuous 10-second idle history.
NVIDIA SMI is automatic;
AMD SMI and Intel XPU-SMI are optional capability sources when installed.
The usage response retains `gpuEnergy` and `gpuEnergyDevices` for programmatic
historical analysis. The Metrics page renders the combined historical total and
calendar; per-device history remains API-only.
Optional Overview card rows show per-device and combined cumulative app-live
energy observed by the Manager process. These host-wide counters are independent
of model selection, reset when the Manager restarts, and are not model-attribution
figures. Matching per-device and combined electricity-cost rows apply the current
tariff to that app-live observed energy and can be added before a session starts.

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

## Application settings

```powershell
llwmctl settings get
llwmctl settings set --set autoUnloadIdleMinutes=30 --set autoLoadGatewayPolicy=singleActive
llwmctl settings set --set showOverviewLiveRuntimeLog=false --set runtimeLogOrder=newestFirst --set showOverviewAllMetrics=false
llwmctl settings set --set electricityCurrencyCode=GBP --set electricityDayRatePerKwh=0.30 --set electricityNightRatePerKwh=0.10 --set electricityNightStartLocal=00:00 --set electricityNightEndLocal=07:00
llwmctl settings set --set modelAccessMode=local --set requireApiKeyAuth=false
llwmctl settings set --set requireApiKeyAuth=true
llwmctl settings rotate-key
```

Settings changes are persisted through the Manager, applied to the live UI, and update Start with Windows. The gateway restarts only when a gateway, access, authentication, or API-key field changes. API-key material is redacted. The running process workspace root is immutable; model API keys can only be rotated, not retrieved or injected through a general patch.

`requireApiKeyAuth=false` is accepted only with `modelAccessMode=local`. It
clears the active key used by direct runtimes and the local gateway while keeping
a protected strong backup. Re-enabling authentication restores that key; rotating
the key also re-enables authentication. Gateway, direct-model, and combined LAN
modes reject attempts to disable authentication.

Electricity rates use currency units per kWh. `electricityCurrencyCode` is a
three-letter display code, and local tariff boundaries use 24-hour `HH:mm`.
Day and night start/end must differ. The estimate covers observed GPU board
energy, not whole-host wall energy, and does not fill telemetry gaps.

The Overview card layout is stored in the versioned
`overviewDashboardLayout` object. Its `cards` contain stable card IDs, one or
more metric IDs, v4 `chartMetricIds`, the singular `chartMetricId` compatibility
field, and v3 `bounds`. Horizontal `x`
and `width` values use a responsive 12-unit surface; vertical `y` and `height`
values use device-independent pixels. The legacy `columnSpan` and height-preset
fields remain normalized compatibility projections. Use the Overview UI for ordinary customization. An
automation that deliberately replaces the structured layout should send the
complete object through `--settings-file` and then verify it with `settings get`.
Unknown or malformed metrics are removed, sizes and counts are bounded, and the
persisted version is normalized by the Manager. Layout version 2 introduced atomic
metric IDs: CPU, RAM, each indexed GPU, individual slot/request counters, average
token rates and totals, average speculative rates and totals, individual KV-cache values, and
raw Prometheus series can be combined freely in any card. Layout version 3 adds
free-form bounds, version 4 adds independent per-metric charts, and version 5
retires unreliable per-poll generation, prompt, and speculative live-rate IDs.
Existing live-rate chart choices migrate to their average-rate equivalents; v1
composite IDs, v2 packed layouts, and v3 singular chart choices migrate automatically.
Layout version 6 limits persisted charts to curated time-varying values. Slot
counters, configured clocks, capacities, and raw runtime samples remain
available as values but no longer create charts. Optional hardware sensors are
offered only after the host probe supplies a finite value, so unsupported CPU
temperatures and GPU sensors do not create unavailable rows or empty plots.
Layout version 7 migrates session-named energy and electricity-cost IDs to
app-live observed-energy IDs. These values are host-wide, independent of model
selection, reset when the Manager restarts, and continue contributing measured
deltas to historical GPU energy. Layout version 8 adds `cardSizesLocked` and
`lockedSurfaceWidth`. The Overview UI captures a valid sizing reference when
the user enables Lock; automation should preserve both fields when replacing a
locked layout rather than guessing a viewport width.
Layout version 9 adds the optional per-card `title` field. Titles are trimmed,
control characters are removed, and values are bounded to 80 characters; an
empty title preserves the compact headerless card.
Layout version 10 adds curated Core, Hardware, Energy, Gateway, Advanced, and
Raw categories plus prompt-cache reuse, draft acceptance percentage, recent
generation/prompt throughput, peak context, context shifts, selected
`llama-server` CPU/private memory, optional GPU memory activity/clock, fan,
power-limit/throttling sensors, and observed gateway latency/health values.
Unsupported optional rows are not offered. Legacy redundant IDs still render
when already saved but are hidden from new choices. Cumulative token/request
counters are values only, and v9 draft-acceptance-average membership migrates to
the acceptance-percentage ID.
The production default uses unlocked, equal-width runtime-summary and host/energy
cards. Hardware discovery adds the GPU template for discrete GPUs only, omitting
GPU core clock from the template while leaving integrated-GPU and core-clock
metrics available for custom cards. Additional GPU cards wrap onto later rows.
Version 12 makes every production-default card the same compact height and keeps
GPU utilization as the sole default GPU chart. GPU power draw remains a value in
the card. Layouts outside the default family are not rewritten.
Gateway duration and first-data latency include request validation and any model
load/swap delay. Response throughput is exposed only when the upstream response
reports a completion-token count; it is not estimated from streaming chunks.

The **UI** compatibility settings include: `showOverviewModelStatus`,
`showOverviewHardware`, `showOverviewSlots`, `showOverviewTokens`,
`showOverviewMtpTokens`, `showOverviewKvCache`,
`showOverviewLiveRuntimeLog`, `showOverviewAllMetrics`, and
`showModelsHuggingFace`. They immediately show or collapse the corresponding
Overview metric group or Models surface and persist across restarts. The six
compatibility booleans add or remove their metric group from
`overviewDashboardLayout`; they do not
replace unrelated custom cards or settings. They do not disable
collection, downloads, or control-API access to logs and metrics.

| Setting | Visible target | Default |
| --- | --- | --- |
| `showOverviewModelStatus` | Model status metric | `false` |
| `showOverviewHardware` | CPU, RAM, and GPU metrics | `true` |
| `showOverviewSlots` | Slot and queued-request metrics | `false` |
| `showOverviewTokens` | Token-rate and token-total metrics | `true` |
| `showOverviewMtpTokens` | Speculative rate and total metrics | `true` |
| `showOverviewKvCache` | KV-cache used, capacity, usage, and allocation metrics | `true` |
| `showOverviewLiveRuntimeLog` | Live Runtime Log section | `true` |
| `runtimeLogOrder` | Live Runtime Log order: `newestFirst` or `oldestFirst` | `newestFirst` |
| `showOverviewAllMetrics` | All llama.cpp Metrics table | `false` |
| `showModelsHuggingFace` | Hugging Face search and download history on Models | `false` |

The response from `settings set` reports the persisted settings result. Follow
with `settings get` when an automation needs an explicit read-after-write
check. The dashboard remains available for customization when it has no cards.
When the log, raw metrics, or Hugging Face section is false, its grid
row and splitter are collapsed as well. Workspaces created by an older version
have no stored value for these fields; missing values use the defaults in the
table above; the Manager migrates them into the default versioned layout.

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
- `GET /api/v1/operations` and `POST /api/v1/operations/{operation}`

For uncommon or future routes, use the raw client while retaining discovery and authentication:

```powershell
llwmctl request GET /api/v1/capabilities
llwmctl request POST /api/v1/models/MODEL/load --body '{"waitForReady":true}'
```

Raw requests sent through `llwmctl request` still enforce the CLI's current-session protection for model unload/restart/delete, `unloadOthers`, and operations that can stop the active runtime or Manager. `--allow-self-stop` is required for the same explicitly authorized cases as the named commands; CLI raw mode is not a safety bypass.
