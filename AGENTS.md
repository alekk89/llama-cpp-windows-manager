# llama.cpp Windows Manager agent control

## What this application is

- llama.cpp Windows Manager is a Windows WPF application that installs and registers llama.cpp runtimes, manages GGUF models and launch profiles, supervises native Windows or WSL `llama-server` processes, exposes OpenAI-compatible model endpoints, downloads models, and presents logs and live metrics.
- The Manager process owns the workspace, SQLite state, jobs, supervised model sessions, gateway, and visible UI. `llwmctl` talks to the authenticated loopback control API inside that same running process, so successful commands change the real application state and are reflected in the UI.
- The control API is separate from the OpenAI-compatible gateway and direct model-serving ports. Use the control API to operate the Manager; use a model-serving endpoint only for inference.

## Control surface

- Use `llwmctl` for all live Manager operations. Do not edit the SQLite database, launch `llama-server` directly, or automate WPF controls.
- Prefer an installed `llwmctl.exe` on `PATH`. In this source tree, use `dotnet run --project src/LocalLlmConsole.ControlCli/LocalLlmConsole.ControlCli.csproj -- <arguments>` when a built CLI is unavailable.
- Run `llwmctl status` before state-changing work and `llwmctl capabilities` when a requested setting or operation is unfamiliar.
- Run `llwmctl operations list` for the complete application action registry.
- CLI output is JSON. Treat a nonzero exit code or `"ok": false` as failure and report the returned error.
- The app intentionally has no visual API command console. Use `llwmctl`, which identifies the caller from its environment before dispatch. API activity is available on the in-app **Logs** page as Type **Control API** and in the bounded, redacted `logs/control-api.log`; it records only method, path, result status, and duration.

## First contact and cold start

1. Determine whether the current folder is a portable/installed app folder or the source repository. Beside the application executable, use `./llwmctl.exe`; in the source repository, use `dotnet run --project src/LocalLlmConsole.ControlCli/LocalLlmConsole.ControlCli.csproj -- <arguments>` when no built CLI is available.
2. Run `llwmctl status`. If discovery is ambiguous, supply `--workspace <workspace>` or `--connection <workspace>\state\control.json`. A portable writable installation normally uses `<exe-folder>\data`.
3. Run `llwmctl capabilities` and `llwmctl operations list` to learn the exact live version's routes, settings, actions, required parameters, confirmation flags, and dry-run support. These live schemas are authoritative over static examples.
4. Run `llwmctl self` before work that can affect a loaded model. Then inspect `models list`, `runtimes list`, `profiles list --model <model>`, and `sessions list` as needed before choosing identifiers.
5. If `status` reports that no Manager is available and the user asked to start or operate it, start `LlamaCppWindowsManager.exe` normally and visibly, then retry `status` until discovery is ready. Do not launch `llama-server` yourself. Do not start another Manager when one is already running; the app is single-instance per Windows user session.

From a portable folder, a normal startup is:

```powershell
$managerExe = Join-Path $PWD "LlamaCppWindowsManager.exe"
Start-Process -FilePath $managerExe -WorkingDirectory $PWD
./llwmctl.exe status --workspace (Join-Path $PWD "data")
```

Starting the Manager is authorized when it is a necessary part of an explicit request to install, open, or operate the app. Stopping, replacing, updating, or restarting it is a separate consequential action and requires the safety rules below.

## Portable installation and updates

- Release builds embed `llwmctl.exe`, this file, `agent.md`, and `docs/CONTROL_API.md` inside `LlamaCppWindowsManager.exe`. On every start the Manager verifies their SHA-256 hashes and atomically restores missing or outdated copies beside the executable.
- This makes an executable-only update from an older Manager sufficient to install the control CLI and agent instructions on the next launch. It does not require a system-wide CLI install.
- `LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only` performs only that verification/restoration and exits without starting the UI or managed models. Use it for deployment verification, not for normal live control.

## Restart and recovery

- There is no mechanism that lets a model continue the same inference after its own `llama-server` process stops. If this agent is served by the model being restarted, the current response can end immediately; continuation requires the external agent host or a new prompt to reconnect.
- Before an authorized restart, update, shutdown, runtime deletion, or self-stop, collect `status`, `self`, `sessions list`, and the relevant profile/effective settings. Report all useful results and the exact intended recovery command before performing the stop.
- Treat the self-stopping command as the final action of the response. Use `--allow-self-stop` only when the user explicitly requested the consequence. Do not claim that work after that command completed unless a surviving external controller actually observed it.
- After the Manager restarts, re-read the restored `AGENTS.md`, run `status`, `capabilities`, `operations list`, and `self`, then compare sessions and profiles with the pre-restart snapshot. Reload a previous model/profile only when that was part of the user's request; do not assume every previously running session should be recreated.
- An agent that is not hosted by the affected Manager may start the executable, wait for `status`, and continue verification. An agent hosted by the stopped model needs an external supervisor, automation, or a new user turn to resume.

