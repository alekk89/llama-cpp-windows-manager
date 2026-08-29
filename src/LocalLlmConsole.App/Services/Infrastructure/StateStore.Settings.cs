namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public async Task<AppSettings> GetAppSettingsAsync(string workspaceRoot)
    {
        var defaults = AppSettings.CreateDefault(workspaceRoot);
        var values = await ListSettingsAsync();
        var corrupt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string StringValue(string key, string fallback)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            try
            {
                var parsed = JsonSerializer.Deserialize<string>(value);
                return string.IsNullOrWhiteSpace(parsed) ? fallback : parsed;
            }
            catch
            {
                corrupt[key] = value;
                return fallback;
            }
        }

        int IntValue(string key, int fallback)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            if (TryReadJsonNumber(value, out var number))
            {
                if (number is >= int.MinValue and <= int.MaxValue) return (int)number;
            }
            corrupt[key] = value;
            return fallback;
        }

        double DoubleValue(string key, double fallback)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            if (TryReadJsonDouble(value, out var number)) return number;
            corrupt[key] = value;
            return fallback;
        }

        bool BoolValue(string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            if (TryReadJsonBool(value, out var parsed)) return parsed;
            corrupt[key] = value;
            return fallback;
        }

        OverviewDashboardLayout? DashboardLayoutValue(string key)
        {
            if (!values.TryGetValue(key, out var value)) return null;
            try
            {
                return JsonSerializer.Deserialize<OverviewDashboardLayout>(value);
            }
            catch
            {
                corrupt[key] = value;
                return null;
            }
        }

        var settings = defaults with
        {
            WorkspaceRoot = StringValue("workspaceRoot", defaults.WorkspaceRoot),
            ModelsRoot = StringValue("modelsRoot", defaults.ModelsRoot),
            RuntimeRoot = StringValue("runtimeRoot", defaults.RuntimeRoot),
            CacheRoot = StringValue("cacheRoot", defaults.CacheRoot),
            ThemeMode = StringValue("themeMode", defaults.ThemeMode),
            ShowOverviewModelStatus = BoolValue("showOverviewModelStatus", defaults.ShowOverviewModelStatus),
            ShowOverviewHardware = BoolValue("showOverviewHardware", defaults.ShowOverviewHardware),
            ShowOverviewSlots = BoolValue("showOverviewSlots", defaults.ShowOverviewSlots),
            ShowOverviewTokens = BoolValue("showOverviewTokens", defaults.ShowOverviewTokens),
            ShowOverviewMtpTokens = BoolValue("showOverviewMtpTokens", defaults.ShowOverviewMtpTokens),
            ShowOverviewKvCache = BoolValue("showOverviewKvCache", defaults.ShowOverviewKvCache),
            ShowOverviewModelSection = BoolValue("showOverviewModelSection", defaults.ShowOverviewModelSection),
            ShowOverviewLiveRuntimeLog = BoolValue("showOverviewLiveRuntimeLog", defaults.ShowOverviewLiveRuntimeLog),
            RuntimeLogOrder = AppPreferenceService.RuntimeLogOrder(StringValue("runtimeLogOrder", defaults.RuntimeLogOrder)),
            ShowOverviewAllMetrics = BoolValue("showOverviewAllMetrics", defaults.ShowOverviewAllMetrics),
            ShowModelsHuggingFace = BoolValue("showModelsHuggingFace", defaults.ShowModelsHuggingFace),
            OverviewDashboardLayout = DashboardLayoutValue("overviewDashboardLayout"),
            ElectricityCurrencyCode = StringValue("electricityCurrencyCode", defaults.ElectricityCurrencyCode),
            ElectricityDayRatePerKwh = DoubleValue("electricityDayRatePerKwh", defaults.ElectricityDayRatePerKwh),
            ElectricityNightRatePerKwh = DoubleValue("electricityNightRatePerKwh", defaults.ElectricityNightRatePerKwh),
            ElectricityNightStartLocal = StringValue("electricityNightStartLocal", defaults.ElectricityNightStartLocal),
            ElectricityNightEndLocal = StringValue("electricityNightEndLocal", defaults.ElectricityNightEndLocal),
            TrackGpuEnergyWhileIdle = BoolValue("trackGpuEnergyWhileIdle", defaults.TrackGpuEnergyWhileIdle),
            BenchmarkPreventSystemSleep = BoolValue("benchmarkPreventSystemSleep", defaults.BenchmarkPreventSystemSleep),
            BenchmarkStopActiveSessions = BoolValue("benchmarkStopActiveSessions", defaults.BenchmarkStopActiveSessions),
            MinimizeBehavior = StringValue("minimizeBehavior", defaults.MinimizeBehavior),
            StartWithWindows = BoolValue("startWithWindows", defaults.StartWithWindows),
            ModelAccessMode = AppPreferenceService.ModelAccessMode(StringValue("modelAccessMode", defaults.ModelAccessMode)),
            AutoLoadGatewayEnabled = BoolValue("autoLoadGatewayEnabled", defaults.AutoLoadGatewayEnabled),
            AutoLoadGatewayPort = Math.Clamp(IntValue("autoLoadGatewayPort", defaults.AutoLoadGatewayPort), 1, 65535),
            AutoLoadGatewayPolicy = AppPreferenceService.GatewaySwapPolicy(StringValue("autoLoadGatewayPolicy", defaults.AutoLoadGatewayPolicy)),
            Host = StringValue("host", defaults.Host),
            RequireApiKeyAuth = BoolValue("requireApiKeyAuth", defaults.RequireApiKeyAuth),
            ModelApiKey = SecretProtector.UnprotectSetting(StringValue("modelApiKey", defaults.ModelApiKey)),
            ModelApiKeyBackup = SecretProtector.UnprotectSetting(StringValue("modelApiKeyBackup", defaults.ModelApiKeyBackup)),
            WslDistro = StringValue("wslDistro", defaults.WslDistro),
            Port = Math.Clamp(IntValue("port", defaults.Port), 1, 65535),
            ContextSize = IntValue("contextSize", defaults.ContextSize),
            GpuLayers = IntValue("gpuLayers", defaults.GpuLayers),
            GpuMode = StringValue("gpuMode", defaults.GpuMode),
            GpuDevices = StringValue("gpuDevices", defaults.GpuDevices),
            GpuSplit = StringValue("gpuSplit", defaults.GpuSplit),
            EnableMetrics = BoolValue("enableMetrics", defaults.EnableMetrics),
            MaxLogFileSizeMb = Math.Clamp(IntValue("maxLogFileSizeMb", defaults.MaxLogFileSizeMb), 1, 4096),
            AutoUnloadIdleMinutes = Math.Clamp(IntValue("autoUnloadIdleMinutes", defaults.AutoUnloadIdleMinutes), 0, 10080),
            DeleteRuntimeSourceAfterSuccessfulBuild = BoolValue("deleteRuntimeSourceAfterSuccessfulBuild", defaults.DeleteRuntimeSourceAfterSuccessfulBuild),
            ReasoningMode = StringValue("reasoningMode", defaults.ReasoningMode),
            ReasoningFormat = StringValue("reasoningFormat", defaults.ReasoningFormat),
            ReasoningEffort = StringValue("reasoningEffort", defaults.ReasoningEffort),
            ReasoningBudget = IntValue("reasoningBudget", defaults.ReasoningBudget),
            ReasoningBudgetMessage = StringValue("reasoningBudgetMessage", defaults.ReasoningBudgetMessage),
            ReasoningPreserve = StringValue("reasoningPreserve", defaults.ReasoningPreserve),
            VisionMode = StringValue("visionMode", defaults.VisionMode),
            VisionProjectorPath = StringValue("visionProjectorPath", defaults.VisionProjectorPath),
            VisionImageMinTokens = IntValue("visionImageMinTokens", defaults.VisionImageMinTokens),
            VisionImageMaxTokens = IntValue("visionImageMaxTokens", defaults.VisionImageMaxTokens),
            FlashAttention = StringValue("flashAttention", defaults.FlashAttention),
            CacheTypeK = StringValue("cacheTypeK", defaults.CacheTypeK),
            CacheTypeV = StringValue("cacheTypeV", defaults.CacheTypeV),
            KvOffload = StringValue("kvOffload", defaults.KvOffload),
            KvUnified = StringValue("kvUnified", defaults.KvUnified),
            PromptCacheMode = StringValue("promptCacheMode", defaults.PromptCacheMode),
            PromptCacheRamMb = IntValue("promptCacheRamMb", defaults.PromptCacheRamMb),
            ContextCheckpointsMode = StringValue("contextCheckpointsMode", defaults.ContextCheckpointsMode),
            ContextCheckpointCount = IntValue("contextCheckpointCount", defaults.ContextCheckpointCount),
            ContextCheckpointEveryNTokens = IntValue("contextCheckpointEveryNTokens", defaults.ContextCheckpointEveryNTokens),
            ContinuousBatching = StringValue("continuousBatching", defaults.ContinuousBatching),
            JinjaMode = StringValue("jinjaMode", defaults.JinjaMode),
            ParallelSlots = IntValue("parallelSlots", defaults.ParallelSlots),
            BatchSize = IntValue("batchSize", defaults.BatchSize),
            MicroBatchSize = IntValue("microBatchSize", defaults.MicroBatchSize),
            Threads = IntValue("threads", defaults.Threads),
            MmapMode = StringValue("mmapMode", defaults.MmapMode),
            MlockMode = StringValue("mlockMode", defaults.MlockMode),
            Temperature = DoubleValue("temperature", defaults.Temperature),
            TopK = IntValue("topK", defaults.TopK),
            TopP = DoubleValue("topP", defaults.TopP),
            MinP = DoubleValue("minP", defaults.MinP),
            MaxTokens = IntValue("maxTokens", defaults.MaxTokens),
            Seed = IntValue("seed", defaults.Seed),
            RepeatLastN = IntValue("repeatLastN", defaults.RepeatLastN),
            RepeatPenalty = DoubleValue("repeatPenalty", defaults.RepeatPenalty),
            PresencePenalty = DoubleValue("presencePenalty", defaults.PresencePenalty),
            FrequencyPenalty = DoubleValue("frequencyPenalty", defaults.FrequencyPenalty),
            RopeScaling = StringValue("ropeScaling", defaults.RopeScaling),
            RopeScale = DoubleValue("ropeScale", defaults.RopeScale),
            RopeFreqBase = DoubleValue("ropeFreqBase", defaults.RopeFreqBase),
            RopeFreqScale = DoubleValue("ropeFreqScale", defaults.RopeFreqScale),
            SpeculativeType = StringValue("speculativeType", defaults.SpeculativeType),
            SpecDraftModelPath = StringValue("specDraftModelPath", defaults.SpecDraftModelPath),
            MtpHeadPath = StringValue("mtpHeadPath", defaults.MtpHeadPath),
            SpecDraftGpuLayers = IntValue("specDraftGpuLayers", defaults.SpecDraftGpuLayers),
            SpecDraftMinTokens = IntValue("specDraftMinTokens", defaults.SpecDraftMinTokens),
            SpecDraftMaxTokens = IntValue("specDraftMaxTokens", defaults.SpecDraftMaxTokens),
            SpecDraftPSplit = DoubleValue("specDraftPSplit", defaults.SpecDraftPSplit),
            SpecDraftPMin = DoubleValue("specDraftPMin", defaults.SpecDraftPMin),
            SpecDraftCacheTypeK = StringValue("specDraftCacheTypeK", defaults.SpecDraftCacheTypeK),
            SpecDraftCacheTypeV = StringValue("specDraftCacheTypeV", defaults.SpecDraftCacheTypeV),
            CudaPackagePreference = AppPreferenceService.CudaPackagePreference(StringValue("cudaPackagePreference", defaults.CudaPackagePreference)),
            CustomParameters = StringValue("customParameters", defaults.CustomParameters),
            UiCulture = StringValue("uiCulture", defaults.UiCulture)
        };

        var legacyDashboardVisibility = OverviewDashboardLayoutPolicy.LegacyVisibility(settings);
        var normalizedDashboardLayout = OverviewDashboardLayoutPolicy.Normalize(
            settings.OverviewDashboardLayout,
            legacyDashboardVisibility);
        var migratedDashboardLayout = !values.ContainsKey("overviewDashboardLayout")
                                      || !string.Equals(
                                          JsonSerializer.Serialize(settings.OverviewDashboardLayout),
                                          JsonSerializer.Serialize(normalizedDashboardLayout),
                                          StringComparison.Ordinal);
        settings = OverviewDashboardLayoutPolicy.WithLayout(settings, normalizedDashboardLayout);

        var storedTariffIsValid = ElectricityTariffPolicy.TryCreate(
            settings.ElectricityCurrencyCode,
            settings.ElectricityDayRatePerKwh,
            settings.ElectricityNightRatePerKwh,
            settings.ElectricityNightStartLocal,
            settings.ElectricityNightEndLocal,
            out var normalizedTariff,
            out _);
        normalizedTariff = storedTariffIsValid
            ? normalizedTariff
            : ElectricityTariffPolicy.FromSettings(defaults);
        var migratedElectricityTariff = !storedTariffIsValid
                                        || !string.Equals(settings.ElectricityCurrencyCode, normalizedTariff.CurrencyCode, StringComparison.Ordinal)
                                        || !string.Equals(settings.ElectricityNightStartLocal, ElectricityTariffPolicy.TimeText(normalizedTariff.NightStartLocal), StringComparison.Ordinal)
                                        || !string.Equals(settings.ElectricityNightEndLocal, ElectricityTariffPolicy.TimeText(normalizedTariff.NightEndLocal), StringComparison.Ordinal);
        settings = settings with
        {
            ElectricityCurrencyCode = normalizedTariff.CurrencyCode,
            ElectricityDayRatePerKwh = normalizedTariff.DayRatePerKwh,
            ElectricityNightRatePerKwh = normalizedTariff.NightRatePerKwh,
            ElectricityNightStartLocal = ElectricityTariffPolicy.TimeText(normalizedTariff.NightStartLocal),
            ElectricityNightEndLocal = ElectricityTariffPolicy.TimeText(normalizedTariff.NightEndLocal)
        };

        var unsafeUnauthenticatedAccess = !settings.RequireApiKeyAuth
                                          && !ModelAccessPolicy.AllowsUnauthenticatedAccess(settings.ModelAccessMode);
        var migratedApiKeyPolicy = unsafeUnauthenticatedAccess
                                   || (settings.RequireApiKeyAuth && !ApiSecurity.IsStrongBearerSecret(settings.ModelApiKey))
                                   || !ApiSecurity.IsStrongBearerSecret(settings.ModelApiKeyBackup)
                                   || (!settings.RequireApiKeyAuth && !string.IsNullOrWhiteSpace(settings.ModelApiKey));
        if (migratedApiKeyPolicy)
        {
            var apiKey = ApiSecurity.StrongBearerSecretOrNew(settings.ModelApiKey, settings.ModelApiKeyBackup);
            var requireApiKeyAuth = settings.RequireApiKeyAuth || unsafeUnauthenticatedAccess;
            settings = settings with
            {
                RequireApiKeyAuth = requireApiKeyAuth,
                ModelApiKey = requireApiKeyAuth ? apiKey : "",
                ModelApiKeyBackup = apiKey
            };
        }

        var migratedLegacyLaunchDefaults = false;
        if (LooksLikeLegacyAppLaunchDefaults(values))
        {
            if (IsStoredIntValue(values, "contextSize", 0))
            {
                settings = settings with { ContextSize = AppSettings.DefaultContextSize };
                migratedLegacyLaunchDefaults = true;
            }
            if (IsStoredIntValue(values, "gpuLayers", 0))
            {
                settings = settings with { GpuLayers = AppSettings.DefaultGpuLayers };
                migratedLegacyLaunchDefaults = true;
            }
            if (IsStoredIntValue(values, "batchSize", 2048))
            {
                settings = settings with { BatchSize = AppSettings.DefaultBatchSize };
                migratedLegacyLaunchDefaults = true;
            }
            if (IsStoredStringValue(values, "cacheTypeK", "f16"))
            {
                settings = settings with { CacheTypeK = AppSettings.DefaultCacheType };
                migratedLegacyLaunchDefaults = true;
            }
            if (IsStoredStringValue(values, "cacheTypeV", "f16"))
            {
                settings = settings with { CacheTypeV = AppSettings.DefaultCacheType };
                migratedLegacyLaunchDefaults = true;
            }
            if (IsStoredDoubleValue(values, "temperature", 0.8))
            {
                settings = settings with { Temperature = AppSettings.DefaultTemperature };
                migratedLegacyLaunchDefaults = true;
            }
        }

        if (corrupt.Count > 0)
        {
            await BackupCorruptSettingsAsync(corrupt);
        }
        if (corrupt.Count > 0 || migratedLegacyLaunchDefaults || migratedApiKeyPolicy || migratedDashboardLayout || migratedElectricityTariff)
        {
            await SaveAppSettingsAsync(settings);
        }

        return settings;
    }

    public async Task SaveAppSettingsAsync(AppSettings settings)
    {
        settings = OverviewDashboardLayoutPolicy.WithLayout(settings, settings.OverviewDashboardLayout);
        var tariff = ElectricityTariffPolicy.FromSettings(settings);
        settings = settings with
        {
            ElectricityCurrencyCode = tariff.CurrencyCode,
            ElectricityDayRatePerKwh = tariff.DayRatePerKwh,
            ElectricityNightRatePerKwh = tariff.NightRatePerKwh,
            ElectricityNightStartLocal = ElectricityTariffPolicy.TimeText(tariff.NightStartLocal),
            ElectricityNightEndLocal = ElectricityTariffPolicy.TimeText(tariff.NightEndLocal)
        };
        var rows = new (string Key, object Value)[]
        {
            ("workspaceRoot", settings.WorkspaceRoot),
            ("modelsRoot", settings.ModelsRoot),
            ("runtimeRoot", settings.RuntimeRoot),
            ("cacheRoot", settings.CacheRoot),
            ("themeMode", settings.ThemeMode),
            ("showOverviewModelStatus", settings.ShowOverviewModelStatus),
            ("showOverviewHardware", settings.ShowOverviewHardware),
            ("showOverviewSlots", settings.ShowOverviewSlots),
            ("showOverviewTokens", settings.ShowOverviewTokens),
            ("showOverviewMtpTokens", settings.ShowOverviewMtpTokens),
            ("showOverviewKvCache", settings.ShowOverviewKvCache),
            ("showOverviewModelSection", settings.ShowOverviewModelSection),
            ("showOverviewLiveRuntimeLog", settings.ShowOverviewLiveRuntimeLog),
            ("runtimeLogOrder", AppPreferenceService.RuntimeLogOrder(settings.RuntimeLogOrder)),
            ("showOverviewAllMetrics", settings.ShowOverviewAllMetrics),
            ("showModelsHuggingFace", settings.ShowModelsHuggingFace),
            ("overviewDashboardLayout", OverviewDashboardLayoutPolicy.Normalize(settings.OverviewDashboardLayout)),
            ("electricityCurrencyCode", settings.ElectricityCurrencyCode),
            ("electricityDayRatePerKwh", settings.ElectricityDayRatePerKwh),
            ("electricityNightRatePerKwh", settings.ElectricityNightRatePerKwh),
            ("electricityNightStartLocal", settings.ElectricityNightStartLocal),
            ("electricityNightEndLocal", settings.ElectricityNightEndLocal),
            ("trackGpuEnergyWhileIdle", settings.TrackGpuEnergyWhileIdle),
            ("benchmarkPreventSystemSleep", settings.BenchmarkPreventSystemSleep),
            ("benchmarkStopActiveSessions", settings.BenchmarkStopActiveSessions),
            ("minimizeBehavior", settings.MinimizeBehavior),
            ("startWithWindows", settings.StartWithWindows),
            ("modelAccessMode", AppPreferenceService.ModelAccessMode(settings.ModelAccessMode)),
            ("autoLoadGatewayEnabled", settings.AutoLoadGatewayEnabled),
            ("autoLoadGatewayPort", settings.AutoLoadGatewayPort),
            ("autoLoadGatewayPolicy", AppPreferenceService.GatewaySwapPolicy(settings.AutoLoadGatewayPolicy)),
            ("host", settings.Host),
            ("requireApiKeyAuth", settings.RequireApiKeyAuth),
            ("modelApiKey", SecretProtector.ProtectSetting(settings.ModelApiKey)),
            ("modelApiKeyBackup", SecretProtector.ProtectSetting(settings.ModelApiKeyBackup)),
            ("wslDistro", settings.WslDistro),
            ("port", settings.Port),
            ("contextSize", settings.ContextSize),
            ("gpuLayers", settings.GpuLayers),
            ("gpuMode", settings.GpuMode),
            ("gpuDevices", settings.GpuDevices),
            ("gpuSplit", settings.GpuSplit),
            ("enableMetrics", settings.EnableMetrics),
            ("maxLogFileSizeMb", settings.MaxLogFileSizeMb),
            ("autoUnloadIdleMinutes", settings.AutoUnloadIdleMinutes),
            ("deleteRuntimeSourceAfterSuccessfulBuild", settings.DeleteRuntimeSourceAfterSuccessfulBuild),
            ("reasoningMode", settings.ReasoningMode),
            ("reasoningFormat", settings.ReasoningFormat),
            ("reasoningEffort", settings.ReasoningEffort),
            ("reasoningBudget", settings.ReasoningBudget),
            ("reasoningBudgetMessage", settings.ReasoningBudgetMessage),
            ("reasoningPreserve", settings.ReasoningPreserve),
            ("visionMode", settings.VisionMode),
            ("visionProjectorPath", settings.VisionProjectorPath),
            ("visionImageMinTokens", settings.VisionImageMinTokens),
            ("visionImageMaxTokens", settings.VisionImageMaxTokens),
            ("flashAttention", settings.FlashAttention),
            ("cacheTypeK", settings.CacheTypeK),
            ("cacheTypeV", settings.CacheTypeV),
            ("kvOffload", settings.KvOffload),
            ("kvUnified", settings.KvUnified),
            ("promptCacheMode", settings.PromptCacheMode),
            ("promptCacheRamMb", settings.PromptCacheRamMb),
            ("contextCheckpointsMode", settings.ContextCheckpointsMode),
            ("contextCheckpointCount", settings.ContextCheckpointCount),
            ("contextCheckpointEveryNTokens", settings.ContextCheckpointEveryNTokens),
            ("continuousBatching", settings.ContinuousBatching),
            ("jinjaMode", settings.JinjaMode),
            ("parallelSlots", settings.ParallelSlots),
            ("batchSize", settings.BatchSize),
            ("microBatchSize", settings.MicroBatchSize),
            ("threads", settings.Threads),
            ("mmapMode", settings.MmapMode),
            ("mlockMode", settings.MlockMode),
            ("temperature", settings.Temperature),
            ("topK", settings.TopK),
            ("topP", settings.TopP),
            ("minP", settings.MinP),
            ("maxTokens", settings.MaxTokens),
            ("seed", settings.Seed),
            ("repeatLastN", settings.RepeatLastN),
            ("repeatPenalty", settings.RepeatPenalty),
            ("presencePenalty", settings.PresencePenalty),
            ("frequencyPenalty", settings.FrequencyPenalty),
            ("ropeScaling", settings.RopeScaling),
            ("ropeScale", settings.RopeScale),
            ("ropeFreqBase", settings.RopeFreqBase),
            ("ropeFreqScale", settings.RopeFreqScale),
            ("speculativeType", settings.SpeculativeType),
            ("specDraftModelPath", settings.SpecDraftModelPath),
            ("mtpHeadPath", settings.MtpHeadPath),
            ("specDraftGpuLayers", settings.SpecDraftGpuLayers),
            ("specDraftMinTokens", settings.SpecDraftMinTokens),
            ("specDraftMaxTokens", settings.SpecDraftMaxTokens),
            ("specDraftPSplit", settings.SpecDraftPSplit),
            ("specDraftPMin", settings.SpecDraftPMin),
            ("specDraftCacheTypeK", settings.SpecDraftCacheTypeK),
            ("specDraftCacheTypeV", settings.SpecDraftCacheTypeV),
            ("cudaPackagePreference", AppPreferenceService.CudaPackagePreference(settings.CudaPackagePreference)),
            ("customParameters", settings.CustomParameters),
            ("uiCulture", settings.UiCulture)
        };

        await WithConnectionAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            foreach (var row in rows)
                await SetSettingUnlockedAsync(row.Key, row.Value, transaction);

            await transaction.CommitAsync();
        });
    }

    public Task<Dictionary<string, string>> ListSettingsAsync()
        => WithConnectionAsync(async () =>
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT key, value_json FROM settings;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) values[reader.GetString(0)] = reader.GetString(1);
            return values;
        });

    private async Task SetSettingUnlockedAsync(string key, object value, System.Data.Common.DbTransaction? transaction = null)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = """
INSERT INTO settings (key, value_json, updated_at)
VALUES ($key, $value_json, $updated_at)
ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
""";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value_json", JsonSerializer.Serialize(value, value.GetType()));
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
