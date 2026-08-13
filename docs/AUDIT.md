# Release Hardening Audit

Audit date: 2026-08-13

## Executive Summary

Overall release posture: **v2.1.0 passes the repository's automated build,
test, coverage, vulnerability, portable-publish, sidecar-bootstrap, and
installer gates. Public artifacts must still come from the protected signed
release workflow, and clean-machine plus hardware-matrix validation remains a
manual release requirement.**

The core release blockers from the full audit have been addressed in code:

- Release build and self-contained publish verify on .NET SDK 10.0.400 with the current .NET 10 servicing runtime.
- Automated release-hardening tests now cover concurrent SQLite access, corrupt settings recovery, deletion boundaries, and runtime host validation.
- SQLite access is serialized and settings saves are transactional.
- Corrupt settings are backed up before defaults are restored; corrupt DB files are quarantined and recreated.
- The workspace is fixed at process startup instead of being editable at runtime.
- Job IDs use GUIDs.
- Hugging Face downloads are bounded to the models folder, block duplicate destinations, reject unsafe local filenames and partial-file links, preflight disk space, and require expected-size or SHA-256 verification before model registration.
- Model serving now requires a strong API key even in local-only mode, and the persisted key is protected with current-user Windows data protection.
- Auto-load gateway request bodies are bounded and oversized payloads return a
  `413 request_too_large` response before proxying.
- Runtime source IDs loaded from custom JSON are sanitized, and recursive runtime deletes are path-bounded.
- WSL shutdown no longer uses a broad port-only kill, and WSL cleanup now
  verifies whether the targeted runtime stopped and logs failures for diagnosis.
- The WSL Linux page now detects WSL, installed non-Docker distros, the default distro, and shows focused WSL/Ubuntu install or update actions.
- Release publish omits PDB files and supports certificate signing with `-CertificateThumbprint` and `-RequireSigned`.
- App update checks are staged through the workspace cache; the app and control
  CLI are copied to verified sibling files and atomically replaced after the
  running process closes, with rollback if either replacement fails.
- App update staging verifies a matching SHA-256 companion asset when present and requires same-certificate signature continuity when the installed app is already signed.
- Runtime onboarding is prebuilt-first: official llama.cpp release packages can
  be installed directly before using source builds.
- Runtime package downloads verify expected sizes and SHA-256 metadata or
  companion checksum files before installation.
- The Windows and WSL setup workflows now cover CPU, CUDA, Vulkan, and Intel
  Arc SYCL prerequisites before advanced source builds start.
- Per-model launch settings now include vision image token allowances and map them to llama.cpp server flags.
- Per-model ports and loaded model sessions allow more than one model endpoint
  to stay available when hardware capacity allows it.
- The auto-load gateway provides one shared OpenAI-compatible endpoint, routes
  by requested model id, starts models on their saved direct ports, and exposes
  policy controls for keeping loaded sessions or switching to one active model.
- LAN exposure is scoped by Settings so users can expose only the gateway, only
  direct model endpoints, both, or neither.
- Per-model launch profiles now support saved variants, auto-detected,
  embedded/model-bundled, or explicit vision head/projector choices, vision
  image token allowances and separate MTP head choices for compatible runtimes.
- The shared gateway publishes one client-neutral route per saved launch profile
  and never edits third-party harness configuration.
- Fresh installer setups offer Start with Windows by default, with a matching
  current-user startup preference in Settings.
- Settings are grouped by category, and Overview preserves completed model load
  duration as a separate Loading Time row after a model becomes ready.
- The local app service now keeps request handlers observed and tolerates
  bounded transient listener errors instead of silently faulting the listener
  loop.

## Remaining External Hardening Work

### Clean Windows VM validation

- Severity: High
- Area: Installation and onboarding
- Status: Follow-up hardening
- Required result: Published app launches with no repository checkout, creates state, shows clear prerequisite guidance, and does not require a developer SDK.

### Trusted signing and distribution

- Severity: High for reducing Windows trust warnings
- Area: Distribution and trust
- Status: Portable single-exe publish and Inno Setup installer source exist; signing support exists; certificate is not present in this repo. The current public release is unsigned and labeled as such.
- Required result: A future trusted release is signed with a trusted certificate and distributed as a signed portable zip or installer with shortcut/uninstall flow.

### GitHub update feed

- Severity: Medium
- Area: Distribution
- Status: Update UI, staged installer, checksum verification, signed-app
  signature continuity, and rollback-safe app/CLI replacement are implemented;
  the public repository and v2.1.0 asset naming are confirmed.
- Required result: Latest GitHub release contains
  `LlamaCppWindowsManager-win-x64.zip`, the standalone
  `LlamaCppWindowsManager.exe` required by v1.x/v2.0 updaters, matching SHA-256
  companion assets, and release notes suitable for the completion popup.

### WSL and hardware matrix

