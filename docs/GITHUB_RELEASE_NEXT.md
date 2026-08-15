# GitHub Release v2.2.0 Draft

This file is the copy/paste source for the next GitHub release.

## Copy/Paste Release Notes

- Refined responsive light/dark UI, clearer Settings and launch controls,
  filtered runtime inventories, a compact searchable Help centre, and an
  updated animated product tour.
- Added launch-profile groups with retention and eviction policies,
  transactional multi-model loading, safer companion detection, and a unified
  runtime install, source-download, build, and discovery workflow.
- Expanded the authenticated `llwmctl` control API with group/profile
  operations, presentation settings, and direct-session or gateway inspection
  that uses the stored serving key without exposing it.
- Improved session lifecycle, gateway behavior, live metrics, download/update
  validation, rollback, process cleanup, accessibility, and maintainable feature
  boundaries, backed by a larger release-hardening test suite.
- Updated all 21 language resource contracts, including localized groups and
  endpoint inspection; eleven packs include translated Help and the remaining
  packs use complete English Help fallbacks. Upgrades preserve existing models,
  runtimes, profiles, settings, and application data.

These artifacts are unsigned. Verify downloads with the matching SHA-256
companion files before running them.

#### Artifacts To Upload

- `dist\LlamaCppWindowsManager-win-x64.zip`
- `dist\LlamaCppWindowsManager-win-x64.zip.sha256`
- `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe`
- `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe.sha256`
- `dist\installer\LlamaCppWindowsManager-Setup-2.2.0-win-x64.exe`
- `dist\installer\LlamaCppWindowsManager-Setup-2.2.0-win-x64.exe.sha256`

The standalone executable and its checksum are required for the in-app updater
used by v1.x, v2.0, and v2.1 installations. Do not publish v2.2.0 without all six
assets above.
