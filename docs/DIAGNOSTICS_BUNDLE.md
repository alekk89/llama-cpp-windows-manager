# Diagnostics bundle schema

Summary schema version 2 contains four structured JSON files plus at most 10
recent redacted log excerpts of 256,000 characters each.

- `summary.json`: app/OS/settings summary and filename-only inventory.
- `probes.json` schema 1: at most 32 outcomes from actual Windows/WSL/tool and
  support-bundle probes; excerpts cap at 4,096 characters. The in-memory history
  is a fixed ring and does not start polling or survive an app restart.
- `session-events.json` schema 1: the most recent 200 lifecycle transitions.
- `build-and-update.json` schema 1: build/channel/install and verification status.

The bundle excludes secrets, private paths, UNC locations, URL credentials,
prompt/completion/request content, database contents, raw commands, and model
data. Redaction is defense in depth. Inspect the archive before sharing it.

Stable codes such as `LLWM-PROBE-WINDOWS`, `LLWM-PROBE-WSL`,
`LLWM-SESSION-UNEXPECTED-EXIT`, and `LLWM-SESSION-STOP-UNVERIFIED` connect a
visible error to the corresponding bounded probe or session event. When an
error asks for diagnostics, open **Logs**, choose **Create diagnostics bundle**,
and review the ZIP before attaching it to a report.
