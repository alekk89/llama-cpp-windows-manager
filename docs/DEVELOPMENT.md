# Development Guide

Last reviewed: 2026-08-15

This repo is a Windows-first .NET 10 WPF app. The app should stay easy to run
from source, but end users should receive the published portable app or
installer from `dist`.

## Repository Onboarding

The canonical repository is <https://github.com/alekk89/llama-cpp-windows-manager>.
End users should install a checksum-verified artifact from GitHub Releases;
cloning the repository is the development path and does not install the app,
models, or llama.cpp runtimes.

```powershell
git clone https://github.com/alekk89/llama-cpp-windows-manager.git
Set-Location llama-cpp-windows-manager
Get-Content AGENTS.md
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./build-app.ps1 -Restore
```

Before launching a source build, check whether the single-instance production
Manager is already running. Builds and tests are safe to run alongside it, but
a second UI process cannot run in the same Windows user session. Use the ignored
`workspace` folder for development state rather than a production workspace.

For GitHub contributions, preserve existing worktree changes, use a feature
branch, keep generated output and local state out of commits, and run the local
gate below. Committing, pushing, opening a pull request, or publishing a release
are separate actions that require the user's authorization. Public trusted
releases must use the protected signed-release workflow; never label an
unsigned local artifact as signed or trusted.

Before committing, inspect `git status --short` and make sure every intended
source, test, manifest, license, and documentation file is tracked. Local builds
compile SDK-globbed untracked `.cs` files, but CI and reviewers cannot see them;
a green local test run is therefore not sufficient while required files still
appear with `??`. Generated `dist`, `bin`, `obj`, workspaces, databases, logs,
models, runtimes, credentials, and signing material must remain untracked.

Treat code from external branches and pull requests as untrusted until it has
been reviewed. Do not execute an untrusted contribution on a machine containing
production Manager data, signing certificates, or release credentials. If a
contributor cannot push to the canonical repository, use their fork and a pull
request only after that GitHub mutation has been requested.

## Local Gate

Run these before opening a release PR or after any architecture-level change:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1
```

That wrapper runs the same gate as the individual commands below:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-coverage.ps1
dotnet format LocalLlmConsole.sln --verify-no-changes --no-restore --verbosity minimal
git diff --check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-vulnerabilities.ps1
```

The coverage gate collects instrumented Debug binaries (public Release binaries intentionally omit PDBs), rejects skipped tests, and requires at least 80% service line
coverage and 95% model/view-model line coverage. WPF composition is additionally
exercised on an STA thread because global coverage is distorted by generated
markup and code-behind. Tests use the .NET 10 Microsoft Testing Platform runner
selected in `global.json`; project-specific commands therefore use `dotnet test
--project <path>`, while a solution-wide run uses `dotnet test --solution
LocalLlmConsole.sln`.

To include packaging on a machine with publish/installer prerequisites, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

Use `-RequireCleanTree` on `test-release-gate.ps1`, `publish-app.ps1`, or
`build-installer.ps1` when producing release artifacts that must come from a
clean Git worktree.

If `dotnet` is not on `PATH`, set `LLAMA_CPP_WINDOWS_MANAGER_DOTNET` to a .NET
10 SDK `dotnet.exe`.

## Module Layout

The durable rules live in `docs/ARCHITECTURE.md` under "Architecture
Contract". Treat that section as the source of truth when deciding whether a
change belongs in `MainWindow`, a page controller, an application service, a
workflow service, a domain service, or infrastructure.

Top-level `Services` files are reserved for composition/root wiring:

- `AppServiceFactory*.cs`
- `MainWindowServices.cs`
  - Defines infrastructure, core, and loaded service bundles by feature.
  - Keep new dependencies in the narrowest matching bundle rather than adding
    another top-level constructor parameter.

Implementation services live under feature modules:

| Folder | Ownership |
| --- | --- |
| `Services/App` | App settings, startup/shutdown, updates, logs, help, cache, and shared app workflows. |
| `Services/Environment` | Windows and WSL detection, setup command planning, and visible tool setup launchers. |
| `Services/Gateway` | Local model gateway host/runtime contracts and gateway activity state. |
| `Services/HuggingFace` | Hugging Face search, metadata, download safety, download history, and launch suggestions. |
| `Services/Infrastructure` | State store, local app service, process runner, filesystem/config safety, dialogs, jobs, formatting, and shell helpers. |
| `Services/Models` | Model catalog, model capabilities, aliases, model launch profiles, and model deletion/import behavior. |
| `Services/Runtimes` | Runtime registry, packages, source/build jobs, launch validation, sessions, metrics, readiness, and process supervision. |

UI factories and page state live under:

- `Ui/Common`
- `Ui/Pages/<Feature>`

The current code keeps file-scoped namespaces stable. Namespace tightening can
happen module-by-module after behavior is settled.

