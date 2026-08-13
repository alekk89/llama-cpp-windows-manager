# GitHub Release v2.1.0 Draft

This file is the copy/paste source for the next GitHub release.

## Copy/Paste Release Notes

### llama.cpp Windows Manager v2.1.0

- Sharper, higher-contrast interface with clearer navigation, actions, metrics,
  and consistent controls in light and dark modes.
- Runtime-aware launch settings and named profiles, including profile-aware
  gateway routing through the standard OpenAI-compatible model catalog.
- More reliable session lifecycle, multi-slot metrics, failure diagnostics,
  update staging, rollback, and process cleanup.
- Updated to .NET 10 and current SQLite/security dependencies, with stricter
  package checks and complete 21-language localization coverage.
- In-app upgrades from v1.x and v2.0 remain supported. Models, runtimes,
  settings, and app data are preserved.

#### Artifacts To Upload

- `dist\LlamaCppWindowsManager-win-x64.zip`
- `dist\LlamaCppWindowsManager-win-x64.zip.sha256`
- `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe`
- `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe.sha256`
- `dist\installer\LlamaCppWindowsManager-Setup-2.1.0-win-x64.exe`
- `dist\installer\LlamaCppWindowsManager-Setup-2.1.0-win-x64.exe.sha256`

The standalone executable and its checksum are required for the in-app updater
used by v1.x and v2.0 installations. Do not publish v2.1.0 without all six
assets above.

## Verification

Full local release gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -Runtime win-x64 -Configuration Release -IncludePublish -IncludeInstaller -InnoSetupPath "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

Public releases should use the protected GitHub release workflow so the
portable executable is signed before upload.
