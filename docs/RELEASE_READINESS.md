# Release Readiness Checklist

Last updated: 2026-09-04

The v2.7 release is explicitly unsigned and ships the installer and portable EXE
with SHA-256 companions. See [UNSIGNED_RELEASE.md](UNSIGNED_RELEASE.md).

The local unsigned preparation passed 987 tests (933 service/core and 54 WPF),
the source/publish gate, standalone sidecar restoration, and pinned v2.6 portable
replacement with data preservation. The packaged updater checked and staged a
simulated future unsigned release using its real version with commit metadata.
Live Proxmox-to-Manager tests passed missing/invalid/valid key handling, model
discovery, inference and streaming through both the gateway and direct LAN
endpoint. Changing text scale during a live stream did not interrupt it.

Keyboard navigation, endpoint copying, light/dark readability and the candidate's
large-scale sidebar were exercised. Cross-monitor dragging was confirmed by the
owner. Spoken Narrator and Windows high-contrast presentation remain unverified;
these are disclosed accessibility validation limits, not confirmed defects.

Before publication, the final commit must pass required PR checks and the clean
Windows unsigned preparation workflow, including installer lifecycle, pinned
v2.6 installer upgrade and portable replacement. Release assets must embed that
exact source commit. Earlier dated counts below describe their audit phases.

## Automated Gate

Run from a clean checkout with the .NET 10 SDK selected by `global.json` on `PATH`, or set `LLAMA_CPP_WINDOWS_MANAGER_DOTNET` to an explicit SDK `dotnet.exe`. The legacy `LLAMA_CPP_CONSOLE_DOTNET` and `LOCAL_LLM_CONSOLE_DOTNET` variables are still accepted.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-coverage.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-vulnerabilities.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

The source and portable gate can run alongside the production Manager with
`test-release-gate.ps1 -IncludePublish`. Adding `-IncludeInstaller` also exercises
installer lifecycle operations: use a clean disposable Windows environment with
Inno Setup and any required signing certificate. The existing-installation guard
must not be bypassed on a production user profile:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

Add `-RequireCleanTree` to `scripts/test-release-gate.ps1`,
`scripts/publish-app.ps1`, or `scripts/build-installer.ps1` when packaging release artifacts; the scripts fail if
`git status --porcelain --untracked-files=all` reports any tracked or untracked
worktree changes.

