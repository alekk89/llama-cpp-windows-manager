namespace LocalLlmConsole.ControlCli;

internal static class ControlCliHelp
{
    public const string Text = """
llwmctl - control llama.cpp Windows Manager

Core:
  llwmctl status | capabilities | self [--endpoint URL|--model ID|--session ID]
  llwmctl models list|get|scan|import|companions|delete
  llwmctl models import --file PATH [--confirm-role] | --folder PATH
  llwmctl load|restart|unload MODEL [--profile NAME] [--runtime ID] [--set name=value] [--wait]
  llwmctl profiles list|create|update|delete --model MODEL [--id ID] [--name NAME] [--set name=value]
  llwmctl groups list|get|create|update|delete [GROUP] [--name NAME] [--retention inherit|pinned|idle-timeout] [--idle-minutes N] [--priority low|normal|high]
  llwmctl groups assign MODEL PROFILE --group GROUP | groups unassign MODEL PROFILE
  llwmctl sessions list|get|logs|metrics|inspect [SESSION]
  llwmctl gateway inspect
  llwmctl metrics [live]
  llwmctl metrics usage [--range 1d|7d|month|30d|90d|all] [--date YYYY-MM-DD ...] [--model ID] [--profile ID] [--runtime ID] [--time-zone ID]
  llwmctl logs list|tail FILE [--tail CHARACTERS]
  llwmctl settings get|set --set name=value | settings rotate-key
  llwmctl runtimes list|scan|register --folder PATH
  llwmctl hf search QUERY
  llwmctl hf download --repo OWNER/REPO --file FILE.gguf [--revision REV]
  llwmctl jobs list|pause|resume|cancel JOB
  llwmctl benchmarks schema | presets | capabilities [RUNTIME] [--wsl-distro DISTRO]
  llwmctl benchmarks validate --plan FILE
  llwmctl benchmarks run --plan FILE [--dry-run|--confirm] [--wait] [--timeout SECONDS]
  llwmctl benchmarks list|inspect|wait|pause|resume|cancel|plan|clone|results|export|log [RUN]
  llwmctl benchmarks compare BASELINE_RUN CANDIDATE_RUN [--include-partial]
  llwmctl benchmarks delete RUN --confirm
  llwmctl operations list
  llwmctl operations run NAME [--set name=value] [--dry-run|--confirm]

Full settings:
  Repeat --set for any field returned by `llwmctl capabilities`.
  Use --settings-file settings.json for large setting objects.
  Launch overrides are one-shot unless --save-profile[=NAME] is supplied.
  Self-stop operations are blocked when identity is known; use --allow-self-stop only on explicit request.

Raw API:
  llwmctl request METHOD /api/v1/path [--body JSON|--body-file FILE]

Connection:
  The CLI auto-discovers the current app. Override with --connection FILE or --workspace PATH.
  Add --compact for single-line JSON.
""";
}
