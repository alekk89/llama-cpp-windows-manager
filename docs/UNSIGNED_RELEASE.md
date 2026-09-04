# Unsigned community release procedure

While signing is unavailable, releases use the protected-main PR pipeline and
manual GitHub publication. Keep `TRUSTED_RELEASE_ENABLED` unset. Do not pass
`-RequireSigned` or describe the resulting files as signed or publisher-verified.

1. Integrate the intended source and tests, including new files. Finalize the
   version and short notes in `docs/releases/v<version>.md`.
2. Run the local source and portable gate. Push the feature branch, open a PR,
   pass required CI checks, and merge into protected `main`.
3. Run **Prepare unsigned release** on that exact `main` commit. The workflow
   uses a fresh Windows runner to run the complete release gate, installer
   lifecycle tests, pinned previous-version installer upgrade, and standalone
   EXE update test. It uploads a reviewable artifact; it does not publish a release.
4. Review the successful run, its commit, checksums and release notes. Create an
   annotated `v<version>` tag at that exact commit and push it. Tag publication
   does not enable the disabled trusted workflow.
5. Download the workflow artifact into an ignored workspace. From the clean
   checkout of the tagged commit, run `scripts/publish-unsigned-release.ps1`
   with `-Tag v2.7.0 -AssetDirectory <downloaded-assets>` to create and verify a
   draft. After the release gates pass, rerun with `-Publish`. The script checks
   hashes, source commit and asset order before publication. Do not replace
   assets of an existing release; fix errors in a new version.

For v2.7.0, the release assets are exactly:

- `LlamaCppWindowsManager-Setup-2.7.0-win-x64.exe`
- `LlamaCppWindowsManager-Setup-2.7.0-win-x64.exe.sha256`
- `LlamaCppWindowsManager.exe`
- `LlamaCppWindowsManager.exe.sha256`

The portable EXE is uploaded first. Older v1.0/v1.1 clients can select the first
EXE when their legacy filename is missing; the publisher refuses to proceed if
GitHub returns the installer first. This prevents the known asset-selection error
but does not certify every historical installation path.

The workflow artifact may be transported by GitHub as a ZIP; that transport
container is not a portable product download and must not be attached to the
release. GitHub's automatic source archives are independent of our binary assets.

To reproduce preparation on a disposable Windows machine:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1 -RequireCleanTree -IncludePublish -IncludeInstaller -ReleaseChannel stable
pwsh -NoProfile -File .\scripts\test-previous-version-upgrade.ps1 -CandidateInstallerPath .\dist\installer\LlamaCppWindowsManager-Setup-2.7.0-win-x64.exe
pwsh -NoProfile -File .\scripts\test-portable-update.ps1 -CandidateExePath .\dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\stage-unsigned-release.ps1
```

The installer tests refuse to modify an existing production installer identity.
Use a disposable runner for those tests. Build-only preparation on a workstation
does not establish that the installer lifecycle gate passed.

The v2.5 and v2.6 updaters require signatures: users must install unsigned v2.7 manually
once, retaining their data folder. Unsigned v2.7 supports subsequent official
HTTPS EXE updates with size and checksum verification. Signed builds retain
mandatory signature and publisher checks. See
[UPDATES_AND_RELEASE_VERIFICATION.md](UPDATES_AND_RELEASE_VERIFICATION.md).

Committing, pushing, tagging and publishing remain separate authorized actions.
