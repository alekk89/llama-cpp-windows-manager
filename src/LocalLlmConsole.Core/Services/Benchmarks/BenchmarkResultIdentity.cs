using System.Text.Json;

namespace LocalLlmConsole.Services;

public static class BenchmarkResultIdentity
{
    private static readonly string[] ConfigurationFields =
    [
        "n_prompt", "n_gen", "n_depth", "n_ctx", "n_batch", "n_ubatch", "n_threads",
        "cpu_mask", "cpu_strict", "poll", "n_gpu_layers", "n_cpu_moe", "type_k", "type_v",
        "split_mode", "main_gpu", "no_kv_offload", "flash_attn", "devices", "tensor_split",
        "tensor_buft_overrides", "load_mode", "lazy_mode", "embeddings", "no_op_offload",
        "no_host", "fit_target", "fit_min_ctx", "execution_mode", "speculative_type", "concurrency"
    ];

    public static string Workload(JsonElement row, string modelFingerprint, string unreportedOptions)
        => BenchmarkPlanService.StableHash(modelFingerprint + "|" + unreportedOptions + "|" +
            string.Join('|', ConfigurationFields.Select(name => name + "=" +
                (row.TryGetProperty(name, out var value) ? value.ToString() : ""))));

    // Older stored rows may have plan-wide signatures. Include the actual row
    // configuration when presenting history without rewriting persisted data.
    public static string StoredConfiguration(LocalLlmConsole.Models.BenchmarkParsedResult result)
    {
        using var document = JsonDocument.Parse(result.RawJson);
        return Workload(document.RootElement, result.ModelFilename, result.WorkloadSignature);
    }
}
