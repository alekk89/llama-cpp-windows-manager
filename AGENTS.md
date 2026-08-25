# llama.cpp Windows Manager operator instructions

## Purpose

llama.cpp Windows Manager is a Windows WPF application that owns its workspace,
SQLite state, runtime/model inventory, downloads, supervised `llama-server`
sessions, OpenAI-compatible gateway, logs, and live metrics.

`llwmctl` is the supported automation interface. It talks to the authenticated
loopback control API inside the running Manager, so successful commands update
the real application state and appear in the UI.

## Non-negotiable rules

- Use `llwmctl` for live Manager operations. Do not edit the SQLite database,
  expose the control API, or automate WPF controls. Do not launch `llama-server`
  directly.
- Start every operational task with `llwmctl status`.
- Run `llwmctl capabilities` and `llwmctl operations list` before using an
  unfamiliar field or action. The live schemas are authoritative.
- Run `llwmctl self` before work that can unload, restart, replace, update, or
  otherwise affect a loaded model.
- Treat a nonzero CLI exit code or JSON response with `"ok": false` as failure.
  Preserve and report the returned error.
- Never use `--confirm` or `--allow-self-stop` unless the user explicitly
  authorized the stated consequence.

## Choose the correct CLI and workspace

Beside an installed or portable application, use the matching executable:

```powershell
./llwmctl.exe status
```

From the source repository, use a built CLI on `PATH` or:

```powershell
dotnet run --project src/LocalLlmConsole.ControlCli/LocalLlmConsole.ControlCli.csproj -- status
```

If discovery is ambiguous, specify the workspace or discovery file:

```powershell
llwmctl status --workspace <workspace>
llwmctl status --connection <workspace>\state\control.json
```

A writable portable installation normally uses `<application-folder>\data`.
Never read, print, copy, or manually decrypt the control token.

If no Manager is available and the user asked to start or operate it, launch
`LlamaCppWindowsManager.exe` normally and visibly, then retry `status`. The app
is single-instance per Windows user session; do not start a second Manager.

## First contact and cold start

Run these commands before choosing model, runtime, profile, or session IDs:

```powershell
llwmctl status
llwmctl capabilities
llwmctl operations list
llwmctl self
llwmctl models list
llwmctl runtimes list
llwmctl sessions list
```

Use `profiles list --model <model>` for saved variants. When `self` is
ambiguous, retry with `--endpoint`, `--model`, `--session`, `--port`, or process
hints; never guess from the UI selection.

## Load, restart, and unload models

Prefer a saved profile and wait for endpoint readiness:

```powershell
llwmctl load <model> --profile <profile> --wait
```

Repeated `--set name=value` options are one-shot overrides. Persist them only
when requested with `--save-profile=<name>` or the profile commands. Obtain the
complete setting names and accepted values from `capabilities`.

Before any restart or unload, identify the current model with `self`. Never
stop the session serving the current operation unless the user explicitly asks
for that consequence and accepts that the response may terminate. Only then may
`--allow-self-stop` be used. Do not use `--unload-others` while identity is
unknown.

## Companions and launch profiles

Inspect compatible helpers before selecting vision, draft, or MTP files:

```powershell
llwmctl models companions <model>
```

Automatic discovery is restricted to the model's exact folder. Explicit
compatible paths may be elsewhere. Profile fields are
`visionProjectorPath`, `specDraftModelPath`, `mtpHeadPath`, and
`speculativeType`.

Model inventory scans classify readable GGUF metadata before narrow filename
fallbacks and report per-file reasons. To register a main model anywhere on disk,
use `llwmctl models import --file <path.gguf>`. If it is reported as ambiguous
or as a companion, inspect the reason and use `--confirm-role` only when the user
intends to treat that valid GGUF as a main model. The confirmation persists across
future scans; invalid or unreadable GGUFs cannot be overridden.

