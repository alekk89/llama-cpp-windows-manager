using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Reflection;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task PortableUpdaterRollsBackAppWhenCliReplacementFails()
    {
        var root = CreateTempRoot();
        var sourceExe = Path.Combine(root, "staged-app.exe");
        var targetExe = Path.Combine(root, AppUpdateService.PortableExeName);
        var sourceCli = Path.Combine(root, "staged-cli.exe");
        var targetCli = Path.Combine(root, AppUpdateService.ControlCliExeName);
        var scriptPath = Path.Combine(root, "update.ps1");
        await File.WriteAllTextAsync(sourceExe, "new-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(targetExe, "old-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sourceCli, "new-cli", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(targetCli);
        var updaterScript = typeof(AppUpdateService)
            .GetMethod("UpdaterScript", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)?.ToString() ?? throw new InvalidOperationException("Updater script was unavailable.");
        Assert.DoesNotContain("Get-FileHash", updaterScript, StringComparison.Ordinal);
        await File.WriteAllTextAsync(scriptPath, updaterScript, TestContext.Current.CancellationToken);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = HostExecutableResolver.WindowsPowerShellExe(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList =
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                "-ParentPid", "999999",
                "-SourceExe", sourceExe,
                "-TargetExe", targetExe,
                "-SourceCli", sourceCli,
                "-TargetCli", targetCli,
                "-WorkingDirectory", root
            }
        }) ?? throw new InvalidOperationException("Could not start updater rollback test.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var diagnostics = (await standardOutput) + Environment.NewLine + (await standardError);

        Assert.NotEqual(0, process.ExitCode);
        Assert.True(
            string.Equals("old-app", await File.ReadAllTextAsync(targetExe, TestContext.Current.CancellationToken), StringComparison.Ordinal),
            diagnostics);
        Assert.True(Directory.Exists(targetCli), diagnostics);
        var temporaryFiles = Directory.EnumerateFiles(root, ".*.new").ToArray();
        var backupFiles = Directory.EnumerateFiles(root, ".*.bak").ToArray();
        Assert.True(
            temporaryFiles.Length == 0,
            $"{diagnostics}{Environment.NewLine}Temporary files: {string.Join(", ", temporaryFiles)}");
        Assert.True(
            backupFiles.Length == 0,
            $"{diagnostics}{Environment.NewLine}Backup files: {string.Join(", ", backupFiles)}");
    }

    [Fact]
    public void AppUpdateServiceExtractsChecksumForSelectedAsset()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var exact = AppUpdateAssetVerifier.ExtractSha256($"{hash}  LlamaCppWindowsManager-win-x64.zip", "LlamaCppWindowsManager-win-x64.zip");
        var unrelated = AppUpdateAssetVerifier.ExtractSha256($"{hash}  other.zip", "LlamaCppWindowsManager-win-x64.zip");

        Assert.Equal(hash, exact);
        Assert.Equal("", unrelated);
    }

    [Fact]
    public async Task AppUpdateServiceRequiresChecksumBeforeStaging()
    {
        var temp = CreateTempRoot();
        var service = CreateAppUpdateService(new HttpClient());
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager.exe",
            "https://example.invalid/LlamaCppWindowsManager.exe",
            1024 * 1024);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, "LlamaCppWindowsManager.exe"), TestContext.Current.CancellationToken));

            Assert.Contains("SHA-256 companion", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsMalformedChecksumCompanion()
    {
        var temp = CreateTempRoot();
        using var handler = new CapturingHttpHandler(request =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true
                ? System.Text.Encoding.UTF8.GetBytes("not-a-checksum  LlamaCppWindowsManager.exe")
                : new byte[1024 * 1024]);
            return response;
        });
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager.exe",
            "https://example.invalid/LlamaCppWindowsManager.exe",
            1024 * 1024,
            "LlamaCppWindowsManager.exe.sha256",
            "https://example.invalid/LlamaCppWindowsManager.exe.sha256");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, "LlamaCppWindowsManager.exe"), TestContext.Current.CancellationToken));

            Assert.Contains("does not contain a checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsInvalidInlineChecksum()
    {
        var temp = CreateTempRoot();
        var service = CreateAppUpdateService(new HttpClient());
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager.exe",
            "https://example.invalid/LlamaCppWindowsManager.exe",
            1024 * 1024,
            ExpectedSha256: "not-a-sha256");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, "LlamaCppWindowsManager.exe"), TestContext.Current.CancellationToken));

            Assert.Contains("invalid SHA-256", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsUpdateArchiveTraversal()
    {
        var temp = CreateTempRoot();
        var escapeName = $"llama-update-escape-{Guid.NewGuid():N}.txt";
        var archiveBytes = CreateZipBytes(
            (AppUpdateService.PortableExeName, Enumerable.Repeat((byte)7, 1024 * 1024).ToArray()),
            ("../" + escapeName, [1, 2, 3]));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archiveBytes)).ToLowerInvariant();
        using var handler = new CapturingHttpHandler(request =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true
                ? System.Text.Encoding.UTF8.GetBytes($"{hash}  LlamaCppWindowsManager-win-x64.zip")
                : archiveBytes);
            return response;
        });
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager-win-x64.zip",
            "https://example.invalid/LlamaCppWindowsManager-win-x64.zip",
            archiveBytes.Length,
            "LlamaCppWindowsManager-win-x64.zip.sha256",
            "https://example.invalid/LlamaCppWindowsManager-win-x64.zip.sha256");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, AppUpdateService.PortableExeName), TestContext.Current.CancellationToken));

            Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escapeName)));
            Assert.False(File.Exists(Path.Combine(temp, escapeName)));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceNormalizesObsoleteExecutableNamesToTheCanonicalName()
    {
        var temp = CreateTempRoot();
        var bytes = Enumerable.Repeat((byte)7, 1024 * 1024).ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        using var handler = new CapturingHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Content = new ByteArrayContent(bytes);
            return response;
        });
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);
        var obsoleteExe = Path.Combine(temp, "LlamaCppConsole.exe");
        var update = new AppUpdateInfo(
            true,
            "v1.1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            AppUpdateService.PortableExeName,
            "https://example.invalid/LlamaCppWindowsManager.exe",
            bytes.Length,
            ExpectedSha256: hash);

        try
        {
            var plan = await service.StageInstallAsync(update, temp, obsoleteExe, TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(temp, AppUpdateService.PortableExeName), plan.TargetExe, ignoreCase: true);
            Assert.Equal(obsoleteExe, plan.ObsoleteExe, ignoreCase: true);
            Assert.True(File.Exists(plan.SourceExe));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task FileSystemSafetyServiceComputesExpectedSha256Asynchronously()
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "payload.bin");
        var bytes = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        var actual = await FileSystemSafetyService.Sha256Async(path, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    private static AppUpdateService CreateAppUpdateService(HttpClient http)
        => new(http, _ => { });

    private static byte[] CreateZipBytes(params (string EntryName, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var stream = zip.CreateEntry(entry.EntryName).Open();
                stream.Write(entry.Bytes);
            }
        }
        return memory.ToArray();
    }

    private sealed class CapturingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

}
