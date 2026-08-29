# Telemetry and energy

Overview cards are presentation only; hiding or moving them does not disable
telemetry. Cards use bounded values and curated time-varying charts. Unsupported
optional sensors stay out of the picker.

The Manager samples observed GPU board power while a session is active and stores
hourly energy buckets. Idle persistence is disabled by default. Enabling
`trackGpuEnergyWhileIdle` uses a ten-second idle interval; otherwise session-free
detection backs off to five minutes without persisting idle energy.

Day/night tariffs derive an estimate from measured buckets. Values are GPU-board
cost, not whole-host cost or a billing ledger. Gaps and downtime are never
estimated, and energy is not attributed to a model when sessions overlap.

Historical definitions and filters are authoritative in [CONTROL_API.md](CONTROL_API.md).