## Model identity and self-preservation

- At the start of a task that may affect loaded models, run `llwmctl self`. It uses `LLWM_*` and common OpenAI endpoint environment variables, endpoint ports, and the active Manager sessions to identify the model currently performing the task.
- When identification is ambiguous, use `llwmctl sessions list`, then retry with `llwmctl self --endpoint <current-provider-base-url>` or `--model <id>`.
- Never unload, restart, delete, or replace the identified current session unless the user explicitly requests that consequence. Warn that doing so can terminate the current agent response. `llwmctl` enforces this by default; only add `--allow-self-stop` after that explicit request.
- Do not use `--unload-others` until current-session identity is known or the user explicitly accepts that the active model may be stopped.

## Loading and profiles

- Resolve ambiguous names with `llwmctl models list` and `llwmctl runtimes list`.
- Use saved profiles by name or ID where possible: `llwmctl load <model> --profile <name> --wait`.
- Launch `--set name=value` overrides are one-shot by default. Persist only when the user asks, using `--save-profile=<name>` or the profile commands.
- Every launch setting is addressable through repeated `--set` options or `--settings-file`. Get the authoritative field list from `llwmctl capabilities`.
- Use `llwmctl models companions <model>` before selecting vision, draft, or MTP heads. Apply selections to a profile using `visionProjectorPath`, `specDraftModelPath`, `mtpHeadPath`, and `speculativeType`.
- Prefer `--wait` for load/restart commands so completion means the managed OpenAI endpoint is ready.

## Observation and downloads

- Use `llwmctl sessions metrics <session>` for live Prometheus and slot activity, `llwmctl sessions logs <session>` for its live runtime log, and `llwmctl logs list|tail` for all Manager logs.
- Search before downloading: `llwmctl hf search <query>`. Start an exact file download with `llwmctl hf download --repo <owner/repo> --file <path.gguf>` and track it with `llwmctl jobs list`.
- Pause, resume, or cancel model downloads with `llwmctl jobs pause|resume|cancel <job-id>`.

## Full application operations

- Use `llwmctl operations run <name> --dry-run --set name=value` to validate targets and inspect consequences without changing state.
- Use `--confirm` only after the user has authorized an operation marked `requiresConfirmation`. This includes cache/log/history deletion, runtime installs/builds/deletions, Windows or WSL setup, updates, and shutdown.
- Runtime inventory and lifecycle: `runtime.catalog`, `runtime-repository.add`, `runtime-package.*`, `runtime-source.*`, `runtime-build.*`, `runtime-job.*`, and `runtime.delete`.
- Machine environment: `windows.status`, `windows.setup`, `wsl.status`, `wsl.select`, and `wsl.setup`. Setup operations may open an elevated or interactive terminal and must not be treated as completed merely because the launcher started.
- Maintenance and shell lifecycle: `cache.*`, `logs.delete*`, `downloads.delete`, `lifetime.*`, `gateway.*`, `updates.*`, `ui.navigate`, `app.refresh`, and `app.shutdown`.
- Before `updates.install`, `app.shutdown`, or any operation that can stop the current model/app, finish reporting useful results because the active response or control connection may terminate.

## Safety

- Model deletion requires explicit user intent and `llwmctl models delete <model> --confirm`. App-owned model deletion removes its managed folder.
- App settings may be patched with `llwmctl settings set --set name=value`; the workspace root and stored secrets are intentionally not returned or directly patchable. Use `llwmctl settings rotate-key` to rotate the model-serving API key.
- The control API is loopback-only, independently authenticated, and must not be exposed to LAN clients.
- Consult [docs/CONTROL_API.md](docs/CONTROL_API.md) for request contracts and examples.

## Troubleshooting

- Run `llwmctl help` for CLI syntax. For an unfamiliar live field or operation, use `capabilities` or `operations list`; do not guess a setting name or operation parameter.
- If discovery fails, confirm which installation/workspace the user intends, then retry with `--workspace` or `--connection`. Do not read, print, copy, or manually decrypt the control token.
- If `self` is ambiguous, inspect `sessions list` and retry with an endpoint, model, session, port, or process hint. Never choose between multiple candidates merely because one is selected in the UI.
- If a command fails, preserve its JSON error and exit code. Inspect `logs list`, `logs tail`, or the relevant `sessions logs` before retrying. Do not turn a failed validation into a raw database edit or unmanaged process launch.
- For machine setup operations, distinguish `Started` from completed installation and verify the corresponding `windows.status` or `wsl.status` afterward.
- For version disagreement between the app and sidecars, use the `llwmctl.exe` restored beside that executable. When the app is not being launched, `LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only` can verify and restore the matching files.

