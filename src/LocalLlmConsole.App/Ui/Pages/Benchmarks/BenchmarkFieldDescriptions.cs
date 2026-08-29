namespace LocalLlmConsole;

internal static class BenchmarkFieldDescriptions
{
    public static string Get(string label, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
        return label switch
        {
            "Model" => "Choose the registered main model whose saved launch profiles you want to add to the benchmark set.",
            "Profile" => "Choose a saved launch profile. Its full serving configuration, including speculative decoding, is used by profile-serving benchmarks.",
            "Runtime" => "Choose the fallback runtime for the selected benchmark set. This is ignored when each profile uses its assigned runtime.",
            "Runtime source" => "Use the runtime saved in each selected profile instead of the shared Runtime selection.",
            "Run name" => "A descriptive name stored with this benchmark run and shown in benchmark history.",
            "Benchmark mode" => "Profile serving measures the real saved-profile server configuration. Low-level mode runs llama-bench directly for parameter sweeps.",
            "Benchmark type" => "Saved-profile server benchmark measures end-to-end requests using the profile's real llama-server settings and reports prompt and generation performance separately. Direct llama-bench runs low-level PP, TG, and PG microbenchmarks without speculative serving.",
            "Preset" => "Populate explicit prompt/generation pairs without a cross-product. Short uses 512/128, 2048/256, and 4096/256; Medium uses 8192/512, 16384/512, and 32768/1024; Long uses 32768/1024, 65536/1024, and 131072/1024. Custom leaves every workload field editable.",
            "Request batches" => "Number of timed request batches for each workload and concurrency. Total measured requests equal request batches multiplied by concurrency.",
            "Prompt targets" => "Saved-profile mode combines every prompt target with every Generation target, but ignores both lists when explicit Prompt / generation pairs are present. Direct llama-bench uses this list for standalone prompt-processing (PP) tests.",
            "Generation targets" => "Saved-profile mode combines every generation target with every Prompt target, but ignores both lists when explicit Prompt / generation pairs are present. Direct llama-bench uses this list for standalone token-generation (TG) tests.",
            "Context lengths" => "Optional comma-separated server context sizes to test. Blank uses each saved profile's context size without changing it.",
            "Prompt / generation pairs" => "Explicit prompt/generation token pairs such as 8192/512. In saved-profile mode, any pair overrides the separate Prompt and Generation target lists. In Direct llama-bench mode, pairs are additional combined PG tests and do not suppress standalone PP or TG tests.",
            "Concurrent requests" => "Comma-separated request concurrency levels for profile-serving tests, for example 1,2,4.",
            "Context depths" => "Comma-separated context positions used by low-level llama-bench tests. Zero measures without an added context-depth offset.",
            "Delay between repetitions" => "Seconds to wait between timed repetitions of the same work item.",
            "Ready timeout" => "Maximum seconds to wait for a profile-serving llama-server endpoint to become ready.",
            "Request timeout" => "Maximum seconds allowed for each measured profile-serving request.",
            "Warm-up" => "Enabled runs one untimed request for each workload and concurrency before collecting repetitions. It primes the loaded server and is excluded from reported measurements. Disabled measures immediately after readiness.",
            "Speculative proof" => "Required rejects a result only when the selected profile is configured for speculative decoding but reports no draft or MTP activity. It has no effect on non-speculative profiles. Not required accepts the result without that proof check.",
            "Threads" => "Optional comma-separated CPU thread counts. Blank inherits the saved profile or runtime default.",
            "Batch sizes" => "Optional comma-separated server batch sizes to test. Blank uses each saved profile's batch size; values create temporary launch variants without modifying the profile.",
            "Micro-batch sizes" => "Optional comma-separated physical micro-batch sizes tested as separate matrix values.",
            "GPU layers" => "Optional comma-separated layer offload counts. Use -1 to request all layers; blank inherits the profile.",
            "CPU MoE layers" => "Optional comma-separated counts of mixture-of-experts layers retained on the CPU.",
            "Flash attention" => "Choose auto, on, or off. The field remains editable so comma-separated choices can form a low-level benchmark matrix; blank inherits the profile.",
            "K cache types" => "Choose a supported K-cache data type. Enter comma-separated choices to compare several types; blank inherits the profile.",
            "V cache types" => "Choose a supported V-cache data type. Enter comma-separated choices to compare several types; blank inherits the profile.",
            "KV offload" => "Choose on or off for KV-cache GPU offload. Enter on,off to compare both; blank inherits the profile.",
            "Split modes" => "Choose none, layer, row, or tensor for multi-GPU splitting. Comma-separated choices create a matrix; blank inherits the runtime behavior.",
            "Main GPUs" => "Optional comma-separated zero-based main-GPU indexes.",
            "Devices" => "Optional comma-separated device identifiers exactly as reported by llama-bench --list-devices.",
            "Tensor splits" => "Optional comma-separated tensor-split specifications. Each complete specification becomes a matrix value.",
            "Load modes" => "Choose none, mmap, mlock, mmap+mlock, or dio. Comma-separated choices compare load strategies; blank uses the default.",
            "Fit target MiB" => "Optional comma-separated free-memory targets in MiB used by llama-bench fit mode.",
            "Fit minimum contexts" => "Optional comma-separated minimum context capacities required by fit mode.",
            "NUMA mode" => "Choose one NUMA policy for the plan: distribute, isolate, or numactl. Blank disables an explicit NUMA override.",
            "Priority" => "Choose one process priority from -1 through 3. Blank keeps the normal/default priority.",
            "CPU masks" => "Optional comma-separated hexadecimal or range-form CPU affinity masks accepted by llama-bench.",
            "CPU strict" => "Choose 0 or 1 to disable or enable strict CPU placement. Enter 0,1 to compare both; blank uses the default.",
            "Poll" => "Optional comma-separated polling levels from 0 through 100.",
            "Embeddings" => "Choose 0 or 1 to disable or enable embedding mode. Enter 0,1 to compare both; blank uses the default.",
            "No-op offload" => "Choose 0 or 1 for the no-op offload switch. Enter 0,1 to compare both; blank uses the default.",
            "No host buffer" => "Choose 0 or 1 to allow or prohibit host buffers. Enter 0,1 to compare both; blank uses the default.",
            "Tensor overrides" => "Optional tensor-pattern-to-buffer overrides. Separate complete benchmark matrix values with commas.",
            "Failure policy" => "Choose whether an automated suite stops at the first failed item or continues with the remaining work.",
            "Cooldown between items" => "Seconds to wait after one expanded work item finishes before the next begins.",
            "Equivalent profiles" => "Repeat low-level profiles even when they resolve to an identical llama-bench command instead of deduplicating them.",
            "Additional arguments" => "Enter one additional low-level llama-bench argument token per line. Manager-owned safety and output arguments cannot be overridden.",
            _ => $"Configure {label} for this benchmark plan."
        };
    }
}
