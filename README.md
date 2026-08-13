# llama.cpp Windows Manager

Windows-first desktop app for installing, configuring, and running local
`llama.cpp` models with native Windows or Ubuntu/WSL runtimes.

This is an unofficial community project. It is not affiliated with, endorsed by,
or maintained by the `llama.cpp` or `ggml-org` projects.

## At a Glance

llama.cpp Windows Manager is a Windows desktop control panel for raw
`llama.cpp`/GGUF workflows. It helps you install runtimes, download or register
models, save per-model launch profiles, run models as local OpenAI-compatible
endpoints, and monitor loaded sessions without living in PowerShell.

![llama.cpp Windows Manager overview](docs/images/overview.png)

Use it when you want the flexibility of `llama-server` on Windows or WSL, but
with a UI for runtimes, ports, launch settings, logs, metrics, updates, and a
client-neutral model gateway. It is not a hosted service or a chat-first model
library replacement.

## What It Does

- Registers local GGUF models and app-managed Hugging Face downloads, with size
  or SHA-256 verification and companion mmproj/projector discovery.
- Installs prebuilt `llama.cpp` runtimes first, including official packages and
  selected fork builds such as Atomic TurboQuant CUDA.
- Runs one or more supervised `llama-server` sessions on native Windows or
  Ubuntu/WSL, with stable per-model ports and OpenAI-compatible `/v1` endpoints.
- Supports CPU, CUDA, Vulkan, and Intel Arc SYCL runtime choices where packages,
  hardware, and drivers support them.
- Keeps advanced source builds available for native Windows or Ubuntu/WSL when a
  custom fork, branch, patch, or backend build is needed.
- Provides an optional auto-load gateway: one shared OpenAI-compatible `/v1`
  endpoint that exposes each saved launch profile as a model, starts the exact
  requested profile on its direct port, proxies requests, and can either keep
  existing sessions or switch to a single active model.
- Scopes LAN exposure independently for gateway and direct model ports.
- Supports saved launch variants, reasoning/template options, vision
  head/projector selection, dynamic-resolution image token settings, MTP head
  selection, and upstream-style speculative draft helpers.
- Shows loaded sessions, logs, jobs, runtime-aware hardware summary, live slot
  activity, preserved model load duration, and two-row token monitors with
  live, average, and total values for normal and MTP token streams on the
  Overview page.
- Offers a Start with Windows installer task, enabled by default for fresh
  installs, plus the same setting inside the app.
- Supports 21 languages: English, Bulgarian, Spanish, Russian, German, French,
  Japanese, Portuguese, Chinese (Simplified), Italian, Turkish, Persian, Polish,
  Dutch, Vietnamese, Korean, Arabic, Indonesian, Hindi, Czech, and Swedish.
  The language is selectable from the sidebar and persisted across restarts.
- Stores settings, jobs, models, runtimes, migrations, and history in SQLite.
- Exposes its complete authenticated loopback control API through `llwmctl` and
  classifies bounded, redacted API activity in the Logs page as **Control API**.
  Request bodies, query values, credentials, and model-serving API keys are not
  written to that audit log.

## Comparison

This app overlaps with other local-LLM tools, but it aims at a narrower Windows
workflow: managing `llama.cpp` builds and `llama-server` launches on native
Windows or inside Ubuntu/WSL without living in a terminal.

| Tool | Primary focus | How llama.cpp Windows Manager differs |
| --- | --- | --- |
| Ollama | Simple local model runner with CLI, app, model library, and local API. | Keeps you closer to raw `llama.cpp`/GGUF workflows: install prebuilt CPU/CUDA/Vulkan/SYCL runtimes for Windows or WSL, keep custom source builds available, choose a runtime per model, and inspect logs/metrics directly. |
| LM Studio | Polished desktop model browser, chat UI, and local OpenAI-compatible server. | Focuses less on chat UX and more on toolchain setup, source builds, runtime selection, launch profiles, and operational monitoring. |
| Jan | Open-source local AI platform with desktop, server/API, CLI, and assistant workflows. | Stays centered on Windows-managed `llama.cpp` runtimes and client-neutral OpenAI-compatible serving instead of being a general assistant platform. |
| `llama-server` | Upstream `llama.cpp` OpenAI-compatible HTTP server. | Wraps `llama-server` with Windows UI for prebuilt runtime downloads, optional Windows/WSL toolchain setup, source checkout/builds, model registration, per-model launch settings, logs, metrics, and update/install flow. |

