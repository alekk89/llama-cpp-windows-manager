# llama.cpp Windows Manager

A Windows desktop manager for installing `llama.cpp` runtimes, organizing GGUF
models, saving launch profiles, and running supervised OpenAI-compatible model
endpoints on native Windows or Ubuntu/WSL.

> Unofficial community project. Not affiliated with or endorsed by
> `llama.cpp` or `ggml-org`.

[Download the latest release](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest)
· [Read the user guide](docs/USER_GUIDE.md)
· [Automate with `llwmctl`](docs/CONTROL_API.md)

<p align="center">
  <a href="https://buymeacoffee.com/alekkson">
    <img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy me a coffee" width="217">
  </a>
</p>

![llama.cpp Windows Manager product tour](docs/images/llama-cpp-windows-manager-demo.gif)

## Why use llama.cpp Windows Manager?

* **Control `llama.cpp` without managing commands and processes by hand.**
* **Run multiple models simultaneously**, each with saved settings, a dedicated
  port, and its own `/v1` endpoint.
* **Choose a runtime for each launch profile:** native Windows or WSL, using CPU,
  CUDA, Vulkan, or Intel Arc SYCL.
* **Connect OpenAI compatible coding and chat clients** through direct model
  endpoints or one shared gateway.
* **Automate and monitor advanced workflows** with model groups, transactional
  loading, idle unloading, live and historical metrics, a selectable 24-month
  activity calendar, cache reuse and throughput statistics, logs, and the
  authenticated `llwmctl` control API.

> Choose a chat focused tool when simplicity is the priority. Choose llama.cpp
> Windows Manager when you need deeper control, multiple managed models, and
> dependable local endpoints.

## Install