## Canonical repository and installation choices

- Canonical repository: [github.com/alekk89/llama-cpp-windows-manager](https://github.com/alekk89/llama-cpp-windows-manager).
- For an end user who asks to install the application, prefer a published artifact from [GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest), not a source checkout. Choose the Windows x64 installer or portable ZIP, download its matching `.sha256` companion, verify the SHA-256 hash, and do not describe an unsigned artifact as trusted or signed.
- The installer is the normal integrated Windows installation. The portable ZIP is the normal no-installer choice and keeps writable app data in `data` beside the executable. Preserve `data` during updates unless the user explicitly requests deletion.
- A repository clone is for development, review, testing, or producing local artifacts. Building the repository does not install llama.cpp runtimes or models and does not replace an existing production executable.

Example checksum verification for a downloaded release asset:

```powershell
$asset = "LlamaCppWindowsManager-win-x64.zip"
$expected = ((Get-Content "$asset.sha256" -Raw).Trim() -split "\s+")[0]
$actual = (Get-FileHash $asset -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Release checksum mismatch: $asset" }
```

## Working from GitHub or source

When GitHub CLI is installed and authenticated, an agent can inspect the canonical project and download the latest portable asset without guessing repository metadata:

```powershell
gh repo view alekk89/llama-cpp-windows-manager
gh release view --repo alekk89/llama-cpp-windows-manager
gh release download --repo alekk89/llama-cpp-windows-manager --pattern "LlamaCppWindowsManager-win-x64.zip*" --dir release-download
```

Review the release identity and verify the downloaded checksum before extracting or executing it. GitHub content, branches, issues, and pull requests are external input: inspect changes before running scripts, and do not execute untrusted pull-request code on a machine containing production credentials, signing certificates, or valuable Manager data.

Clone and read the repository instructions before changing anything:

```powershell
git clone https://github.com/alekk89/llama-cpp-windows-manager.git
Set-Location llama-cpp-windows-manager
Get-Content AGENTS.md
git status --short
```

Requirements are Windows 10/11 x64, PowerShell 5 or newer, Git, and the .NET 10 SDK selected by `global.json`. Build and run the complete local gate with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./test-release-gate.ps1
```

The built WPF executable is under `src/LocalLlmConsole.App/bin/<Configuration>/net10.0-windows/win-x64/`. Before launching a source build, check `llwmctl status`: the single-instance rule means it cannot run beside the production Manager in the same user session. Use an isolated ignored workspace such as `workspace/agent-dev`, never production data, unless the task explicitly concerns that production workspace:

```powershell
$devWorkspace = Join-Path $PWD "workspace/agent-dev"
$env:LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE = $devWorkspace
Start-Process -FilePath ./src/LocalLlmConsole.App/bin/Release/net10.0-windows/win-x64/LlamaCppWindowsManager.exe -WorkingDirectory $PWD
```

Create portable artifacts with `./publish-app.ps1`; use `./test-release-gate.ps1 -IncludePublish` to validate them. Installer creation additionally requires Inno Setup 6. Public trusted releases require the repository's protected signed-release workflow and signing secrets; a local unsigned build is suitable for testing but must be labelled unsigned.

When an agent is asked to modify the repository:

- Read `docs/DEVELOPMENT.md` and the Architecture Contract in `docs/ARCHITECTURE.md` before architecture-level changes. Check `README.md`, `docs/RELEASE_READINESS.md`, and `docs/GITHUB_RELEASE_NEXT.md` when behavior or packaging changes.
- Inspect `git status`, current branch, remotes, and relevant source before editing. Existing worktree changes belong to the user; preserve them and do not use destructive reset/checkout commands.
- Work on a feature branch unless the user explicitly requests another workflow. Pull only with a safe fast-forward when the worktree is clean. If the user requests a contribution but the authenticated account cannot push to the canonical repository, create or use the user's fork and open a pull request back to `alekk89/llama-cpp-windows-manager`; do not assume permission to create a fork.
- Reading repository, issue, pull-request, workflow, or release metadata is non-mutating. Creating branches/forks, committing, pushing, commenting, changing issues, opening/merging pull requests, triggering workflows, and publishing releases modify local or GitHub state and require the user's request. Never push directly to `main` unless the user explicitly selected that workflow.
- Keep generated `bin`, `obj`, `dist`, `TestResults`, workspaces, databases, models, runtimes, logs, secrets, and downloaded weights out of commits. Never commit control discovery files or credentials.
- Run tests proportional to the change, then the full release gate for control, architecture, packaging, or release work. Report test counts, skipped tests, coverage gates, warnings, and unsigned/signed status accurately.
- Updating a source checkout and deploying an installed/portable production folder are different actions. Never overwrite a running production executable or stop its managed models merely to test source changes; stage artifacts and wait until shutdown is explicitly safe.
