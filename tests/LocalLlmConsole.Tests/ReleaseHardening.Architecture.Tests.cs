namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void ViewModelsDoNotOwnIoBoundaries()
    {
        var viewModelRoot = Path.Combine(
            Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!,
            "src",
            "LocalLlmConsole.App",
            "ViewModels");
        var sources = Directory.GetFiles(viewModelRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .ToArray();

        foreach (var source in sources)
        {
            Assert.DoesNotContain("File.Exists(", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Open", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Read", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Write", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.Exists(", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.Enumerate", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.Get", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.Create", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Process.Start(", source.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("new HttpClient", source.Source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RuntimeSessionStopAwaitsVerifiedProcessTermination()
    {
        var sessions = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "LoadedModelSessionManager.cs"));
        var supervisor = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "LlamaProcessSupervisor.Lifecycle.cs"));
        var nativeStop = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "NativeRuntimeStopService.cs"));
        var wslStop = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Environment", "WslRuntimeStopService.cs"));

        Assert.Contains("await session.Supervisor.StopVerifiedAsync(cancellationToken)", sessions, StringComparison.Ordinal);
        Assert.Contains("public async Task<StopVerification> StopVerifiedAsync", supervisor, StringComparison.Ordinal);
        Assert.Contains("WaitForExitAsync", nativeStop, StringComparison.Ordinal);
        Assert.DoesNotContain(".WaitForExit(", nativeStop, StringComparison.Ordinal);
        Assert.DoesNotContain("public void Stop(", wslStop, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowLaunchAndOverviewFilesAreThinControllerAdapters()
    {
        var launchAdapter = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "MainWindow.LaunchSettings.cs"));
        var overviewAdapter = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "MainWindow.OverviewSelection.cs"));

        Assert.Contains("_launchSettingsController", launchAdapter, StringComparison.Ordinal);
        Assert.Contains("_overviewSelection", overviewAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelLaunchSettingsWorkflowService", launchAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("EndpointInspectionDialogFactory", overviewAdapter, StringComparison.Ordinal);
        Assert.True(File.ReadLines(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "MainWindow.LaunchSettings.cs")).Count() < 80);
        Assert.True(File.ReadLines(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "MainWindow.OverviewSelection.cs")).Count() < 80);
    }



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
    public void ModelGroupDialogFactoryStaysSplitByDialogResponsibility()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var shell = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelGroupDialogFactory.cs"));

        AssertServicePartials(appRoot, Path.Combine("Ui", "Pages", "Models"), "ModelGroupDialogFactory", 300,
            "ModelGroupDialogFactory.Assignment.cs",
            "ModelGroupDialogFactory.Common.cs",
            "ModelGroupDialogFactory.Editor.cs",
            "ModelGroupDialogFactory.Manager.cs");
        Assert.Contains("public static partial class ModelGroupDialogFactory", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("public static ModelGroupManagerResult? ShowManager", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("public static ModelGroupAssignmentResult? ShowAssignment", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private static ModelGroupEditorResult? ShowGroupEditor", shell, StringComparison.Ordinal);
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
    public void MainWindowPartialsRemainBoundedShellAdapters()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var oversizedPartials = Directory
            .EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new
            {
                Path = path,
                NonBlankLines = File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line))
            })
            .Where(file => file.NonBlankLines > 300)
            .Select(file => $"{Path.GetFileName(file.Path)}: {file.NonBlankLines}")
            .ToArray();
        var shell = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.Empty(oversizedPartials);
        Assert.DoesNotContain("private static DataGrid GridFor", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Grid AddMetric", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("private static void ApplyTheme", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlRuntimePackageAsync", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlRuntimeBuildAsync", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlRuntimeOperationsOwnRuntimeAutomationWorkflows()
    {
        var service = File.ReadAllText(FindRepositoryFile(
            "src",
            "LocalLlmConsole.App",
            "Services",
            "Control",
            "ControlRuntimeOperationApplicationService.cs"));
        var shell = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ControlOperations.cs"));

        Assert.Contains("RuntimePackageApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("RuntimeSourceApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("RuntimeBuildApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("RuntimeBuildJobApplicationService", service, StringComparison.Ordinal);
        Assert.Contains("ControlRuntimeOperationApplicationService.CanHandle", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveRuntimePackagePreset", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveRuntimeBuildPreset", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveRuntimeSource", shell, StringComparison.Ordinal);
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

    [Fact]
    public void LargeFilesystemWorkStaysOutsideSynchronousUiContinuations()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var services = Path.Combine(repositoryRoot, "src", "LocalLlmConsole.App", "Services");
        var fileSafety = File.ReadAllText(Path.Combine(services, "Infrastructure", "FileSystemSafetyService.cs"));
        var downloads = File.ReadAllText(Path.Combine(services, "HuggingFace", "HuggingFaceService.Downloads.cs"));
        var install = File.ReadAllText(Path.Combine(services, "Runtimes", "RuntimePackageInstallService.cs"));
        var buildExecution = File.ReadAllText(Path.Combine(services, "Runtimes", "RuntimeBuildExecutionService.cs"));
        var sourceRepository = File.ReadAllText(Path.Combine(services, "Runtimes", "RuntimeSourceRepositoryService.cs"));
        var deletion = File.ReadAllText(Path.Combine(services, "Runtimes", "RuntimeDeletionExecutorService.cs"));
        var catalog = File.ReadAllText(Path.Combine(services, "Runtimes", "RuntimeCatalogApplicationService.cs"));
        var diagnostics = File.ReadAllText(Path.Combine(services, "App", "DiagnosticsBundleService.cs"));
        var update = File.ReadAllText(Path.Combine(services, "App", "AppUpdateService.cs"));

        Assert.Contains("SHA256.HashDataAsync", fileSafety, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => RunDownloadWorkerAsync", downloads, StringComparison.Ordinal);
        Assert.Contains("RuntimePackageInstallFileService.ExtractArchive", install, StringComparison.Ordinal);
        Assert.Contains("await Task.Run", install, StringComparison.Ordinal);
        Assert.Contains("DeleteSafeRuntimeFolderAsync", buildExecution, StringComparison.Ordinal);
        Assert.Contains("DeleteSafeRuntimeFolderAsync", sourceRepository, StringComparison.Ordinal);
        Assert.Contains("DeleteRuntimeFilesAsync", deletion, StringComparison.Ordinal);
        Assert.Contains("var rows = await Task.Run", catalog, StringComparison.Ordinal);
        Assert.Contains("return await Task.Run", diagnostics, StringComparison.Ordinal);
        Assert.Contains("var stagedFiles = await Task.Run", update, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalControlApiDelegatesSettingsMutationAndValidation()
    {
        var api = ReadLocalControlApiSources();
        var mutations = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Control", "ControlAppSettingsMutationService.cs"));

        Assert.Contains("_settingsMutations.Patch", api, StringComparison.Ordinal);
        Assert.Contains("_settingsMutations.RotateModelApiKey", api, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ValidateAppSettings", api, StringComparison.Ordinal);
        Assert.DoesNotContain("private static AppSettings NormalizeAppSettings", api, StringComparison.Ordinal);
        Assert.Contains("ControlJsonPatch.Apply", mutations, StringComparison.Ordinal);
        Assert.Contains("ModelAccessPolicy.AllowsUnauthenticatedAccess", mutations, StringComparison.Ordinal);
        Assert.Contains("Gateway port", mutations, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlApiAndApplicationResourcesRemainDecomposedByResponsibility()
    {
        var controlRoot = Path.GetDirectoryName(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Control", "LocalControlApi.cs"))!;
        var controlHost = File.ReadAllText(Path.Combine(controlRoot, "LocalControlApi.cs"));
        var appXaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml"));

        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlModelEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlProfileEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlModelGroupEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlRuntimeEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlSessionEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlSettingsEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlLogEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlJobEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlOperationEndpoints.cs")));
        Assert.True(File.Exists(Path.Combine(controlRoot, "ControlEndpointHandler.cs")));
        Assert.True(File.ReadLines(Path.Combine(controlRoot, "LocalControlApi.cs")).Count() < 250);
        Assert.DoesNotContain("partial class LocalControlApi", controlHost, StringComparison.Ordinal);
        Assert.Contains("_models.ModelsAsync", controlHost, StringComparison.Ordinal);
        Assert.Contains("_sessions.SessionsAsync", controlHost, StringComparison.Ordinal);
        Assert.Contains("_operations.HandleAsync", controlHost, StringComparison.Ordinal);
        Assert.Contains("Themes/Palette.xaml", appXaml, StringComparison.Ordinal);
        Assert.Contains("Themes/Foundation.xaml", appXaml, StringComparison.Ordinal);
        Assert.Contains("Themes/Inputs.xaml", appXaml, StringComparison.Ordinal);
        Assert.Contains("Themes/DataAndSurfaces.xaml", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreProjectOwnsPortableModelsAndApplicationPolicy()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var coreRoot = Path.Combine(repositoryRoot, "src", "LocalLlmConsole.Core");
        var coreProject = File.ReadAllText(Path.Combine(coreRoot, "LocalLlmConsole.Core.csproj"));
        var appProject = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var forbiddenReferences = new[]
        {
            "System.Windows",
            "System.Windows.Forms",
            "Microsoft.Win32",
            "System.Management",
            "Microsoft.Data.Sqlite",
            "LocalLlmConsole.Localization"
        };

        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("-windows", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<UseWPF>", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<UseWindowsForms>", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<ProjectReference", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LocalLlmConsole.Core.csproj", appProject, StringComparison.Ordinal);

        var portabilityViolations = Directory
            .EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .SelectMany(path => forbiddenReferences
                .Where(reference => File.ReadAllText(path).Contains(reference, StringComparison.Ordinal))
                .Select(reference => $"{Path.GetRelativePath(coreRoot, path)}: {reference}"))
            .ToArray();
        Assert.Empty(portabilityViolations);

        var appModelsRoot = Path.Combine(repositoryRoot, "src", "LocalLlmConsole.App", "Models");
        var appModelSources = Directory.Exists(appModelsRoot)
            ? Directory.EnumerateFiles(appModelsRoot, "*.cs", SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();
        Assert.Empty(appModelSources);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(coreRoot, "Models"), "*.cs", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ProductionAndTestSourceFilesRemainReviewableInSize()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var oversizedProduction = OversizedSources(Path.Combine(repositoryRoot, "src"), 425);
        var oversizedTests = OversizedSources(Path.Combine(repositoryRoot, "tests"), 675);

        Assert.Empty(oversizedProduction);
        Assert.Empty(oversizedTests);
    }

    private static string[] OversizedSources(string root, int maximumLines)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .Select(path => new
            {
                Path = Path.GetRelativePath(root, path),
                Lines = File.ReadLines(path).Count()
            })
            .Where(file => file.Lines > maximumLines)
            .Select(file => $"{file.Path}: {file.Lines} lines (maximum {maximumLines})")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
