# Gateway and networking

Direct model endpoints and the shared gateway are separate. Direct endpoints use
the host and port saved in the profile. Advanced **Server → Host IP** emits the
llama.cpp `--host` value, but a non-loopback host is honored only when Settings
permits direct-model LAN access. The gateway provides one `/v1` base URL and
route IDs listed by `GET /v1/models`.

Both default to loopback. API-key authentication is enabled by default. Disabling
it forces Local-only access, clears the active runtime key, and preserves its
protected backup. Every LAN mode requires a strong key.

The Manager control API is independent: always loopback-only with its own
protected token. Do not expose its discovery file. Host, Origin, request-size,
and bearer-token checks remain fail-secure. See [CONTROL_API.md](CONTROL_API.md).

Gateway requests use a bounded wait for the upstream response headers, including
any model load or profile swap. Once a response begins, streamed completion
bodies are allowed to remain open until the client disconnects, the app shuts
down, or the upstream finishes; long generations are not cut off by the header
timeout.
