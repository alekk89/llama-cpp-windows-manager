## Related issue and dependencies

Closes #<primary-issue> (use a reference instead if this is a partial contribution).
List prerequisite PRs and their merge order, or write "None". For a release PR,
link its release-planning issue and the included issue PRs or milestone.

## What changed

Describe the user-visible behavior and the reason for the change.

## Risk and compatibility

List affected shared contracts such as saved profiles/settings, database
migrations, model/runtime behavior, gateway/control APIs, packaging, or
localization. Explain compatibility with current `main` and related changes,
including combined-behavior tests and any user migration. For a large coherent
change, explain why it stays in one PR and how to review it.

## Validation

- [ ] This PR addresses one primary issue; unrelated work has separate PRs.
- [ ] Dependencies are merged and this PR is up to date with `main` before merge.
- [ ] Relevant behavioral and integration tests were added or updated, or are not applicable (explain).
- [ ] `scripts/test-release-gate.ps1` passes, or the omitted step is explained.
- [ ] UI behavior was checked at the default window size when applicable.
- [ ] Documentation and in-app Help were updated when behavior changed.
- [ ] No generated output, workspace data, models, runtimes, logs, or secrets are included.
- [ ] Public artifacts are not described as signed without a verified signature.

## Release notes

Link `docs/releases/unreleased/<issue>.md`, or explain why no user-facing entry
is needed. Release PRs collect completed entries into the versioned notes.

## Manual checks still required

List hardware, WSL, clean-install, migration, or other checks CI cannot perform.
