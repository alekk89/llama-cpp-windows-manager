using System.Reflection;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class SemanticArchitectureTests : ManagerRegressionTestBase
{
    [Fact]
    public void ViewModelsHaveNoCompiledReferencesToIoOrPlatformInfrastructure()
    {
        var viewModels = typeof(OverviewPageViewModel).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("LocalLlmConsole.ViewModels", StringComparison.Ordinal) == true)
            .ToArray();

        var violations = viewModels.SelectMany(ForbiddenSymbolReferences).Order(StringComparer.Ordinal).ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void SemanticBoundaryDetectsAliasedAndFullyQualifiedIoCalls()
    {
        var violations = ForbiddenSymbolReferences(typeof(SemanticIoCanary));

        Assert.Contains(violations, violation => violation.Contains("System.IO.File.Exists", StringComparison.Ordinal));
    }

    [Fact]
    public void SemanticBoundaryDetectsConcreteStorageAndProcessServices()
    {
        var violations = ForbiddenSymbolReferences(typeof(SemanticInfrastructureCanary));

        Assert.Contains(violations, violation => violation.Contains(typeof(StateStore).FullName!, StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains(typeof(IProcessRunner).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void CoreAssemblyHasNoWindowsOrApplicationInfrastructureReferences()
    {
        var references = typeof(AppSettings).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "")
            .ToArray();
        var forbidden = references.Where(name =>
            name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PresentationCore", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.Data.Sqlite", StringComparison.OrdinalIgnoreCase)
            || name.Equals("LocalLlmConsole.App", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void ShellAndClipboardAdaptersDelegatePrivilegedOperations()
    {
        var shellReferences = ReferencedMembers(typeof(ShellIntegrationService)).ToArray();
        var clipboardReferences = ReferencedMembers(typeof(ClipboardService)).ToArray();

        Assert.DoesNotContain(shellReferences, member =>
            member.DeclaringType == typeof(System.Diagnostics.Process)
            && member.Name == nameof(System.Diagnostics.Process.Start));
        Assert.DoesNotContain(clipboardReferences, member =>
            member.DeclaringType?.FullName == "System.Windows.Clipboard"
            && member.Name == "SetText");
    }

    private static string[] ForbiddenSymbolReferences(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
        var violations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            foreach (var member in ReferencedMembers(method))
            {
                var referencedType = member as Type ?? member.DeclaringType;
                if (referencedType is null || !IsForbiddenViewModelDependency(referencedType)) continue;
                violations.Add($"{type.FullName}.{method.Name} -> {referencedType.FullName}.{member.Name}");
            }
        }

        foreach (var memberType in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                     .Select(field => field.FieldType)
                     .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(property => property.PropertyType)))
        {
            if (IsForbiddenViewModelDependency(memberType))
                violations.Add($"{type.FullName} stores {memberType.FullName}");
        }
        return violations.ToArray();
    }

    private static bool IsForbiddenViewModelDependency(Type type)
    {
        var candidate = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var name = candidate.FullName ?? "";
        return name is "System.IO.File" or "System.IO.Directory" or "System.IO.FileInfo" or "System.IO.DirectoryInfo"
            or "System.Diagnostics.Process" or "System.Net.Http.HttpClient"
            || candidate == typeof(StateStore)
            || candidate == typeof(IProcessRunner)
            || candidate == typeof(TrackedProcessRunner)
            || candidate == typeof(LlamaProcessSupervisor)
            || name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal)
            || name.StartsWith("System.Windows", StringComparison.Ordinal)
            || name.StartsWith("LocalLlmConsole.Services.Infrastructure", StringComparison.Ordinal);
    }

    private sealed class SemanticIoCanary
    {
        public static bool Exists(string path) => System.IO.File.Exists(path);
    }

    private sealed class SemanticInfrastructureCanary
    {
#pragma warning disable CS0169
        private StateStore? _store;
        private IProcessRunner? _processRunner;
#pragma warning restore CS0169
    }
}