- Severity: Medium
- Area: llama.cpp runtime/build support
- Status: Requires manual hardware coverage
- Required result: Validate missing WSL, missing distro, CPU build, missing Git/CMake/compiler, CUDA-visible WSL, Vulkan-visible WSL, Intel Arc/SYCL-visible Windows and WSL, and unsupported backend paths.
- Added support: The app can detect installed non-Docker distros and guide WSL install/update, Ubuntu install/update, CPU tools, CUDA Toolkit, Vulkan tool setup, Intel GPU runtime setup, and Intel oneAPI setup from the WSL Linux page. The Windows page detects native CPU/CUDA/Vulkan/SYCL tool readiness.

### Runtime/archive authenticity verification

- Severity: Medium
- Area: Third-party binaries
- Status: Prebuilt runtime downloads are installed from their configured package
  sources, including official GitHub release assets and selected fork binary
  feeds, then locally fingerprinted for source/prebuilt equivalence where
  possible. Package authenticity still depends on the package source transport
  and release trust unless matching trusted upstream checksums or signatures
  become available.
- Required result: Prefer trusted upstream checksums or signatures for runtime
  archives when upstream publishes them.

## Automated Checks

Current passing checks:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-vulnerabilities.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -SkipPublish
```

The latest local architecture/release pass on 2026-08-13 ran
`test-release-gate.ps1 -IncludePublish -IncludeInstaller`, which wraps the
release build, release-hardening suite, formatting verification,
`git diff --check`, vulnerability/deprecation/direct-package currency audit,
publish smoke, and installer artifact checks. Service/unit tests passed
(`458/458`) and the WPF smoke test
passed (`1/1`) with no skips. The v2 portable zip contains the Manager, control
CLI, agent guides, control API reference, and
checksum companions; the updater and installer remove the obsolete pre-rename
executable during migration. The local verification artifacts were unsigned.

## Post-v1.1.2 Hardening

After publishing `v1.1.2`, a follow-up bug-report triage fixed the actionable
low-risk items that were safe to take immediately:

- Runtime backend inference now prefers explicit packaged metadata and nearby
  runtime files over loose folder/path text, avoiding false CUDA/SYCL/Vulkan
  classification from names like `cuda-backup`.
- `LlamaProcessSupervisor` runtime state transitions are now atomic/volatile
  across process output callbacks, readiness checks, and exit handling.
- `LogFileService.Head` now detects byte-order marks like `Tail` already did.
- `GgufMetadataReader` now ignores unsupported/future GGUF versions instead of
  silently parsing unknown metadata layouts.
- Runtime package and portable app update archives are now prevalidated for
  absolute paths, traversal paths, and unsafe tar link/device entries before
  extraction.
- Runtime package downloads now require size/checksum verification metadata and
  delete failed downloads after verification errors.
- The auto-load gateway now rejects oversized request bodies with `413` instead
  of buffering unbounded client payloads.
- WSL runtime cleanup now returns and logs verification details instead of
  swallowing all stop failures.
- Release scripts can be run with `-RequireCleanTree` so publish, installer, and
  release-gate packaging fail on dirty worktrees.
- Overview runtime metrics now use compact aggregate token monitors and
  60-sample trend graphs for normal, speculative, and KV-cache streams. Slot
  fallback totals survive parallel task resets without double counting, while
  the live Slots card reports active capacity. Hardware reports CPU telemetry,
  normalized hardware metric separators, and vendor-neutral Windows GPU fallback
  summaries for AMD/Intel/Vulkan systems.
- Overview Model Status now separates Loading/Loaded Model from Loading Time and
  keeps the completed load duration visible after startup.
- Settings now renders preferences in category sections for easier navigation.

Verification for this hardening pass:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

Result on 2026-06-01: release-hardening tests passed (`432/432`), formatting was
clean, the Release build succeeded with zero warnings, no vulnerable packages
were found, the diff had no whitespace errors, and publish/installer artifact
checks passed locally.

## Edge Cases To Keep Testing

- No internet during Hugging Face search or download.
- Slow internet with cancellation during a large GGUF download.
- Interrupted app shutdown during model download or llama.cpp build.
- Disk full during download, build, extract, or SQLite write.
- Missing WSL, missing configured Ubuntu distro, or WSL disabled.
- Git, CMake, compiler, CUDA, Vulkan, or Intel oneAPI/SYCL missing inside Ubuntu.
- Permission denied for workspace, models, runtime, or cache folders.
- Invalid, partial, renamed, or moved GGUF model files.
- Missing or deleted llama-server executable after registration.
- Manually edited or corrupt SQLite/settings state.
- Unicode, spaces, long paths, and non-default drive letters.
- Third-party OpenAI-compatible clients with stale model ids or credentials.

## Release Decision

v2.1.0 is acceptable for release after the protected workflow produces signed
portable and installer artifacts and the manual clean-machine checklist is
completed. Local unsigned artifacts are suitable only for testing and must
remain labelled unsigned.
