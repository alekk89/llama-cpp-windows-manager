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

The Manager control API is independent: always loopback-only with its own
protected token. Do not expose its discovery file. Host, Origin, request-size,
and bearer-token checks remain fail-secure. See [CONTROL_API.md](CONTROL_API.md).

Gateway requests use a bounded wait for the upstream response headers, including
any model load or profile swap. Once a response begins, streamed completion
bodies are allowed to remain open until the client disconnects, the app shuts
down, or the upstream finishes; long generations are not cut off by the header
timeout.
