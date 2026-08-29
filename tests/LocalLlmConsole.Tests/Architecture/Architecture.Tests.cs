using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ArchitectureTests : ManagerRegressionTestBase
{
    [Fact]
    public void TestProjectUsesFeatureFoldersAndCurrentNaming()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var testRoot = Path.Combine(repositoryRoot, "tests", "LocalLlmConsole.Tests");
        var expectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "App",
            "Architecture",
            "Benchmarks",
            "Control",
            "Environment",
            "Gateway",
            "Integration",
            "Models",
            "Overview",
            "Release",
            "Runtime",
            "Telemetry",
            "TestSupport",
            "Ui"
        };

        var rootSources = Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.TopDirectoryOnly).ToArray();
        var missingFolders = expectedFolders
            .Where(folder => !Directory.Exists(Path.Combine(testRoot, folder)))
            .ToArray();
        var unexpectedFolders = Directory.EnumerateDirectories(testRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(folder => folder is not null
                && folder is not "bin" and not "obj"
                && !expectedFolders.Contains(folder))
            .ToArray();
        var legacyNames = Directory.EnumerateFiles(testRoot, "ReleaseHardening*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(testRoot, path))
            .ToArray();
        var mismatchedTestClasses = Directory.EnumerateFiles(testRoot, "*.Tests.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(testRoot, path),
                ExpectedClass = $"{Path.GetFileName(path)[..^".Tests.cs".Length]}Tests",
                Source = File.ReadAllText(path)
            })
            .Where(file => !file.Source.Contains(
                $"public sealed class {file.ExpectedClass}",
                StringComparison.Ordinal))
            .Select(file => $"{file.Path} -> {file.ExpectedClass}")
            .ToArray();

        Assert.Empty(rootSources);
        Assert.Empty(missingFolders);
        Assert.Empty(unexpectedFolders);
        Assert.Empty(legacyNames);
        Assert.Empty(mismatchedTestClasses);
    }

    [Fact]
    public void WpfTestProjectUsesSurfaceFoldersAndSharedSupport()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var testRoot = Path.Combine(repositoryRoot, "tests", "LocalLlmConsole.UiTests");
        var expectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Lifetime",
            "Models",
            "Overview",
            "Runtime",
            "Settings",
            "Shell",
            "Surfaces",
            "TestSupport"
        };

        var unexpectedRootSources = Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "GlobalUsings.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var missingFolders = expectedFolders
            .Where(folder => !Directory.Exists(Path.Combine(testRoot, folder)))
            .ToArray();
        var unexpectedFolders = Directory.EnumerateDirectories(testRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(folder => folder is not null
                && folder is not "bin" and not "obj"
                && !expectedFolders.Contains(folder))
            .ToArray();
        var legacyNames = Directory.EnumerateFiles(testRoot, "WpfUiSmoke*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(testRoot, path))
            .ToArray();

        Assert.Empty(unexpectedRootSources);
        Assert.Empty(missingFolders);
        Assert.Empty(unexpectedFolders);
        Assert.Empty(legacyNames);
    }

    [Fact]
    public void MainWindowStoresFocusedLaunchAndOverviewControllers()
    {
        var fields = typeof(LocalLlmConsole.MainWindow)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.Contains(typeof(LocalLlmConsole.LaunchSettingsPageController), fields);
        Assert.Contains(typeof(LocalLlmConsole.OverviewSelectionController), fields);
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
            "Pages",
            "Shell"
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

        AssertServicePartials(appRoot, Path.Combine("Ui", "Pages", "Models"), "LaunchSettingsPanelFactory", 200,
            "LaunchSettingsPanelFactory.Controls.cs",
            "LaunchSettingsPanelFactory.Layout.cs",
            "LaunchSettingsPanelFactory.Pickers.cs",
            "LaunchSettingsPanelFactory.Sections.cs");
        Assert.NotNull(typeof(LocalLlmConsole.LaunchSettingsPanelRequest));
        Assert.NotNull(typeof(LocalLlmConsole.LaunchSettingsPanelControls));
    }

    [Fact]
    public void ModelGroupDialogFactoryStaysSplitByDialogResponsibility()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        AssertServicePartials(appRoot, Path.Combine("Ui", "Pages", "Models"), "ModelGroupDialogFactory", 300,
            "ModelGroupDialogFactory.Assignment.cs",
            "ModelGroupDialogFactory.Common.cs",
            "ModelGroupDialogFactory.Editor.cs",
            "ModelGroupDialogFactory.Manager.cs");
        var methods = typeof(LocalLlmConsole.ModelGroupDialogFactory).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.Contains(methods, method => method.Name == "ShowManager");
        Assert.Contains(methods, method => method.Name == "ShowAssignment");
        Assert.Contains(methods, method => method.Name == "ShowGroupEditor");
    }

    [Fact]
    public void MainWindowPartialsDoNotKeepEmptyPlaceholders()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var emptyPartials = EnumerateMainWindowSources(appRoot)
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
        var partials = EnumerateMainWindowSources(appRoot)
            .Select(path => new
            {
                Path = path,
                NonBlankLines = File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line))
            })
            .ToArray();
        var oversizedPartials = partials
            .Where(file => file.NonBlankLines > 300)
            .Select(file => $"{Path.GetFileName(file.Path)}: {file.NonBlankLines}")
            .ToArray();
        Assert.Empty(oversizedPartials);
        Assert.True(partials.Length <= 55, $"MainWindow uses {partials.Length} partial files; maximum is 55.");
        Assert.True(partials.Sum(file => file.NonBlankLines) <= 5200,
            $"MainWindow partials contain {partials.Sum(file => file.NonBlankLines)} non-blank lines; maximum is 5200.");
        var methodNames = typeof(LocalLlmConsole.MainWindow).GetMethods(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("GridFor", methodNames);
        Assert.DoesNotContain("AddMetric", methodNames);
        Assert.DoesNotContain("ApplyTheme", methodNames);
        Assert.DoesNotContain("ControlRuntimePackageAsync", methodNames);
        Assert.DoesNotContain("ControlRuntimeBuildAsync", methodNames);
        Assert.DoesNotContain("ControlCacheAsync", methodNames);
        Assert.DoesNotContain("ControlLogsAsync", methodNames);
        Assert.DoesNotContain("ControlLifetimeAsync", methodNames);
        Assert.DoesNotContain("ControlDownloadDeleteAsync", methodNames);
        Assert.DoesNotContain("ControlWindowsSetup", methodNames);
        Assert.DoesNotContain("ControlWslSetup", methodNames);
        Assert.DoesNotContain("ControlUpdateInstallAsync", methodNames);
    }

    [Fact]
    public void MainWindowShellUsesFeatureFolders()
    {
        var appRoot = Path.GetDirectoryName(
            FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs"))!;
        var shellRoot = Path.Combine(appRoot, "Ui", "Shell", "MainWindow");
        var expectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Benchmarks",
            "Core",
            "Environment",
            "Gateway",
            "Lifetime",
            "Models",
            "Navigation",
            "Overview",
            "Runtimes",
            "Settings"
        };
        var actualFolders = Directory.EnumerateDirectories(shellRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expectedFolders, actualFolders);
        Assert.Empty(Directory.EnumerateFiles(shellRoot, "*.cs", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void RuntimeServicesUseResponsibilityFolders()
    {
        var appRoot = Path.GetDirectoryName(
            FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs"))!;
        var runtimeRoot = Path.Combine(appRoot, "Services", "Runtimes");
        var expectedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Build",
            "Catalog",
            "Deletion",
            "Launch",
            "Packages",
            "Readiness",
            "Sessions",
            "Telemetry"
        };
        var actualFolders = Directory.EnumerateDirectories(runtimeRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expectedFolders, actualFolders);
        Assert.Empty(Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ControlRuntimeOperationsOwnRuntimeAutomationWorkflows()
    {
        var dependencies = typeof(ControlRuntimeOperationDependencies).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();
        var shellFields = typeof(LocalLlmConsole.MainWindow)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.Contains(typeof(RuntimePackageApplicationService), dependencies);
        Assert.Contains(typeof(RuntimeSourceApplicationService), dependencies);
        Assert.Contains(typeof(RuntimeBuildApplicationService), dependencies);
        Assert.Contains(typeof(RuntimeBuildJobApplicationService), dependencies);
        Assert.Contains(typeof(ControlRuntimeOperationApplicationService), shellFields);
        Assert.True(ControlRuntimeOperationApplicationService.CanHandle("runtime-package.install"));
        Assert.False(ControlRuntimeOperationApplicationService.CanHandle("unrelated.operation"));
    }

    [Fact]
    public void ControlNonRuntimeOperationsOwnApplicationAutomationWorkflows()
    {
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("cache.clear"));
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("logs.delete-all"));
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("lifetime.delete"));
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("downloads.delete"));
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("windows.setup"));
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("wsl.setup"));
        Assert.True(ControlNonRuntimeOperationApplicationService.CanHandle("updates.install"));
        Assert.False(ControlNonRuntimeOperationApplicationService.CanHandle("ui.navigate"));

        var logShellSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Navigation", "MainWindow.Logs.cs"));
        Assert.DoesNotContain("X509Certificate", logShellSource, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyMetadataAttribute", logShellSource, StringComparison.Ordinal);
        Assert.NotNull(typeof(BuildAndUpdateDiagnosticsService));
    }


    [Fact]
    public void ModelGatewayHostKeepsTransportResponsibilitiesSplit()
    {
        var fieldTypes = typeof(ModelGatewayService)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();

        Assert.Contains(typeof(ModelGatewayRequestAccessPolicy), fieldTypes);
        Assert.Contains(typeof(ModelGatewayUpstreamProxy), fieldTypes);
        Assert.DoesNotContain(typeof(HttpClient), fieldTypes);
        Assert.NotEmpty(typeof(ModelGatewayRequestResolver).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
        Assert.NotEmpty(typeof(ModelGatewayResponseWriter).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
    }

    [Fact]
    public void RuntimeDeletionPlanningAndExecutionStaySeparate()
    {
        var plannerMethods = typeof(RuntimeDeletionPlanner).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var executorMethods = typeof(RuntimeDeletionExecutorService).GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.Contains(plannerMethods, method => method.Name == "PlanRuntimeDeletionAsync");
        Assert.Contains(plannerMethods, method => method.Name == "PlanPackageDeletionAsync");
        Assert.Contains(plannerMethods, method => method.Name == "PlanBuildPresetDeletionAsync");
        Assert.DoesNotContain(plannerMethods, method => method.Name.StartsWith("Delete", StringComparison.Ordinal));
        Assert.Contains(executorMethods, method => method.Name == "DeleteRuntimeAsync");
        Assert.Contains(executorMethods, method => method.Name == "DeletePackageAsync");
        Assert.Contains(executorMethods, method => method.Name == "DeleteBuildPresetAsync");
    }

    [Fact]
    public void LargeFilesystemOperationsExposeAsynchronousServiceBoundaries()
    {
        (Type Type, string Method)[] boundaries =
        [
            (typeof(FileSystemSafetyService), nameof(FileSystemSafetyService.Sha256Async)),
            (typeof(HuggingFaceService), nameof(HuggingFaceService.StartDownloadAsync)),
            (typeof(RuntimePackageInstallService), nameof(RuntimePackageInstallService.InstallAsync)),
            (typeof(RuntimeBuildExecutionService), nameof(RuntimeBuildExecutionService.ExecuteAsync)),
            (typeof(RuntimeSourceRepositoryService), nameof(RuntimeSourceRepositoryService.DownloadAsync)),
            (typeof(RuntimeDeletionExecutorService), nameof(RuntimeDeletionExecutorService.DeleteRuntimeAsync)),
            (typeof(RuntimeCatalogApplicationService), nameof(RuntimeCatalogApplicationService.RefreshAsync)),
            (typeof(DiagnosticsBundleService), nameof(DiagnosticsBundleService.CreateAsync)),
            (typeof(AppUpdateService), nameof(AppUpdateService.StageInstallAsync))
        ];

        foreach (var boundary in boundaries)
        {
            var methods = boundary.Type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static);
            Assert.Contains(methods, method => method.Name == boundary.Method
                && typeof(Task).IsAssignableFrom(method.ReturnType));
        }
    }

    [Fact]
    public void LocalControlApiDelegatesSettingsMutationAndValidation()
    {
        var appAssembly = typeof(LocalLlmConsole.MainWindow).Assembly;
        var contextType = appAssembly.GetType("LocalLlmConsole.Services.ControlEndpointContext", throwOnError: true)!;
        var endpointBaseType = appAssembly.GetType("LocalLlmConsole.Services.ControlEndpointHandler", throwOnError: true)!;
        var contextProperty = contextType.GetProperty("SettingsMutations");
        var endpointBaseProperty = endpointBaseType.GetProperty(
            "_settingsMutations",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.Equal(typeof(ControlAppSettingsMutationService), contextProperty?.PropertyType);
        Assert.Equal(typeof(ControlAppSettingsMutationService), endpointBaseProperty?.PropertyType);
    }

    [Fact]
    public void ControlApiAndApplicationResourcesRemainDecomposedByResponsibility()
    {
        var controlRoot = Path.GetDirectoryName(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Control", "LocalControlApi.cs"))!;
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
        var endpointFields = typeof(LocalControlApi)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ToArray();
        Assert.Contains(endpointFields, type => type.FullName == "LocalLlmConsole.Services.ControlModelEndpoints");
        Assert.Contains(endpointFields, type => type.FullName == "LocalLlmConsole.Services.ControlSessionEndpoints");
        Assert.Contains(endpointFields, type => type.FullName == "LocalLlmConsole.Services.ControlOperationEndpoints");
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
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", coreProject, StringComparison.Ordinal);
        Assert.DoesNotContain("-windows", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<UseWPF>", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<UseWindowsForms>", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<ProjectReference", coreProject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LocalLlmConsole.Core.csproj", appProject, StringComparison.Ordinal);

        var appModelsRoot = Path.Combine(repositoryRoot, "src", "LocalLlmConsole.App", "Models");
        var appModelSources = Directory.Exists(appModelsRoot)
            ? Directory.EnumerateFiles(appModelsRoot, "*.cs", SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();
        Assert.Empty(appModelSources);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(coreRoot, "Models"), "*.cs", SearchOption.TopDirectoryOnly));
    }

    private static IEnumerable<string> EnumerateMainWindowSources(string appRoot)
    {
        var shellRoot = Path.Combine(appRoot, "Ui", "Shell", "MainWindow");
        return new[] { Path.Combine(appRoot, "MainWindow.xaml.cs") }
            .Concat(Directory.EnumerateFiles(shellRoot, "MainWindow*.cs", SearchOption.AllDirectories));
    }
}