Trusted signed release builds use:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-app.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1 -IncludePublish -IncludeInstaller -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
```

Trusted signed GitHub releases run `.github/workflows/release.yml` from a signed
annotated `v*` tag, or by manual dispatch selecting an existing signed tag. The
protected `release` environment must provide the Windows, tag-verification, and
manifest-signing credentials and configured publisher identity. The workflow
fails closed when any required identity is missing; it does not fall back to an
unsigned release. Local and pull-request artifacts remain unsigned development
builds and must be described that way.

## Release Gate

- Resize action columns until their icons appear, then widen the window and
  reopen the page. Confirm extra space goes to the leftmost text column while
  the other widths stay fixed; repeat after reordering and narrowing columns.
- Confirm Verify, Check and Install have compact icons with full action
  tooltips, and endpoint model IDs/names remain readable in dark and light themes.
- Load a profile without an alias and confirm its direct endpoint advertises
  and copies the short GGUF ID. Set a direct suffix, reload and check the real
  `/v1/models` value. Load duplicates on separate ports and confirm unique IDs.
- With a different profile of the same model running, exercise alongside,
  replace and cancel. Confirm unrelated models remain running and group,
  gateway and control loads retain their explicit policies.

- Publish the standalone `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe` from a clean checkout; do not publish a portable ZIP.
- Build `dist\installer\LlamaCppWindowsManager-Setup-2.7.0-win-x64.exe` from the published app with Inno Setup 6.
- Confirm the publish folder contains no `.pdb` files.
- Confirm the portable EXE and installer each have a matching `.sha256` companion file. For signed builds, generate the companion file after signing.
- Confirm signed installer builds fail before compilation if `-SkipPublish`
  points at an unsigned published executable.
- Confirm the publish folder contains `LlamaCppWindowsManager.exe` and does not contain the removed `LlamaCppConsole.exe` alias.
- Confirm the portable EXE bundle and installer contain `LICENSE`,
  `THIRD-PARTY-NOTICES.md`, `licenses\Apache-2.0.txt`, and the bundled .NET
  license/notices. Run executable-only `--bootstrap-agent-sidecars-only`
  verification and confirm all compliance notices are restored beside the app.
- Confirm the protected signing workflow pins third-party actions to immutable
  commit SHAs and installs the exact Inno Setup version before importing the
  code-signing certificate.
- Confirm fresh installer default path is `D:\LlamaCppWindowsManager` when `D:` exists, `%LocalAppData%\Programs\LlamaCppWindowsManager` when it does not, and that the setup wizard still allows the user to change the install folder.
- Confirm the installer detects an existing install and reuses its install directory on update or repair.
- Confirm the final installer page can launch `LlamaCppWindowsManager.exe`.
- Confirm fresh installer setups offer Start with Windows checked by default,
  and that Settings can disable or re-enable the current-user startup entry.
- Confirm installer update/repair does not delete `data`, models, runtimes, cache, logs, or state.
- Confirm uninstall keeps `data` by default and only deletes it when the user explicitly chooses to delete app data.
- Launch the published app on a clean Windows user profile with no repository checkout.
- Confirm only one app instance can run in the same user session.
- Confirm Runtime Downloads can check the upstream official llama.cpp release feed and list the official prebuilt packages for CUDA, Vulkan, ROCm, Intel Arc SYCL, and CPU on every Windows/WSL platform published upstream.
- Confirm Runtimes has no advanced-view toggle or Runtime Jobs section, and each Runtime Downloads row places a compact **Build from source** action—sized consistently with the other row actions—immediately left of **Install**. Confirm job supervision and control remain available through Logs and `llwmctl`.
- Confirm **Saved Launch Profiles** has no redundant **Open Folder** column; **Model Files** retains the folder action for the actual GGUF.
- Right-click a saved launch profile and choose **Add to favorites**. Confirm the
  same star state appears in Saved Launch Profiles, Overview, Benchmarks,
  Metrics, and the tray. Minimize the Manager to the tray, right-click its icon,
  and confirm favourites appear first,
  followed by an alphabetical Models submenu whose profile controls are created
  only when that model is opened. Confirm light, dark, system, high-contrast, and
  RTL presentation use the application menu theme.
- From the tray, start a stopped profile, stop its exact running profile, and
  start another profile for the same model without stopping the first. Confirm loading/stopping actions
  are disabled, repeated clicks do not queue duplicate lifecycle operations,
  normal results use tray notifications, and a required VRAM confirmation
  restores the Manager before displaying the themed prompt.
- Restart the Manager and confirm favourites persist. Remove a favourited saved
  profile and confirm its tray preference is removed without affecting other
  profiles. Leave the tray menu closed and confirm it adds no idle polling or CPU
  activity.
- Confirm a source row progresses **Check** -> **Download** -> **Build**, direct download is blocked before a successful source check, and a successful table build deletes its downloaded source and resets the action to **Check**.
- Confirm **Installed Local Builds** and **Runtime Downloads** each share one header row with their right-aligned Type and Platform filters, with no redundant descriptive sentence below either title. Confirm Type filters select AMD/Vulkan/ROCm, Intel/SYCL, or NVIDIA/CUDA and Platform filters select Windows or Linux/WSL on both inventories. Confirm CPU rows remain under All and filtering never hides Add custom source repository.
- Confirm Runtime Downloads can check the Atomic TurboQuant binary feed, install the Windows CUDA package when published, and show the WSL CUDA row as not published until a matching Linux/WSL asset exists.
- Confirm Runtime Downloads can check the TheTom TurboQuant release feed and select only the published CUDA Windows, Vulkan WSL, and CPU WSL assets. Confirm every selected asset carries a GitHub release SHA-256 digest before installation.
- Confirm Runtime Repositories lists `ik_llama.cpp` CPU/CUDA choices for both Windows and WSL, plus TheTom TurboQuant CUDA Windows/WSL, Vulkan WSL, and CPU WSL choices. Confirm built runtimes are attributed to the matching provider/backend row rather than upstream llama.cpp.
- Confirm runtime package downloads fail closed when the downloaded byte count
  exceeds or does not match release metadata, including when the response omits
  or misreports `Content-Length`, or when no SHA-256 metadata/companion checksum
  is available for a required package asset.
- Confirm installing a prebuilt runtime does not require Git, CMake, Visual Studio Build Tools, WSL build tools, or source checkout.
- Confirm installed prebuilt runtimes are registered, can be selected per model, and show update/delete state on the Runtime Downloads page.
- Select a newly installed managed runtime and confirm its details show provider,
  repository, release, assets, checksum/signature status, installed time,
  backend, version, and **Local integrity checked**. Confirm the details explain
  that this is local change detection rather than publisher authentication. Run
  **Verify**, modify a copied test runtime file, add an unexpected file, verify
  again, and confirm the runtime reports both problems.
  Confirm manual runtimes are labelled **Unverified custom runtime** and legacy
  managed installs without a manifest explain that reinstall is required.
- Confirm changing the runtime on the Models launch form immediately clears the previous runtime's discovered controls, names the runtime being scanned, and renders only the newly selected executable's safe options in grouped two-column sections without dropping unmatched custom parameters. Confirm discovered editors match the curated 28px control sizing and compact field proportions, readable labels replace raw flags, exact `--flag-name` searches still work, and unknown text/choice defaults remain visually blank. Confirm advertised positive/negative switch pairs cycle Default/Enabled/Disabled and emit the matching alias, unpaired switches expose only their advertised direction, and search-filtered runtime options reflow without blank half-rows.
- Confirm official prebuilt CUDA downloads include the matching runtime DLL/archive companion when upstream publishes one.
- Confirm source-built official runtimes can be reconciled with matching prebuilt runtimes by local runtime fingerprint.
- Confirm WSL is installed and the configured Ubuntu distro exists when a WSL runtime or WSL source build is selected, or missing prerequisites are reported clearly.
- Confirm the WSL Linux page detects `wsl.exe`, installed distros, the WSL default distro, and the app-selected distro.
- Confirm Docker-managed WSL distros such as `docker-desktop` are not shown as selectable runtime distros.
- Confirm the app prefers an installed Ubuntu distro instead of keeping a missing hardcoded distro.
- Confirm WSL install appears when WSL is missing.
- Confirm Ubuntu install appears when WSL exists but no Ubuntu distro is installed.
- Confirm Ubuntu install attempts to install `cmake` and the CPU build toolchain after the distro is ready.
- Confirm the WSL Linux page offers an Install CPU Tools action for existing Ubuntu distros and does not imply CUDA is installed.
- Confirm the WSL Linux page offers an Install CUDA action for existing Ubuntu distros and that it verifies `nvcc` and `libcudart`.
- Confirm the WSL Linux page offers an Install Vulkan action for existing Ubuntu distros and that it verifies `vulkaninfo --summary`.
- Confirm the WSL Linux page offers Intel GPU runtime and Intel oneAPI actions for existing Ubuntu distros and that they verify `sycl-ls`/Level Zero visibility for SYCL.
- Confirm the Windows page detects Git, CMake, MSVC, CUDA, Vulkan, Intel oneAPI/SYCL tools, and whether an Intel GPU is visible to `sycl-ls`.
- Confirm CPU/CUDA/Vulkan/SYCL actions switch to Update/Repair when detected and show Delete actions only when detected.
- Confirm Delete WSL and Delete Ubuntu actions require explicit confirmation and open visible PowerShell.
- Confirm WSL and Ubuntu update checks appear when those components are installed.
- Confirm the WSL row shows Install WSL when WSL is missing and Update WSL when WSL exists.
- Confirm the Ubuntu row shows Install Ubuntu when Ubuntu is missing and Update Ubuntu when Ubuntu exists.
- Confirm the local service binds only to `127.0.0.1`.
- Confirm model serving defaults to local-only `127.0.0.1`.
- Confirm Settings LAN exposure maps Local only to loopback, Gateway LAN only to the router listener, Direct models LAN only to runtime hosts, and Gateway + direct LAN to both serving surfaces.
- Confirm Settings LAN exposure changes only model-serving endpoints, not the app-local control service.
- Set a profile Host IP to a LAN address with Local only or Gateway LAN only:
  confirm the preview explains the loopback listener, the loaded endpoint uses
  loopback, and readiness/metrics succeed. Enable Direct models LAN only and
  restart: confirm the command and endpoint use the saved LAN address.
- Load a profile whose saved JSON predates Host IP and confirm it inherits the
  app host default; confirm explicitly saved loopback addresses remain intact.
- With a runtime advertising projector offload switches, confirm Disabled emits
  `--no-mmproj-offload`, Enabled emits `--mmproj-offload`, and Default emits
  neither. Save/reopen the profile and verify its selected value is retained.
- Confirm the Overview Loaded Model Sessions grid shows an auto-load gateway
  router row with endpoint, policy, LAN exposure, and current direct-session
  count.
- Double-click a running model row and click its direct endpoint link. Confirm
  both open the themed endpoint report populated from `/health`, `/v1/models`,
  `/props`, and `/slots`, including context, output limit, reasoning/template
  capability, sampling defaults, and current slot state without generating text.
  Confirm field text and table cells can be selected, **Copy endpoint** copies
  the direct `/v1` URL, **Copy report** includes the visible endpoint details but
  no API key, and **Copy API key** copies the credential used by that session.
- In both direct and gateway endpoint reports, select model ID and name text
  independently and use each row's **Copy model ID** button. Confirm it copies
  the exact ID/alias, including suffixes, without the display name or headers.
  Check multiple rows, long IDs, and a temporarily unavailable clipboard.
- Inspect the gateway row and endpoint link. Confirm the report shows advertised
  profile model IDs, running sessions, policy, and exposure, and explains that
  context/reasoning/output defaults belong to each routed model. Confirm a runtime
  without `/props` or `/slots` still shows available data plus a compact warning.
  Confirm the gateway's dedicated API-key action copies the current model API
  key from Settings.
- Confirm Overview places Model, Launch profile, and Load on one row; Model and
  Launch profile grow and shrink with the available window width while keeping
  their left-side stars visible.
  Open both dropdowns, type a partial name, and confirm the typed query remains
  visible in the popup while choices filter immediately. Star and unstar a model
  and profile, confirm favorites move to the top without selecting them, and
  confirm the same favorite order appears in Benchmarks and Metrics. Confirm
  closing without choosing preserves the prior selection.
  Confirm each available model is labelled `Name · size` in the Model dropdown,
  while group choices keep their `Group · name (count)` label. Remove a registered
  GGUF, rescan, and confirm the Models Size column shows `Missing`, Overview shows
  `Name · Missing`, and the model's launch profiles remain registered.
  When a model is running, selecting its active profile hides Load, while
  selecting a different profile shows Load and starts that exact saved profile
  as a second session. Confirm both profiles remain listed and can be stopped or
  restarted independently.
- Confirm the Overview Model Status card shows Loading Model / Loaded Model and
  Loading Time as separate rows, and that Loading Time remains at the completed
  duration after the model becomes ready.
- At the default window size, confirm Overview uses two metric-card columns with
  no clipped card content and the loaded-session Runtime column remains readable;
  maximize the window and confirm the cards reflow to three columns. Enter
  Drag cards directly and resize the visible outer card from the top, bottom,
  left, right, and all four corners without entering an edit mode. Confirm each
  edge and corner shows the corresponding Windows resize cursor, no overlay
  handles exist, and only the visible card border highlights on hover.
  Confirm idle cards have no decorative blue line in their upper-left corner.
  Confirm nearby cards snap together, cannot overlap, and retain a minimum gap.
  Select **Lock** beside **Add card**, resize the application wider and narrower,
  and confirm card dimensions remain fixed while cards wrap to new rows. Confirm
  border resize cursors are disabled while locked, **Unlock** restores responsive
  sizing and manual resizing, and the lock state survives an application restart.
  With two cards snapped side by side, drag one card's top and bottom borders
  near the matching neighbor edges and confirm the corresponding edges align;
  confirm the card still respects the minimum height required by its rows.
  Confirm cards stop shrinking when
  their wrapped labels, values, details, or inline chart need the space. Confirm exact bounds survive
  restart and horizontal positions scale with the window width. Resize the window
  narrowly and add charts/details that increase card height; confirm measured
  outer cards are separated again before rendering.
- Open another page, wait for at least one hardware polling interval, and return
  to Overview. Confirm the existing cards and charts appear immediately without
  an empty rebuild phase, and hardware readings update without switching pages twice.
- Confirm each Overview metric uses its predetermined row presentation with
  label, value, unit, and optional detail kept in separate fields. Confirm rates,
  token counts, percentages, active/capacity slots, CPU, RAM, and each GPU do not
  depend on the legacy free-form metric-line parser. Confirm numeric values use
  aligned tabular figures, row rules stay subtle, telemetry cards match the
  surrounding raised surfaces, and plot grids remain legible without dominating
  the values. Run two parallel
  requests and confirm totals continue increasing after either slot is reused.
- Confirm Tokens, Speculative Tokens, and KV Cache show compact trend graphs on
  the bottom row, retain at most 60 samples, and reset when the selected runtime
  changes. Confirm Slots appears on the top row and shows active/total capacity,
  queued requests, and busy decode slots.
- Right-click a card and confirm metrics can be added or removed, the card can be
  given an optional title or returned to a headerless state, removed, and only
  chart-capable metrics are offered in the Chart submenu.
  Confirm there is no Size submenu, one separator remains immediately above
  Remove card, and direct side/corner dragging remains the only resize control.
  Click Remove metric and Chart, select a child command, and confirm the card
  updates after the popup closes. Enable and disable charts for multiple metrics
  in the same card independently. Right-click open dashboard space to add a card.
  Open Add metrics and confirm Cancel and Add selected have identical rendered
  width and height in every supported language.
  Focus a card and confirm Shift+F10/Menu opens its actions, Ctrl+Arrow moves it,
  Ctrl+Shift+Arrow resizes it while unlocked, and Alt+Up/Down reorders a focused
  metric. Confirm focus returns to the changed card or row and every card/metric
  exposes a useful automation name and help text.
  Confirm token, speculative, and KV-cache labels remain on one line whenever
  the combined label, measured value, and unit fit, with values still aligned to
  the card's right edge and no reserved half-row gap.
  Confirm the searchable picker exposes CPU load, temperature, and clock; RAM
  load, used capacity, and clock; and every observed GPU's load, VRAM, draw power,
  core clock, and temperature when provided by the host probe, plus individual
  slot/request counters, average token rates and totals, average speculative
  rates and totals, but no unreliable generation/prompt/speculative live rates,
  KV-cache values, and currently visible raw Prometheus metrics. Mix unrelated
  metrics in one card, confirm cards have no fixed type, reset restores the default
  layout, and confirm all choices survive restart.
- With multiple models loaded, switch the Overview telemetry source and confirm
  the matching Loaded Model Sessions row remains highlighted across refreshes.
  Unload all models and confirm the grid has no highlighted row.
- Generate requests with prompt-cache reuse, open **Metrics**, and confirm the
  7-day, current-month, 90-day, and all-time ranges show evaluated input, cached input, output,
  totals, and cache hit rate. Confirm model/profile/runtime filters update the
  responsive activity calendar and breakdown without restarting the app.
  Confirm fixed-size day boxes reveal more history as the window grows, up to
  the 24-month calendar data window, while dates before tracking and future
  dates are unavailable rather than false zero usage. Click a day twice to
  select and clear it; use Ctrl+click for disjoint
  dates, Shift+click for a continuous week/range, and Ctrl+Shift+click to add a
  range. Confirm selected dates drive every summary card and model row. Confirm the compact
  7D/Month/90D/All selector updates the calendar in one click, and Month includes
  every calendar day through the end of the current month.
- Confirm the model/profile/runtime dropdowns on Benchmarks and Metrics, plus
  the runtime dropdown above Model launch settings, filter by partial text as it
  is typed and commit a filtered choice only once.
- On a host with a supported GPU power sensor, leave the Manager running through
  at least two 10-second samples. Confirm the Metrics page displays combined
  historical energy in its summary and calendar without per-GPU cards. Before
  loading a model, confirm the optional observed-energy live total and each
  power-reporting GPU energy row are available to add to an Overview card. Add them and
  an Overview card; confirm they increase independently of model selection and
  continue across model load/unload. Confirm they reset after the Manager restarts. Compare the
  live per-GPU sum with its live combined row. Compare the observed
  GPU count with installed adapters. On mixed hardware where only some adapters
  expose power, confirm the summary says partial coverage. Suspend the machine or
  stop sampling for more than 30 seconds and confirm the gap is not backfilled.
  Verify `llwmctl metrics usage --range 1d` retains historical
  `gpuEnergy.wattHours`, `gpuEnergyDevices`, per-day energy, sampled seconds,
  and coverage counts for later automation use.
- Configure distinct day/night electricity rates, a currency code, and a tariff
  boundary that crosses an hourly bucket. Confirm invalid currency/rate/time
  values are rejected, Settings normalizes times to `HH:mm`, Metrics shows the
  selected historical estimated cost beside combined energy, and the control
  response exposes `gpuElectricityCost`. Add combined and per-GPU observed live
  cost rows before loading a model; confirm their sum matches, a tariff edit
  recalculates them, a Manager restart resets them, and no cost is invented across a power
  telemetry gap.
- Confirm active days, average tokens per active day, peak day, tracked-token
  share, and prompt/generation throughput match the persisted facts. Confirm
  request totals remain unavailable for runtimes without a compatible counter
  and become selectable in the calendar when a counter is exposed.
- Confirm an upgraded workspace preserves its previous lifetime totals, labels
  them as predating daily tracking, and does not fabricate historical days.
  Verify `llwmctl metrics usage --range month` matches the page totals.
  Verify repeated `--date YYYY-MM-DD` values match the same multi-day UI selection.
- Confirm the independent CPU, RAM, and indexed GPU metrics remain available
  regardless of which card contains them. CPU temperature/current clock and RAM
  used capacity/configured clock should appear when available. NVIDIA GPU VRAM,
  draw-power, core-clock, and temperature metrics should be independently
  chartable. NVIDIA SMI should be used for CUDA when available; installed AMD SMI
  and Intel XPU-SMI tools should enrich matching adapters with power telemetry,
  with Windows GPU performance-counter fallback for unsupported fields and no
  stale cached hardware data after switching runtimes.
- Confirm the Settings API key Generate action creates a new model API key.
- With **Auto-load models** enabled, confirm the gateway `/v1/models` response lists every saved launch profile,
  reports each profile's configured `context_length`, exposes accurate GGUF
  training context, parameter count, and file size in `meta`, and requesting
  another profile for a running model keeps both profile routes loaded under
  **Prefer keeping loaded models** while **Single active model** stops the others.
- Set **Auto-load models** to **No**. Confirm discovery lists only running
  profiles, keeps their alias suffixes, and serves those exact sessions. With
  either gateway policy, requests for unloaded profiles must return
  `503 model_not_loaded` without starting or stopping sessions. Check empty
  discovery when nothing is loaded, persistence after restart, the
  `gatewayAutoLoadModels` control setting, and re-enabling normal auto-loading.
- Confirm Settings is separated into named category sections arranged in two
  equal-width columns rather than one large full-width settings grid. Confirm
  Network and UI remain in opposite columns and narrow values/actions do not
  force a page-level horizontal scrollbar.
- Confirm Settings uses readable 28px editors inside compact rows, narrow
  right-aligned dropdowns, no Save Settings button, and a visible automatic-apply
  hint. Confirm Network has no blanket Action column and API-key Show, Copy, and
  Generate controls appear only inside the API-key value row.
- Confirm Settings includes a **UI** category with **Show/Hide** choices for
  Model Status, Live Runtime Log, and the Models Hugging Face section.
  Confirm each choice applies automatically, hidden rows leave no blank
  splitter/space, and choices persist after restart. Confirm hiding Model Status
  collapses the complete dashboard section without changing its saved cards and
  leaves the model/profile controls and loaded-session table visible. Confirm Hardware, Slots, Tokens, Speculative tokens, and
  KV cache are customized from the Overview dashboard rather than duplicated as
  Settings rows. Confirm the raw-metrics and compatibility API fields remain
  available; the latter still add or remove their metric group from the dashboard
  without discarding unrelated cards, grouping, sizes, or charts.
- Confirm a workspace without stored UI visibility keys defaults Hardware,
  Tokens, Speculative tokens, KV cache, Model Status section, and Live Runtime
  Log to `Show`; the Model status metric, Slots, raw metrics, and Hugging Face
  default to `Hide`. Use
  `llwmctl settings set` followed by `llwmctl settings get` to verify the same
  ten fields can be changed and read back through the live Manager without
  restarting it.
- Confirm the Overview live runtime log remains a compact 24-line viewport and
  scrolls to older captured lines. Change **Runtime log order** between **Latest
  on top** and **Latest on bottom**, confirm the viewport follows the selected
  edge immediately and after restart, and confirm the persisted log file and
  control-API log output remain chronological.
- On a GPU whose driver reports a finite VRAM temperature, confirm the per-GPU
  **VRAM temperature** row appears in Add metrics, updates independently of core
  temperature, and can be charted. Confirm it is absent rather than shown as
  unavailable on GPUs that do not expose the sensor.
- Confirm the compact title-bar menu icon collapses the full navigation sidebar,
  expands the current page without navigating away, and restores the sidebar on
  the next activation.
- Confirm Settings shows cache size at the top and Clear removes cache contents only when downloads/builds are idle.
- Confirm Help opens with six compact categories and collapsed task articles,
  category selection filters without leaving stale search text, and article
  actions navigate to the correct app page.
- Confirm Help search updates immediately across every category, ranks useful
  matches for API key, 401, GGUF, CUDA, memory, port, and download queries,
  exposes a useful empty state, and can be cleared with the button or Escape.
- Confirm Ctrl+F focuses Help search and that the search field, category actions,
  result announcement, expanders, and navigation buttons expose automation names.
- Switch Help to one of Arabic, Bulgarian, Czech, German, Spanish, Persian,
  French, Hindi, Indonesian, Italian, or Japanese and confirm its
  category text, complete article content, actions, search state, and search
  results are translated and searchable. Other language packs currently use
  English fallbacks for the newly rebuilt Help articles.
- Confirm **Saved Launch Profiles** shows a compact Group column, ungrouped rows
  expose an unclipped inline **Add** button, grouped rows expose their group name
  with **Change group…** and **Remove from group**, and **Groups…** opens a compact
  table matching the Runtimes grids. The **New group** dialog captures name, keep-live policy, idle
  timeout, and eviction priority, **Edit** changes all four while preserving
  membership, **Profiles…** supports multi-select launch-profile assignment and removal, and
  right-click **Assign to group…** can assign a profile or return it to global policy.
- Confirm Overview lists `Group · name (count)` choices after physical models.
  A valid group starts all assigned profiles, including multiple profiles backed
  by the same GGUF; unavailable-runtime, duplicate-port, missing-GPU-telemetry,
  and aggregate-VRAM failures display an error
  before any member starts. Confirm CPU-only groups work without VRAM telemetry.
- Switch between Dark and Light without restarting on Models while the launch
  settings form is visible. Confirm every panel changes theme, then inspect Overview,
  Runtimes, and Settings for distinct sidebar, page, card, header, input, alternate-row,
  border, hover, selection, disabled, success, warning, and danger surfaces.
- Confirm launch-profile groups persist across restart; profiles of the same model
  can use different policies, and deleting a group clears membership
  without deleting GGUF files, model registrations, running sessions, or launch profiles.
- Confirm pinned models are excluded from automatic idle unload, group idle timeout
  overrides the global timeout, and simultaneous quiet candidates unload one at a
  time in low/normal/high eviction-priority order. Confirm active slots are not
  unloaded and priority does not change inference request ordering.
- Confirm `llwmctl groups` CRUD/assign/unassign commands and the corresponding
  `/api/v1/model-groups` and `/api/v1/models/{model}/profiles/{profile}/group`
  routes update the running Manager and appear on launch profiles in normal model inventory results.
- Confirm local-only model serving launches with an API key, direct
  `/v1/chat/completions` rejects missing or invalid credentials, and the gateway
  rejects unauthenticated `/v1/models` requests. Upstream direct health or model
  catalog metadata may be public and is not an inference-authentication test.
- Confirm readiness performs the protected-route authentication check before a
  session is marked loaded, and that a stub runtime accepting the unauthenticated
  request is stopped with a visible status instead of remaining available.
- Run the deterministic fake-runtime integration tests and confirm supervised
  startup, bearer-authenticated health/model probes, served-model discovery,
  redacted logs, deliberate crash reporting, and verified process termination.
- Load a model and run `llwmctl sessions inspect <session>`; confirm health,
  models, defaults, and slots are returned without the serving API key appearing
  in the response or Control API log. Run `llwmctl gateway inspect` and confirm
  the same authenticated behavior through the shared gateway.
- Confirm the persisted model API key is protected at rest for the current Windows user.
- Confirm ports outside `1..65535` are rejected on Settings save.
- Confirm protected model serving cannot launch without a strong model API key.
  Confirm explicitly unauthenticated Local-only serving launches with an empty
  active key, and every LAN exposure mode rejects disabled authentication.
- Install a runtime package and confirm `local-llm-runtime.json` records its
  release tag, published target, selected asset hashes, and installed-file hash
  manifest. Download source from a moving branch and confirm the downloaded
  source/build records the resolved commit used by that installation.
- Confirm control-API settings patches can disable API-key authentication only
  with Local-only exposure, cannot replace protected secrets/workspace paths,
  cannot use invalid ports, and cannot enable the gateway on a port occupied by
  a running model.
- At 100%, 125%, 150%, and 200% display scale, confirm the initial window fits
  the monitor work area and the Overview model/profile/load bar reflows without
  clipping at narrow widths.
- With Windows scaling unchanged, move the **UI scale** slider through its 75%
  to 175% range. Confirm it snaps in 1% steps, displays the current percentage,
  resizes synchronously while moving in either direction without pausing,
  persists once on pointer/key release, survives restart, and does not compound
  transforms when returned to 100%.
- Move the **Text scale** slider through the same range. Confirm text changes
  synchronously and survives restart, while control sizes, spacing, window
  chrome, and layout transforms remain unchanged and repeated changes do not
  compound.
- In **Settings → Load profiles on startup**, type into the dropdown search,
  add at least two saved model/profile pairs, and confirm the dropdown and Add
  button match the compact height of the surrounding settings controls. Remove
  one row, then toggle another with **Load on startup** from Saved Launch
  Profiles. Restart and confirm only the remaining selections load, a failed
  selection does not prevent later selections, and deleting a saved profile
  removes its startup reference.
- Resize and reorder representative table columns on Overview, Models, Runtimes,
  Benchmarks, Metrics, and Logs. Resize the splitters that control page-section
  proportions, close and reopen each page, then restart the Manager and confirm
  every page restores its own layout. Move the window between monitor layouts
  and confirm stale bounds are clamped back to the visible desktop.
- Narrow every resizable Delete/Remove action column below its full label width
  and confirm the action changes to **×** without clipping; widen it and confirm
  the localized full label returns. Repeat on Models, Saved Launch Profiles,
  startup profiles, Runtimes, runtime packages, Logs, Hugging Face history, and
  Benchmarks.
- On Models and Runtimes, shrink ordinary text and non-destructive action
  columns to 48 px using both the left and right sides of shared header
  boundaries. Confirm text may clip without the old wide minimum snapping back.
  Narrow Models Folder until **Open** becomes the folder glyph, then widen it
  and confirm the full localized label returns while its tooltip and automation
  name remain unchanged.
- Confirm Model Files, Saved Launch Profiles, and Runtimes provide compact search
  controls and visible star actions. Favorite rows must sort first without a
  selection highlight or automatic detail expansion. Confirm only the star
  changes color, stars align vertically across every row and selector, and the
  Runtimes vertical-ellipsis detail action remains square and aligned.
- Narrow Settings below its responsive breakpoint and confirm every section
  appears in its original order in one column. Widen it and confirm sections
  return to the balanced two-column layout without losing edited values.
- At the minimum supported width, confirm Models keeps the launch form and
  tables usable through horizontal scrolling instead of clipping actions.
- Confirm Arabic and Persian switch the shell and owned dialogs to right-to-left
  flow. Confirm Arabic and Hindi are visibly labeled as partial previews, while
  production language packs pass the localization coverage floor.
- Open Model Groups and inspect a direct endpoint and the gateway in at least one
  non-English language. Confirm titles, buttons, columns, validation messages,
  report fields, and status messages are translated; repeat in Arabic or Persian
  and confirm owned dialogs use right-to-left flow.
- With Windows Narrator or Accessibility Insights, confirm custom title-bar
  buttons and the language selector have accessible names, changing status is
  announced politely, section titles are headings, and row-action buttons expose
  both names and help text.
- Enable Windows high contrast while the app is running and confirm palette
  resources update without restart, text and controls retain system contrast,
  and keyboard focus remains visibly outlined across inputs, tables, links,
  menu items, and dashboard cards.
- Confirm a LAN client can reach the selected OpenAI-compatible `/v1` serving
  surface only after Windows Firewall and WSL networking allow the configured
  gateway or direct model port.
- Confirm the WPF app is the only user-facing surface; no web UI is launched.
- Confirm no command prompt windows remain open for app services.
- Confirm app-local API requests without the session token return `401`.
- Confirm SQLite state tables are created under the startup workspace.
- Confirm corrupt settings are backed up and defaulted.
- Confirm corrupt SQLite DB files are quarantined and the app recreates state.
- Confirm interrupted jobs are marked `Interrupted` on restart and can be resumed or removed.
- Confirm oversized auto-load gateway request bodies are rejected with `413`
  before proxying to a model runtime.
- Confirm a gateway request times out if upstream response headers never arrive,
  but a long streaming completion continues after headers until completion or
  client/app cancellation.
- Confirm Hugging Face downloads cannot write outside the configured models folder.
- Confirm a filesystem error while preparing a Hugging Face download records a failed job with its error, releases the active-download slot, and preserves files that were never opened for transfer.
- Confirm completed downloads are not registered when the final byte count mismatches the expected size or no expected size/SHA-256 metadata exists.
- Confirm imported external model deletion removes only app registration files.
- Confirm Settings exposes **API key auth**. With **LAN exposure = Local only**,
  set it to **Disable**, verify the information prompt explains that the Manager
  changed exposure to **Local only**, verify the displayed active key is empty,
  load a model, and
  access the llama.cpp WebUI and inference without credentials. Re-enable it and
  verify the preserved key returns.
- Confirm every LAN exposure mode rejects **API key auth = Disable**, while rotating
  the model key re-enables authentication. The Manager control API must remain
  authenticated in either model-serving mode.
- Type multi-character values into the electricity price and other ordinary
  Settings text fields at a normal pace. Confirm no intermediate value saves
  between keystrokes, the final value applies after typing pauses, leaving the
  field commits naturally, and choice controls still apply quickly.
- Confirm **Scan Models Folder** registers a readable main GGUF whose filename
  contains a generic interior `-MTP-` token, reports standalone assistant and
  projector GGUFs as companions, and explains ambiguous or invalid files.
- Confirm **Add model file…** can register a valid GGUF outside the configured
  models root, requires confirmation for an ambiguous/companion role, rejects an
  unreadable `.gguf`, and preserves the confirmed registration after rescanning.
- Confirm app-owned downloaded model deletion cannot escape the configured model root.
- Confirm vision-capable model settings persist image min/max token allowances and launch `llama-server` with `--image-min-tokens` / `--image-max-tokens` when set.
- Confirm per-model Vision head choices persist for auto-detect,
  embedded/model-bundled, and explicit external projectors; explicit projectors
  launch with `--mmproj`, while embedded choices omit `--mmproj`. Confirm auto
  discovery searches only the exact model folder and does not infer embedded
  vision from a vision-capable language GGUF.
- Confirm per-model MTP head choices persist separately from Vision head,
  `Spec type = atomic-mtp` launches legacy compatible forks with `--spec-type mtp --mtp-head`, an embedded positive
  `*.nextn_predict_layers` value makes `draft-mtp` omit `--model-draft`, and an
  explicitly selected external draft model still launches with
  `--model-draft`. Confirm each draft type selects only its matching MTP,
  DFlash, DSpark, Eagle3, or simple-draft category; parent/child folders and
  incompatible family, version, or target-size helpers are not auto-selected.
- Confirm `Spec type = draft-dspark` launches a DSpark GGUF with
  `--spec-type draft-dspark --model-draft <path> --spec-draft-n-max 7` on a
  llama.cpp b10164-or-newer runtime and reports draft acceptance metrics.
- Confirm GPU mode `single` emits `--split-mode none`, multi-GPU modes emit the
  selected `layer`, `row`, or `tensor` split mode, and optional GPU device IDs
  and proportions emit `--device` and `--tensor-split`.
- Confirm downloaded runtime source and build deletion cannot escape the configured runtimes folder.
- Confirm Runtime Downloads table builds always delete the downloaded source after success. Confirm lower-level source-build operations delete the source when Settings > Runtime > Delete source after build is `Yes` and preserve it when set to `No`.
- Confirm multiple models can be loaded at the same time on different saved model ports when hardware capacity allows it.
- Confirm the auto-load gateway serves one shared `/v1` endpoint, launches the
  requested model on its saved direct port, and proxies requests to that direct
  endpoint.
- Confirm Gateway policy > Prefer keeping loaded models preserves existing
  sessions and blocks/warns clearly when VRAM admission predicts that another
  GPU model is unsafe.
- Confirm Gateway policy > Single active model unloads other direct sessions
  before loading the requested model.
- Confirm CPU-only Ubuntu/WSL llama.cpp source build path succeeds after Install CPU Tools, or fails early if Git/CMake/compiler tools are still missing inside Ubuntu.
- Confirm CUDA Ubuntu/WSL llama.cpp source build path succeeds after Install CUDA on supported NVIDIA hardware, or fails early with a clear driver/toolkit error.
- Confirm Vulkan Ubuntu/WSL llama.cpp source build path succeeds after Install Vulkan on supported WSL Vulkan hardware, or fails early with a clear driver/toolkit error.
- Confirm Intel Arc SYCL Windows and WSL launches/source builds fail early with clear oneAPI/SYCL prerequisite messages when tools or Level Zero GPU visibility are missing.
- Confirm custom runtime repository row can add an HTTPS repo and then download/check/delete it from Runtime Repositories.
- Confirm CUDA runtime builds fail before CMake with a clear message when `nvcc` or `libcudart`/CUDA Toolkit runtime libraries are missing inside the selected WSL distro.
- Confirm Vulkan runtime builds fail before CMake with a clear message when Vulkan headers, `glslc`, `vulkaninfo`, `libvulkan.so`, SPIR-V headers, or a WSL-visible Vulkan device are unavailable.
- Confirm no harness-specific configuration page or settings appear in the app.
- Confirm startup update checks change the left-nav Updates item to Install Update when a newer GitHub release exists.
- Confirm manual Check For Updates shows a no-update popup when current, or an install confirmation when a newer release exists.
- Confirm the release includes the installer and standalone EXE, each with a
  matching SHA-256 companion, and no portable ZIP. Checksum failures prevent staging.
- Install unsigned v2.7 manually from v2.5 or v2.6; their existing updaters require signatures.
- Confirm unsigned v2.7 can check and stage a subsequent checksum-verified unsigned release.
- Confirm a signed installed app refuses an unsigned or differently signed staged update.
- Confirm a completed staged update restarts `LlamaCppWindowsManager.exe` and shows the GitHub release notes.
- Confirm a non-critical staging-cleanup failure after successful replacement is
  reported but does not prevent the replacement executable from restarting.
- Confirm an older renamed portable install migrates to `LlamaCppWindowsManager.exe` and removes the obsolete executable after shutdown.
- Confirm shutdown continues remaining cleanup after a non-critical cleanup
  failure, bounds background-task drain to 15 seconds, stops runtime sessions
  before tearing down local hosts/state, and records cleanup warnings.

## Current 2.7 prerelease status

The stabilized candidate includes the documented profile, naming, benchmark, and
LAN changes plus the audit reliability fixes. The 2026-09-04 source and portable
gate passed 972 tests (918 service/core and 54 WPF), with no failures or skips;
service coverage was 84.4% and model/view-model coverage was 97.7%. These results
describe that local candidate, not a clean tagged or signed release.

Second-machine LAN checks verified access-mode isolation, missing/wrong-key
rejection, explicit versus inherited profile hosts, and control-API loopback
isolation. Successful-key remote inference and streaming remain pending. The
first-use gateway permission retry also passed in the deployed application: a
previously unreserved port gained its Windows reservation and became healthy
without a manual gateway restart. A remote client reached it and received the
expected missing-key rejection. The appearance and decline behavior of the
interactive UAC prompt are not certified by that successful grant path.

Portable replacement/restart and locked-file rollback passed on the preceding
candidate before the gateway-only permission fix. The exact final release
artifacts still require clean-machine installer install, repair, uninstall, and
pinned previous-version upgrade validation. Installer compilation is not proof
of installer lifecycle success.

The default installer and portable upgrade baseline is now the published v2.6.0
release, with asset sizes and SHA-256 values pinned in
`tests/release-baselines/v2.6.0.json`. The earlier v2.4.0 baseline remains available
for explicit compatibility runs. Changing the baseline does not itself validate
an upgrade. The installer fallback version matches the 2.7.0 app and CLI.

The candidate contains uncommitted and required untracked source files. Integrate
all intended files, then rerun the release gate from a clean checkout. Signing
credentials, the release enable switch, and protected repository settings must
be verified for publication; they are not certified by local tests.

The earlier higher private-memory observation did not reproduce in a matched
short comparison of v2.6 and the exact deployed v2.7 executable. Each completed
63 streamed requests after the same UI warmup and then two minutes of idle.
Post-collection private memory was 267.0 MiB for v2.6 and 259.7 MiB for deployed
v2.7; retained managed graphs were 25.7 and 25.6 MiB. This is not a full-duration
soak or proof of leak freedom. A forced-length chat workload also exposed an
upstream runtime parser error directly and through the gateway; the successful
comparison used identical plain completions instead.

All three available RTX 3090 devices passed individual CUDA load, 128-token
generation, and unload checks. Live power and energy readings were visible with
the installed driver. Other hardware/backend combinations, full Narrator/keyboard
workflows, per-monitor DPI, and clean-machine checklist items remain unverified
where they lack explicit evidence.

High-scale visual inspection exposed sidebar navigation hidden behind its footer.
The release candidate now scrolls that area; a compiled WPF regression verifies
that the first and last navigation buttons remain reachable at constrained height.
This sidebar fix is included in the new portable build, not the deployed binary
used for the resource and GPU checks. This candidate is not yet cleared for publication.

## Historical Local Verification

The following snapshots apply to earlier source and artifact states. Their test
counts and completed checks must not be substituted for final-artifact evidence.

The 2.7.0 host-policy and projector-offload changes were verified on 2026-09-02.
Release app and CLI builds passed with zero warnings. All 826 service/unit tests
and 26 WPF tests passed without skips; service coverage was 83.4% and
model/view-model coverage was 97.5%. Code shape, formatting, and whitespace
checks passed. After aligning the documentation check with the GitHub draft
workflow, `test-release-gate.ps1 -IncludePublish` passed in full. Portable
packaging and artifact verification passed; the package audit found no known
vulnerabilities, outdated direct packages, or deprecated dependencies.

Regression coverage includes policy-forced loopback with a saved LAN host,
matching command/endpoint addresses, legacy profiles without a saved host,
explicit saved loopback overrides, and discovered projector-offload controls.
The local installation was updated to the checksum-verified unsigned 2.7.0 app
and matching CLI. A real Qwen3.8 27B Q4 model with vision enabled loaded through
`llwmctl` using one-shot host and `--no-mmproj-offload` overrides. Windows socket
inspection and authenticated Manager probes agreed on the selected LAN address;
endpoint inspection, telemetry, and gateway health succeeded. An unauthenticated
inference request returned HTTP 401. Saved profiles and app settings were
preserved. Access from a second LAN machine and installer checks remain pending.

The repository gate was rerun for the themed tray-profile module on 2026-08-26:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1
```