For upstream `draft-mtp`, leave `specDraftModelPath` empty when the main GGUF
reports `embeddedDraftMtp: true`; the Manager then uses embedded NextN/MTP
tensors. Use `visionProjectorPath=embedded` only when the selected runtime and
model package explicitly support an embedded multimodal projector.

## Model groups and retention

Groups are assigned to launch profiles, not directly to model records:

```powershell
llwmctl groups list
llwmctl groups create --name "Interactive" --retention pinned --priority high
llwmctl groups create --name "Batch" --retention idle-timeout --idle-minutes 15 --priority low
llwmctl groups assign <model> <profile> --group "Batch"
llwmctl groups unassign <model> <profile>
```

Valid retention modes are `inherit`, `pinned`, and `idle-timeout`; priorities
are `low`, `normal`, and `high`. A group load preflights duplicate model
assignments, runtimes, ports, and aggregate VRAM before starting anything.
Retention affects automatic idle unload, not inference scheduling. Explicit
lifecycle operations and the gateway's Single active policy still take
precedence.

## Observe sessions, logs, and downloads

```powershell
llwmctl sessions inspect <session>
llwmctl gateway inspect
llwmctl sessions metrics <session>
llwmctl metrics usage --range month
llwmctl metrics usage --date 2026-08-18 --date 2026-08-20
llwmctl sessions logs <session>
llwmctl logs list
llwmctl logs tail
llwmctl hf search <query>
llwmctl hf download --repo <owner/repo> --file <path.gguf>
llwmctl jobs list
```

The shared gateway's `GET /v1/models` response lists saved profile routes and
reports each route's configured context size as `context_length`. A value of `0`
means the profile uses automatic context sizing; it is not the model's inferred
training limit or current KV-cache availability. The optional `meta.n_ctx_train`,
`meta.n_params`, and `meta.size` values describe the underlying GGUF and are
shared by its profiles; unavailable values remain null rather than being guessed.

`metrics` without an action returns live raw samples. `metrics usage --range
month` returns the complete current calendar month, including empty future days;
persisted daily tokens, cache reuse, active-processing throughput, and optional
request counters can be filtered with `--model`, `--profile`, or `--runtime`.
The same response includes host-wide `gpuEnergy` Wh/kWh and per-day energy when
a GPU power sensor is observed. Energy is sampled independently of model filters,
reports observed versus detected GPU coverage, and does not estimate app downtime.
By default, historical energy is persisted only while a model session is active;
idle detection backs off to five minutes. Set `trackGpuEnergyWhileIdle=true` for
continuous ten-second idle sampling and persistence.
When a tariff is configured, `gpuElectricityCost` derives the selected historical
cost from those measured hourly buckets. It uses the current app-level currency,
day/night rates, and local boundary; it is GPU-board cost, not whole-host cost,
and is not a persisted billing ledger.
Missing cache, timing, or request statistics mean the runtime did not expose the
optional counter and must not be interpreted as zero. Repeat `--date
YYYY-MM-DD` to aggregate exact local dates; dates before daily tracking began
remain unavailable and must not be inferred from legacy totals.

Pause, resume, or cancel a Hugging Face model download with `jobs
pause|resume|cancel <job-id>`. Generic job commands reject runtime-build,
runtime-package, and unknown job kinds without changing their state; use the
matching runtime operation for those jobs.

Runtime source work must follow the staged operation flow: run
`runtime-source.check`, then `runtime-source.download`, then
`runtime-build.start` with the downloaded source returned by `runtime.catalog`.
Use `operations run <name> --dry-run --set name=value` before consequential
operations.

## Application settings and UI visibility

Patch settings through the running Manager, never its database:

```powershell
llwmctl settings set --set showOverviewHardware=false --set showModelsHuggingFace=true
llwmctl settings set --set electricityCurrencyCode=GBP --set electricityDayRatePerKwh=0.30 --set electricityNightRatePerKwh=0.10 --set electricityNightStartLocal=00:00 --set electricityNightEndLocal=07:00
llwmctl settings set --set trackGpuEnergyWhileIdle=false
llwmctl settings set --set modelAccessMode=local --set requireApiKeyAuth=false
llwmctl settings get
```

