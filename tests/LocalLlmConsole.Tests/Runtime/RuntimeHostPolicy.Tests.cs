using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class RuntimeHostPolicyTests
{
    [Theory]
    [InlineData("local", "10.10.10.21", "127.0.0.1", "127.0.0.1")]
    [InlineData("gateway", "10.10.10.21", "127.0.0.1", "127.0.0.1")]
    [InlineData("models", "10.10.10.21", "10.10.10.21", "10.10.10.21")]
    [InlineData("both", " 10.10.10.21 ", "10.10.10.21", "10.10.10.21")]
    [InlineData("models", "", "0.0.0.0", "127.0.0.1")]
    [InlineData("local", "", "127.0.0.1", "127.0.0.1")]
    [InlineData("models", "0.0.0.0", "0.0.0.0", "127.0.0.1")]
    [InlineData("both", "::", "::", "[::1]")]
    [InlineData("both", "2001:db8::21", "2001:db8::21", "[2001:db8::21]")]
    [InlineData("models", "127.0.0.1", "127.0.0.1", "127.0.0.1")]
    public void PreviewAndEndpointChecksUseTheEffectiveListener(
        string accessMode, string host, string listener, string clientHost)
    {
        var settings = AppSettings.CreateDefault("workspace") with
        {
            ModelAccessMode = accessMode,
            Host = host,
            Port = 8080
        };

        Assert.Equal(listener, ModelAccessPolicy.RuntimeHost(accessMode, host));
        var tokens = CustomLaunchParameterParser.Parse(RuntimeLaunchRequestFactory.Preview(settings, null)).ToList();
        Assert.Equal(listener, tokens[tokens.IndexOf("--host") + 1]);
        Assert.Equal($"http://{clientHost}:8080", RuntimeEndpointService.LocalServerBaseUrl(settings));
        Assert.StartsWith($"http://{clientHost}:8080/v1", RuntimeEndpointService.EndpointDisplay(settings), StringComparison.Ordinal);
        if (accessMode is "local" or "gateway")
            Assert.Equal("http://127.0.0.1:8080", RuntimeEndpointService.LanServerBaseUrl(settings));
    }

    [Theory]
    [InlineData("local", "127.0.0.1")]
    [InlineData("gateway", "127.0.0.1")]
    [InlineData("models", "0.0.0.0")]
    [InlineData("both", "0.0.0.0")]
    public void LegacyProfileWithoutHostInheritsApplicationDefault(string accessMode, string expectedHost)
    {
        var defaults = AppSettings.CreateDefault("workspace");
        var json = JsonSerializer.SerializeToNode(ModelLaunchSettings.FromAppSettings(defaults))!.AsObject();
        json.Remove("Host");
        var profile = JsonSerializer.Deserialize<ModelLaunchSettings>(json.ToJsonString())!;
        var appSettings = defaults with { ModelAccessMode = accessMode, Host = expectedHost };

        Assert.Equal("", profile.Host);
        var effective = profile.ApplyTo(appSettings);
        Assert.Equal(expectedHost, effective.Host);
        Assert.Contains($"--host {expectedHost}", RuntimeLaunchRequestFactory.Preview(effective, null), StringComparison.Ordinal);
        var roundTrip = JsonSerializer.Deserialize<ModelLaunchSettings>(JsonSerializer.Serialize(profile))!;
        Assert.Equal(expectedHost, roundTrip.ApplyTo(appSettings).Host);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.10.10.21")]
    public void ExplicitSavedHostIsPreservedWhenApplicationDefaultChanges(string host)
    {
        var defaults = AppSettings.CreateDefault("workspace");
        var profile = ModelLaunchSettings.FromAppSettings(defaults with { Host = host });
        var restored = JsonSerializer.Deserialize<ModelLaunchSettings>(JsonSerializer.Serialize(profile))!;

        Assert.Equal(host, restored.ApplyTo(defaults with { ModelAccessMode = "both", Host = "0.0.0.0" }).Host);
    }
}
