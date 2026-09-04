using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static class BenchmarkResultService
{
    public static bool TryParse(
        string jsonLine,
        string modelFingerprint,
        string effectiveCommandSignature,
        RuntimeMode runtimeMode,
        RuntimeBackend runtimeBackend,
        out BenchmarkParsedResult? result,
        out string error,
        string managerVersion = "",
        string operatingEnvironment = "")
    {
        result = null;
        error = "";
        try
        {
            using var document = JsonDocument.Parse(jsonLine);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Benchmark output row was not a JSON object.";
                return false;
            }
            if (!TryInt(root, "n_prompt", out var prompt)
                || !TryInt(root, "n_gen", out var generation)
                || !TryDouble(root, "avg_ts", out var throughput)
                || throughput < 0
                || prompt < 0
                || generation < 0
                || (prompt == 0 && generation == 0))
            {
                error = "Benchmark output row did not contain a valid llama-bench workload and throughput.";
                return false;
            }
            var classification = Classify(prompt, generation);
            var environment = string.Join('|',
                String(root, "build_commit"), Int(root, "build_number"), runtimeMode, runtimeBackend,
                operatingEnvironment, String(root, "cpu_info"), String(root, "gpu_info"),
                String(root, "backends"), String(root, "devices"), managerVersion);
            result = new BenchmarkParsedResult(
                classification,
                jsonLine,
                Hash($"{modelFingerprint}|{classification}|{prompt}|{generation}|{Int(root, "n_depth")}|{effectiveCommandSignature}"),
                Hash(environment),
                managerVersion,
                operatingEnvironment,
                String(root, "build_commit"),
                Int(root, "build_number"),
                String(root, "cpu_info"),
                String(root, "gpu_info"),
                String(root, "backends"),
                String(root, "model_filename"),
                String(root, "model_type"),
                Long(root, "model_size"),
                Long(root, "model_n_params"),
                prompt,
                generation,
                Int(root, "n_depth"),
                Int(root, "n_batch"),
                Int(root, "n_ubatch"),
                Int(root, "n_threads"),
                String(root, "cpu_mask"),
                Bool(root, "cpu_strict"),
                Int(root, "poll"),
                Int(root, "n_gpu_layers"),
                Int(root, "n_cpu_moe"),
                String(root, "type_k"),
                String(root, "type_v"),
                String(root, "split_mode"),
                Int(root, "main_gpu"),
                Bool(root, "no_kv_offload"),
                String(root, "flash_attn"),
                String(root, "devices"),
                String(root, "tensor_split"),
                StringAny(root, "tensor_buft_overrides", "tensor_buffer_overrides"),
                String(root, "load_mode"),
                Bool(root, "embeddings"),
                Bool(root, "no_op_offload"),
                Bool(root, "no_host"),
                Long(root, "fit_target"),
                Int(root, "fit_min_ctx"),
                Long(root, "avg_ns"),
                Long(root, "stddev_ns"),
                Double(root, "avg_ts"),
                Double(root, "stddev_ts"),
                String(root, "test_time"),
                String(root, "execution_mode").Equals("profile_serving", StringComparison.OrdinalIgnoreCase)
                    ? BenchmarkExecutionMode.ProfileServing
                    : BenchmarkExecutionMode.LlamaBench,
                String(root, "profile_id"),
                String(root, "profile_name"),
                String(root, "speculative_type"),
                Math.Max(1, Int(root, "concurrency")),
                Int(root, "request_count"),
                Int(root, "failed_request_count"),
                Double(root, "avg_prompt_ts"),
                Double(root, "avg_latency_ms"),
                Double(root, "stddev_latency_ms"),
                Long(root, "draft_tokens"),
                Long(root, "accepted_draft_tokens"),
                Double(root, "draft_acceptance_percent"),
                Bool(root, "speculative_metrics_observed"),
                Int(root, "n_ctx"),
                Long(root, "gpu_memory_used_mib"),
                root.TryGetProperty("gpu_memory_peaks", out var peaks)
                    ? peaks.Deserialize<BenchmarkGpuMemoryPeak[]>() : null,
                Int(root, "gpu_memory_sample_interval_ms"),
                Int(root, "vulkan_allocation_block_size_mib"),
                String(root, "gpu_memory_measurement_window"));
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Malformed benchmark JSONL: {ex.Message[..Math.Min(ex.Message.Length, 256)]}";
            return false;
        }
    }

    public static BenchmarkResultClassification Classify(int promptTokens, int generationTokens)
        => (promptTokens, generationTokens) switch
        {
            ( > 0, 0) => BenchmarkResultClassification.PromptProcessing,
            (0, > 0) => BenchmarkResultClassification.TokenGeneration,
            ( > 0, > 0) => BenchmarkResultClassification.PromptAndGeneration,
            _ => BenchmarkResultClassification.Unknown
        };

    private static string String(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) ? value.ToString() : "";
    private static string StringAny(JsonElement root, params string[] names)
        => names.Select(name => String(root, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    private static int Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : 0;
    private static bool TryInt(JsonElement root, string name, out int parsed)
    {
        parsed = 0;
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out parsed);
    }
    private static long Long(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed) ? parsed : 0;
    private static double Double(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed) && double.IsFinite(parsed) ? parsed : 0;
    private static bool TryDouble(JsonElement root, string name, out double parsed)
    {
        parsed = 0;
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out parsed) && double.IsFinite(parsed);
    }
    private static bool Bool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