## Safety Defaults

- The app control service binds to `127.0.0.1` only and requires a per-session
  bearer token for non-health API calls.
- Model serving defaults to `127.0.0.1`. Settings can expose only the gateway,
  only direct model ports, or both serving surfaces to trusted LAN clients.
- Model serving exposes the upstream `llama-server` OpenAI-compatible endpoint,
  plus the optional gateway router, not the app-local control API.
- Model serving requires a strong API key in every local-only or LAN exposure
  mode.
- The model API key is protected with Windows current-user DPAPI at rest.
- The model API key is passed via environment variable (`LLAMA_API_KEY`) rather
  than command-line arguments, keeping it out of process command lines visible
  in Task Manager.
- Custom launch parameters are validated to prevent overriding security-critical
  `llama-server` flags (`--host`, `--port`, `--api-key`).
- Native `llama-server.exe` processes are bound to a Windows Job Object that
  terminates them automatically if the app crashes or is force-killed.
- Destructive deletes are bounded by app ownership and path-root checks.
- External/imported models are registration-only deletes by default.
- Hugging Face downloads reject unsafe Windows filenames, symlink/hardlink
  partials, and incomplete files.
- Runtime package downloads verify expected byte counts and SHA-256 metadata or
  companion checksum files before install. Runtime package archives and portable
  app updates are also validated before extraction to reject absolute paths,
  traversal paths, and unsafe tar entries.
- Corrupt settings are backed up before defaults are loaded.
- Corrupt SQLite database files are quarantined and recreated on startup.
- Installer updates, repairs, and default uninstalls preserve `data`, models,
  runtimes, cache, logs, and state unless the user explicitly chooses to delete
  app data during uninstall.

## Quick Start

The normal path is prebuilt-first:

1. Open **Runtimes** and install the prebuilt runtime that matches your target:
   Windows or WSL, then CPU, CUDA, Vulkan, or Intel Arc SYCL. Atomic TurboQuant
   CUDA Windows/WSL entries are available beside the official packages.
2. Open **Models**, search Hugging Face, and download a GGUF model file.
3. Select the model, choose the runtime, keep or adjust its saved model port,
   and click **Save For Model**.
4. Open **Overview**, choose the model, and click **Load**. Additional models
   can be loaded on their own saved ports when hardware capacity allows it. The
   Model Status card keeps Loading/Loaded Model and Loading Time on separate
   rows, preserving the final load duration after startup completes.
5. Optional: keep **Settings > Auto-load gateway** enabled so any
   OpenAI-compatible client can use one shared endpoint and the app can load
   requested model profiles on demand. Query `GET /v1/models` and use the
   returned model id; every saved launch profile is listed separately.
   Use **Gateway policy** to choose whether the router keeps existing models
   loaded or switches to a single active model.

## Agent and CLI Control

The release also includes `llwmctl.exe`, an authenticated local control client
for coding agents, scripts, and terminal users. It controls the running
Manager rather than launching unmanaged `llama-server` processes.

```powershell
llwmctl status
llwmctl self
llwmctl models list
llwmctl load qwen3-30b-q4-k-m --profile CUDA --set contextSize=65536 --wait
llwmctl sessions metrics qwen3-30b-q4-k-m
llwmctl sessions logs qwen3-30b-q4-k-m
```

The control surface covers model and runtime discovery, full launch-profile
settings, one-shot launch overrides, load/restart/unload, self-identification,
vision/draft/MTP companions, live metrics and slots, logs, application settings,
Hugging Face search/download, and download job control. The API binds only to
loopback and uses a separate per-process token protected for the current Windows
user. Agents should start with [AGENTS.md](AGENTS.md), which includes cold-start,
self-restart recovery, troubleshooting, GitHub installation, source-build, and
safe repository-contribution workflows. See also
[Local control API and `llwmctl`](docs/CONTROL_API.md).

Use **Show advanced** in Runtimes only when you need to download source and build
a custom fork, branch, patch, or runtime target without a prebuilt package. The
**Windows** and **WSL Linux** setup pages live under **Tools** and
are mainly for advanced source builds or troubleshooting missing toolchains.

## Companion GGUF Auto-Detection

