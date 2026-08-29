# Code signing policy

Last reviewed: 2026-08-25

The project has applied for the SignPath Foundation open-source code-signing
programme. Until the application is accepted and the protected workflow has
completed successfully, release artifacts must continue to be described as
unsigned.

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

## Scope

Only official llama.cpp Windows Manager artifacts built from the canonical
repository may be submitted for signing. Release signing is restricted to an
annotated signed version tag whose commit is reachable from protected `main`.
The signed inputs and returned artifacts are checked by the release workflow;
signed files are never modified before packaging or publication.

The intended signed set is:

- `LlamaCppWindowsManager.exe`;
- `llwmctl.exe`;
- the Windows installer containing those already-signed files.

Bundled upstream open-source binaries retain their upstream identity and are
not presented as binaries produced by this project.

## Roles and approval

- Committer and reviewer: repository owner `alekk89`. Changes from external
  contributors require review before merge.
- Release approver: repository owner `alekk89`.
- Every stable signing request requires explicit release approval after the
  protected build and test checks complete.

Repository access and signing accounts require multi-factor authentication.
Signing policy, workflow, packaging, and dependency changes are covered by
[CODEOWNERS](../.github/CODEOWNERS).

## Privacy and network behaviour

The Manager does not send prompts, completions, API keys, control tokens,
database contents, model files, or diagnostics bundles to the project.
Diagnostics bundles are created locally and are shared only by the user.

The app makes a metadata request to the public GitHub Releases API during its
startup update check. User-requested actions can contact GitHub, Hugging Face,
configured runtime repositories, model endpoints, and Windows/WSL package
providers. These connections are documented in the relevant guides. No usage
analytics or project-operated telemetry service is included.

## Verification and incident response

Published signatures, signed manifests, checksums, SBOMs, and provenance are
verified as described in
[Updates and release verification](UPDATES_AND_RELEASE_VERIFICATION.md).
Suspected misuse or a compromised release should be reported through the
repository security advisory channel. The affected release will not be
silently replaced; a corrected patch release and any necessary certificate or
manifest-key revocation will be published.
