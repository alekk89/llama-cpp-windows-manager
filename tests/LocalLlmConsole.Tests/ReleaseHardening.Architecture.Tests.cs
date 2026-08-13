namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{


    [Fact]
    public void UiImplementationFilesStayInCommonOrPageModules()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var uiRoot = Path.Combine(appRoot, "Ui");
        var expectedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Common",
            "Pages"
        };

        var rootFiles = Directory
            .EnumerateFiles(uiRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToArray();
        var missingModules = expectedModules
            .Where(module => !Directory.Exists(Path.Combine(uiRoot, module)))
            .ToArray();
        var unexpectedModules = Directory
            .EnumerateDirectories(uiRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(module => module is not null && !expectedModules.Contains(module))
            .ToArray();

        Assert.Empty(rootFiles);
        Assert.Empty(missingModules);
        Assert.Empty(unexpectedModules);
    }


    [Fact]
    public void ServiceAndUiModuleFileNamesStayUnambiguous()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var checkedRoots = new[]
        {
            Path.Combine(appRoot, "Services"),
            Path.Combine(appRoot, "Ui")
        };

        var duplicates = checkedRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(path => Path.GetRelativePath(appRoot, path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))}")
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void LaunchSettingsPanelFactoryStaysSplitByPanelResponsibility()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var shell = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPanelFactory.cs"));

        AssertServicePartials(appRoot, Path.Combine("Ui", "Pages", "Models"), "LaunchSettingsPanelFactory", 200,
            "LaunchSettingsPanelFactory.Controls.cs",
            "LaunchSettingsPanelFactory.Layout.cs",
            "LaunchSettingsPanelFactory.Pickers.cs",
            "LaunchSettingsPanelFactory.Sections.cs");
        Assert.Contains("public sealed record LaunchSettingsPanelRequest", shell, StringComparison.Ordinal);
        Assert.Contains("public sealed class LaunchSettingsPanelControls", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private static LaunchSettingsFormControls AddLaunchSections", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Grid VisionProjectorPicker", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class LaunchSettingsPanelBuilder", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowPartialsDoNotKeepEmptyPlaceholders()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var emptyPartials = Directory
            .EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line)) <= 5)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Empty(emptyPartials);
    }


    [Fact]
    public void ModelGatewayHostKeepsTransportResponsibilitiesSplit()
    {
        var host = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayService.cs"));
        var accessPolicy = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayRequestAccessPolicy.cs"));
        var resolver = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayRequestResolver.cs"));
        var proxy = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayUpstreamProxy.cs"));
        var responseWriter = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayResponseWriter.cs"));

        Assert.Contains("ModelGatewayRequestAccessPolicy", host, StringComparison.Ordinal);
        Assert.Contains("ModelGatewayRequestResolver.ExtractRequestedModel", host, StringComparison.Ordinal);
        Assert.Contains("ModelGatewayRequestResolver.ResolveModel", host, StringComparison.Ordinal);
        Assert.Contains("ModelGatewayResponseWriter.WriteJsonAsync", host, StringComparison.Ordinal);
        Assert.Contains("_upstreamProxy.ForwardAsync", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ProxiedPostPaths", host, StringComparison.Ordinal);
        Assert.DoesNotContain("HopByHopHeaders", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpClient", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BearerTokenMatches", host, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer.Serialize", host, StringComparison.Ordinal);

        Assert.Contains("BearerTokenMatches", accessPolicy, StringComparison.Ordinal);
        Assert.Contains("Access-Control-Allow-Origin", accessPolicy, StringComparison.Ordinal);
        Assert.Contains("ProxiedPostPaths", resolver, StringComparison.Ordinal);
        Assert.Contains("ExtractRequestedModel", resolver, StringComparison.Ordinal);
        Assert.Contains("ResolveModel", resolver, StringComparison.Ordinal);
        Assert.Contains("HopByHopHeaders", proxy, StringComparison.Ordinal);
        Assert.Contains("SendAsync", proxy, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Serialize", responseWriter, StringComparison.Ordinal);
        Assert.Contains("GatewayClientLoadError", responseWriter, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeDeletionPlanningAndExecutionStaySeparate()
    {
        var planner = ReadServicePartialSources("RuntimeDeletionPlanner");
        var executor = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDeletionExecutorService.cs"));
        var buildDeletionApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildDeletionApplicationService.cs"));
        var packageApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimePackageApplicationService.cs"));

        Assert.Contains("PlanRuntimeDeletionAsync", planner, StringComparison.Ordinal);
        Assert.Contains("PlanPackageDeletionAsync", planner, StringComparison.Ordinal);
        Assert.Contains("PlanBuildPresetDeletionAsync", planner, StringComparison.Ordinal);
        Assert.Contains("ReplacementRuntimeAsync", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task DeleteRuntimeAsync", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFileService.DeleteRuntimeFiles", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeFileService.DeleteSafeRuntimeFolder", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveModelLaunchSettingsAsync", planner, StringComparison.Ordinal);

        Assert.Contains("public async Task DeleteRuntimeAsync", executor, StringComparison.Ordinal);
        Assert.Contains("public async Task DeletePackageAsync", executor, StringComparison.Ordinal);
        Assert.Contains("public async Task DeleteBuildPresetAsync", executor, StringComparison.Ordinal);
        Assert.Contains("DeleteRuntimeFiles", executor, StringComparison.Ordinal);
        Assert.Contains("DeleteSafeRuntimeFolder", executor, StringComparison.Ordinal);
        Assert.Contains("SaveNamedModelLaunchProfileAsync", executor, StringComparison.Ordinal);

        Assert.Contains("_deletionPlanner.PlanRuntimeDeletionAsync", buildDeletionApplication, StringComparison.Ordinal);
        Assert.Contains("_deletionExecutor.DeleteRuntimeAsync", buildDeletionApplication, StringComparison.Ordinal);
        Assert.Contains("_deletionPlanner.PlanPackageDeletionAsync", packageApplication, StringComparison.Ordinal);
        Assert.Contains("_deletionExecutor.DeletePackageAsync", packageApplication, StringComparison.Ordinal);
    }

    [Fact]
    public void AppUpdateServiceKeepsParsingAndVerificationHelpersSeparate()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AppUpdateService.cs"));
        var parser = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AppUpdateReleaseParser.cs"));
        var verifier = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AppUpdateAssetVerifier.cs"));

        Assert.Contains("AppUpdateReleaseParser.ParseLatestRelease", service, StringComparison.Ordinal);
        Assert.Contains("AppUpdateAssetVerifier.VerifyChecksumAssetAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectPortableAsset", service, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizeVersion", service, StringComparison.Ordinal);
        Assert.DoesNotContain("public static string ExtractSha256", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ComputeSha256", service, StringComparison.Ordinal);

        Assert.Contains("ParseLatestRelease", parser, StringComparison.Ordinal);
        Assert.Contains("SelectPortableAsset", parser, StringComparison.Ordinal);
        Assert.Contains("NormalizeVersion", parser, StringComparison.Ordinal);
        Assert.Contains("VerifyChecksumAssetAsync", verifier, StringComparison.Ordinal);
        Assert.Contains("ExtractSha256", verifier, StringComparison.Ordinal);
        Assert.Contains("ComputeSha256", verifier, StringComparison.Ordinal);
    }
}
