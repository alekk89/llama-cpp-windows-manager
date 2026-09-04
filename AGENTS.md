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

`host` is a saved per-profile launch field. A non-loopback value is effective
only when the application `modelAccessMode` permits direct-model LAN access;
do not weaken that application policy merely to make a profile load.
Profiles whose saved data omits `host`, or leaves it blank, inherit the app host
default. Explicit saved addresses, including loopback, remain overrides. Launch
commands and endpoint probes both apply the same LAN access policy.

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
are `low`, `normal`, and `high`. A group load preflights duplicate ports,
runtimes, missing profiles, and aggregate VRAM before starting anything.
Different profiles of the same model may run together when their ports and
hardware allocations do not conflict.
Retention affects automatic idle unload, not inference scheduling. Explicit
lifecycle operations and the gateway's Single active policy still take
precedence.

## Observe sessions, logs, and downloads

```powershell
llwmctl sessions inspect <session>
llwmctl gateway inspect
llwmctl sessions metrics <session>
llwmctl metrics usage --range 30d
llwmctl sessions logs <session>
llwmctl logs list
llwmctl logs tail
llwmctl hf search <query>
llwmctl hf download --repo <owner/repo> --file <path.gguf>
llwmctl jobs list
```

`metrics` without an action returns live raw samples. `metrics usage` returns
persisted daily token history and prompt-cache statistics; filter it with
`--model`, `--profile`, or `--runtime`. A missing cache rate means that the
runtime did not expose the optional cache counter and must not be interpreted as
zero cache reuse.

Pause, resume, or cancel a Hugging Face model download with `jobs
pause|resume|cancel <job-id>`. Generic job commands reject runtime-build,
runtime-package, and unknown job kinds without changing their state; use the
matching runtime operation for those jobs.

Runtime source work must follow the staged operation flow: run
`runtime-source.check`, then `runtime-source.download`, then
`runtime-build.start` with the downloaded source returned by `runtime.catalog`.
Use `operations run <name> --dry-run --set name=value` before consequential
operations.

Prebuilt package work starts with `runtime-package.check` so the Manager resolves
the newest compatible published release and exact platform/backend asset. Inspect
the returned catalog state before an explicitly authorized
`runtime-package.install`; do not infer release ordering or substitute another
backend.

## Benchmark automation

Discover the versioned plan contract and runtime capabilities before creating a
plan:

```powershell
llwmctl benchmarks schema
llwmctl benchmarks presets
llwmctl benchmarks capabilities <runtime>
llwmctl benchmarks validate --plan <plan.json>
```

Starting a benchmark applies sustained load and requires explicit confirmation.
Run `llwmctl self` and `llwmctl sessions list` first. A plan may stop active
sessions only when its validated plan and the `benchmarkStopActiveSessions`
setting allow it; the CLI still refuses to stop the identified current session
without the user's explicit self-stop authorization.

```powershell
llwmctl benchmarks run --plan <plan.json> --dry-run
llwmctl benchmarks run --plan <plan.json> --confirm --wait
llwmctl benchmarks inspect <run>
llwmctl benchmarks results <run>
llwmctl benchmarks export <run> --format csv
```

Use `pause`, `resume`, or `cancel` for an active benchmark and `compare` for two
compatible persisted runs. Deleting a run also deletes its persisted results and
requires explicit authorization plus `benchmarks delete <run> --confirm`.

## Application settings and UI visibility

`directModelAliasSuffix` is an optional direct-endpoint ID suffix, such as
`-direct`; its default is empty. New loads without an explicit alias advertise
the short GGUF name, and duplicate running IDs receive `:2`, `:3`, etc. Always
read the endpoint's advertised IDs. Existing sessions keep their current IDs
until reloaded. `sameModelLoadPolicy` accepts `ask`, `alongside`, or `replace`
for individual interactive UI loads only; it does not change control or gateway
lifecycle policies or authorize an agent to stop sessions.

Set `gatewayAutoLoadModels=false` to keep the gateway available for already-loaded
profiles only. This disables request-triggered loads and swaps, independently
of `autoLoadGatewayEnabled` and the saved gateway policy. Unloaded profiles are
omitted from discovery and return `503 model_not_loaded` if requested. Manual
lifecycle commands and idle-unload policies still apply.

Patch settings through the running Manager, never its database:

```powershell
llwmctl settings set --set showOverviewHardware=false --set showModelsHuggingFace=true
llwmctl settings set --set uiScalePercent=125 --set fontScalePercent=110
llwmctl settings get
```

`uiScalePercent` adds a bounded application-only scale multiplier on top of
Windows DPI scaling and applies to current and newly opened Manager windows.
`fontScalePercent` uses the same bounded range and applies only to application
text, leaving controls and spacing at their normal size. The UI labels this
setting **Text scale**; the automation field retains its compatibility name.
The visibility fields are `showOverviewModelStatus`,
`showOverviewHardware`, `showOverviewSlots`, `showOverviewTokens`,
`showOverviewMtpTokens`, `showOverviewKvCache`,
`showOverviewLiveRuntimeLog`, `showOverviewAllMetrics`, and
`showModelsHuggingFace`. They apply automatically and do not disable the
underlying telemetry, logs, or downloads.

The default runtime, favorite models/profiles/runtimes, **Load profiles on startup** selections, and
remembered window/table/splitter layouts are UI-owned preferences and are not
part of the current settings-patch contract. Do not edit their SQLite tables or
automate WPF controls to change them.

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

For end-user installation, prefer the installer or portable EXE from
[GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest)
and verify its matching `.sha256` file. Do not describe an unsigned artifact as
trusted or signed.

```powershell
$asset = "LlamaCppWindowsManager.exe"
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