The app scans nearby GGUF files for model companions. Nearby means the main
model's folder, direct child folders, or the parent folder. Companion files are
not registered as main models when their names clearly identify them as helper
files.

Vision/projector companions are detected when the filename contains one of:
`mmproj`, `projector`, `clip`, `vision-head`, `visual-head`, `image-head`,
`head-vision`, `head-visual`, `head-image`, `mtp-vision`, or `vision-mtp`.
Use these with the Vision head setting or embedded/model-bundled vision.

Speculative draft and MTP companions are detected when the filename starts with
`mtp-`, `draft-`, or `spec-`, or contains `-mtp-head`, `mtp-head`, `dspark`,
`dflash`, `-draft-`, or `-spec-`. Underscores and dots are treated like hyphens
for matching. Names that only say `assistant` are not auto-detected, so prefer names such as
`mtp-gemma-4-31b-it-qat-q4_0-assistant.gguf` for upstream `draft-mtp`.

For official upstream `llama.cpp` draft modes, set **Spec type** to a
`draft-*` value such as `draft-mtp`, `draft-dflash`, or `draft-dspark` and use
**Draft model** or auto-detect. DSpark block-7 checkpoints should use
**Draft max** = `7`. For compatible Atomic MTP forks that accept `--mtp-head`, use **Spec type** =
`atomic-mtp` and **MTP head** instead. MTP assistant GGUFs are separate from
Vision head / `--mmproj` projectors.

Per-model launch settings also expose **GPU mode**:

- `auto` keeps llama.cpp's default GPU behavior.
- `single` uses one GPU; optionally set **GPU devices** to an ID such as `CUDA1`.
- `layer` and `row` split work across every available GPU or the comma-separated
  devices you select, such as `CUDA0,CUDA1,CUDA2`.
- `tensor` enables llama.cpp's experimental tensor-parallel mode.

Use **GPU split** for optional proportions matching the selected devices, such
as `1,1` for an even two-GPU split or `3,1` for a 75/25 split. Leave devices and
split empty to let llama.cpp use all available GPUs and choose the proportions.

Advanced launch settings include **Server > Custom params** for raw
`llama-server` flags that do not yet have a first-class app field. These values
are saved with the launch profile and appended after the app-generated
arguments, so examples like `--n-cpu-moe 999` work after you save and restart
the model. Quote values with spaces, for example
`--model-draft "D:\Models\draft model.gguf"`.

## Runtime Compatibility

llama.cpp Windows Manager is designed to let each model choose the runtime that fits
your machine instead of forcing one global backend.

| Target | Runtime choices | Normal path | Advanced path |
| --- | --- | --- | --- |
| Native Windows | CPU, CUDA, Vulkan, Intel Arc SYCL | Install a prebuilt runtime from **Runtimes**. | Use **Tools > Windows** plus **Runtimes > Show advanced** for source builds. |
| Ubuntu/WSL | CPU, CUDA, Vulkan, Intel Arc SYCL | Install a prebuilt WSL/Linux runtime from **Runtimes**. | Use **Tools > WSL Linux** plus **Runtimes > Show advanced** for source builds. |

GPU runtimes still depend on the matching vendor driver/runtime being available
to Windows or WSL. CPU runtimes are the simplest fallback when GPU support is
not available.

## End-User Distribution

End users should receive a release artifact, not the source tree.

Preferred artifact:

```text
dist\installer\LlamaCppWindowsManager-Setup-2.1.0-win-x64.exe
```

Portable artifacts:

```text
dist\LlamaCppWindowsManager-win-x64.zip
dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe
```

The portable zip contains the canonical `LlamaCppWindowsManager.exe` application
and `llwmctl.exe` control client.
When updating an older renamed portable copy, the updater migrates to the canonical
filename and removes the obsolete executable after the running process exits.

Fresh installer defaults:

- `D:\LlamaCppWindowsManager` when `D:` exists.
- `%LocalAppData%\Programs\LlamaCppWindowsManager` when `D:` is unavailable.
- Existing installs reuse the previous install directory.
- Fresh installs offer a checked-by-default **Start with Windows** task. The
  same preference can be changed later in Settings.

Portable runs create a workspace beside the executable when writable:

```text
LlamaCppWindowsManager.exe
data\
  models\
  runtimes\
  cache\
  state\
  logs\
```

