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

## Missing live power or energy

Energy requires an observed power reading from the GPU driver. An unavailable
sensor is not a zero-watt reading, and a configured power limit is not measured
power draw. The Manager does not substitute that limit or fill missing intervals
with estimates. Previously recorded energy can remain visible when current power
readings are unavailable.

For NVIDIA devices, compare with this read-only driver query:

```powershell
nvidia-smi --query-gpu=index,name,power.draw,power.limit --format=csv
```

If the driver also reports `N/A` for power draw, the missing input is outside the
Manager's display path. This alone does not identify the underlying driver or
hardware cause, or establish that a reboot will fix it. If the driver reports
watts while the Manager does not, capture the application version, timing, and
reviewed diagnostics for investigation. Windows may also enumerate an integrated
GPU alongside discrete cards; an additional adapter is not necessarily a duplicate.

## Benchmark GPU memory

Benchmarks sample Windows GPU Adapter Memory counters every second, plus an
initial and final reading. DXGI supplies device names and dedicated capacity;
adapter LUIDs match those devices to the counters. This supports WDDM drivers for
NVIDIA, AMD and Intel without polling an external process or requiring a new
package. A driver that does not expose a counter leaves that measurement unavailable.

Reports show sampled peaks of dedicated and shared GPU memory separately for
each device. Values cover the whole device, including other applications, and
are not attributed solely to the model. Shared usage is normal on integrated
GPUs and is not by itself proof of spill. Capacity is physical dedicated memory,
not a guarantee that all of it is available for allocation. Brief peaks between
samples can be missed; these are observed peaks, not exact allocation maxima.

Profile-serving measurements cover each workload/concurrency pair, including
warmup and repetitions after the server is ready. llama-bench measurements cover
the whole process attempt, including all workloads it emits; its buffered output
cannot reliably delimit individual workloads. The report labels that distinction.
Repeated report rows use the maximum memory reading, not the average.

CSV exports add `gpu_N_id`, `gpu_N_name`, `gpu_N_peak_dedicated_mib`,
`gpu_N_dedicated_capacity_mib`, `gpu_N_peak_shared_mib`, and
`gpu_N_memory_samples` for each detected device. Device columns remain stable
within an export. Blank numeric cells mean unavailable; an observed zero remains
zero. `gpu_memory_status`, `gpu_memory_scope`, `gpu_memory_window`, and
`gpu_memory_sample_interval_ms` describe collection. The existing
`gpu_memory_used_mib` column remains an older snapshot value, never relabeled as
a peak; old zero/unknown values export blank. Old reports cannot recover peaks
that were never sampled. `vulkan_allocation_block_size_mib` records the saved
profile override for profile-serving results; zero leaves runtime defaults.
