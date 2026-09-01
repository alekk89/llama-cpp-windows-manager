# Signing Windows Releases

Last reviewed: 2026-08-25

Trusted Windows releases should be Authenticode-signed and timestamped before
upload. Unsigned community releases should be described as unsigned wherever
they are linked. The release scripts support signing with a certificate already
available in the Windows certificate store:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-app.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1 -IncludePublish -IncludeInstaller -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
```

When `scripts/build-installer.ps1 -SkipPublish -RequireSigned` reuses an existing
publish folder, the script verifies that the published executable is already
signed before compiling and signing the installer.

The Manager executable embeds `llwmctl.exe`, `AGENTS.md`, `agent.md`,
`docs/CONTROL_API.md`, `LICENSE`, and third-party/.NET notices, then restores
verified sidecar copies at startup. Any
change to those operator documents therefore requires a fresh publish before
signing; copying edited Markdown beside an already signed executable does not
update the embedded release contract and the next bootstrap can restore the
embedded version.

Trusted stable builds use `.github/workflows/release.yml`, triggered by a signed
annotated `v*` tag (or a manual dispatch that selects an existing signed tag).
The trusted job runs only when the repository variable
`TRUSTED_RELEASE_ENABLED` is set to `true`. Leave it unset while publishing
explicitly labelled unsigned community releases; their tags then skip the
trusted job without creating a failed `release` environment deployment.
The protected `release` environment holds the PFX, trusted tag public key, and
release-manifest private key. The workflow verifies the tag and protected-main
reachability, runs the complete signed gate, validates upgrade from the pinned
previous stable installer, signs the manifest, attests artifacts, and publishes
the GitHub release itself. It refuses missing credentials and an existing release
instead of downgrading trust or replacing assets. Pull-request CI uses unsigned
packaging smoke tests because untrusted changes never receive secrets.

The workflow pins GitHub actions by commit and installs a fixed Inno Setup
version before importing the signing certificate. Keep all package/tool setup
before certificate import so third-party installer code never runs while the
private key is available to the job.

## Free Options

- **Free and publicly useful for qualifying OSS:** apply to SignPath Foundation
  for open-source code signing. The application is pending; if accepted, adapt
  the protected workflow to SignPath's returned-artifact flow and enforce the
  repository [code-signing policy](CODE_SIGNING_POLICY.md).
- **Free but not publicly trusted:** self-signed certificates are useful for
  local testing and enterprise environments where the certificate is deployed to
  trusted stores. They do not remove SmartScreen or public trust warnings for
  normal users.
- **Free integrity, not Authenticode trust:** publish `.sha256` companion files
  and GitHub release provenance. This helps users verify downloads, but it is
  not a substitute for Windows code signing.

## Trusted Release Rule

Do not describe a release as signed, trusted, or production-hardened unless:

1. `LlamaCppWindowsManager.exe` is signed before the installer is compiled.
2. `llwmctl.exe` and every other shipped PE file is signed by the same publisher.
3. `LlamaCppWindowsManager-Setup-<version>-win-x64.exe` is signed.
4. `LlamaCppWindowsManager-win-x64.zip` is generated from signed contents.
5. The detached signed manifest binds the tag, commit, publisher, names, sizes,
   hashes, and SBOM, and GitHub provenance verifies.
6. Each uploaded binary/archive has a matching `.sha256` companion asset generated after
   signing.
