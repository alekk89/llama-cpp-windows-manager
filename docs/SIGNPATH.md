# SignPath onboarding

The SignPath Foundation account is provisioned. The test certificate is valid;
the release certificate is still **CSR PENDING**. SignPath support will order and
import the production certificate after technical setup and origin verification
are complete. Account provisioning alone does not make public releases signed.

## Test workflow

Run **SignPath test signing** (`.github/workflows/signpath-test.yml`) manually from
`main`. It builds the real Native AOT `llwmctl.exe` on a GitHub-hosted Windows
runner with locked dependencies, uploads it as a raw executable, submits it via
the SignPath GitHub connector, and downloads the signed executable. It uses:

| Setting | Value |
| --- | --- |
| Organization ID | `4834e5f4-7a05-47a1-8aa7-abae116a9135` |
| Project | `llama-cpp-windows-manager` |
| Signing policy | `test-signing` |
| Artifact configuration | `initial` |
| Expected test certificate thumbprint | `38044475DB3D73116E9394D9E52D7323A449DF1D` |

`archive: false` and `skip-decompress: true` match the existing single-PE artifact
configuration. The current v3 connector requires the SignPath GitHub App to be
installed for this repository; an API token alone is insufficient. See the
[SignPath GitHub integration documentation](https://docs.signpath.io/trusted-build-systems/github).

The workflow pins the signer certificate, requires the expected `NotTrusted`
status for that self-signed certificate and a timestamp, and checks that
modifying the executable produces a hash mismatch. It does not add the test
certificate to any trusted root store. The test artifacts are clearly labelled,
expire after 14 days, and are never published as GitHub release assets. Do not
install the test certificate on users' machines or describe these artifacts as
publicly trusted.

## Credentials and execution

1. Create the GitHub environment `signpath-test` with deployment branches limited
   to the branch `main`. Keep the existing `release` environment's tag restrictions.
2. Install the official [SignPath GitHub App](https://github.com/apps/signpath)
   with **Only select repositories** and select only
   `alekk89/llama-cpp-windows-manager`. Review its requested permissions before
   approving: read access to Actions, code and metadata, and read/write access to
   repository administration. If the connector reports `Failed to retrieve
   GitHub App token`, check that this installation exists and is not suspended.
3. In SignPath, open **Users and Groups > CI builds**. Use its existing API token
   if it has been retained securely; otherwise the account owner must regenerate
   it. Regeneration invalidates the previous token. Store it directly in the
   `signpath-test` environment secret `SIGNPATH_API_TOKEN`; never put it in code,
   issues, chat, command arguments, or logs.
4. The existing CI account is a submitter on both policies. Keep the token in the
   protected environment, keep pull-request jobs secret-free, and retain the
   approval requirement on `release-signing`. This workflow hardcodes only
   `test-signing` and cannot select a release policy through dispatch inputs.
5. Merge the workflow through the normal required PR checks, then dispatch it
   from `main`. The workflow itself also rejects other repositories and refs.
6. Inspect the completed request in SignPath: confirm the GitHub repository,
   commit, workflow and artifact origin match the run. The initial test policy
   does not enforce origin verification, so a successful signature alone is not
   evidence that release-origin requirements have passed.
7. Provide SignPath support with the workflow run and signing-request links and
   ask them to verify origin and proceed with the production certificate.

The run summary records the signing request link. A successful test is onboarding
evidence, not approval to publish a signed stable release.

## Production activation

After SignPath imports the release certificate, adapt the protected production
workflow to retrieve signed artifacts instead of importing a local PFX. Preserve
signed-tag verification, protected-main reachability, explicit release approval,
release-manifest signatures, provenance, and immutable release assets.

The packaging order is significant: sign `llwmctl.exe`, embed that signed CLI
and the matching sidecars in the Manager, sign the Manager, build the installer
from those signed files, then sign the installer. Generate checksums only after
the final signatures. Verify the actual standalone Manager restores the signed
CLI. Configure the separate tag and manifest keys and expected release publisher,
then enable `TRUSTED_RELEASE_ENABLED` only after the complete production gate
succeeds. See [Signing Windows releases](SIGNING.md) and
[Code signing policy](CODE_SIGNING_POLICY.md).