Model API-key authentication may be disabled only with Local-only access. In
that mode the active key is empty and local browser/client requests omit
credentials; the protected backup is restored when authentication is re-enabled.
Every LAN access mode requires a strong key and rejects the opt-out. The Manager
control API remains independently authenticated and loopback-only.

The Settings UI auto-applies choices quickly and delays ordinary text-field
updates until typing pauses before using the shared save debounce. The
`llwmctl settings set` command is immediate and is not subject to the UI typing
delay.

The customizable card state is the versioned `overviewDashboardLayout` setting.
Prefer the Overview UI for layout changes; automation replacing the structured
object should use `settings set --settings-file <json>` and verify the result.
The compatibility presentation fields are `showOverviewModelStatus`,
`showOverviewHardware`, `showOverviewSlots`, `showOverviewTokens`,
`showOverviewMtpTokens`, `showOverviewKvCache`,
`showOverviewLiveRuntimeLog`, `runtimeLogOrder`, `showOverviewAllMetrics`, and
`showModelsHuggingFace`. Cards are generic containers whose v2 contents are
atomic metrics such as CPU load/temperature/clock, RAM load/used capacity/clock,
indexed GPU load/VRAM/power/clock/core temperature/VRAM temperature, an average
token rate, or a slot counter. `runtimeLogOrder` accepts `newestFirst` or
`oldestFirst` and changes only the compact Overview projection; persisted logs
remain chronological.
Version 3 adds bounded free-form card positions and sizes; horizontal bounds use
a responsive 12-unit surface and vertical bounds use device-independent pixels.
Version 4 adds independent per-metric charts. Version 5 removes unreliable
per-poll generation, prompt, and speculative live rates and migrates their chart
choices to the corresponding average-rate metrics. Version 6 limits charts to
curated time-varying readings; optional hardware sensors appear only after the
host probe supplies a finite value.
Version 7 migrates session-named energy rows to app-live observed-energy rows.
They measure host GPU board energy from Manager startup, reset on Manager restart,
and feed the separately persisted historical energy totals.
Version 8 adds a dashboard-wide card-size lock plus the captured surface width;
locked cards retain their dimensions across window resizing and wrap before the
single-card viewport safety clamp is used.
Version 9 adds an optional bounded title per card. Empty titles render no header;
metric values and units use measured width so the label receives all remaining
row space before wrapping.
Version 10 curates the picker into Core, Hardware, Energy, Gateway, Advanced,
and Raw categories. It adds cache reuse, draft acceptance, recent throughput,
context high-water/shift, selected llama-server process, optional extended GPU,
and observed gateway request metrics. Redundant legacy rows remain renderable in
saved layouts but are not offered for new cards; cumulative counters cannot be
charted, and the former draft-acceptance-rate row migrates to acceptance percent.
The six compatibility fields add or remove their metric group from the layout
without discarding unrelated customization. They apply automatically and
do not disable the underlying telemetry, logs, or downloads.
Version 11 makes the default runtime-summary, discrete-GPU, and host/energy cards
unlocked and equal-width. Integrated graphics do not receive default GPU cards,
and GPU core clock is omitted from the default template; both remain available
for custom cards. Reconciliation is limited to that default layout family;
unrelated custom layouts are preserved.
Version 12 gives every production-default card the same compact height. GPU power
draw remains a value in each discrete-GPU card but is no longer charted by
default; GPU utilization remains charted. Existing custom layouts are preserved.
Electricity rates are currency units per kWh. Currency is a three-letter display
code; local night boundaries use `HH:mm` and must differ. App-live combined and
per-GPU cost rows can be configured before model load and are calculated only
from observed GPU board energy.
`trackGpuEnergyWhileIdle` defaults to `false`. When false, session-free power
checks run every five minutes for live detection without adding historical energy;
when true, idle energy is sampled and persisted every ten seconds.

