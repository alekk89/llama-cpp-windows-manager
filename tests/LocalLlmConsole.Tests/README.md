# Test suite structure

The test project is organized by production responsibility rather than release
history or test count:

| Folder | Scope |
| --- | --- |
| `App` | startup, shutdown, settings, updates, localization, logs, storage, and app infrastructure |
| `Architecture` | compiled dependency rules and durable repository layout contracts |
| `Control` | control API routing, request admission, settings, operations, and endpoint inspection |
| `Environment` | Windows and WSL detection, setup, and tool workflows |
| `Gateway` | gateway transport, scheduling, authentication, and model loading |
| `Integration` | broad release-gate scenarios that cross feature boundaries |
| `Models` | catalog, import, companions, profiles, groups, downloads, and launch settings |
| `Overview` | dashboard layout, presentation, selection, and overview telemetry |
| `Release` | sidecars, manifests, repository metadata, and distribution contracts |
| `Runtime` | adapters, packages, builds, lifecycle, sessions, readiness, and runtime telemetry |
| `Telemetry` | usage, energy, and live metric recommendations |
| `TestSupport` | shared builders, fixtures, and regression-suite support only |
| `UI` | view-model and shell coordination behavior that does not require an STA surface |

Composed WPF tests live in the sibling `LocalLlmConsole.UiTests` project.

## Test design rules

- Name tests as `Subject_Condition_ExpectedOutcome` or an equally clear sentence.
- Keep one test class per file and give both the same subject-oriented name.
- Prefer observable behavior over source-string inspection.
- Keep one coherent scenario per test; do not combine unrelated cases to reduce
  the test count.
- Use theories only when every row exercises the same behavior contract.
- Put reusable fakes and builders in `TestSupport`; do not duplicate them across
  feature files.
- Reserve `Architecture` source inspection for durable repository, packaging,
  security, and lifecycle rules that cannot be expressed against compiled code.
- Keep integration tests broad only when the cross-feature sequence is itself
  the behavior under test.
- Every test must run without production Manager data, credentials, or a live
  llama.cpp runtime.
