using System.Reflection;
using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class AppSettingsPersistenceContractTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task EveryAppSettingIsClassifiedAndHasOnePersistedRow()
    {
        var secretProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(AppSettings.ModelApiKey),
            nameof(AppSettings.ModelApiKeyBackup)
        };
        var compatibilityProjectionProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(AppSettings.ShowOverviewModelStatus),
            nameof(AppSettings.ShowOverviewHardware),
            nameof(AppSettings.ShowOverviewSlots),
            nameof(AppSettings.ShowOverviewTokens),
            nameof(AppSettings.ShowOverviewMtpTokens),
            nameof(AppSettings.ShowOverviewKvCache),
            nameof(AppSettings.ShowOverviewModelSection),
            nameof(AppSettings.ShowOverviewLiveRuntimeLog),
            nameof(AppSettings.ShowOverviewAllMetrics),
            nameof(AppSettings.ShowModelsHuggingFace)
        };
        var structuredProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(AppSettings.OverviewDashboardLayout)
        };
        var properties = typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var classifiedProperties = OrdinaryPersistedProperties
            .Concat(secretProperties)
            .Concat(compatibilityProjectionProperties)
            .Concat(structuredProperties)
            .ToArray();

        Assert.Equal(properties.Length, classifiedProperties.Length);
        Assert.Equal(
            properties.Select(property => property.Name).Order(StringComparer.Ordinal),
            classifiedProperties.Order(StringComparer.Ordinal));

        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "settings-contract.db"));
        await store.InitializeAsync();
        await store.SaveAppSettingsAsync(AppSettings.CreateDefault(root));
        var rows = await store.ListSettingsAsync();
        var expectedKeys = properties
            .Select(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..])
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedKeys, rows.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task EveryPersistedAppSettingRoundTripsThroughSqlite()
    {
        var root = CreateTempRoot();
        var key = new string('k', 40);
        var settings = AppSettings.CreateDefault(root) with
        {
            WorkspaceRoot = Path.Combine(root, "persisted-workspace"),
            ModelsRoot = Path.Combine(root, "models-custom"),
            RuntimeRoot = Path.Combine(root, "runtimes-custom"),
            CacheRoot = Path.Combine(root, "cache-custom"),
            ThemeMode = "dark",
            MinimizeBehavior = "trayOnly",
            StartWithWindows = true,
            ModelAccessMode = "both",
            AutoLoadGatewayEnabled = false,
            AutoLoadGatewayPort = 9191,
            AutoLoadGatewayPolicy = "keepLoaded",
            Host = "0.0.0.0",
            RequireApiKeyAuth = true,
            ModelApiKey = key,
            ModelApiKeyBackup = key,
            WslDistro = "Ubuntu-Test",
            Port = 9192,
            ContextSize = 65_536,
            GpuLayers = 123,
            EnableMetrics = false,
            MaxLogFileSizeMb = 17,
            AutoUnloadIdleMinutes = 23,
            DeleteRuntimeSourceAfterSuccessfulBuild = false,
            ReasoningMode = "on",
            ReasoningFormat = "deepseek",
            ReasoningBudget = 2048,
            VisionMode = "on",
            VisionProjectorPath = "projector.gguf",
            VisionImageMinTokens = 64,
            VisionImageMaxTokens = 256,
            FlashAttention = "on",
            CacheTypeK = "q4_0",
            CacheTypeV = "q5_0",
            KvOffload = "off",
            KvUnified = "on",
            ContinuousBatching = "off",
            JinjaMode = "on",
            ParallelSlots = 3,
            BatchSize = 2048,
            MicroBatchSize = 256,
            Threads = 12,
            MmapMode = "off",
            MlockMode = "on",
            Temperature = 0.72,
            TopK = 31,
            TopP = 0.91,
            MinP = 0.03,
            MaxTokens = 4096,
            Seed = 42,
            RepeatLastN = 96,
            RepeatPenalty = 1.07,
            PresencePenalty = 0.2,
            FrequencyPenalty = -0.3,
            RopeScaling = "yarn",
            RopeScale = 1.25,
            RopeFreqBase = 10_000,
            RopeFreqScale = 0.75,
            SpeculativeType = "draft-mtp",
            SpecDraftModelPath = "draft.gguf",
            MtpHeadPath = "mtp.gguf",
            SpecDraftGpuLayers = 77,
            SpecDraftMinTokens = 2,
            SpecDraftMaxTokens = 8,
            SpecDraftPSplit = 0.6,
            SpecDraftPMin = 0.2,
            SpecDraftCacheTypeK = "q4_1",
            SpecDraftCacheTypeV = "q5_1",
            CudaPackagePreference = "compatibility",
            PromptCacheMode = "on",
            PromptCacheRamMb = 12_345,
            ContextCheckpointsMode = "on",
            ContextCheckpointCount = 17,
            ContextCheckpointEveryNTokens = 333,
            CustomParameters = "--custom-flag value",
            UiCulture = "fr",
            GpuMode = "tensor",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "2,1",
            ReasoningEffort = "high",
            ReasoningBudgetMessage = "Return the final answer.",
            ReasoningPreserve = "off",
            ShowOverviewModelStatus = true,
            ShowOverviewHardware = false,
            ShowOverviewSlots = true,
            ShowOverviewTokens = false,
            ShowOverviewMtpTokens = false,
            ShowOverviewKvCache = false,
            ShowOverviewModelSection = false,
            ShowOverviewLiveRuntimeLog = false,
            RuntimeLogOrder = "oldestFirst",
            ShowOverviewAllMetrics = true,
            ShowModelsHuggingFace = true,
            OverviewDashboardLayout = OverviewDashboardLayoutPolicy.Default,
            ElectricityCurrencyCode = "USD",
            ElectricityDayRatePerKwh = 0.31,
            ElectricityNightRatePerKwh = 0.12,
            ElectricityNightStartLocal = "22:30",
            ElectricityNightEndLocal = "06:15",
            TrackGpuEnergyWhileIdle = true,
            BenchmarkPreventSystemSleep = false,
            BenchmarkStopActiveSessions = true
        };
        var expected = OverviewDashboardLayoutPolicy.WithLayout(settings, settings.OverviewDashboardLayout);
        var tariff = ElectricityTariffPolicy.FromSettings(expected);
        expected = expected with
        {
            ElectricityCurrencyCode = tariff.CurrencyCode,
            ElectricityDayRatePerKwh = tariff.DayRatePerKwh,
            ElectricityNightRatePerKwh = tariff.NightRatePerKwh,
            ElectricityNightStartLocal = ElectricityTariffPolicy.TimeText(tariff.NightStartLocal),
            ElectricityNightEndLocal = ElectricityTariffPolicy.TimeText(tariff.NightEndLocal)
        };

        await using var store = new StateStore(Path.Combine(root, "state", "settings-roundtrip.db"));
        await store.InitializeAsync();
        await store.SaveAppSettingsAsync(settings);
        var loaded = await store.GetAppSettingsAsync(root);

        foreach (var property in typeof(AppSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == nameof(AppSettings.OverviewDashboardLayout))
            {
                Assert.Equal(
                    JsonSerializer.Serialize(property.GetValue(expected)),
                    JsonSerializer.Serialize(property.GetValue(loaded)));
                continue;
            }

            Assert.Equal(property.GetValue(expected), property.GetValue(loaded));
        }
    }

    private static readonly string[] OrdinaryPersistedProperties =
    [
        nameof(AppSettings.WorkspaceRoot),
        nameof(AppSettings.ModelsRoot),
        nameof(AppSettings.RuntimeRoot),
        nameof(AppSettings.CacheRoot),
        nameof(AppSettings.ThemeMode),
        nameof(AppSettings.MinimizeBehavior),
        nameof(AppSettings.StartWithWindows),
        nameof(AppSettings.ModelAccessMode),
        nameof(AppSettings.AutoLoadGatewayEnabled),
        nameof(AppSettings.AutoLoadGatewayPort),
        nameof(AppSettings.AutoLoadGatewayPolicy),
        nameof(AppSettings.Host),
        nameof(AppSettings.RequireApiKeyAuth),
        nameof(AppSettings.WslDistro),
        nameof(AppSettings.Port),
        nameof(AppSettings.ContextSize),
        nameof(AppSettings.GpuLayers),
        nameof(AppSettings.EnableMetrics),
        nameof(AppSettings.MaxLogFileSizeMb),
        nameof(AppSettings.AutoUnloadIdleMinutes),
        nameof(AppSettings.DeleteRuntimeSourceAfterSuccessfulBuild),
        nameof(AppSettings.ReasoningMode),
        nameof(AppSettings.ReasoningFormat),
        nameof(AppSettings.ReasoningBudget),
        nameof(AppSettings.VisionMode),
        nameof(AppSettings.VisionProjectorPath),
        nameof(AppSettings.VisionImageMinTokens),
        nameof(AppSettings.VisionImageMaxTokens),
        nameof(AppSettings.FlashAttention),
        nameof(AppSettings.CacheTypeK),
        nameof(AppSettings.CacheTypeV),
        nameof(AppSettings.KvOffload),
        nameof(AppSettings.KvUnified),
        nameof(AppSettings.ContinuousBatching),
        nameof(AppSettings.JinjaMode),
        nameof(AppSettings.ParallelSlots),
        nameof(AppSettings.BatchSize),
        nameof(AppSettings.MicroBatchSize),
        nameof(AppSettings.Threads),
        nameof(AppSettings.MmapMode),
        nameof(AppSettings.MlockMode),
        nameof(AppSettings.Temperature),
        nameof(AppSettings.TopK),
        nameof(AppSettings.TopP),
        nameof(AppSettings.MinP),
        nameof(AppSettings.MaxTokens),
        nameof(AppSettings.Seed),
        nameof(AppSettings.RepeatLastN),
        nameof(AppSettings.RepeatPenalty),
        nameof(AppSettings.PresencePenalty),
        nameof(AppSettings.FrequencyPenalty),
        nameof(AppSettings.RopeScaling),
        nameof(AppSettings.RopeScale),
        nameof(AppSettings.RopeFreqBase),
        nameof(AppSettings.RopeFreqScale),
        nameof(AppSettings.SpeculativeType),
        nameof(AppSettings.SpecDraftModelPath),
        nameof(AppSettings.MtpHeadPath),
        nameof(AppSettings.SpecDraftGpuLayers),
        nameof(AppSettings.SpecDraftMinTokens),
        nameof(AppSettings.SpecDraftMaxTokens),
        nameof(AppSettings.SpecDraftPSplit),
        nameof(AppSettings.SpecDraftPMin),
        nameof(AppSettings.SpecDraftCacheTypeK),
        nameof(AppSettings.SpecDraftCacheTypeV),
        nameof(AppSettings.CudaPackagePreference),
        nameof(AppSettings.PromptCacheMode),
        nameof(AppSettings.PromptCacheRamMb),
        nameof(AppSettings.ContextCheckpointsMode),
        nameof(AppSettings.ContextCheckpointCount),
        nameof(AppSettings.ContextCheckpointEveryNTokens),
        nameof(AppSettings.CustomParameters),
        nameof(AppSettings.UiCulture),
        nameof(AppSettings.GpuMode),
        nameof(AppSettings.GpuDevices),
        nameof(AppSettings.GpuSplit),
        nameof(AppSettings.ReasoningEffort),
        nameof(AppSettings.ReasoningBudgetMessage),
        nameof(AppSettings.ReasoningPreserve),
        nameof(AppSettings.RuntimeLogOrder),
        nameof(AppSettings.ElectricityCurrencyCode),
        nameof(AppSettings.ElectricityDayRatePerKwh),
        nameof(AppSettings.ElectricityNightRatePerKwh),
        nameof(AppSettings.ElectricityNightStartLocal),
        nameof(AppSettings.ElectricityNightEndLocal),
        nameof(AppSettings.TrackGpuEnergyWhileIdle),
        nameof(AppSettings.BenchmarkPreventSystemSleep),
        nameof(AppSettings.BenchmarkStopActiveSessions)
    ];
}
