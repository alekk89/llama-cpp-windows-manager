namespace LocalLlmConsole;

internal static class OverviewDashboardMetricNaming
{
    public static string BuiltInTooltip(string displayName, string id)
        => $"{displayName}\n{Loc.T("Dashboard.Tooltip.TechnicalMetric", id)}";

    public static string RawTooltip(string name, string labels, string type, string help)
    {
        var source = string.IsNullOrWhiteSpace(labels) ? name : $"{name}{{{labels}}}";
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(help)) details.Add(help.Trim());
        details.Add(Loc.T("Dashboard.Tooltip.SourceMetric", source));
        if (!string.IsNullOrWhiteSpace(type))
            details.Add(Loc.T("Dashboard.Tooltip.PrometheusType", type.Trim()));
        return string.Join("\n", details);
    }

    public static string FriendlyRawName(string name)
    {
        var semanticName = Regex.Replace(name ?? "", @"^(?:llamacpp:|llamacpp_|llama_cpp_|llama_)", "",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var known = semanticName.ToLowerInvariant() switch
        {
            "tokens_predicted_total" => "Generated tokens (total)",
            "tokens_predicted_seconds_total" => "Generation time (total)",
            "predicted_tokens_seconds" => "Average generation throughput",
            "prompt_tokens_total" => "Prompt tokens processed (total)",
            "prompt_tokens_cached_total" => "Cached prompt tokens (total)",
            "prompt_seconds_total" => "Prompt processing time (total)",
            "prompt_tokens_seconds" => "Average prompt throughput",
            "requests_processing" => "Active requests",
            "requests_deferred" => "Queued requests",
            "requests_completed_total" => "Completed requests (total)",
            "requests_failed_total" => "Failed requests (total)",
            "n_busy_slots_per_decode" => "Average busy slots per decode",
            "n_decode_total" => "Decode calls (total)",
            "n_tokens_max" => "Largest observed sequence length",
            "spec_decode_num_draft_tokens_total" => "Speculative draft tokens generated (total)",
            "spec_decode_num_accepted_tokens_total" => "Speculative draft tokens accepted (total)",
            "spec_decode_num_drafts_total" => "Speculative verification steps (total)",
            "mtp_tokens_generated_total" => "Speculative tokens generated (total)",
            "mtp_tokens_generated_seconds_total" => "Speculative generation time (total)",
            "mtp_tokens_accepted_total" => "Speculative tokens accepted (total)",
            "mtp_tokens_accepted_seconds_total" => "Speculative acceptance time (total)",
            _ => ""
        };
        if (!string.IsNullOrWhiteSpace(known)) return known;

        var words = Regex.Split(semanticName, @"[_:./-]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(FriendlyWord)
            .ToArray();
        if (words.Length == 0) return name ?? "";
        var result = string.Join(" ", words);
        return char.ToUpper(result[0], CultureInfo.CurrentCulture) + result[1..];
    }

    public static string FriendlyLabels(string labels)
    {
        if (string.IsNullOrWhiteSpace(labels)) return "";
        var values = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(label => label.Split('=', 2, StringSplitOptions.TrimEntries))
            .Select(parts => parts.Length == 2
                ? $"{FriendlyRawName(parts[0])}: {parts[1].Trim().Trim('"')}"
                : FriendlyRawName(parts[0]));
        return $" · {string.Join(" · ", values)}";
    }

    private static string FriendlyWord(string word)
        => word.ToLowerInvariant() switch
        {
            "api" => "API",
            "cpu" => "CPU",
            "gpu" => "GPU",
            "id" => "ID",
            "kv" => "KV",
            "mtp" => "MTP",
            "ram" => "RAM",
            "vram" => "VRAM",
            _ => word.ToLower(CultureInfo.CurrentCulture)
        };
}
