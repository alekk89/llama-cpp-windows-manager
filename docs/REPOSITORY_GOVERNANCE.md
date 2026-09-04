# Repository Governance

Last reviewed: 2026-09-04

## Protected `main`

The required policy is for `main` to accept changes through pull requests with
these checks before merge. Verify the live settings before release:

- **Build, test, and publish**;
- **Analyze C#**;
- **Review dependency changes**.

The branch must be current, review conversations must be resolved, and force
pushes and deletion are blocked. The approval count remains zero while the
project has one maintainer so that self-approval is not required. Add an
independent approval and enable required CODEOWNERS review when a second trusted
maintainer is available.

## Administrator bypass

Routine administrator bypass must be disabled. An exceptional recovery requires
an explicitly authorized temporary policy change and restoration afterward.
Examples are a GitHub service incident, a broken required check that cannot be
rerun, or an urgent security correction. Before a
bypass, record the reason in an issue. Afterward, open a follow-up pull request,
run every omitted check, and link the successful run from the issue. Never use a
bypass for routine feature delivery or to avoid a failing test.

## Release planning

[CONTRIBUTING.md](../CONTRIBUTING.md#issues-and-pull-requests) owns the issue and
PR-scope rules. Release size is independent of PR size: a release may include
many features, major improvements, or coordinated changes delivered through
separate issue PRs.

1. Review release readiness roughly weekly. This is a planning rhythm, not an
   automatic publishing schedule or deadline: release sooner for an urgent fix,
   or allow more time for a larger update. Never include unfinished work merely
   to meet a date.
2. Use a release-planning issue or milestone to list the intended issue PRs,
   dependencies, compatibility/migration risks, and manual validation needed.
   Merge compatible, completed PRs through the normal protected-main checks.
   A candidate includes every change in its commit history; a milestone does not
   exclude other changes already merged into `main`. Keep work that must wait
   on its branch until the appropriate release cycle.
3. Open a focused release PR linked to the planning issue. Set the version,
   collect the completed entries from `docs/releases/unreleased` into
   `docs/releases/v<version>.md`, and update release-specific metadata or baseline
   expectations when needed. Remove only the entries included in those notes;
   retain the unreleased directory's README. Keep unrelated implementation out
   of the release PR.
4. Select the exact merged commit as the release candidate. Validate the combined
   application, build its release binaries once, and run the full release gate
   plus installer lifecycle, pinned-version upgrade, and portable-update checks
   on those binaries. Complete the recorded manual checks. Independent PR checks
   do not replace this combined validation.
5. If validation finds a defect, record it in an issue and fix it through a
   focused PR. Select a new candidate and rebuild/revalidate its affected paths
   and required release gates. An unresolved flaky check is a failure to
   investigate, not a reason to skip the gate. Do not add unrelated features to
   a candidate being prepared for publication.
6. Publish only the exact artifacts validated for the selected commit, following
   the appropriate publication procedure below. Verify draft asset names,
   download selection, checksums, source version, and notes before publication.
   An interrupted upload must reconcile the draft against those same verified
   files before continuing; never blindly publish or rebuild under the same tag.
   Record the successful checks and final commit in the release-planning issue.

Current CI runs its required build, tests, quality, and packaging checks for every
PR. The release preparation workflow additionally validates and stages the final
combined artifacts. This policy does not introduce skipped checks or a new CI
speed tier. Changes to the checks themselves require their own focused issue PR.

## Release authority

Unsigned community releases are permitted while signing is unavailable. They
use the same protected-main PR and CI checks, an annotated version tag, the
unsigned preparation workflow, and manual publication after reviewing artifacts.
See [UNSIGNED_RELEASE.md](UNSIGNED_RELEASE.md). They must be labelled unsigned.

Trusted signed releases originate from signed annotated `v*` tags that point to commits
reachable from protected `main`. The protected `release` environment owns the
Windows signing certificate and release-manifest private key. Pull-request jobs
cannot access either credential.

The environment accepts deployments only from `v*` tags. Add a second trusted
maintainer as required environment reviewer when available; GitHub cannot require
an independent self-review in the current solo-maintainer configuration.

Release assets are immutable. Correct a faulty release with a new patch version;
do not replace files beneath an existing tag.

## Required release configuration

Configure these protected-environment secrets:

- `WINDOWS_SIGNING_PFX_BASE64`;
- `WINDOWS_SIGNING_PFX_PASSWORD`;
- `RELEASE_TAG_SIGNING_PUBLIC_KEY_BASE64`;
- `RELEASE_MANIFEST_SIGNING_KEY_PEM_BASE64`.

Configure these repository or protected-environment variables:

- `TRUSTED_RELEASE_ENABLED=true` after every required release credential is configured;
- `RELEASE_TAG_SIGNING_FINGERPRINT`;
- `RELEASE_MANIFEST_KEY_ID`;
- `RELEASE_MANIFEST_PUBLIC_KEY_SPKI`;
- `RELEASE_MANIFEST_NEXT_KEY_ID` and `RELEASE_MANIFEST_NEXT_PUBLIC_KEY_SPKI`
  during a planned rotation;
- `WINDOWS_EXPECTED_PUBLISHER`.

The manifest public keys are not secrets. They are embedded into the release
binary; changing them requires a release signed by a key already trusted by the
installed application.
