# Gateway request allocation check

Issue: [#53](https://github.com/alekk89/llama-cpp-windows-manager/issues/53).

Run the reproducible allocation guard with the SDK from `global.json`:

```powershell
dotnet test --project tests/LocalLlmConsole.Tests --filter-class '*GatewayRequestAllocationTests' --output Detailed
```

The test reads a 4,194,340-byte JSON body from a synchronous `MemoryStream`,
rewrites its gateway model name to the runtime alias, and checks the resulting
JSON. Input construction and output validation are outside the allocation
measurement. It warms the code and pools before asserting a broad allocation
budget; elapsed time is deliberately not asserted.

Measured locally on Windows x64 with .NET 10 on 2026-09-04:

| Warm sample | Read allocations | Read plus alias rewrite |
| --- | ---: | ---: |
| Before (`e9a1d1f`, isolated audit harness) | 8,470,944 bytes | 21,055,408 bytes |
| After (regression test) | 4,194,632 bytes | 8,389,576 bytes |

That is approximately 60% less managed allocation for this workload. These are
per-thread allocated bytes, not peak process memory, latency, or inference
throughput. The original baseline harness did not include the test assertion
overhead, so tiny byte differences are not significant.

Known-length reads transfer the stream-owned array only when its length exactly
matches the received body. Unknown or inaccurate lengths retain bounded buffered
reading and may require growth or a final copy. The scratch buffer is pooled and
cleared on return; the returned request body is never a pooled buffer.

Alias forwarding scans JSON and replaces only top-level model values in one
exact-sized output array. Other request bytes, including whitespace, number
spellings, nested model fields, and escaped payloads, remain untouched. This
avoids decoding and re-encoding large prompt strings. The separate model-routing
lookup still validates/parses the incoming request as before.
