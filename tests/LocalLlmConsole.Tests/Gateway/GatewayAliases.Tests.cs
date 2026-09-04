using System.Net;
using System.Text;
using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayAliasesTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData("--alias dirk-qwen3.8-27b@iq3_xxs", "dirk-qwen3.8-27b@iq3_xxs")]
    [InlineData("-a 'owner/model name'", "owner/model name")]
    [InlineData("--alias=owner/model:2", "owner/model:2")]
    [InlineData("-a=Qwen", "Qwen")]
    [InlineData("--threads 4 --alias \" primary , secondary, primary ,,\" -a third", "primary|secondary|third")]
    [InlineData("--alias '' --alias second", "second")]
    [InlineData("--alias --threads 4", "")]
    [InlineData("--alias", "")]
    [InlineData("--alias-suffix foo --something-alias bar", "")]
    [InlineData("--jinja-kwargs '{\"text\":\"--alias fake\"}'", "")]
    [InlineData("--alias 'unterminated", "")]
    [InlineData("--alias valid --other 'unterminated", "")]
    [InlineData("--alias invalid\0", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ReadsRuntimeAliasesUsingLaunchArgumentSyntax(string? parameters, string expected)
        => Assert.Equal(expected, string.Join('|', RuntimeModelAliasService.ReadAliases(parameters)));

    [Fact]
    public void DuplicateAliasesAreNumberedAcrossTheWholeCatalogAndKeepLegacyRoutes()
    {
        var root = CreateTempRoot();
        var model = Model(root, "qwen-model");
        var source = Profiles(root, model).Select(profile => new ModelGatewayModelRoute(model, profile)).ToArray();
        var routes = ModelGatewayRouteId.EnsureUnique(source.Reverse().ToArray());

        Assert.Equal(
            ["qwen", "qwen:2", "qwen:3", "qwen:4"],
            source.Select(original => routes.Single(route => route.Profile.Id == original.Profile.Id).Id));
        foreach (var route in routes)
        {
            Assert.Same(route, ModelGatewayRequestResolver.ResolveModel(routes, route.Id));
            Assert.Same(route, ModelGatewayRequestResolver.ResolveModel(routes, route.Id.ToUpperInvariant()));
            Assert.Same(route, ModelGatewayRequestResolver.ResolveModel(routes, route.LegacyId));
            Assert.Same(route, ModelGatewayRequestResolver.ResolveModel(routes, route.Profile.Id));
        }
        var repeated = ModelGatewayRouteId.EnsureUnique(routes);
        Assert.Equal(routes.Select(route => route.Id), repeated.Select(route => route.Id));

        var edited = ModelGatewayRouteId.EnsureUnique(source.Select(route => route with
        {
            Profile = route.Profile with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(1) }
        }).ToArray());
        Assert.Equal(source.Select(route => route.Profile.Id), edited.Select(route => route.Profile.Id));
        Assert.Equal(["qwen", "qwen:2", "qwen:3", "qwen:4"], edited.Select(route => route.Id));
    }

    [Fact]
    public void LargeDuplicateAliasCatalogKeepsDeterministicUniqueSuffixes()
    {
        var root = CreateTempRoot();
        var model = Model(root, "qwen-model");
        var sourceProfile = Profiles(root, model)[0];
        var source = Enumerable.Range(0, 1000).Select(index => new ModelGatewayModelRoute(model, sourceProfile with
        {
            Id = $"profile-{index:D4}",
            Name = $"GPU {index:D4}",
            IsDefault = index == 0
        })).ToArray();
        var routes = ModelGatewayRouteId.EnsureUnique(source.Reverse().ToArray());
        var ordered = routes.OrderBy(route => route.Profile.Id, StringComparer.Ordinal).ToArray();
        Assert.Equal("qwen", ordered[0].Id);
        Assert.Equal(Enumerable.Range(2, 999).Select(index => $"qwen:{index}"), ordered.Skip(1).Select(route => route.Id));
        Assert.Equal(routes.Select(route => route.Id), ModelGatewayRouteId.EnsureUnique(routes).Select(route => route.Id));
    }

    [Fact]
    public void SuffixesReserveExplicitAliasesAndCannotHijackLegacyIds()
    {
        var root = CreateTempRoot();
        var model = Model(root, "qwen-model");
        var profiles = Profiles(root, model);
        var other = Model(root, "qwen:3");
        var legacyProfile = profiles[0] with
        {
            Id = "default:other",
            ModelId = other.Id,
            Settings = profiles[0].Settings with { CustomParameters = "" }
        };
        var source = new[]
        {
            new ModelGatewayModelRoute(model, profiles[0]),
            new ModelGatewayModelRoute(model, profiles[1] with { Settings = profiles[1].Settings with { CustomParameters = "--alias QWEN" } }),
            new ModelGatewayModelRoute(model, profiles[2] with { Settings = profiles[2].Settings with { CustomParameters = "--alias qwen:2" } }),
            new ModelGatewayModelRoute(model, profiles[3] with { Settings = profiles[3].Settings with { CustomParameters = "--alias qwen:3" } }),
            new ModelGatewayModelRoute(other, legacyProfile)
        };
        var routes = ModelGatewayRouteId.EnsureUnique(source);

        Assert.Equal(["qwen", "QWEN:4", "qwen:2", "qwen:3:2", "qwen:3"], routes.Select(route => route.Id));
        Assert.Equal(routes.Count, routes.Select(route => route.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(other.Id, ModelGatewayRequestResolver.ResolveModel(routes, "qwen:3")!.Model.Id);
    }

    [Fact]
    public void AddingAnAliasPreservesLegacyHashedProfileCollisionRoutes()
    {
        var root = CreateTempRoot();
        var model = Model(root, "qwen-model");
        var profile = Profiles(root, model)[1];
        var oldRoutes = ModelGatewayRouteId.EnsureUnique([
            new(model, profile with { Id = "Profile / A", Settings = profile.Settings with { CustomParameters = "" } }),
            new(model, profile with { Id = "profile-a", Settings = profile.Settings with { CustomParameters = "" } })
        ]);
        var newRoutes = ModelGatewayRouteId.EnsureUnique(oldRoutes.Select(route => new ModelGatewayModelRoute(
            route.Model, route.Profile with { Settings = profile.Settings })).ToArray());

        Assert.Equal(["qwen", "qwen:2"], newRoutes.Select(route => route.Id));
        foreach (var oldRoute in oldRoutes)
            Assert.Equal(oldRoute.Profile.Id, ModelGatewayRequestResolver.ResolveModel(newRoutes, oldRoute.Id)!.Profile.Id);
    }

    [Fact]
    public void GatewayUsesTheFirstNonemptyAliasWithoutSanitizingIt()
    {
        var root = CreateTempRoot();
        var model = Model(root, "internal-id");
        var profile = Profiles(root, model)[0] with
        {
            Settings = Profiles(root, model)[0].Settings with { CustomParameters = "--alias ' ,owner/Qwen-27B@iq3_xxs,secondary'" }
        };
        Assert.Equal("owner/Qwen-27B@iq3_xxs", new ModelGatewayModelRoute(model, profile).Id);
    }

    [Fact]
    public void ForwardingChangesOnlyTheTopLevelModelAndUsesTheActiveRuntimeAlias()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { CustomParameters = "--alias 'live-alias,secondary'" };
        var body = Encoding.UTF8.GetBytes("""
            {"model":"qwen:2","messages":[{"role":"user","content":"qwen:2"}],"stream":true,
             "tools":[{"type":"function","function":{"name":"model","parameters":{"model":"qwen:2"}}}],"seed":9223372036854775807}
            """);
        var result = ModelGatewayRequestResolver.BodyForRuntime(body, settings);
        using var before = JsonDocument.Parse(body);
        using var after = JsonDocument.Parse(result);
        Assert.Equal("live-alias", after.RootElement.GetProperty("model").GetString());
        foreach (var property in before.RootElement.EnumerateObject().Where(property => property.Name != "model"))
            Assert.True(JsonElement.DeepEquals(property.Value, after.RootElement.GetProperty(property.Name)));

        Assert.Same(body, ModelGatewayRequestResolver.BodyForRuntime(body, settings with { CustomParameters = "" }));
        var secondary = Encoding.UTF8.GetBytes("""{ "model": "secondary", "stream": false }""");
        Assert.Same(secondary, ModelGatewayRequestResolver.BodyForRuntime(secondary, settings));
    }

    [Fact]
    public async Task SavedAliasesSurviveReopeningAndGatewayRequestsSelectTheNumberedProfile()
    {
        var root = CreateTempRoot();
        var model = Model(root, "qwen-internal-hash");
        var profiles = Profiles(root, model);
        var database = Path.Combine(root, "state", "aliases.db");
        await using (var initialStore = new StateStore(database))
        {
            await initialStore.InitializeAsync();
            await initialStore.UpsertModelAsync(model);
            foreach (var profile in profiles.Reverse()) await initialStore.SaveNamedModelLaunchProfileAsync(profile);
        }
        await using var store = new StateStore(database);
        await store.InitializeAsync();
        var catalog = new ModelGatewayRouteCatalogApplicationService(store);
        var actions = new ModelGatewayRouteCatalogActions((_, _) => Task.FromException(new InvalidOperationException("Defaults already exist.")));
        var sessions = new List<LoadedModelSessionSnapshot>();
        var loadedProfiles = new List<string>();
        var appSettings = AppSettings.CreateDefault(root);
        var runtime = new ModelGatewayRuntimeController(new ModelGatewayRuntimeControllerActions(
            token => catalog.ListAsync(actions, token),
            _ => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>(sessions.ToArray()),
            (route, _, _) =>
            {
                loadedProfiles.Add(route.Profile.Id);
                var session = RuntimeMetricSession(root, route.Profile.Settings.ApplyTo(appSettings)) with
                {
                    SessionId = route.Profile.Id,
                    ModelId = model.Id,
                    ModelName = model.Name,
                    LaunchProfileId = route.Profile.Id,
                    LaunchProfileName = route.Profile.Name
                };
                sessions.Add(session);
                return Task.FromResult(session);
            }));
        using var handler = new AliasCheckingHandler();
        using var upstreamClient = new HttpClient(handler);
        var port = ReserveLoopbackPort();
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, "", false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime, new ModelGatewayUpstreamProxy(upstreamClient));
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        using var models = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        using var listing = JsonDocument.Parse(await models.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var ids = listing.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetString()).Order().ToArray();
        Assert.Equal(["qwen", "qwen:2", "qwen:3", "qwen:4"], ids);

        foreach (var id in new[] { "qwen:3", "qwen:2", "qwen", "qwen:4", "qwen:3" })
        {
            using var response = await client.PostAsync("v1/chat/completions", JsonContent(JsonSerializer.Serialize(new
            {
                model = id,
                messages = new[] { new { role = "user", content = "hello" } }
            })), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.Equal([profiles[2].Id, profiles[1].Id, profiles[0].Id, profiles[3].Id], loadedProfiles);
        Assert.Equal([8102, 8101, 8100, 8103, 8102], handler.Ports);
        Assert.All(sessions, session => Assert.Equal("--alias qwen", session.LaunchSettings.CustomParameters));
    }

    [Fact]
    public void ControlSelfIdentificationResolvesNumberedAliasesToTheCorrectProfile()
    {
        var root = CreateTempRoot();
        var model = Model(root, "qwen-internal");
        var profiles = Profiles(root, model);
        var session = RuntimeMetricSession(root, AppSettings.CreateDefault(root)) with
        {
            ModelId = model.Id,
            LaunchProfileId = profiles[2].Id
        };
        Assert.True(ControlSelfIdentification.MatchesModelHint(session, [model], profiles, "provider/qwen:3"));
        Assert.False(ControlSelfIdentification.MatchesModelHint(session, [model], profiles, "provider/qwen:2"));
    }

    private static ModelRecord Model(string root, string id)
        => new(id, "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);

    private static NamedModelLaunchProfile[] Profiles(string root, ModelRecord model)
        => Enumerable.Range(0, 4).Select(index => new NamedModelLaunchProfile(
            index == 0 ? $"default:{model.Id}" : $"profile:{model.Id}:{4 - index}", model.Id,
            index == 0 ? "Default" : $"CUDA {index - 1}",
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with { CustomParameters = "--alias qwen", Port = 8100 + index }),
            DateTimeOffset.UtcNow, IsDefault: index == 0)).ToArray();

    private sealed class AliasCheckingHandler : HttpMessageHandler
    {
        public List<int> Ports { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ports.Add(request.RequestUri!.Port);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(body.RootElement.GetProperty("model").GetString() == "qwen" ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = JsonContent("""{"model":"qwen","choices":[]}""")
            };
        }
    }
}
