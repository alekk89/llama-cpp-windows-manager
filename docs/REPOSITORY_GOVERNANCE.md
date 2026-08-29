# Repository Governance

Last reviewed: 2026-08-25

## Protected `main`

The `main` branch accepts changes through pull requests. The repository requires
these checks before merge:

- **Build, test, and publish**;
- **Analyze C#**;
- **Review dependency changes**.

The branch must be current, review conversations must be resolved, and force
pushes and deletion are blocked. The approval count remains zero while the
project has one maintainer so that self-approval is not required. Add an
independent approval and enable required CODEOWNERS review when a second trusted
maintainer is available.

## Administrator bypass

Administrator bypass is for recovery only: a GitHub service incident, a broken
required check that cannot be rerun, or an urgent security correction. Before a
bypass, record the reason in an issue. Afterward, open a follow-up pull request,
run every omitted check, and link the successful run from the issue. Never use a
bypass for routine feature delivery or to avoid a failing test.

## Release authority

Stable releases originate from signed annotated `v*` tags that point to commits
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

- `RELEASE_TAG_SIGNING_FINGERPRINT`;
- `RELEASE_MANIFEST_KEY_ID`;
- `RELEASE_MANIFEST_PUBLIC_KEY_SPKI`;
- `RELEASE_MANIFEST_NEXT_KEY_ID` and `RELEASE_MANIFEST_NEXT_PUBLIC_KEY_SPKI`
  during a planned rotation;
- `WINDOWS_EXPECTED_PUBLISHER`.

The manifest public keys are not secrets. They are embedded into the release
binary; changing them requires a release signed by a key already trusted by the
installed application.
