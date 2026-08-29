# Updates and release verification

Stable releases come from a trusted signed annotated tag reachable from protected
`main`. The workflow tests exact artifacts, validates upgrade from the pinned
previous stable version, signs shipped PE files, creates an SBOM and signed
manifest, and emits GitHub provenance.

```powershell
$signature = Get-AuthenticodeSignature .\LlamaCppWindowsManager.exe
if ($signature.Status -ne "Valid") { throw "Authenticode verification failed" }
$expected = ((Get-Content .\LlamaCppWindowsManager.exe.sha256 -Raw).Trim() -split "\s+")[0]
$actual = (Get-FileHash .\LlamaCppWindowsManager.exe -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Checksum mismatch" }
gh attestation verify .\LlamaCppWindowsManager.exe --repo alekk89/llama-cpp-windows-manager
```

Compare the Windows signer with the publisher in `release-manifest.json`. The
stable updater never falls back to checksum-only trust: it verifies the detached
manifest signature against embedded current/next keys, version, channel, expiry,
asset name, size, hash, and Authenticode publisher.

Update assets are size-bounded while streaming, so a false or missing
`Content-Length` cannot bypass the manifest limit. After verified replacement,
the updater attempts non-critical staging cleanup but still starts the new
executable if that cleanup fails; cleanup residue is reported separately from
update trust and replacement failures.

Key rotation stages the next public key in a release signed by the current key,
then promotes it after deployed clients trust both. Required secrets and policy
are in [REPOSITORY_GOVERNANCE.md](REPOSITORY_GOVERNANCE.md).