## Service Naming

Use these names consistently:

- `WorkflowService`: owns a domain sequence or business workflow.
- `ApplicationService`: adapts a workflow to UI-facing actions and status.
- `Controller`: owns stateful UI coordination, timers, reentrancy, or lifecycle state.
- `Factory`: constructs controls or services.
- `State`: stores control references or page/session state without business rules.

Avoid adding a new service for a single pass-through method. Prefer extending an
existing feature service unless the new type owns a real decision, state, or
boundary.

## MainWindow Direction

`MainWindow` is the shell, navigation host, app lifetime coordinator, and event
broker. Keep feature behavior in services, workflow/application services, page
state, view models, or page controllers.

Use these rules when touching `MainWindow`:

- Persistent fields should be shell state, service bundles, loaded-service
  lifecycle holders, page state, or page-controller bundles.
- Raw WPF control references should stay grouped behind page state objects.
- Core services should be reached through named bundles such as
  `_coreServices.App`, `_coreServices.Ui`, `_coreServices.Models`,
  `_coreServices.Runtime`,
  `_coreServices.HuggingFaceServices`, and `_coreServices.Environment`.
- Loaded services should be reached through `AppServices`, `ModelServices`,
  `GatewayServices`, and `RuntimeServices`. Do not add flat pass-through aliases
  to `MainWindowLoadedServices`.
- Page-specific row/event routing belongs in page controllers. Models, Hugging
  Face download history, Runtimes, Windows, WSL, Overview, Logs,
  Lifetime, and Settings pages already follow this pattern.
- Runtime control-API dispatch and workflow composition belong in
  `ControlRuntimeOperationApplicationService`; theme resource mutation belongs
  in `ApplicationThemeService`; shared visual-tree and accessibility helpers
  belong under `Ui/Common`.
- Keep each `MainWindow*.cs` shell adapter at or below 300 nonblank lines. The
  architecture test enforces this limit and rejects reintroduced UI factories,
  theme policy, or runtime control workflows in the window.
- Keep `ModelGroupDialogFactory` split by dialog responsibility; each partial is
  limited to 300 nonblank lines by the architecture test.
- Empty placeholder partials should be deleted.

## Test Guidance

Prefer behavior tests over source-shape tests. Source-shape tests are acceptable
for architectural guardrails, but they should check durable boundaries, not
fragile line-by-line implementation details.

Useful test groups:

- `ReleaseHardening.Architecture.Tests.cs`: module layout guardrails.
- `ReleaseHardening.Runtime.Tests.cs`: runtime/session/metrics/build behavior.
- `ReleaseHardening.HuggingFace.Tests.cs`: search/download/safety behavior.
- `ReleaseHardening.Ui.Tests.cs`: view model and UI composition invariants.

### Adding an app-level UI preference

Overview visibility is app state, not a model launch option. A new persistent
UI preference requires all of the following:

1. Add a backward-compatible defaulted property to `AppSettings`.
2. Add its Settings row in `SettingsPageDefinitionService` and parse it in
   `AppSettingsUpdateService`.
3. Add both read and write mappings in `StateStore.Settings`; the record property
   alone does not persist an individual SQLite settings key.
4. Apply it through the relevant page state so automatic Settings persistence
   updates the already-running page without requiring a restart.
5. Keep hidden telemetry presentation-only unless the product requirement
   explicitly changes collection behavior.
6. Add update-service, SQLite save/reload, WPF visibility/reflow, localization,
   control-schema, Help, and release-readiness coverage.

The six Overview status-card switches and live runtime log are default-`true`.
The dense raw metrics table and Models Hugging Face section are default-`false`.
Choose defaults deliberately when adding future optional surfaces.

## Documentation Guidance

When behavior changes, update both the repo docs and in-app Help in the same
pass:

- `README.md` for the public feature overview, quick start, safety defaults,
  and distribution behavior.
- `docs/ARCHITECTURE.md` for module ownership, serving topology, and durable
  architectural guardrails.
- `docs/RELEASE_READINESS.md` for manual validation steps and latest verified
  command results.
- `docs/GITHUB_RELEASE_NEXT.md` for unreleased user-visible changes.
- `AGENTS.md`, `agent.md`, and `docs/CONTROL_API.md` when an automation-facing
  field or operation changes; these are embedded release sidecars.
- `Services/App/HelpCatalogService.cs` for concise Help topics and search terms,
  plus `Ui/Pages/Help/*` for Help search, presentation, and navigation behavior.

Prefer describing current behavior over refactor history. Historical release
notes should stay historically accurate and point to the next-release notes for
newer behavior.

## Generated Output

Generated output is ignored and should stay out of commits:

- `bin`
- `obj`
- `dist`
- `TestResults`
- local `data`, `models`, `runtimes`, `cache`, `state`, and `logs`
