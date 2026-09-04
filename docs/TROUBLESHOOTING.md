# Troubleshooting

1. Record the application version and any stable `LLWM-*` error code.
2. Run `llwmctl status`, then inspect the relevant session, gateway, job, or log.
3. Retry only after checking that no existing work would be harmed.
4. From **Logs**, create a diagnostics bundle and review every included file.

Hardware failures distinguish unsupported capability, timeout, probe/parser
failure, and runtime failure. Session failures record bounded transitions with
readiness, exit category, and verified-stop result.

For missing live power or energy, compare the Manager with the driver's power
reading and distinguish unavailable data from zero usage or a configured power
limit. See [Telemetry and energy](TELEMETRY_AND_ENERGY.md#missing-live-power-or-energy).

If gateway startup requests a one-time Windows port permission, approve it only
for the intended gateway port and access mode. A declined permission leaves
startup failed; inspect the reported error before retrying. Granting permission
does not replace API-key authentication or configure the Windows firewall.

For a report, include minimal steps, versions, the error code, and reviewed ZIP.
See [DIAGNOSTICS_BUNDLE.md](DIAGNOSTICS_BUNDLE.md) for privacy limits.
