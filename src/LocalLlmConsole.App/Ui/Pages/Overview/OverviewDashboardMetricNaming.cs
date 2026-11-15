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
            "tokens_predicted_total" => Loc.T("Dashboard.RawMetric.GeneratedTokensTotal"),
            "tokens_predicted_seconds_total" => Loc.T("Dashboard.RawMetric.GenerationTimeTotal"),
            "predicted_tokens_seconds" => Loc.T("Dashboard.RawMetric.AverageGenerationThroughput"),
            "prompt_tokens_total" => Loc.T("Dashboard.RawMetric.PromptTokensTotal"),
            "prompt_tokens_cached_total" => Loc.T("Dashboard.RawMetric.CachedPromptTokensTotal"),
            "prompt_seconds_total" => Loc.T("Dashboard.RawMetric.PromptTimeTotal"),
            "prompt_tokens_seconds" => Loc.T("Dashboard.RawMetric.AveragePromptThroughput"),
            "requests_processing" => Loc.T("Dashboard.RawMetric.ActiveRequests"),
            "requests_deferred" => Loc.T("Dashboard.RawMetric.QueuedRequests"),
            "requests_completed_total" => Loc.T("Dashboard.RawMetric.CompletedRequestsTotal"),
            "requests_failed_total" => Loc.T("Dashboard.RawMetric.FailedRequestsTotal"),
            "n_busy_slots_per_decode" => Loc.T("Dashboard.RawMetric.BusySlotsPerDecode"),
            "n_decode_total" => Loc.T("Dashboard.RawMetric.DecodeCallsTotal"),
            "n_tokens_max" => Loc.T("Dashboard.RawMetric.LargestSequenceLength"),
            "spec_decode_num_draft_tokens_total" => Loc.T("Dashboard.RawMetric.DraftTokensGeneratedTotal"),
            "spec_decode_num_accepted_tokens_total" => Loc.T("Dashboard.RawMetric.DraftTokensAcceptedTotal"),
            "spec_decode_num_drafts_total" => Loc.T("Dashboard.RawMetric.VerificationStepsTotal"),
            "mtp_tokens_generated_total" => Loc.T("Dashboard.RawMetric.SpeculativeTokensGeneratedTotal"),
            "mtp_tokens_generated_seconds_total" => Loc.T("Dashboard.RawMetric.SpeculativeGenerationTimeTotal"),
            "mtp_tokens_accepted_total" => Loc.T("Dashboard.RawMetric.SpeculativeTokensAcceptedTotal"),
            "mtp_tokens_accepted_seconds_total" => Loc.T("Dashboard.RawMetric.SpeculativeAcceptanceTimeTotal"),
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