If the executable folder is not writable, the app falls back to
`%LocalAppData%\llama.cpp Windows Manager`. Override the workspace before launch with
`LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE`. The legacy `LLAMA_CPP_CONSOLE_WORKSPACE`
and `LOCAL_LLM_CONSOLE_WORKSPACE` variables are still accepted.

## Developer Prerequisites

- Windows 10/11 x64.
- PowerShell 5+.
- .NET 10 SDK.
- For prebuilt runtimes: no build toolchain is required. Windows or WSL
  GPU drivers/toolkits may still be needed for the chosen runtime to see the
  hardware.
- For native Windows source builds: Git, CMake, Visual Studio C++ Build Tools,
  and optional Windows CUDA, Vulkan, or Intel oneAPI/SYCL SDKs.
- For WSL source builds: WSL with an Ubuntu distro plus Git, CMake, compiler
  tools, and optional CUDA, Vulkan, or Intel oneAPI/SYCL tools inside Ubuntu.
- Inno Setup 6 for installer builds.

If `dotnet` is not on `PATH`, point the scripts at an SDK explicitly:

```powershell
$env:LLAMA_CPP_WINDOWS_MANAGER_DOTNET = "C:\Path\To\dotnet.exe"
```

The legacy `LLAMA_CPP_CONSOLE_DOTNET` and `LOCAL_LLM_CONSOLE_DOTNET` variables
are also accepted.

For Inno Setup, prefer:

```powershell
$env:LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP = "C:\Path\To\ISCC.exe"
```

The legacy `LLAMA_CPP_CONSOLE_INNO_SETUP` variable is also accepted.

## Build, Test, Publish

Run the local release gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1
```

That wrapper builds the app, runs the release-hardening tests, rejects skipped
tests, enforces targeted service/model coverage, verifies formatting, checks
diff whitespace, and rejects vulnerable, deprecated, or outdated direct packages. Include
packaging smoke checks on a release machine with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

CI runs the same gate on `windows-latest` through
[.github/workflows/ci.yml](.github/workflows/ci.yml). `global.json` pins the SDK
feature band used by CI and local scripts.

Architecture, module ownership, and contributor-facing development guidance are
tracked in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) and
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

Signed release builds can be produced with a certificate thumbprint:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
```

The publish and installer scripts write `.sha256` companion files beside the
generated binaries. The app updater requires a matching SHA-256 asset before
staging an update.

Add `-RequireCleanTree` to publish, installer, or release-gate commands when
packaging artifacts that must come from a clean Git worktree.

For v2.0 and newer, publish the `LlamaCppWindowsManager-win-x64.zip` archive
and its `.sha256` file. The zip contains only the canonical
`LlamaCppWindowsManager.exe` executable.

Launch a published local build:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start-app.ps1
```

## Repository Hygiene

Generated output is intentionally ignored: `bin`, `obj`, `TestResults`, `dist`,
logs, local workspaces, SQLite state, and model/checkpoint files.

Clean local build/test output while keeping the current `dist` package:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\clean-repo.ps1
```

Remove `dist` too:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\clean-repo.ps1 -AllDist
```

For a pre-release scrub, remove all ignored build, test, diagnostics, publish,
distribution, and isolated development-workspace output:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\clean-repo.ps1 -AllGenerated
```

## Project Layout

- `src/LocalLlmConsole.App/` - WPF app, SQLite state, services, model/runtime
  management, process supervision, and UI pages.
- `src/LocalLlmConsole.App/tools/` - embedded runtime build helper extracted on
  demand into the app workspace.
- `tests/LocalLlmConsole.Tests/` - release-hardening tests for storage, safety,
  runtime validation, UI behavior, updates, and packaging.
- `installer/` - Inno Setup source.
- `docs/` - architecture notes, installer notes, audit notes, signing notes, and
  release-readiness checklist.

The source namespace remains `LocalLlmConsole`; the product and published
executable are `llama.cpp Windows Manager` and `LlamaCppWindowsManager.exe`.

## Known Limitations

- Installer builds require Inno Setup 6 locally or
  `LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP`.
- Hardware coverage still needs validation across missing WSL, CPU-only,
  CUDA-visible, Vulkan-visible, Intel Arc/SYCL-visible, and unsupported-backend
  machines.
- macOS/Linux desktop packaging is not a release target.

## License

This project is released under the MIT License. See [LICENSE](LICENSE).