Download the Windows x64 installer or portable ZIP from
[GitHub Releases](https://github.com/alekk89/llama-cpp-windows-manager/releases/latest),
along with its matching `.sha256` file. Releases are self-contained; installing
the .NET runtime separately is not required.

Verify a downloaded artifact before running it:

```powershell
$asset = "LlamaCppWindowsManager-win-x64.zip"
$expected = ((Get-Content "$asset.sha256" -Raw).Trim() -split "\s+")[0]
$actual = (Get-FileHash $asset -Algorithm SHA256).Hash
if ($actual -ne $expected) { throw "Release checksum mismatch" }
```

- **Installer:** integrated installation, Start Menu entry, updater support, and
  an optional Start with Windows task.
- **Portable ZIP:** no installer; writable application data stays in `data`
  beside `LlamaCppWindowsManager.exe`.
- **Requirements:** Windows 10 or 11 x64. GPU runtimes also require compatible
  vendor drivers and, for WSL backends, a working Ubuntu/WSL environment.

Published artifacts are unsigned unless a release explicitly states that its
signature was produced by the protected signing workflow. A checksum verifies
file integrity; it is not a publisher identity guarantee.

## Quick start

1. Open **Runtimes** and install a prebuilt runtime for Windows or WSL. You can
   also place a folder containing `llama-server` or `llama-server.exe` under the
   configured runtimes folder, then scan or register it.
2. Open **Models** to download a GGUF from Hugging Face or register an existing
   model. To add one manually, copy its `.gguf` file anywhere under the
   configured models folder, then choose **Scan Models Folder**. You can also
   choose **Add model file…** to select a GGUF anywhere without moving it.
   Discovery classifies readable GGUF metadata first and reports files it skips;
   a valid ambiguous file can be confirmed once and remains registered on later
   scans.
3. Select a runtime, adjust the launch settings, and save a profile for the
   model.
4. Open **Overview**, select the model/profile, and choose **Load**. The endpoint
   is ready when its state becomes **Loaded**.
5. Point an OpenAI-compatible client at the displayed direct endpoint, or enable
   the shared gateway in **Settings** and use a model ID returned by
   `GET /v1/models`. Each gateway model entry also reports the saved profile's
   configured `context_length` plus available GGUF training-context, parameter,
   and file-size metadata for client discovery.

The Overview live dashboard is customizable without changing telemetry
collection. Add or remove cards, combine metrics, drag cards directly, and
resize the outer card from any border or corner without entering a separate edit
mode. Resize hit areas are part of the outer border rather than overlay controls;
the pointer uses the matching Windows side/corner cursor and the card border
highlights on hover. Use **Lock** beside **Add card** to preserve the current
card dimensions while the window is resized; locked cards wrap to another row
before shrinking and manual border resizing is disabled until unlocked.
Cards cannot overlap or be reduced below the space required for their wrapped
labels, values, details, and inline charts. Nearby cards snap together while
retaining a minimum gap; when side-by-side cards are snapped, resizing a top or
bottom edge near its neighbor aligns their corresponding edges. The Overview
surface and its latest readings are retained while navigating between pages, so
returning does not rebuild every card and chart. Right-click a card to manage metrics and independent
per-metric charts, set or clear an optional card title, or remove the card; resize
directly from its sides and corners. Metric labels use the remaining row width
after the measured value and unit, avoiding premature wrapping around an empty
fixed-width value column.
Right-click open dashboard space to add a card. The hardware catalog exposes
CPU load, temperature, and clock; RAM load, used capacity, and clock; and indexed
GPU load, VRAM, draw power, core clock, core temperature, and VRAM temperature when the host probe
provides them; unsupported optional sensors stay out of the picker and do not
render unavailable rows. Charts are limited to curated time-varying readings,
so static capacities, configured clocks, slot counters, and raw runtime values
remain readable without creating dead plots. The runtime catalog exposes averages and totals when llama.cpp does not provide
a dependable per-poll live rate, avoiding permanently empty live-rate rows.
Responsive horizontal bounds, vertical positions, exact sizes, and chart choices
persist across restarts. The telemetry presentation uses the application theme's
existing surfaces and accents, with tabular measurement typography, restrained
row rules, and compact scientific-style plotting grids.

The Manager integrates observed host GPU power into combined and per-device
hourly energy buckets while a model session is active. Metrics displays the combined historical total and
calendar history; per-device history remains available through the control API.
Settings provides a currency code, day/night prices per kWh, and local night
start/end times. The current tariff is applied to the measured hourly history,
so Metrics displays estimated cost beside combined energy without storing a
second mutable billing ledger. Overview offers combined and per-GPU app-live
cost rows alongside energy rows. Costs cover observed GPU board energy only,
not whole-system wall consumption, and telemetry gaps are never estimated.
The Overview metric picker exposes optional cumulative observed-energy
energy rows for the combined observed GPUs and each power-reporting GPU; these
reset when the Manager restarts. Power-reporting GPUs are offered in
the picker before a model session starts. NVIDIA power is discovered through
`nvidia-smi`; AMD SMI and Intel XPU-SMI are used when their official CLI is
installed and the adapter exposes a power sensor. A dedicated sampler runs every
10 seconds while a session is active, reusing a recent full hardware snapshot or
falling back to a smaller power-only probe that skips CPU and RAM discovery.
With no active session, historical persistence stops and idle power detection
backs off to five minutes. Settings can enable continuous idle tracking to retain
the previous 10-second behavior.
Long polling gaps and app downtime are never
estimated, and mixed systems identify partial sensor coverage instead of
presenting an incomplete sum as total machine GPU energy. Host energy is kept
separate from model token accounting because it cannot be attributed reliably
when models overlap or the GPU is used by another process.
Per-GPU history begins when a version that supports device-level buckets first
observes that adapter; older combined energy remains in the host total and is
not assigned retroactively to individual devices.

Model inference requires the API key configured in **Settings** by default. For
local browser or client testing, set **API key auth** to **Disable**. The Manager
changes LAN exposure to **Local only**, the active key becomes empty, and no `LLAMA_API_KEY`
is passed to `llama-server`; the previous strong key remains protected for the
current Windows user and is restored when authentication is re-enabled. Every
LAN exposure mode requires authentication and cannot be saved with an empty
active key. Before a session is marked loaded, the Manager verifies the expected
policy: protected endpoints must reject an unauthenticated request and accept
the configured credential, while explicitly open Local-only endpoints must
accept the unauthenticated readiness probe.

Settings continue to save automatically. Choice controls apply quickly, while
ordinary text fields wait for a short pause in typing before saving so partial
electricity prices, ports, paths, and other text values are not committed
between normal keystrokes.

## Profiles, groups, and companions

A launch profile records the runtime, port, context, GPU allocation, sampling,
server, multimodal, and speculative-decoding options for one model. One-shot
overrides do not change the saved profile unless explicitly saved.

Groups are assigned to profiles rather than model records. Loading a group
preflights every runtime, port, duplicate-model assignment, and aggregate VRAM
requirement before starting any member. Retention policy controls automatic idle
unloading; it does not schedule inference requests.

Vision, draft, and MTP companion auto-discovery is intentionally limited to the
main model's exact folder. Explicit compatible paths may point elsewhere. See
[Launch settings schema](docs/LAUNCH_SETTINGS_SCHEMA.md) for how curated and
runtime-discovered options are rendered and persisted.

## Control API and `llwmctl`

`llwmctl.exe` is the command-line client for the authenticated, loopback-only
Manager control API (`/api/v1/*`). This API operates the running application and
is separate from the API-key-protected OpenAI-compatible gateway and direct
model-inference endpoints. It does not start unmanaged `llama-server`
processes.

```powershell
llwmctl status
llwmctl capabilities
llwmctl self
llwmctl models list
llwmctl runtimes list
llwmctl profiles list --model <model>
llwmctl load <model> --profile <profile> --wait
llwmctl sessions inspect <session>
llwmctl gateway inspect
llwmctl sessions metrics <session>
llwmctl metrics usage --range month
llwmctl sessions logs <session>
```

Automation tools should read [AGENTS.md](AGENTS.md) before operating the app.
The full command and HTTP contracts are documented in
[Local control API and `llwmctl`](docs/CONTROL_API.md).

## Security and data

- The Manager control API is loopback-only and independently authenticated.
- Model serving defaults to loopback. Gateway and direct-port LAN exposure are
  separate, explicit settings.
- Security-owned `llama-server` arguments such as host, port, and API key cannot
  be replaced through custom launch parameters.
- Native child processes are attached to a Windows Job Object so they terminate
  if the Manager exits unexpectedly.
- Downloads and updates validate sizes, checksums when supplied, filenames, and
  archive paths before installation.
- Managed runtime installs record file hashes for visible provenance and later
  re-verification; manually registered runtimes are clearly marked unverified.
- Installer repair, update, and normal uninstall preserve application data
  unless data removal is explicitly selected.

Portable data is normally stored in `data` beside the executable. When that
location is not writable, the app uses `%LocalAppData%\llama.cpp Windows Manager`.
Set `LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE` before launch to choose another
workspace.

## Development

Source development requires Windows 10/11 x64, PowerShell 5+, and the .NET 10
SDK selected by `global.json`. Inno Setup 6 is required only for installer
builds.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1
```

Add `-IncludePublish` to validate the portable package and
`-IncludeInstaller` on a machine configured with Inno Setup. Generated `bin`,
`obj`, `TestResults`, `dist`, logs, local workspaces, databases, and model files
are ignored by Git. Use `scripts/clean-repo.ps1` to remove generated output.

Create the installer directly with `scripts/build-installer.ps1` after configuring
Inno Setup and, for trusted releases, the signing certificate.

Architecture and contribution details are in
[Development guide](docs/DEVELOPMENT.md) and
[Architecture contract](docs/ARCHITECTURE.md).

## Roadmap

- [First-class vLLM runtime support through WSL](https://github.com/alekk89/llama-cpp-windows-manager/issues/17)
  is planned and open for contributions. Comment on the issue before starting
  so the runtime contract, model support, and UI work can be coordinated.

## Documentation

- [User guide](docs/USER_GUIDE.md)
- [Release readiness](docs/RELEASE_READINESS.md)
- [Windows installer](docs/INSTALLER.md)
- [Signing releases](docs/SIGNING.md)
- [Local control API](docs/CONTROL_API.md)
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Support](SUPPORT.md)

## License

Released under the [MIT License](LICENSE). Bundled dependencies retain their own
licenses; see [third-party notices](THIRD-PARTY-NOTICES.md).
