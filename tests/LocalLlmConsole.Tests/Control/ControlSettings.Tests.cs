using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlSettingsTests : ManagerRegressionTestBase
{
    [Fact]
    public void ControlSettingsMutationNormalizesElectricityTariff()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot());
        var patch = JsonNode.Parse("""
            {
              "electricityCurrencyCode": " eur ",
              "electricityDayRatePerKwh": 0.31,
              "electricityNightRatePerKwh": 0.12,
              "electricityNightStartLocal": "7:05",
              "electricityNightEndLocal": "23:30",
              "trackGpuEnergyWhileIdle": true,
              "benchmarkPreventSystemSleep": false,
              "benchmarkStopActiveSessions": true
            }
            """)!.AsObject();

        var updated = new ControlAppSettingsMutationService().Patch(current, patch, []);

        Assert.Equal("EUR", updated.ElectricityCurrencyCode);
        Assert.Equal("07:05", updated.ElectricityNightStartLocal);
        Assert.Equal("23:30", updated.ElectricityNightEndLocal);
        Assert.True(updated.TrackGpuEnergyWhileIdle);
        Assert.False(updated.BenchmarkPreventSystemSleep);
        Assert.True(updated.BenchmarkStopActiveSessions);
    }

    [Fact]
    public void ControlSettingsExposeAndPatchOverviewModelSectionVisibility()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot());
        var schema = System.Text.Json.JsonSerializer.Serialize(ControlEndpointHandler.SettingsSchema<AppSettings>());

        Assert.Contains("\"name\":\"showOverviewModelSection\"", schema, StringComparison.Ordinal);

        var updated = new ControlAppSettingsMutationService().Patch(
            current,
            JsonNode.Parse("""{"showOverviewModelSection":false}""")!.AsObject(),
            []);

        Assert.False(updated.ShowOverviewModelSection);
    }

    [Fact]
    public void ControlSettingsExposeAndNormalizeApplicationScales()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot());
        var schema = System.Text.Json.JsonSerializer.Serialize(ControlEndpointHandler.SettingsSchema<AppSettings>());

        Assert.Contains("\"name\":\"uiScalePercent\"", schema, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"fontScalePercent\"", schema, StringComparison.Ordinal);

        var updated = new ControlAppSettingsMutationService().Patch(
            current,
            JsonNode.Parse("""{"uiScalePercent":999,"fontScalePercent":1}""")!.AsObject(),
            []);

        Assert.Equal(175, updated.UiScalePercent);
        Assert.Equal(75, updated.FontScalePercent);
    }

    [Theory]
    [InlineData("{\"electricityCurrencyCode\":\"UK\"}")]
    [InlineData("{\"electricityDayRatePerKwh\":-1}")]
    [InlineData("{\"electricityNightStartLocal\":\"noon\"}")]
    public void ControlSettingsMutationRejectsInvalidElectricityTariffs(string json)
    {
        var current = AppSettings.CreateDefault(CreateTempRoot());
        Assert.Throws<InvalidOperationException>(() =>
            new ControlAppSettingsMutationService().Patch(current, JsonNode.Parse(json)!.AsObject(), []));
    }

    [Fact]
    public void ControlSettingsMutationNormalizesSafeValuesAndPreservesProtectedState()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot());
        var service = new ControlAppSettingsMutationService();
        var patch = JsonNode.Parse("""
            {
              "themeMode": "LIGHT",
              "minimizeBehavior": "Tray + taskbar",
              "modelAccessMode": "Gateway LAN only",
              "uiCulture": " de "
            }
            """)!.AsObject();

        var updated = service.Patch(current, patch, []);

        Assert.Equal("light", updated.ThemeMode);
        Assert.Equal("trayAndTaskbar", updated.MinimizeBehavior);
        Assert.Equal("gateway", updated.ModelAccessMode);
        Assert.Equal("127.0.0.1", updated.Host);
        Assert.Equal("de", updated.UiCulture);
        Assert.Equal(current.WorkspaceRoot, updated.WorkspaceRoot);
        Assert.True(updated.RequireApiKeyAuth);
        Assert.True(ApiSecurity.IsStrongBearerSecret(updated.ModelApiKey));
        Assert.Equal(updated.ModelApiKey, updated.ModelApiKeyBackup);
    }

    [Theory]
    [InlineData("{\"workspaceRoot\":\"D:/other\"}", "cannot be changed")]
    [InlineData("{\"modelApiKey\":\"replacement\"}", "cannot be changed")]
    [InlineData("{\"port\":0}", "Default model port")]
    [InlineData("{\"autoUnloadIdleMinutes\":10081}", "Auto-unload idle minutes")]
    public void ControlSettingsMutationRejectsUnsafeOrInvalidPatches(string json, string expectedError)
    {
        var current = AppSettings.CreateDefault(CreateTempRoot());
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ControlAppSettingsMutationService().Patch(current, JsonNode.Parse(json)!.AsObject(), []));

        Assert.Contains(expectedError, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlSettingsMutationAllowsLocalOnlyAuthenticationOptOutAndRejectsLanCombination()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            ModelApiKey = new string('a', 32),
            ModelApiKeyBackup = new string('a', 32)
        };
        var service = new ControlAppSettingsMutationService();

        var disabled = service.Patch(current, JsonNode.Parse("""{"requireApiKeyAuth":false}""")!.AsObject(), []);

        Assert.False(disabled.RequireApiKeyAuth);
        Assert.Equal("", disabled.ModelApiKey);
        Assert.Equal(current.ModelApiKey, disabled.ModelApiKeyBackup);
        var error = Assert.Throws<InvalidOperationException>(() => service.Patch(
            disabled,
            JsonNode.Parse("""{"modelAccessMode":"both"}""")!.AsObject(), []));
        Assert.Contains("Local only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlSettingsMutationRejectsGatewayPortUsedByRunningSession()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            AutoLoadGatewayEnabled = false,
            AutoLoadGatewayPort = 18080
        };
        var running = new LoadedModelSessionSnapshot(
            "session-1",
            "model-1",
            "Model",
            "runtime-1",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            current with { Port = 18081 },
            "runtime.log",
            DateTimeOffset.UtcNow,
            "marker",
            1,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true);
        var patch = JsonNode.Parse("""
            { "autoLoadGatewayEnabled": true, "autoLoadGatewayPort": 18081 }
            """)!.AsObject();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ControlAppSettingsMutationService().Patch(current, patch, [running]));

        Assert.Contains("already used by a running model", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlSettingsMutationRotatesToOneStrongMandatorySecret()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            RequireApiKeyAuth = false,
            ModelApiKey = "weak",
            ModelApiKeyBackup = "different"
        };

        var rotated = new ControlAppSettingsMutationService().RotateModelApiKey(current);

        Assert.True(rotated.RequireApiKeyAuth);
        Assert.True(ApiSecurity.IsStrongBearerSecret(rotated.ModelApiKey));
        Assert.Equal(rotated.ModelApiKey, rotated.ModelApiKeyBackup);
        Assert.NotEqual(current.ModelApiKey, rotated.ModelApiKey);
    }
}
