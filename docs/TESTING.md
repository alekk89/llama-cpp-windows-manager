# Testing

Test count is not a target. Keep a case when it protects a distinct behavior,
failure mode, or boundary. Parameterize related inputs for maintainability;
do not combine unrelated scenarios merely to reduce the reported count.

## Running and measuring

During development, run the affected classes:

```powershell
dotnet test --project tests/LocalLlmConsole.Tests --no-restore --filter-class '*DownloadTransferTests'
```

Before completing control, runtime, architecture, or release work, run the full
[development gate](DEVELOPMENT.md#local-gate). Focused runs do not replace it,
and the separate uninstrumented updater-handoff check remains required.

The coverage gate rejects skipped tests and requires 80% service, 95%
model/view-model, and 80% Control CLI line coverage. It merges App/Core/CLI lines
across both test projects while retaining project identity and excluding generated
sources. Reports are parsed once. `coverage-by-file.csv` records covered/missed
lines per file; use it to prioritize consequential uncovered branches instead
of increasing global thresholds. Coverage is evidence of execution, not proof
that assertions detect faulty behavior.

An existing report directory can be remeasured without rerunning tests:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/measure-test-coverage.ps1 -ResultsRoot TestResults/coverage-<run-id>
```

This only measures stored artifacts; it does not establish that the current
checkout passes. `CoverageGateTests` runs the actual measurement script against
synthetic reports to verify cross-project identity, merged hits, generated-source
exclusion, missing scopes, and threshold failures.

## Workspace ownership

`ManagerRegressionTestBase` owns a lazily created workspace for each test.
`CreateTempRoot()` allocates isolated subdirectories in that workspace, including
when called by static helpers during test execution. Do not allocate workspaces
from discovery/data-provider methods.

Successful tests release only their own SQLite pools and delete their own
directories. Always dispose stores, streams, and processes before the test
returns. An actual Windows file-lock failure gets a bounded cleanup retry;
persistent cleanup failures fail the test and report the retained path.
Failed or incomplete tests retain their workspace and print its location to test
output. No teardown sweeps other tests' workspaces or historical temporary files.
The WPF suite retains its existing serialized STA execution and isolated process
workspace.

## Assertions and test boundaries

- Prefer results and observable side effects: saved settings, registered models,
  exact transferred bytes, rejected unsafe requests, and released resources.
- Preserve semantic architecture checks for Core's platform boundary and
  compiled dependency restrictions. Source inspection is appropriate for durable
  packaging/security/layout requirements that cannot be exercised behaviorally.
- Do not assert exact local variable names, delegation statements, or source-line
  ordering. Runtime readiness, telemetry, and status tests exercise their compiled
  workflows/controllers; the redundant source inventories have been removed.
- Help contracts check unique identifiers, nonempty content, search, and valid
  navigation. Localization contracts check required keys, placeholders, fallback,
  and declared translation-quality floors, without fixed article/key counts or
  failing when a preview translation improves.
- `DownloadTransferTests` runs the real worker against an injected HTTP handler:
  truncated/oversized/denied transfers, pause versus stop, duplicate destinations,
  range resume, server restart, verification, and registration. No external
  download or model inference is needed.
- `ControlMaintenanceTests` exercises real temporary storage and workflow
  dispatch with inert process/update actions. `ControlOperationConfirmationTests`
  checks the API confirmation boundary for every consequential operation.
- `ControlCliContractsTests` checks wire requests, escaped identifiers, typed
  payloads, validation, and benchmark wait/error behavior.
- Readiness tests inject a delay function. Production still waits its default
  two seconds; tests verify that interval, retries, and cancellation without
  spending wall-clock time waiting for it. Keep real process/timer integration
  checks where operating-system behavior is what the test protects.

When replacing a test, identify the behavior that survives in existing or new
coverage. For high-risk logic, a small deliberate fault that makes the relevant
test fail is stronger evidence than a larger passing test count.
