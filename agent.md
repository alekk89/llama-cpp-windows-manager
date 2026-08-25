# Automation quick start

Read [AGENTS.md](AGENTS.md) before operating llama.cpp Windows Manager. It is
the authoritative guide for discovery, model identity, lifecycle safety,
restarts, downloads, settings, installation, and source work.

The canonical source repository is
<https://github.com/alekk89/llama-cpp-windows-manager>; use GitHub Releases for
installation and clone the repository only for development or review.

Use the `llwmctl.exe` beside the installed or portable application:

```powershell
./llwmctl.exe status
./llwmctl.exe capabilities
./llwmctl.exe operations list
./llwmctl.exe self
```

`llwmctl` controls the running Manager through its authenticated loopback API.
Do not edit the Manager database, launch `llama-server` directly, expose the
control API, or automate the WPF interface.

Overview card customization is stored as the versioned
`overviewDashboardLayout` application setting. Prefer the Overview UI for
interactive changes. Existing `showOverviewModelStatus`, `showOverviewHardware`,
`showOverviewSlots`, `showOverviewTokens`, `showOverviewMtpTokens`, and
`showOverviewKvCache` patches remain supported and add or remove the matching
atomic metric group without replacing unrelated layout choices. Cards themselves
are generic containers and may mix metrics from any group. Version 5 layouts
omit unreliable per-poll generation, prompt, and speculative live-rate metrics
and migrate their charts to the corresponding averages. Version 6 removes
static/dead chart selections and hides unsupported optional hardware sensors.
Per-GPU VRAM temperature is one such optional sensor and appears only after the
vendor driver reports a finite value. The `runtimeLogOrder` setting accepts
`newestFirst` or `oldestFirst`; it changes the compact Overview projection only,
while persisted runtime logs remain chronological.
Version 11 makes the default production cards unlocked and equal-width. Hardware
discovery adds GPU cards only for discrete devices and omits GPU core clock from
the default template; integrated-GPU and core-clock metrics remain available for
custom cards. Unrelated custom layouts are not rewritten.
Version 12 makes all production-default cards the same compact height and removes
GPU power draw from their default charts while retaining its live value. GPU
utilization remains charted, and unrelated custom layouts remain unchanged.
Version 7 migrates session-named energy rows to host energy observed since the
Manager started; those live values reset on restart while their deltas continue
to the persisted historical totals.
Version 8 persists the dashboard-wide Lock state and its captured surface width,
so cards wrap instead of resizing with ordinary window changes.
Version 9 adds optional card titles while keeping untitled cards headerless, and
uses measured value/unit columns to avoid premature metric-label wrapping.

Never update the local deployment while a model session is loaded or running.
Check `status`, `self`, and `sessions list` first, and leave tested artifacts
staged until the Manager is idle. Do not unload the model unless explicitly asked.

Resolve identifiers before acting:

```powershell
llwmctl models list
llwmctl models scan
llwmctl models import --file <path.gguf>
llwmctl runtimes list
llwmctl profiles list --model <model>
llwmctl sessions list
```

Model scans return metadata-first role diagnostics. An explicitly selected valid
GGUF can be registered from any folder. Use `--confirm-role` only after reviewing
an ambiguous/companion classification and confirming the file is intended as a
main model; that decision persists across later scans. Unreadable GGUFs remain
blocked.

Prefer saved profiles and wait for readiness:

```powershell
llwmctl load <model> --profile <profile> --wait
llwmctl sessions inspect <session>
llwmctl gateway inspect
llwmctl sessions metrics <session>
llwmctl metrics usage --range month
llwmctl metrics usage --date 2026-08-18 --date 2026-08-20
llwmctl sessions logs <session>
```

Usage reports also expose host-wide GPU energy in Wh/kWh when power sensors are
available. Treat partial coverage as tracked energy, not a whole-machine total;
model/profile/runtime filters do not attribute host energy to one model.
Historical energy is session-only by default. `trackGpuEnergyWhileIdle=true`
enables continuous ten-second sampling and persistence without a loaded model;
otherwise idle detection runs every five minutes and is not written to history.
With configured electricity settings, reports also expose the estimated cost of
that measured GPU energy. The current day/night tariff is applied at report time;
telemetry gaps and non-GPU host power are not estimated.

The shared gateway's `GET /v1/models` catalog reports every saved profile route
and its configured context size as `context_length`; `0` means automatic context
sizing. Its optional `meta` object reports GGUF training context, parameter
count, and file size without guessing missing values.

Model-serving API-key authentication can be disabled only in Local-only access
mode. The active key is then empty, the protected backup is retained for
re-enabling authentication, and all LAN modes remain blocked until authentication
is restored. This never changes the separate authenticated Manager control API.

Settings choices auto-apply quickly. Text fields wait until typing pauses before
entering the save debounce; `llwmctl settings set` remains immediate.

Run `self` before any action that can stop or replace a loaded model. Never use
`--allow-self-stop` or `--confirm` without explicit authorization for the stated
consequence. Validate unfamiliar or consequential operations with the live
schema and `operations run <name> --dry-run`.

If discovery is ambiguous, use `--workspace <path>` or
`--connection <workspace>\state\control.json`. If the Manager is not running and
the user asked to operate it, start `LlamaCppWindowsManager.exe` normally, then
retry `status`; never start a second instance.

Release builds restore the matching CLI and operator documentation beside the
application executable. Verify this without starting the UI with:

```powershell
LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only
```

See [Local control API and `llwmctl`](docs/CONTROL_API.md) for the full command
and HTTP contracts.