## Consequential operations

The complete action registry includes runtime install/build/delete, Windows and
WSL setup, cache/log/history maintenance, gateway control, updates, navigation,
refresh, and shutdown.

```powershell
llwmctl operations run <operation> --dry-run --set name=value
llwmctl operations run <operation> --confirm --set name=value
```

Use `--confirm` only for an operation whose live schema marks
`requiresConfirmation` and whose consequence the user authorized. Model deletion
also requires explicit intent and `models delete <model> --confirm`. App-owned
model deletion removes its managed folder; imported models are registration-only
by default.

## Restart and recovery

Before an authorized update, shutdown, runtime deletion, or self-stop, collect
`status`, `self`, `sessions list`, and the relevant effective profile/settings.
Report the state and recovery command before issuing the stopping action. Treat
a self-stopping command as the final action unless an independent controller can
observe the restart.

After restart, re-read the restored `AGENTS.md`, then run `status`,
`capabilities`, `operations list`, and `self`. Compare sessions and profiles with
the pre-restart snapshot. Reload only the sessions included in the request.

Release builds embed and restore the matching `llwmctl.exe`, this file,
`agent.md`, and `docs/CONTROL_API.md`. To verify those sidecars without opening
the UI:

```powershell
LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only
```

## Working from GitHub or source

For end-user installation, prefer the installer or portable ZIP from
[GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest)
and verify its matching `.sha256` file. Do not describe an unsigned artifact as
trusted or signed.

```powershell
$asset = "LlamaCppWindowsManager-win-x64.zip"
$expected = ((Get-Content "$asset.sha256" -Raw).Trim() -split "\s+")[0]
$actual = (Get-FileHash $asset -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Release checksum mismatch: $asset" }
```

The canonical source repository is
[github.com/alekk89/llama-cpp-windows-manager](https://github.com/alekk89/llama-cpp-windows-manager).

For repository changes, read `docs/DEVELOPMENT.md` and, for architectural work,
`docs/ARCHITECTURE.md`. Preserve existing worktree changes and generated-data
boundaries. Run tests proportional to the change and the full gate for control,
architecture, packaging, or release work:

`src/LocalLlmConsole.Core` is the platform-neutral model and policy assembly.
The WPF app may reference Core, but Core must not reference the app or use WPF,
Windows Forms, registry, SQLite, or app-localization APIs. Keep OS/process,
storage, localization, and UI composition in `LocalLlmConsole.App`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1
```

Before launching a source build, check `llwmctl status`; it cannot run beside a
production Manager in the same user session. Use an isolated ignored workspace,
never production data:

```powershell
$developmentWorkspace = Join-Path $PWD "workspace/development"
$env:LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE = $developmentWorkspace
Start-Process -FilePath .\src\LocalLlmConsole.App\bin\Release\net10.0-windows\win-x64\LlamaCppWindowsManager.exe -WorkingDirectory $PWD
```

Local builds and packages are unsigned unless signing is explicitly configured.
Do not overwrite or restart a running production installation merely to test a
source change.

Before updating a local deployment, run `llwmctl status`, `llwmctl self`, and
`llwmctl sessions list`. If any model session is loaded or running, do not deploy,
restart, or replace the application. Leave the tested source/package staged until
the Manager has no model sessions; do not unload a model unless the user separately
and explicitly requests it.

## Troubleshooting

- Run `llwmctl help` for syntax and inspect the live schemas instead of guessing.
- On command failure, keep the returned JSON and exit code, then inspect
  `logs list`, `logs tail`, or the relevant session log.
- For Windows/WSL setup, distinguish **Started** from completed installation and
  verify the corresponding status operation afterward.
- For version disagreement, use the `llwmctl.exe` restored beside that exact
  application executable.
- See [docs/CONTROL_API.md](docs/CONTROL_API.md) for request contracts and route
  details.
