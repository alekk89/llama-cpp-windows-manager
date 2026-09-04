# Unreleased change notes

Add one Markdown file per user-facing issue, named `<issue-number>.md`. Keep the
entry to short, plain-language bullets describing the behavior change and any
required upgrade action. Multiple PRs completing the same issue can update its
entry. Docs-only and internal changes may omit an entry with an explanation in
the PR.

Example content:

```text
- Keep saved profiles available when a model file is temporarily missing.
```

During release preparation, collect entries for completed issues into the
versioned release notes, then remove only those consumed entries. Keep this
README and entries for changes not included in that release. Do not change notes
for an already published release as part of preparing a new one.

See [Contributing](../../../CONTRIBUTING.md#release-note-entries) and
[release planning](../../REPOSITORY_GOVERNANCE.md#release-planning).
