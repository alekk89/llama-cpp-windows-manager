# LLM operator guide

The canonical Codex-compatible instructions are in [AGENTS.md](AGENTS.md). Read that file before controlling llama.cpp Windows Manager.

The canonical source repository is <https://github.com/alekk89/llama-cpp-windows-manager>. Use GitHub Releases for end-user installation and clone the repository only for development, review, testing, or local packaging. `AGENTS.md` contains the cold-start, checksum, source-build, Git workflow, restart/recovery, and troubleshooting rules.

Portable release builds restore this guide, `AGENTS.md`, `llwmctl.exe`, and `docs/CONTROL_API.md` from the main executable when they are missing or outdated. The bootstrap can be verified without opening the UI by running `LlamaCppWindowsManager.exe --bootstrap-agent-sidecars-only`.

From the folder containing the installed or portable executable, use:

```powershell
./llwmctl.exe status
./llwmctl.exe capabilities
./llwmctl.exe self
```

When working directly inside a portable installation, prefer `./llwmctl.exe` and its adjacent `data` workspace. Use `--workspace <path>` when automatic discovery is ambiguous or the workspace was overridden. If the Manager is not running and the user asked to start or operate it, start `LlamaCppWindowsManager.exe` normally, wait for `status`, and never launch `llama-server` directly.

Use `models list`, `runtimes list`, and `profiles list --model <model>` to resolve identifiers. Load a model with `load <model> --profile <name> --wait`; apply temporary settings with repeated `--set name=value`. Use `--save-profile=<name>` only when persistence was requested.

Use `sessions metrics`, `sessions logs`, and `logs list|tail` for observation. Search/download models with `hf search` and `hf download`; use `--dry-run` to validate an exact Hugging Face file without downloading it.

Run `operations list` for every application function. Validate consequential work with:

```powershell
llwmctl operations run <operation> --dry-run --set name=value
```

Operations marked `requiresConfirmation` require `--confirm`. Never unload, restart, delete, shut down, update, or replace the model/application running the current LLM unless the user explicitly requested that consequence. `llwmctl` blocks identified self-stop operations; use `--allow-self-stop` only after that explicit request.
