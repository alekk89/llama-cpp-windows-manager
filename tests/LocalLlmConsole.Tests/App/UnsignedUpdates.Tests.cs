using LocalLlmConsole.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;

public sealed class UnsignedUpdatesTests : ManagerRegressionTestBase
{
    private const string AssetUrl = AppUpdateService.RepositoryUrl + "/releases/download/v2.8.0/LlamaCppWindowsManager.exe";

    [Theory]
    [InlineData("v2.7.0")]
    [InlineData("v2.7.0+a48a8b9dca99736792096de66446fdf7d28bf585")]
    public async Task UnsignedExeReleaseChecksAndStagesWithChecksumWithoutClaimingSignature(string currentVersion)
    {
        var root = CreateTempRoot();
        try
        {
            using var handler = new UnsignedHandler();
            using var http = new HttpClient(handler);
            using var service = Service(http, currentVersion: currentVersion);
            var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);
            Assert.True(update.IsAvailable);
            Assert.False(update.AuthenticityVerified);
            var plan = await service.StageInstallAsync(update, root, Path.Combine(root, AppUpdateService.PortableExeName), TestContext.Current.CancellationToken);
            Assert.True(File.Exists(plan.ScriptPath));
            Assert.Equal(handler.Bytes, await File.ReadAllBytesAsync(plan.SourceExe, TestContext.Current.CancellationToken));
            Assert.Contains(service.VerificationDiagnostics(), item => item.Message == "checksum-verified-unsigned");
            Assert.Contains("unsigned", AppUpdateWorkflowService.DescribeCheckResult(update, true).DialogMessage);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("missing-checksum")]
    [InlineData("wrong-origin")]
    [InlineData("wrong-tag")]
    [InlineData("http")]
    [InlineData("installer-only")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("manifest-without-signature")]
    [InlineData("signature-without-manifest")]
    public async Task UnsignedUpdateRejectsInvalidReleaseBeforeDownloadingExecutable(string fault)
    {
        using var handler = new UnsignedHandler();
        var assets = handler.Release["assets"]!.AsArray();
        switch (fault)
        {
            case "missing-checksum": assets.RemoveAt(1); break;
            case "wrong-origin": assets[0]!["browser_download_url"] = "https://example.invalid/app.exe"; break;
            case "wrong-tag": assets[0]!["browser_download_url"] = AssetUrl.Replace("v2.8.0", "v2.9.0"); break;
            case "http": assets[0]!["browser_download_url"] = AssetUrl.Replace("https:", "http:"); break;
            case "installer-only": assets[0]!["name"] = "LlamaCppWindowsManager-Setup-2.8.0-win-x64.exe"; break;
            case "draft": handler.Release["draft"] = true; break;
            case "prerelease": handler.Release["prerelease"] = true; break;
            default: assets.Add(new JsonObject { ["name"] = fault == "manifest-without-signature" ? "release-manifest.json" : "release-manifest.json.sig" }); break;
        }
        using var http = new HttpClient(handler);
        using var service = Service(http);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CheckLatestAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, handler.AssetRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsignedUpdateRejectsCorruptionAndTruncation(bool truncate)
    {
        var root = CreateTempRoot();
        try
        {
            using var handler = new UnsignedHandler();
            using var http = new HttpClient(handler);
            using var service = Service(http);
            var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);
            if (truncate) handler.Bytes = handler.Bytes[..^1];
            else handler.Bytes[^1] ^= 0xff;
            var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.StageInstallAsync(update, root, null, TestContext.Current.CancellationToken));
            Assert.Contains(truncate ? "size mismatch" : "checksum mismatch", error.Message);
            Assert.Empty(Directory.GetFiles(root, "Install-*.ps1", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SignedPolicyRejectsUnsignedRelease()
    {
        using var handler = new UnsignedHandler();
        using var http = new HttpClient(handler);
        using var service = Service(http, allowUnsigned: false);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.CheckLatestAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnsignedPolicyRejectsForgedRollbackPlan()
    {
        using var handler = new UnsignedHandler();
        using var http = new HttpClient(handler);
        using var service = Service(http);
        var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.StageInstallAsync(update with { LatestVersion = "v2.6.0" }, "unused", null, TestContext.Current.CancellationToken));
        Assert.Contains("not newer", error.Message);
    }

    private static AppUpdateService Service(HttpClient http, bool allowUnsigned = true, string currentVersion = "v2.7.0")
        => new(http, _ => throw new InvalidOperationException("Must not launch while staging"),
            currentVersion: () => currentVersion, allowUnsignedUpdates: allowUnsigned);

    private sealed class UnsignedHandler : HttpMessageHandler
    {
        public byte[] Bytes = Enumerable.Repeat((byte)0x5a, 1024 * 1024).ToArray();
        public JsonObject Release { get; }
        public int AssetRequests { get; private set; }
        private readonly string _checksum;

        public UnsignedHandler()
        {
            _checksum = Convert.ToHexStringLower(SHA256.HashData(Bytes)) + "  " + AppUpdateService.PortableExeName;
            Release = new JsonObject
            {
                ["tag_name"] = "v2.8.0",
                ["assets"] = new JsonArray(
                    new JsonObject { ["name"] = AppUpdateService.PortableExeName, ["browser_download_url"] = AssetUrl, ["size"] = Bytes.Length },
                    new JsonObject { ["name"] = AppUpdateService.PortableExeName + ".sha256", ["browser_download_url"] = AssetUrl + ".sha256", ["size"] = _checksum.Length })
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            HttpContent content;
            if (url.EndsWith("/releases/latest", StringComparison.Ordinal)) content = new StringContent(Release.ToJsonString());
            else if (url == AssetUrl + ".sha256") content = new StringContent(_checksum);
            else if (url == AssetUrl) { AssetRequests++; content = new ByteArrayContent(Bytes); }
            else throw new InvalidOperationException("Unexpected request: " + url);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
