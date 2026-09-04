# Progressive dashboard telemetry

Issue: [#56](https://github.com/alekk89/llama-cpp-windows-manager/issues/56).

The dashboard now applies a completed response for the currently selected
session before waiting for other sessions or the GPU summary. The poller invokes
completion callbacks serially on the caller's synchronization context while
retaining its existing bounded HTTP concurrency. Results returned to batch
consumers retain the original session order.

Endpoint health, lifetime-token accounting, and idle-unload decisions still run
once after the complete batch. The final render rechecks the selection and any
unload result, without applying the same selected sample twice. A failed callback
or canceled refresh cancels and drains outstanding polls before the refresh guard
is released.

This is a presentation-latency improvement. A slow endpoint can still lengthen
the overall batch and delay the next refresh. Polling cadence, endpoint timeouts,
retention policy, and background collection are unchanged. There is no permanent
background polling service or cache of stale counter samples.

Run the deterministic behavioral checks with:

```powershell
dotnet test --project tests/LocalLlmConsole.Tests --filter-class '*ProgressiveTelemetryTests' --output Detailed
```

The tests hold a peer endpoint and GPU refresh behind explicit completion gates.
They assert that the healthy selected model renders before either gate opens,
then verify complete batch accounting, selection changes, unload rendering,
bounded concurrency, cancellation, and callback-error recovery. This establishes
ordering rather than claiming a hardware-independent millisecond improvement.

A manual multi-session UI check remains useful before release: select a healthy
session while another endpoint is slow, switch the selection during polling,
and check chart continuity and the final endpoint status. Use an isolated test
environment and the supported control interface for runtime operations.
