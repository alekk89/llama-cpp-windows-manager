# Release Readiness Checklist

Last updated: 2026-08-15

## Automated Gate

Run from a clean checkout with the .NET 10 SDK selected by `global.json` on `PATH`, or set `LLAMA_CPP_WINDOWS_MANAGER_DOTNET` to an explicit SDK `dotnet.exe`. The legacy `LLAMA_CPP_CONSOLE_DOTNET` and `LOCAL_LLM_CONSOLE_DOTNET` variables are still accepted.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-coverage.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-vulnerabilities.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

The same source-level gate can be run through the local wrapper, with packaging
included when the machine has Inno Setup and any required signing certificate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

Add `-RequireCleanTree` to `test-release-gate.ps1`, `publish-app.ps1`, or
`build-installer.ps1` when packaging release artifacts; the scripts fail if
`git status --porcelain --untracked-files=all` reports any tracked or untracked
worktree changes.

Trusted signed release builds use:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
```

Trusted signed GitHub release builds may run `.github/workflows/release.yml`
manually with the protected `release` environment and its
`WINDOWS_SIGNING_PFX_BASE64` and `WINDOWS_SIGNING_PFX_PASSWORD` secrets
configured. Version tags do not trigger that optional signing workflow.
Unsigned releases must pass the normal release gate, include matching SHA-256
companions, and be described as unsigned.

## Release Gate

- Publish `dist\LlamaCppWindowsManager-win-x64.zip` and `dist\LlamaCppWindowsManager-win-x64\LlamaCppWindowsManager.exe` from a clean checkout.
- Build `dist\installer\LlamaCppWindowsManager-Setup-2.2.0-win-x64.exe` from the published app with Inno Setup 6.
- Confirm the publish folder contains no `.pdb` files.
- Confirm the portable zip, published executable, and installer each have a matching `.sha256` companion file. For signed builds, generate the companion file after signing.
- Confirm signed installer builds fail before compilation if `-SkipPublish`
  points at an unsigned published executable.
- Confirm the portable zip contains `LlamaCppWindowsManager.exe` and does not contain the removed `LlamaCppConsole.exe` alias.
- Confirm the portable zip and installer contain `LICENSE`,
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
- Confirm Runtime Downloads can check the upstream official llama.cpp release feed and list the official prebuilt packages for CUDA Windows, CUDA WSL, Vulkan Windows, Vulkan WSL, Intel Arc SYCL Windows, Intel Arc SYCL WSL, CPU Windows, and CPU WSL.
- Confirm Runtimes has no advanced-view toggle or Runtime Jobs section, and each Runtime Downloads row places a compact **Build from source** action—sized consistently with the other row actions—immediately left of **Install**. Confirm job supervision and control remain available through Logs and `llwmctl`.
- Confirm **Saved Launch Profiles** has no redundant **Open Folder** column; **Model Files** retains the folder action for the actual GGUF.
- Confirm a source row progresses **Check** -> **Download** -> **Build**, direct download is blocked before a successful source check, and a successful table build deletes its downloaded source and resets the action to **Check**.
- Confirm **Installed Local Builds** and **Runtime Downloads** each share one header row with their right-aligned Type and Platform filters, with no redundant descriptive sentence below either title. Confirm Type filters select AMD/Vulkan, Intel/SYCL, or NVIDIA/CUDA and Platform filters select Windows or Linux/WSL on both inventories. Confirm CPU rows remain under All and filtering never hides Add custom source repository.
- Confirm Runtime Downloads can check the Atomic TurboQuant binary feed, install the Windows CUDA package when published, and show the WSL CUDA row as not published until a matching Linux/WSL asset exists.
- Confirm runtime package downloads fail closed when the downloaded byte count
  does not match release metadata or when no SHA-256 metadata/companion checksum
  is available for a required package asset.
- Confirm installing a prebuilt runtime does not require Git, CMake, Visual Studio Build Tools, WSL build tools, or source checkout.
- Confirm installed prebuilt runtimes are registered, can be selected per model, and show update/delete state on the Runtime Downloads page.
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
- Confirm the Overview Loaded Model Sessions grid shows an auto-load gateway
  router row with endpoint, policy, LAN exposure, and current direct-session
  count.
- Double-click a running model row and click its direct endpoint link. Confirm
  both open the themed endpoint report populated from `/health`, `/v1/models`,
  `/props`, and `/slots`, including context, output limit, reasoning/template
  capability, sampling defaults, and current slot state without generating text.
- Inspect the gateway row and endpoint link. Confirm the report shows advertised
  profile model IDs, running sessions, policy, and exposure, and explains that
  context/reasoning/output defaults belong to each routed model. Confirm a runtime
  without `/props` or `/slots` still shows available data plus a compact warning.
- Confirm Overview places Model, Launch profile, and Load on one row; Model and
  Launch profile retain fixed practical widths at non-maximized window sizes.
  When a model is running, selecting its active profile hides Load, while
  selecting a different profile shows Load and replaces the model session with
  that exact saved profile.
- Confirm the Overview Model Status card shows Loading Model / Loaded Model and
  Loading Time as separate rows, and that Loading Time remains at the completed
  duration after the model becomes ready.
- At the default window size, confirm Overview uses two metric-card columns with
  no clipped card content and the loaded-session Runtime column remains readable;
  maximize the window and confirm the cards reflow to three columns.
- Confirm Overview token monitors use two compact rows in the form
  `0.0 t/s (Gen) | 0.0 t/s (Avg) | 0 t (Total)`, with matching Prompt and
  Accepted rows, live rates falling back to `0.0 t/s` when idle, and average or
  total segments omitted when those values are unavailable. Run two parallel
  requests and confirm totals continue increasing after either slot is reused.
- Confirm Tokens, Speculative Tokens, and KV Cache show compact trend graphs on
  the bottom row, retain at most 60 samples, and reset when the selected runtime
  changes. Confirm Slots appears on the top row and shows active/total capacity,
  queued requests, and busy decode slots.
- Confirm the Overview Hardware card shows CPU temperature for CPU-backed
  sessions, uses NVIDIA metrics for CUDA when available, falls back to Windows
  GPU performance counters for AMD/Intel/Vulkan-backed sessions, and does not
  show stale cached hardware data after switching runtimes.
- Confirm the Settings API key Generate action creates a new model API key.
- Confirm the gateway `/v1/models` response lists every saved launch profile and
  requesting another profile for a running model restarts it with that profile.
- Confirm Settings is separated into named category sections arranged in two
  equal-width columns rather than one large full-width settings grid. Confirm
  Network and UI remain in opposite columns and narrow values/actions do not
  force a page-level horizontal scrollbar.
- Confirm Settings uses readable 28px editors inside compact rows, narrow
  right-aligned dropdowns, no Save Settings button, and a visible automatic-apply
  hint. Confirm Network has no blanket Action column and API-key Show, Copy, and
  Generate controls appear only inside the API-key value row.
- Confirm Settings includes a **UI** category with
  independent switches for Model status, Hardware, Slots, Tokens, Speculative
  tokens, KV cache, Live Runtime Log, All llama.cpp Metrics, and the Models
  Hugging Face section. Confirm these choices read **Show/Hide**, each switch applies automatically, hidden rows
  leave no blank splitter/space, card layout reflows, and choices persist after
  restart.
- Confirm a workspace without stored UI visibility keys defaults the six status
  cards and live log to `Show`, and raw metrics and Hugging Face to `Hide`. Use
  `llwmctl settings set` followed by `llwmctl settings get` to verify the same
  nine fields can be changed and read back through the live Manager without
  restarting it.
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
  A valid group starts all assigned profiles; duplicate-model, unavailable-runtime,
  port-conflict, missing-GPU-telemetry, and aggregate-VRAM failures display an error
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
- Load a model and run `llwmctl sessions inspect <session>`; confirm health,
  models, defaults, and slots are returned without the serving API key appearing
  in the response or Control API log. Run `llwmctl gateway inspect` and confirm
  the same authenticated behavior through the shared gateway.
- Confirm the persisted model API key is protected at rest for the current Windows user.
- Confirm ports outside `1..65535` are rejected on Settings save.
- Confirm model serving cannot launch without a strong model API key in any
  local-only or LAN exposure mode.
- Confirm control-API settings patches cannot disable API-key authentication,
  replace protected secrets/workspace paths, use invalid ports, or enable the
  gateway on a port occupied by a running model.
- At 100%, 125%, 150%, and 200% display scale, confirm the initial window fits
  the monitor work area and the Overview model/profile/load bar reflows without
  clipping at narrow widths.
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
- Confirm Hugging Face downloads cannot write outside the configured models folder.
- Confirm completed downloads are not registered when the final byte count mismatches the expected size or no expected size/SHA-256 metadata exists.
- Confirm imported external model deletion removes only app registration files.
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
- Confirm the GitHub release includes the portable ZIP and standalone
  `LlamaCppWindowsManager.exe`, each with its matching SHA-256 companion. The
  standalone asset preserves in-app updates from v1.x, v2.0, and v2.1; a bad checksum
  must prevent staging.
- Confirm a signed installed app refuses an unsigned or differently signed staged update.
- Confirm a completed staged update restarts `LlamaCppWindowsManager.exe` and shows the GitHub release notes.
- Confirm an older renamed portable install migrates to `LlamaCppWindowsManager.exe` and removes the obsolete executable after shutdown.

## Latest Local Verification

Current local check on 2026-08-15:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-release-gate.ps1 -IncludePublish -IncludeInstaller
```

