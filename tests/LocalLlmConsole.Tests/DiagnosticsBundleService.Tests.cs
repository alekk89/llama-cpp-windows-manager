using System.IO.Compression;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class DiagnosticsBundleServiceTests
{
    [Fact]
    public async Task CreatesReviewableBundleWithoutSecretsOrFullPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "llwm-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "logs");
        var output = Path.Combine(root, "diagnostics");
        Directory.CreateDirectory(logs);
        try
        {
            var secret = "diagnostics-secret-key";
            var settings = AppSettings.CreateDefault(root) with
            {
                ModelApiKey = secret,
                CustomParameters = "--private-value should-not-appear"
            };
            var modelPath = Path.Combine(settings.ModelsRoot, "example.gguf");
            var logPath = Path.Combine(logs, "app-private.log");
            await File.WriteAllTextAsync(
                logPath,
                $"workspace={root}\nmodel={modelPath}\n--api-key {secret}\nAuthorization: Bearer control-token-value\ncontrolToken=another-private-token",
                TestContext.Current.CancellationToken);

            var result = await DiagnosticsBundleService.CreateAsync(
                new DiagnosticsBundleRequest(
                    output,
                    logs,
                    "v-test",
                    settings,
                    [new ModelRecord("model-1", "Example", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow)],
                    [],
                    [],
                    [],
                    new WslEnvironmentReport(false, false, "Not installed", "private details", "", "", "", []),
                    $"GPU probe from {root}",
                    "CPU probe"),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(result.ArchivePath));
            Assert.Equal(1, result.IncludedLogCount);
            using var archive = ZipFile.OpenRead(result.ArchivePath);
            var names = archive.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Contains("README.txt", names);
            Assert.Contains("summary.json", names);
            Assert.Single(names, name => name.StartsWith("logs/", StringComparison.Ordinal));

            var combined = string.Join(
                "\n",
                archive.Entries.Select(ReadEntry));
            Assert.DoesNotContain(secret, combined, StringComparison.Ordinal);
            Assert.DoesNotContain(root, combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("another-private-token", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("should-not-appear", combined, StringComparison.Ordinal);
            Assert.Contains("[redacted]", combined, StringComparison.Ordinal);
            Assert.Contains("Review the archive before sharing it", combined, StringComparison.Ordinal);
            Assert.Contains("example.gguf", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("private details", combined, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
