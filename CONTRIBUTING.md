# Contributing

Thanks for helping improve llama.cpp Windows Manager.

## Before starting

Search existing issues, keep a change focused, and explain the user problem it
solves. Security reports belong in the private process described in
[SECURITY.md](SECURITY.md).

Development requires Windows 10 or 11 x64, PowerShell 5+, and the .NET 10 SDK
selected by `global.json`. Read [AGENTS.md](AGENTS.md),
[Development](docs/DEVELOPMENT.md), and [Architecture](docs/ARCHITECTURE.md)
before changing runtime, control API, persistence, packaging, or release code.

```powershell
git clone https://github.com/alekk89/llama-cpp-windows-manager.git
Set-Location llama-cpp-windows-manager
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Restore -LockedRestore
```

Use a feature branch. Preserve unrelated worktree changes and never commit
models, runtimes, databases, logs, credentials, generated workspaces, `bin`,
`obj`, `TestResults`, or `dist`.

## Issues and pull requests

Use one focused branch and pull request per issue or feature. Link the primary
GitHub issue in the PR; create an issue first when the work has no existing one.
Keep the implementation, relevant tests, documentation, and release-note entry
for that issue together. Do not bundle unrelated fixes or features because they
are intended for the same release. These rules apply to maintainers and coding
agents as well as external contributors.

A large release can contain any number of completed issue PRs. For a substantial
feature, use a tracking issue and split independently reviewable work into linked
child issues and PRs where practical. A larger PR is appropriate when one coherent
change cannot safely be split; explain why and provide a review and validation
plan. There is no arbitrary line-count or release-size limit.

Each PR should state the problem, resulting behavior, compatibility risks, and
actual validation performed. Use `Closes #<issue>` only when the issue's acceptance
criteria are met; use a reference for a partial contribution. Dependency links
must name the prerequisite PRs and intended merge order. Do not merge a dependent
PR until its prerequisites are merged and it has been checked against `main`.

## Compatibility before merge

Keep `main` usable. Before merging, update the PR with the latest `main`, resolve
conflicts deliberately, and rerun required checks. The strict up-to-date branch
requirement in [Repository governance](docs/REPOSITORY_GOVERNANCE.md) enforces this:
when another PR merges, the next PR must update and pass again. Do not bypass the
checks or force-push `main`. Passing separate checks on stale branches does not
establish that the combined changes work.

Identify shared contracts in the PR: settings and saved profiles, database
migrations, control/API responses, gateway behavior, runtime arguments, and
installer/update behavior. When two changes interact, add or update behavioral
integration tests covering their combined behavior. Preserve existing user data
and supported clients, or explicitly document and test the migration. Resolving
text conflicts alone is not a compatibility check.

Record hardware, WSL, UI, or other manual checks that CI cannot establish. A
failed or intermittent check needs diagnosis and useful logs; do not repeatedly
rerun it solely to obtain a green result. Unfinished or incompatible work stays
on its branch until it can be integrated safely.

## Release-note entries

For user-facing changes, add a short bullet to a file named after the issue in
[docs/releases/unreleased](docs/releases/unreleased/README.md), for example
`51.md`. Describe the behavior users gain or the problem fixed. Include any
upgrade or compatibility action users need to take. Docs-only or internal changes
may omit the entry; explain why in the PR. Do not bump the app version in each
feature PR or rewrite notes for an already published release.

Release scope, cadence, and final combined validation are defined in
[Repository governance](docs/REPOSITORY_GOVERNANCE.md#release-planning).

## Change guidelines

- Keep `MainWindow` as shell coordination; put behavior in the appropriate
  feature service, workflow, application service, controller, or view model.
- Keep the control API loopback-only and use `llwmctl` for live operations.
- Preserve profiles when a model file temporarily disappears.
- Add behavioral tests for fixes. Source-shape tests are only architecture
  guardrails.
- Update the README, in-app Help, control/operator docs, and release-readiness
  checks when user-visible or automation-visible behavior changes.
- Do not describe local or public artifacts as signed unless a verifiable
  Authenticode signature was produced by the protected signing workflow.

## Validate the change

Run the full local gate for architecture, control, runtime, packaging, or release
work:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1
```

Before opening a pull request, also inspect `git status --short` and
`git diff --check`. The pull request should describe behavior, risk, tests, and
any manual validation still required.
