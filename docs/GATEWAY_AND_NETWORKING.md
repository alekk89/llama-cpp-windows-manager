# Gateway and networking

Direct model endpoints and the shared gateway are separate. Direct endpoints use
the host and port saved in the profile. Advanced **Server → Host IP** emits the
llama.cpp `--host` value, but a non-loopback host is honored only when Settings
permits direct-model LAN access. The gateway provides one `/v1` base URL and
route IDs listed by `GET /v1/models`.

Open an endpoint report from Overview to copy a client model value directly.
Each advertised-model row has a **Copy model ID** button that copies the exact
ID or alias, without the display name, headers, or other row fields. The ID and
display name can also be selected independently. This works for both gateway
and direct endpoint reports.

When gateway LAN exposure is enabled, its endpoint report lists the loopback
and LAN `/v1` URLs separately. The health result is a loopback probe from the
Manager; it does not prove that another device can reach the LAN URL. The same
report shows whether the Manager's named Windows Firewall rule is installed for
the current port. **Allow LAN through Windows Firewall** is an explicit,
administrator-approved action that creates an inbound TCP rule limited to
`LocalSubnet` on Private and Domain networks. **Remove firewall rule** removes
only that named Manager rule. The Manager never changes the firewall merely by
enabling LAN exposure or opening the report.

**Settings > Network > Auto-load models** controls just-in-time loading through
the gateway. It defaults to **Yes**, including after upgrading older settings.
Set it to **No** to keep the shared endpoint available for manually loaded
profiles. In this mode:

- `/v1/models` lists only profiles with running sessions. An empty list means
  no saved profiles are currently loaded.
- Requests use the exact loaded profile. A different loaded profile of the
  same GGUF does not substitute for the requested profile.
- Requests for a known but unloaded profile return HTTP `503` with error code
  `model_not_loaded`; unknown routes still return `404`.
- The gateway never loads, unloads, or swaps models for a request, regardless
  of **Gateway policy**. Explicit lifecycle commands and idle-unload rules
  still apply.

Changing this setting reconfigures the gateway listener without restarting model
sessions. **Model gateway** separately enables or disables the shared endpoint.
Overview and endpoint inspection show **Loaded profiles only** when auto-loading
is off. `/health` also reports `autoLoadModels`.

The gateway uses the **Alias** saved in a profile's additional runtime options
(`--alias` / `-a`) as its model ID. For example, four profiles with alias
`dirk-qwen3.8-27b@iq3_xxs` appear as that name, followed by `:2`, `:3`, and `:4`.
Names are assigned across all saved profiles before discovery filters for loaded
sessions, so loading or unloading a session does not renumber the names.
Default profiles take precedence, followed by model and
profile name order, with internal IDs breaking ties. Adding, removing, renaming,
or changing aliases on profiles can reassign the numbered names; use distinct
aliases when a client must always select a particular profile. Explicit names
such as `qwen:2` and existing internal route IDs are reserved before suffixes
are allocated.

Save the profile to update gateway discovery. If `--alias` contains a
comma-separated list or is repeated, its first nonempty alias is the gateway
name. The other aliases remain available at the direct endpoint. Profiles with
no alias keep their existing gateway IDs, and old internal model/profile route
IDs remain accepted after an alias is added. The `model_id` and `profile_id`
metadata in `/v1/models` still identify the underlying records.

Numbered gateway requests select the exact saved profile and forward its active
runtime alias in the request's `model` field. The Manager does not append
`-gateway`. **Settings > Network > Direct model ID suffix** optionally appends
a suffix such as `-direct` to direct endpoint aliases. It is blank by default,
preserving explicit aliases. Concurrent direct aliases receive `:2`, `:3`, etc.
in load order when necessary; existing running sessions retain their IDs.
Changing a saved alias updates the gateway catalog immediately, but the direct
endpoint adopts it when that profile is restarted.

Without an explicit alias, new launches advertise the GGUF filename without its
directory, `.gguf` extension or split-file suffix. For example,
`D:\models\Qwen-00001-of-00003.gguf` becomes `Qwen`, or `Qwen-direct` with the
optional suffix. This is the runtime's actual advertised ID, not a shortened
clipboard label. Older running sessions may still advertise their file path
until reloaded; their report continues to copy the exact advertised ID.

**Settings > LAN exposure** must be **Direct models LAN only** or **Gateway +
direct LAN** to bind a model to a LAN address such as `10.10.10.21`. **Local only**
and **Gateway LAN only** keep direct model servers on `127.0.0.1`, even when a
profile stores a different Host IP. The command preview explains the effective
listener, and endpoint links, readiness checks, and telemetry use that same
address. Restart the model after changing its host or LAN policy.

Profiles saved before the Host IP field existed inherit the application's host
default. Explicitly saved addresses, including `127.0.0.1`, remain explicit;
clear Host IP to inherit the app default again.

Both default to loopback. API-key authentication is enabled by default. Disabling
it forces Local-only access, clears the active runtime key, and preserves its
protected backup. Every LAN mode requires a strong key.

Settings changes restart the gateway only when its effective listener options
change. Display visibility, UI/Text scale, unchanged saves, and access-mode
changes that leave the gateway's effective options unchanged preserve requests
already in progress. Changing the gateway port, key, or other effective gateway
options can interrupt requests; schedule those changes between generations.
Changing direct-model settings still requires restarting affected profiles.

The gateway checks the client's peer address when enforcing local-only access;
a leftover wildcard Windows URL reservation or a spoofed Host header does not
authorize a remote client. A newly selected port may require a one-time Windows
permission. After permission is granted, startup retries with a fresh listener.
That URL reservation permission and the Windows Firewall rule are separate;
both may be required for LAN access. API-key authentication remains required
regardless of either permission.

Browser requests are checked by their `Origin` header before any model is
loaded. Requests without `Origin`, such as SDKs, scripts, and server-side
harnesses, are unaffected. In **Local only** and **Direct models LAN only**
modes, the gateway accepts only loopback `http`/`https` origins such as
`http://localhost:5173`; any other browser origin, including preflight
`OPTIONS` requests and opaque `null` origins, receives HTTP `403`. Gateway LAN
modes always require the API key and keep accepting any `http`/`https` browser
origin that presents it. Upstream llama-server responses cannot replace the
gateway's CORS, `Vary`, or `Cache-Control` headers.

The Manager control API is independent: always loopback-only with its own
protected token. Do not expose its discovery file. Host, Origin, request-size,
and bearer-token checks remain fail-secure. See [CONTROL_API.md](CONTROL_API.md).

Gateway requests use a bounded wait for the upstream response headers, including
any model load or profile swap. Once a response begins, streamed completion
bodies are allowed to remain open until the client disconnects, the app shuts
down, or the upstream finishes; long generations are not cut off by the header
timeout.

Under **Single active model**, a request that needs a swap first waits for
gateway responses already in progress on the other sessions. New requests for
those sessions wait until the swap finishes and then load their model again.
If the active responses take longer than 10 minutes, the swapping request fails
with `503 model_load_failed` instead of interrupting them. Idle unload skips a
session while a gateway request on it is active, and a request that arrives
while an idle session is stopping waits and reloads it. Requests sent directly
to a model's own endpoint are not tracked by the gateway; explicit unloads and
restarts still stop sessions immediately.
