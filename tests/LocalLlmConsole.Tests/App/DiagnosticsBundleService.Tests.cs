using System.IO.Compression;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class DiagnosticsBundleServiceTests
{
    [Fact]
    public void ProbeHistoryRetainsOnlyTheNewestBoundedRecords()
    {
        var history = new DiagnosticProbeHistory();
        for (var index = 0; index < DiagnosticProbeHistory.Capacity + 5; index++)
        {
            history.Add(new DiagnosticProbeRecord(
                $"probe-{index}", "1", "test", true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                "success", "success", "parsed", "", ""));
        }

        var records = history.Snapshot();
        Assert.Equal(DiagnosticProbeHistory.Capacity, records.Count);
        Assert.Equal("probe-5", records[0].Name);
        Assert.Equal($"probe-{DiagnosticProbeHistory.Capacity + 4}", records[^1].Name);
    }

    [Fact]
    public async Task ProbeHistorySupportsConcurrentWritersWithoutGrowing()
    {
        var history = new DiagnosticProbeHistory();
        await Task.WhenAll(Enumerable.Range(0, 128).Select(index => Task.Run(() =>
            history.Add(new DiagnosticProbeRecord(
                $"probe-{index}", "1", "test", true,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                "success", "success", "parsed", "", "")))));

        Assert.Equal(DiagnosticProbeHistory.Capacity, history.Snapshot().Count);
    }

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
            var windowsUser = "diagnostics-windows-canary";
            var wslUser = "diagnostics-wsl-canary";
            var promptCanary = "diagnostics-prompt-canary";
            var urlPassword = "diagnostics-url-password";
            var uncShare = @"\\private-host\private-share\private-file.txt";
            var settings = AppSettings.CreateDefault(root) with
            {
                ModelApiKey = secret,
                CustomParameters = "--private-value should-not-appear"
            };
            var modelPath = Path.Combine(settings.ModelsRoot, "example.gguf");
            var logPath = Path.Combine(logs, "app-private.log");
            await File.WriteAllTextAsync(
                logPath,
                $"workspace={root}\nmodel={modelPath}\n--api-key {secret}\nAuthorization: Bearer control-token-value\n"
                + $"controlToken=another-private-token\nC:\\Users\\{windowsUser}\\secret.txt\n/home/{wslUser}/secret.txt\n"
                + $"{uncShare}\nhttps://private-user:{urlPassword}@example.invalid/path\n\"prompt\":\"{promptCanary}\"",
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
                    "CPU probe",
                    [new DiagnosticProbeRecord(
                        "nvidia-smi", "1", "NVIDIA", true,
                        DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow,
                        "failure", "tool-error", "no-adapter",
                        $"probe {root} https://user:{urlPassword}@example.invalid", uncShare,
                        new Dictionary<string, bool> { ["gpu"] = false }, "test")],
                    [new SessionLifecycleDiagnosticEvent(
                        "session-1", "model-1", "runtime-1", "running", "failed", DateTimeOffset.UtcNow,
                        "supervisor", "LLWM-SESSION-EXIT", "unexpected", "ready", $"failed at /home/{wslUser}/state")],
                    new BuildAndUpdateDiagnostics(
                        new string('a', 40), "stable", "portable", "valid", "valid", "no-update")),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(result.ArchivePath));
            Assert.Equal(1, result.IncludedLogCount);
            using var archive = ZipFile.OpenRead(result.ArchivePath);
            var names = archive.Entries.Select(entry => entry.FullName).ToArray();
            Assert.Contains("README.txt", names);
            Assert.Contains("summary.json", names);
            Assert.Contains("probes.json", names);
            Assert.Contains("session-events.json", names);
            Assert.Contains("build-and-update.json", names);
            Assert.Single(names, name => name.StartsWith("logs/", StringComparison.Ordinal));

            var combined = string.Join(
                "\n",
                archive.Entries.Select(ReadEntry));
            Assert.DoesNotContain(secret, combined, StringComparison.Ordinal);
            Assert.DoesNotContain(root, combined, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("another-private-token", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("should-not-appear", combined, StringComparison.Ordinal);
            Assert.DoesNotContain(windowsUser, combined, StringComparison.Ordinal);
            Assert.DoesNotContain(wslUser, combined, StringComparison.Ordinal);
            Assert.DoesNotContain(promptCanary, combined, StringComparison.Ordinal);
            Assert.DoesNotContain(urlPassword, combined, StringComparison.Ordinal);
            Assert.DoesNotContain("private-host", combined, StringComparison.Ordinal);
            Assert.Contains("[redacted]", combined, StringComparison.Ordinal);
            Assert.Contains("Review the archive before sharing it", combined, StringComparison.Ordinal);
            Assert.Contains("example.gguf", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("private details", combined, StringComparison.Ordinal);
            Assert.Contains("LLWM-SESSION-EXIT", combined, StringComparison.Ordinal);
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
