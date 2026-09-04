# Updates and release verification

## Unsigned community releases

Version 2.7 is an unsigned community release. Download the installer or standalone
`LlamaCppWindowsManager.exe` and its matching `.sha256` companion from
[GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases).
The portable ZIP is no longer published. The EXE restores its bundled CLI,
automation guides, and license notices when started.

```powershell
$asset = "LlamaCppWindowsManager.exe"
$expected = ((Get-Content "$asset.sha256" -Raw).Trim() -split "\s+")[0]
$actual = (Get-FileHash $asset -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Checksum mismatch" }
```

Checksums detect mismatched downloads; they are not publisher signatures.
Windows may display an unknown-publisher or SmartScreen prompt.

The v2.5 and v2.6 updaters require signatures, so install unsigned v2.7 manually once.
Exit the Manager, then run the installer over the existing installation or
replace the portable EXE in its existing folder. Keep the `data` folder.

Unsigned v2.7 builds allow subsequent unsigned updates from the official GitHub
repository over HTTPS. The updater requires the exact standalone EXE, matching
tag URLs, a positive advertised size, and its SHA-256 companion. It checks the
download size and checksum before staging, rejects non-newer versions, and labels
the install prompt as unsigned. A supplied manifest or signature must verify;
an incomplete or invalid signature pair never falls back to unsigned handling.

## Earlier updater compatibility

Signature enforcement was introduced in v2.5. Tagged v1.1.2 through v2.4 code
accepts checksum-verified unsigned updates and recognizes the current portable
EXE name, so those versions are not blocked by the signing requirement.
This source review does not certify every historical version's end-to-end upgrade.

Versions v1.0 and v1.1.0 expect the older `LlamaCppConsole.exe` name. Without a
ZIP or that exact asset they fall back to the first EXE, which can be the installer.
Use a manual upgrade from these oldest versions with the new asset layout.

## Signed releases when signing is configured

The trusted release job runs only when `TRUSTED_RELEASE_ENABLED=true`. It verifies
a signed annotated tag reachable from protected `main`, signs the application,
CLI and installer, verifies upgrade behavior, creates a signed release manifest
and SBOM, and emits GitHub provenance. It does not fall back to unsigned publishing.

`publish-app.ps1 -RequireSigned` embeds `RequireSignedUpdates=true`. Those builds
require a verified manifest and expected Authenticode publisher for updates.
The unsigned community build explicitly embeds `RequireSignedUpdates=false`.
No UI or environment setting changes this policy in an installed binary.

Update assets are size-bounded while streaming, so a false or missing
`Content-Length` cannot bypass the advertised size limit. After verified replacement,
the updater attempts non-critical staging cleanup but still starts the new
executable if that cleanup fails; cleanup residue is reported separately from
update trust and replacement failures.

Before closing for an update, the Manager requires the helper to verify the
staged application and matching CLI and acknowledge readiness. A failed or timed-out
handoff keeps the current Manager open. Replacement waits for the old process
to exit. If replacement fails, the helper attempts rollback; if restoration also
fails, it preserves available backups and reports their location rather than
deleting the remaining recovery files. Keep those files and the error log when
requesting support; rollback is not a guarantee against every filesystem failure.

Key rotation stages the next public key in a release signed by the current key,
then promotes it after deployed clients trust both. Required secrets and policy
are in [REPOSITORY_GOVERNANCE.md](REPOSITORY_GOVERNANCE.md).
