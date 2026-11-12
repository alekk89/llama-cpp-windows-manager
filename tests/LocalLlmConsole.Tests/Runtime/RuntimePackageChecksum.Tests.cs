using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class RuntimePackageChecksumTests : ManagerRegressionTestBase
{
    private static readonly string Target = new('a', 64);
    private static readonly string Other = new('b', 64);

    public static TheoryData<string, string> ChecksumFiles => new()
    {
        // A similarly named asset listed first must not satisfy the lookup.
        { $"{Other}  runtime.zip.sig\n{Other}  old-runtime.zip\n{Target}  runtime.zip\n", Target },
        { $"{Other} *runtime-cuda.zip\n{Target} *runtime.zip\n", Target },
        { $"{Target}  ./dist/runtime.zip\n", Target },
        { $"{Target}  dist\\runtime.zip\n", Target },
        { $"SHA256 (runtime.zip.sig) = {Other}\nSHA256 (runtime.zip) = {Target}\n", Target },
        { $"# generated\nsha256:{Target.ToUpperInvariant()}  RUNTIME.ZIP\r\n", Target },
        { $"﻿{Target}\n", Target },
        { $"{Other}  runtime.zip.sig\n{Other}  old-runtime.zip\n", "" },
        { $"{Target}\n{Other}\n", "" },
        { $"{Target}  other.zip\n{Other}\n", "" },
        { "not a checksum file", "" }
    };

    [Theory]
    [MemberData(nameof(ChecksumFiles))]
    public void ChecksumLookupMatchesTheExactAssetFileName(string checksumText, string expected)
        => Assert.Equal(expected, RuntimePackageAssetVerifier.ExtractSha256(checksumText, "runtime.zip"));

    [Fact]
    public void ChecksumLookupWithoutAnAssetNameUsesTheFirstEntry()
        => Assert.Equal(Other, RuntimePackageAssetVerifier.ExtractSha256($"{Other}  a.zip\n{Target}  b.zip", ""));

    [Theory]
    [InlineData(true, RuntimePackageChecksumStatus.Verified, "Verified against the published SHA-256 digest")]
    [InlineData(false, RuntimePackageChecksumStatus.SkippedByUser, "Not verified")]
    public async Task InstalledPackageRecordsTheActualChecksumStatus(bool publishDigest, string expectedStatus, string expectedDetail)
    {
        var root = CreateTempRoot();
        var source = Path.Combine(root, "package-source", "llama-b9354-bin-win-cpu-x64", "bin");
        var archive = Path.Combine(root, "runtime.zip");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "llama-server.exe"), "fake server", TestContext.Current.CancellationToken);
        System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(root, "package-source"), archive);
        var archiveBytes = await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken);
        var digest = publishDigest ? $",\n\"digest\": \"sha256:{Sha256Hex(archiveBytes)}\"" : "";
        var releaseJson = $$"""
        {
          "tag_name": "b9354",
          "target_commitish": "9777256c3130",
          "html_url": "https://example.com/release",
          "published_at": "2026-05-28T10:00:00Z",
          "assets": [
            {
              "name": "llama-b9354-bin-win-cpu-x64.zip",
              "browser_download_url": "https://example.com/win-cpu.zip",
              "size": {{archiveBytes.Length}}{{digest}}
            }
          ]
        }
        """;
        using var handler = new CapturingHttpHandler(request =>
        {
            if (request.RequestUri?.ToString() == RuntimePackageSourceCatalog.LatestReleaseApiUrl)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(releaseJson) };
            if (request.RequestUri?.ToString() == "https://example.com/win-cpu.zip")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(archiveBytes) };
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var installer = new RuntimePackageInstallService(http, new RuntimeRegistryService(store));
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");

        // Installing without published metadata is only possible after the user's explicit confirmation,
        // which the application service expresses as RequireChecksum: false.
        var result = await installer.InstallAsync(new RuntimePackageInstallRequest(
            preset,
            AppSettings.CreateDefault(root),
            Path.Combine(root, "logs", "runtime-package.log"),
            BoundedLogFile.MegabytesToBytes(1),
            _ => Task.CompletedTask,
            CancellationToken: TestContext.Current.CancellationToken,
            RequireChecksum: publishDigest));
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(result.RuntimeFolder, "local-llm-runtime.json"),
            TestContext.Current.CancellationToken));
        var provenance = RuntimeInstallationVerificationService.Describe(Assert.Single(await store.ListRuntimesAsync()));

        Assert.Equal(expectedStatus, metadata?["checksumStatus"]?.ToString());
        Assert.Equal(expectedStatus, metadata?["assets"]?[0]?["checksumStatus"]?.ToString());
        Assert.Contains($"Checksum status: {expectedDetail}", provenance.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChecksumRequiredInstallStillRejectsPackagesWithoutMetadata()
    {
        var root = CreateTempRoot();
        var bytes = "runtime"u8.ToArray();
        using var handler = new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler);
        var asset = new RuntimePackageAsset("runtime.zip", "https://example.com/runtime.zip", bytes.Length);

        var skipped = await RuntimePackageInstallFileService.DownloadAssetAsync(
            http, asset, Path.Combine(root, "skipped.zip"), requireChecksum: false, TestContext.Current.CancellationToken);
        var required = await Assert.ThrowsAsync<InvalidOperationException>(() => RuntimePackageInstallFileService.DownloadAssetAsync(
            http, asset, Path.Combine(root, "required.zip"), requireChecksum: true, TestContext.Current.CancellationToken));

        Assert.Equal(RuntimePackageChecksumStatus.SkippedByUser, skipped);
        Assert.Contains("missing SHA-256", required.Message, StringComparison.OrdinalIgnoreCase);
    }
}
