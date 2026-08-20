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
