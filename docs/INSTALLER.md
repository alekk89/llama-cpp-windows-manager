# Windows Installer

Last reviewed: 2026-08-15

The installer is built with Inno Setup 6 from the self-contained `win-x64` publish output.

## Build

Install Inno Setup 6, make sure `ISCC.exe` is on `PATH`, or set:

```powershell
$env:LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

The legacy `LLAMA_CPP_CONSOLE_INNO_SETUP` variable is still accepted.

Then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

For a public build, sign the app and installer:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
```

The setup executable is written to:

```text
dist\installer\LlamaCppWindowsManager-Setup-2.2.0-win-x64.exe
```

## Install Behavior

- Fresh installs prefer `D:\LlamaCppWindowsManager` when the `D:` drive exists.
- If `D:` is unavailable, the installer defaults to `%LocalAppData%\Programs\LlamaCppWindowsManager`.
- The install folder is still editable in the setup wizard before files are copied.
- Existing installations are detected by a stable Inno Setup `AppId`, so updates and repairs reuse the previous install folder.
- Updating an older install removes the old `LlamaCppConsole.exe` app binary while preserving the existing install directory and `data` folder.
- The final installer page includes a launch-after-install option.
- The installer creates a Start Menu shortcut and offers an optional Desktop shortcut.
- Fresh installs offer a checked-by-default Start with Windows task. The app also exposes the same startup preference in Settings.
- The installer includes the project `LICENSE`, dependency notices, and the
  self-contained .NET runtime license/notices under `licenses\dotnet`.

## Data Preservation

The app creates its workspace under `data` beside `LlamaCppWindowsManager.exe` when that location is writable:

```text
data\
  models\
  runtimes\
  cache\
  state\
  logs\
```

Installer updates and repairs overwrite application files only. They do not delete `data`, models, runtimes, logs, cache, or state.

UI visibility choices are stored in the preserved SQLite state. An update keeps
existing choices. When upgrading a workspace that predates the **Settings >
UI** fields, the six Overview status cards and live log default visible; raw
llama.cpp metrics and the Models Hugging Face section default hidden.

Uninstall keeps `data` by default. If `data` exists, the uninstaller asks whether to delete it, with the safe default set to keep the data.
