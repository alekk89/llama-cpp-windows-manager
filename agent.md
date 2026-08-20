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

Resolve identifiers before acting:

```powershell
llwmctl models list
llwmctl runtimes list
llwmctl profiles list --model <model>
llwmctl sessions list
```

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

The shared gateway's `GET /v1/models` catalog reports every saved profile route
and its configured context size as `context_length`; `0` means automatic context
sizing. Its optional `meta` object reports GGUF training context, parameter
count, and file size without guessing missing values.

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
