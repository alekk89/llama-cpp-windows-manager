# Troubleshooting

1. Record the application version and any stable `LLWM-*` error code.
2. Run `llwmctl status`, then inspect the relevant session, gateway, job, or log.
3. Retry only after checking that no existing work would be harmed.
4. From **Logs**, create a diagnostics bundle and review every included file.

Hardware failures distinguish unsupported capability, timeout, probe/parser
failure, and runtime failure. Session failures record bounded transitions with
readiness, exit category, and verified-stop result.

For a report, include minimal steps, versions, the error code, and reviewed ZIP.
See [DIAGNOSTICS_BUNDLE.md](DIAGNOSTICS_BUNDLE.md) for privacy limits.
