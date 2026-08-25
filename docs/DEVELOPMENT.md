# Development Guide

Last reviewed: 2026-08-25

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
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/build-app.ps1 -Restore -LockedRestore
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

Localization packs may omit untranslated keys. The runtime deliberately falls
back to the English pack, and the localization contract tests verify placeholder
compatibility for every translated value. Do not copy English text into another
pack merely to satisfy key parity; leave the key absent until it is translated.

## Local Gate

Run these before opening a release PR or after any architecture-level change:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1
```

That wrapper runs the same gate as the individual commands below:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-coverage.ps1
dotnet format LocalLlmConsole.sln --verify-no-changes --no-restore --verbosity minimal
git diff --check
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-vulnerabilities.ps1
```

The coverage gate collects instrumented Debug binaries (public Release binaries intentionally omit PDBs), rejects skipped tests, and requires at least 80% service line
coverage and 95% model/view-model line coverage. WPF composition is additionally
exercised on an STA thread because global coverage is distorted by generated
markup and code-behind. Tests use the .NET 10 Microsoft Testing Platform runner
selected in `global.json`; project-specific commands therefore use `dotnet test
--project <path>`, while a solution-wide run uses `dotnet test --solution
LocalLlmConsole.sln`.

NuGet lock files are committed for every project. CI and release preparation
must restore them in locked mode. Update lock files intentionally with
`dotnet restore LocalLlmConsole.sln --force-evaluate`, review the dependency
diff, then return to `-LockedRestore`.

To include packaging on a machine with publish/installer prerequisites, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

Use `-RequireCleanTree` on `scripts/test-release-gate.ps1`,
`scripts/publish-app.ps1`, or `scripts/build-installer.ps1` when producing release artifacts that must come from a
clean Git worktree.

If `dotnet` is not on `PATH`, set `LLAMA_CPP_WINDOWS_MANAGER_DOTNET` to a .NET
10 SDK `dotnet.exe`.

## Module Layout

The durable rules live in `docs/ARCHITECTURE.md` under "Architecture
Contract". Treat that section as the source of truth when deciding whether a
change belongs in `MainWindow`, a page controller, an application service, a
workflow service, a domain service, or infrastructure.

`src/LocalLlmConsole.Core` targets platform-neutral `net10.0`. It owns all
shared model contracts and reusable behavior that does not require WPF,
Windows-specific APIs, SQLite, or app localization. Keep its dependency graph
closed: `LocalLlmConsole.App` may reference Core, but Core must never reference
the app. The architecture test enforces this rule.

Core feature folders currently contain:

| Folder | Ownership |
| --- | --- |
| `Models` | Shared settings, model/runtime/session/job records, UI row contracts, runtime catalog/package contracts, and telemetry snapshots. |
| `Services/App` | Portable preference and access policy normalization. |
| `Services/HuggingFace` | README/config/command launch-setting suggestion parsing. |
| `Services/Infrastructure` | Platform-neutral display formatting only. |
| `Services/Models` | Pure model allocation policy. |
| `Services/Runtimes` | Endpoint addressing, launch parsing/options, package selection, runtime/session decisions, and telemetry/dashboard policy. |

Top-level WPF-app `Services` files are reserved for composition/root wiring:

- `AppServiceFactory*.cs`
- `MainWindowServices.cs`
  - Defines infrastructure, core, and loaded service bundles by feature.
  - Keep new dependencies in the narrowest matching bundle rather than adding
    another top-level constructor parameter.

Windows, storage, network-hosting, localization, and UI-facing implementation
services remain under `src/LocalLlmConsole.App` feature modules:

| Folder | Ownership |
| --- | --- |
| `Services/App` | App settings, startup/shutdown, updates, logs, help, cache, and shared app workflows. |
| `Services/Environment` | Windows and WSL detection, setup command planning, and visible tool setup launchers. |
| `Services/Gateway` | Local model gateway host/runtime contracts and gateway activity state. |
| `Services/HuggingFace` | Hugging Face search, metadata, download safety, and download history. |
| `Services/Infrastructure` | State store, local app service, process runner, filesystem/config safety, dialogs, jobs, and shell helpers. |
| `Services/Models` | Model catalog, model capabilities, aliases, model launch profiles, and model deletion/import behavior. |
| `Services/Runtimes` | Runtime registry, source/build jobs, launch execution, sessions, metric polling, readiness, and process supervision. |

Runtime readiness is also the authentication-policy boundary. Once an endpoint
responds, a non-inference probe must prove the configured policy before the
session is marked loaded: protected endpoints reject an unauthenticated request
and accept the configured model API key, while explicitly unauthenticated
Local-only endpoints accept the credential-free probe. Keep public upstream
health/catalog behavior separate from this route check.

