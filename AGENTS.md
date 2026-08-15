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
llwmctl sessions logs <session>
llwmctl logs list
llwmctl logs tail
llwmctl hf search <query>
llwmctl hf download --repo <owner/repo> --file <path.gguf>
llwmctl jobs list
```

Pause, resume, or cancel a model download with `jobs pause|resume|cancel
<job-id>`.

Runtime source work must follow the staged operation flow: run
`runtime-source.check`, then `runtime-source.download`, then
`runtime-build.start` with the downloaded source returned by `runtime.catalog`.
Use `operations run <name> --dry-run --set name=value` before consequential
operations.

## Application settings and UI visibility

Patch settings through the running Manager, never its database:

```powershell
llwmctl settings set --set showOverviewHardware=false --set showModelsHuggingFace=true
llwmctl settings get
```

The presentation fields are `showOverviewModelStatus`,
`showOverviewHardware`, `showOverviewSlots`, `showOverviewTokens`,
`showOverviewMtpTokens`, `showOverviewKvCache`,
`showOverviewLiveRuntimeLog`, `showOverviewAllMetrics`, and
`showModelsHuggingFace`. They apply automatically and do not disable the
underlying telemetry, logs, or downloads.

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

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1
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