Result: Release app and CLI builds succeeded with zero warnings; service/unit
tests passed (`699/699`) and eleven WPF tests passed with no skips. Service
coverage was 83.0% and model/view-model coverage was 97.4%. Formatting,
documentation, whitespace, and package vulnerability/deprecation/currency checks
passed. Publish and installer checks were not part of this feature-only rerun.

Historical repository-only release candidate audit on 2026-08-25:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-release-gate.ps1 -IncludePublish
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -SkipPublish
```

Result: the platform-neutral .NET 10 Core library, Windows Release app, and CLI
builds succeeded with zero warnings; service/unit tests passed (`695/695`) and
nine independently diagnosable WPF tests passed with no skips. Service coverage
was 83.0% and model/view-model coverage was 97.0%. Formatting, documentation
format/link/version checks, whitespace, vulnerability, deprecation, and
package-currency checks passed. Sanitized upstream fixtures cover valid,
partial, unknown-field, malformed, and rate-limited release-feed responses, plus
valid and malformed WSL and GPU probe output.

The WPF harness uses a resource-only test application and a unique temporary
workspace; it cannot invoke the production startup lifecycle. Five consecutive
stress passes covering process supervision, gateway transport, shutdown, update,
and diagnostics behavior passed 40 tests each and left no fake runtime process
behind.

The packaging audit passed portable publish, development-manifest signing and
verification, a real v2.4.0-to-candidate portable helper update, and the Inno
Setup 6.7.2 candidate build. The portable and installer artifacts have matching
SHA-256 companions, and the portable package contains the SBOM, licenses, CLI,
and automation sidecars without PDBs or the removed legacy executable alias.

The exact-installer clean install, repair, uninstall, and pinned previous-version
upgrade were intentionally not rerun in this Windows user session. The production
installer identity and Start Menu shortcut are present and Windows Sandbox is not
available. Both hardened scripts refused before creating a test install. Those
checks remain mandatory on a clean disposable Windows runner; the protected
release workflow runs the signed installer gate and pinned upgrade before publishing.

Real llama.cpp model serving, Windows/WSL hardware lanes, LAN reachability,
interactive DPI checks, Narrator or Accessibility Insights, and live high contrast
remain clean-machine or hardware validation items. Exercising them in this user
session would require stopping or sharing state with the production Manager and
was outside this isolated audit.

The original audit above evaluated the signed release path. The maintainer has
since selected an explicitly unsigned community release for v2.7. Follow
[UNSIGNED_RELEASE.md](UNSIGNED_RELEASE.md), retaining the same test and PR gates.
The trusted workflow remains separate and never falls back to unsigned publishing.

## Manual Clean-Machine Test

Use a disposable Windows user environment for this section. The production
installer has a fixed per-user Inno Setup identity and Start Menu shortcut.
Automated installer and previous-version upgrade scripts now refuse to run when
either marker already exists, which prevents a temporary test from replacing a
real installation's uninstall registration or shortcut.

1. Start from a clean Windows VM.
2. Install `dist\installer\LlamaCppWindowsManager-Setup-2.7.0-win-x64.exe`.
3. Confirm the installer prefers `D:\LlamaCppWindowsManager` when `D:` exists and allows choosing a different folder before install.
4. Confirm the launch-after-install option opens the app.
5. Confirm first launch creates `data\models`, `data\runtimes`, `data\cache`, `data\state`, and `data\logs` beside the exe when the install folder is writable.
6. Run the installer again and confirm it detects and updates the existing install without deleting `data`.
7. Uninstall and confirm `data` is kept by default; repeat on a disposable install and choose the explicit delete-data option to confirm data removal.
8. Copy only `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe` into a writable portable test folder.
9. Confirm launching from a non-writable location falls back to `%LocalAppData%\llama.cpp Windows Manager`, reuses `%LocalAppData%\llama.cpp Console` or `%LocalAppData%\LocalLlmConsole` only for an existing legacy folder, or reports a clear workspace error.
10. Launch the app without Git, CMake, or CUDA.
11. Verify the app opens, creates state, and explains missing Ubuntu/WSL prerequisites without crashing.
12. Use Runtime Downloads to install an official prebuilt CPU Windows runtime, then confirm it appears in model launch runtime choices.
13. On suitable hardware, repeat Runtime Downloads for CUDA, Vulkan, or Intel Arc SYCL Windows/WSL packages.
14. Use the WSL Linux page to install or detect Ubuntu only when testing WSL runtimes or source builds.
15. Use Install CPU Tools to install Git, CMake, and build tools inside Ubuntu, then validate CPU-only WSL source-build preflight.
16. Try a CUDA source build without CUDA Toolkit inside Ubuntu/WSL and confirm the app reports that the WSL CUDA Toolkit is missing before CMake runs.
17. Try a Vulkan source build without Vulkan tools or a WSL-visible Vulkan device and confirm the app reports the missing Vulkan prerequisite before CMake runs.
18. Try a SYCL launch/source build without oneAPI or a Level Zero-visible Intel GPU and confirm the app reports the missing Intel Arc prerequisite.
19. Change the selected distro and validate missing-distro errors.
20. Download a small GGUF, interrupt the app mid-download, relaunch, and verify job recovery.
21. Load two small models on different saved ports and confirm both endpoints remain reachable.
22. Enable Gateway LAN only, confirm a LAN client can reach the gateway but not
    direct model ports; then enable Direct models LAN only and confirm the
    inverse.
23. Import an external model folder, delete the registration, and verify GGUF files remain.
24. Select an external GGUF with **Add model file…**, confirm an intentionally ambiguous role, rescan, and verify the registration remains; repeat with an unreadable `.gguf` and verify it is rejected.
25. Add a downloaded app-owned model, delete it, and verify only app-owned paths are removed.
26. Verify `GET /v1/models` lists each saved profile as a separate model id and reports its configured `context_length` plus available GGUF `meta` values.
27. Verify app update checks can reach the GitHub release feed, and that update install works from a copied portable exe folder.
28. Create a diagnostics bundle from Logs, review its contents, and confirm it contains no credentials, private paths, model data, or sensitive launch arguments.

## Repository Settings After Merge

Apply these settings after the workflow files are present on the default branch:

- Add topics: `llama-cpp`, `local-llm`, `gguf`, `windows`, `wpf`, `wsl`,
  `openai-api`, and `llm-server`.
- Set the project homepage to the latest release or user guide and enable
  Discussions if community support will be monitored.
- Protect `main`: block force pushes and deletion, require the **Build, test,
  and publish** check, require branches to be current, disable routine
  administrator bypass, and require review for external contributors. Follow
  [Repository governance](REPOSITORY_GOVERNANCE.md) for exceptional recovery.
- Confirm CodeQL, dependency review, and Dependabot are enabled and allowed by
  the repository's Actions/security settings.

## Release Blockers

- Any unauthenticated mutating localhost API.
- Any wildcard CORS header on a local control API.
- Any recursive delete not bounded by ownership and path-root checks.
- Any llama.cpp launch default that binds model serving to `0.0.0.0`.
- Any LAN model-serving mode that does not require a strong API key.
- Any completed download registered without expected-size or SHA-256 validation.
- Any clean-machine startup path that silently assumes hidden developer setup.
- Any release artifact described as signed or trusted when it is unsigned.
- Any signed install that can be replaced by an unsigned or differently signed update.
- Any installer uninstall, repair, or update path that deletes models, runtimes, logs, cache, or state without explicit user confirmation.

## Stabilization validation

Before release, verify that an update helper acknowledges verified sibling staging
before the Manager exits; missing sources or cancelled handoffs keep it open.
Exercise replacement, restart, locked-file failure, and app/CLI rollback in an
isolated installation. Each supervised session must own a newly created log.
Decline or cancel a replacement admission warning and verify all old sessions
remain running. Apply visibility, scale and unchanged settings during a gateway
stream and verify completion. Test startup registration with two isolated
executable paths. Endpoint model metadata headers must remain readable at 620 and
760 DIP in English and German, with horizontal scrolling when necessary. Feed
malformed numeric benchmark rows followed by a valid row and verify parsing
continues; the repository workload helper must fail truncated streams and missing
usage rather than emit successful throughput measurements.

Rapidly switch through pages before they finish loading, then resize the final
page’s columns and verify those widths survive navigation and restart. Closing
the window must cancel pending layout restoration and detach layout observers.

Run a native benchmark fixture whose parent exits while a descendant inherits
its output pipe. Verify the owned descendant stops promptly, output draining
remains cancellable after parent exit, and no child is left behind.
