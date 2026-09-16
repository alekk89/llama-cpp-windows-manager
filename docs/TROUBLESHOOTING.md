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

For a gateway that is healthy locally but unreachable from another LAN device:

1. Open the gateway endpoint report from Overview. Copy the **LAN endpoint**,
   not the loopback endpoint, and confirm the report says LAN exposure is
   enabled. Local health verifies only the loopback route.
2. Check the Windows Firewall state in that report. If the Manager rule is
   missing, use **Allow LAN through Windows Firewall** and approve the Windows
   administrator prompt. The rule is limited to the current TCP port,
   `LocalSubnet`, and Private/Domain networks.
3. From the other device, run `Test-NetConnection <manager-LAN-address> -Port
   <gateway-port>`. A failed TCP test points to firewall, network profile,
   subnet, or client-isolation routing rather than an API key.
4. If TCP connects but the HTTP request returns `401`, the gateway is reachable;
   supply the current bearer API key. A connection timeout or refusal occurs
   before authentication and should be investigated as listener, firewall, or
   network reachability.

The endpoint report cannot verify the route from another device. Remove the
Manager rule from the same report if LAN access is no longer wanted.

For a report, include minimal steps, versions, the error code, and reviewed ZIP.
See [DIAGNOSTICS_BUNDLE.md](DIAGNOSTICS_BUNDLE.md) for privacy limits.