The control API host and router remain in `Services/Control/LocalControlApi.cs`;
focused handlers own model, profile, group, runtime, session/gateway/metrics,
settings, logs, jobs/Hugging Face, and operation routes, while
`ControlEndpointHandler` owns shared request parsing and response projection.
The host is not partial and does not implement domain endpoints.
Application-wide WPF resources are composed from the focused dictionaries under
`Themes`.

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
- Page-specific row/event routing belongs in page controllers. Models, launch
  settings, Hugging Face download history, Runtimes, Windows, WSL, Overview,
  Logs, Lifetime, and Settings pages already follow this pattern.
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
- Production C# files must stay at or below 425 lines and test C# files at or
  below 675 lines. The architecture test enforces both limits. Split by stable
  responsibility before reaching the limit; do not create numbered part files.

## Test Guidance

Prefer behavior tests over source-shape tests. Source-shape tests are acceptable
for architectural guardrails, but they should check durable boundaries, not
fragile line-by-line implementation details.

The coverage gate includes both Core and App source paths. A new Core service
must remain behavior-tested even though it is consumed through the WPF app.

Useful test groups:

- `ReleaseHardening.Architecture.Tests.cs`: module layout guardrails.
- `ReleaseHardening.RuntimeProcessLifecycle.Tests.cs` and
  `ReleaseHardening.RuntimeAdapter.Tests.cs`: process and launch behavior.
- `ReleaseHardening.RuntimeMetricParsing.Tests.cs` and
  `ReleaseHardening.RuntimeTelemetry.Tests.cs`: metric parsing, state, and UI
  application behavior.
- `ReleaseHardening.ModelCompanions.Tests.cs`,
  `ReleaseHardening.DownloadSafety.Tests.cs`, and
  `ReleaseHardening.ModelCatalogIntegrity.Tests.cs`: model and download safety.
- `ReleaseHardening.UiShell.Tests.cs`,
  `ReleaseHardening.UiThemesAndLayout.Tests.cs`, and
  `ReleaseHardening.UiApplicationBoundaries.Tests.cs`: shell, theme, layout,
  view-model, and application-boundary invariants.
- `FakeRuntimeIntegration.Tests.cs`: real process supervision against the
  deterministic test runtime under `tests/LocalLlmConsole.FakeRuntime`.

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

The versioned Overview dashboard layout stores generic card containers whose
v2 contents are atomic metrics such as CPU, RAM, an indexed GPU, a token rate,
or a slot counter. Version 3 adds bounded free-form card geometry using a
responsive 12-unit horizontal surface and device-independent vertical pixels.
Version 4 adds independent charts. Version 5 removes rate rows that cannot be supplied dependably on every
poll and migrates their chart selections to the corresponding average rates.
Version 8 adds a dashboard-wide fixed-card-size mode using the surface width
captured when the user locks the layout.
Version 9 adds optional bounded card titles; untitled cards retain the compact
headerless rendering. Metric-row values are content-sized so labels wrap only
after the value and unit consume their measured width.
Version 10 adds the curated Core, Hardware, Energy, Gateway, Advanced, and Raw
catalog. Optional process, GPU, and gateway rows are registered only after an
observation. Keep deprecated IDs resolvable for saved layouts but hide them from
the picker, and never chart static configuration or cumulative counters.
Version 1 composite metrics, v2 packed layouts, and v3 singular chart choices
are migrated during normalization. WPF owns geometric border hit areas,
content minimums, snapping, minimum spacing, and rendering.
Version 11 makes the production cards responsive, equal-width, and unlocked by
default. After hardware discovery, the application adds the GPU template only
for discrete GPUs and omits GPU core clock from that template; integrated-GPU
and core-clock metrics remain available for custom cards. Additional GPU cards
wrap onto later rows. Custom layouts outside this default family are not
rewritten. Version 12 gives every production-default card the same compact
height and charts GPU utilization without charting GPU power draw; power remains
available as a live value. The live runtime log defaults to visible with newest
entries first. The persisted Runtime log order preference can instead present
chronological entries with the newest entry at the bottom.
The six legacy status-card switches remain compatibility projections
into metric-group presence in that layout. The dense
raw metrics table and Models Hugging Face section are default-hidden. Add future
dashboard metrics through the registry and layout policy, and provide separate
value/unit/detail readings for the semantic row renderer rather than adding more
card-specific settings, free-form line parsing, controls, or `MainWindow` fields.

## Documentation Guidance

When behavior changes, update both the repo docs and in-app Help in the same
pass:

- `README.md` for the public feature overview, quick start, safety defaults,
  and distribution behavior.
- `docs/ARCHITECTURE.md` for module ownership, serving topology, and durable
  architectural guardrails.
- `docs/RELEASE_READINESS.md` for manual validation steps and latest verified
  command results.
- GitHub release drafts for unreleased user-visible changes; do not keep
  copy/paste release notes or internal working notes in the source tree.
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
