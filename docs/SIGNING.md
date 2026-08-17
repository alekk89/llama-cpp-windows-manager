# Signing Windows Releases

Last reviewed: 2026-08-15

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

Optional trusted signed builds use `.github/workflows/release.yml` through a
manual dispatch. Configure the protected `release` environment with
`WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PFX_PASSWORD`. The workflow
refuses to build signed artifacts without both secrets, imports the certificate
only for the job, runs the release gate with `-RequireSigned`, and removes the
imported certificate afterward. Version tags do not start this optional job,
so repositories without signing secrets can publish accurately labelled
unsigned releases without a failing status check. Ordinary pull-request CI
continues to use an unsigned packaging smoke test because untrusted PRs must
never receive signing credentials.

The workflow pins GitHub actions by commit and installs a fixed Inno Setup
version before importing the signing certificate. Keep all package/tool setup
before certificate import so third-party installer code never runs while the
private key is available to the job.

## Free Options

- **Free and publicly useful for qualifying OSS:** apply to SignPath Foundation
  for open-source code signing. If accepted, use their signing workflow for
  release artifacts.
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
2. `LlamaCppWindowsManager-Setup-<version>-win-x64.exe` is signed.
3. `LlamaCppWindowsManager-win-x64.zip` is generated from signed contents.
4. Each uploaded binary/archive has a matching `.sha256` companion asset generated after
   signing.