Result: .NET 10 Release app and CLI builds succeeded with zero warnings;
service/unit tests passed (`548/548`) and the WPF smoke test passed (`1/1`) with
no skips; service coverage was 80.9% and model/view-model coverage was 97.4%;
formatting, diff whitespace, and the vulnerability, deprecation, and
direct-package currency audit passed. The portable publish and embedded
operator/control sidecar packaging also succeeded, as did the installer gate.
The current local portable and installer artifacts remain unsigned test builds.
The next release notes draft is tracked in
`docs/GITHUB_RELEASE_NEXT.md`.

## Manual Clean-Machine Test

1. Start from a clean Windows VM.
2. Install `dist\installer\LlamaCppWindowsManager-Setup-2.2.0-win-x64.exe`.
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
24. Add a downloaded app-owned model, delete it, and verify only app-owned paths are removed.
25. Verify `GET /v1/models` lists each saved profile as a separate model id.
26. Verify app update checks can reach the GitHub release feed, and that update install works from a copied portable exe folder.

## Release Blockers

- Any unauthenticated mutating localhost API.
- Any wildcard CORS header on a local control API.
- Any recursive delete not bounded by ownership and path-root checks.
- Any llama.cpp launch default that binds model serving to `0.0.0.0`.
- Any model-serving mode that does not require an API key.
- Any completed download registered without expected-size or SHA-256 validation.
- Any clean-machine startup path that silently assumes hidden developer setup.
- Any release artifact described as signed or trusted when it is unsigned.
- Any signed install that can be replaced by an unsigned or differently signed update.
- Any installer uninstall, repair, or update path that deletes models, runtimes, logs, cache, or state without explicit user confirmation.
