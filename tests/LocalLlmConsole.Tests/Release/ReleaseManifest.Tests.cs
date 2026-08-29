using LocalLlmConsole.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;

public sealed class ReleaseManifestTests : ManagerRegressionTestBase
{
    private static readonly DateTimeOffset ManifestTestNow = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppUpdateServiceAcceptsValidSignedStableManifest()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer);
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));

        var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);

        Assert.True(update.IsAvailable);
        Assert.True(update.AuthenticityVerified);
        Assert.Equal("stable", update.ReleaseChannel);
        Assert.Equal("current", update.ManifestKeyId);
        Assert.Equal(fixture.AssetSha256, update.ExpectedSha256);
        Assert.Equal("Test Publisher", update.ExpectedWindowsPublisher);
        Assert.Equal(fixture.AssetBytes.Length, update.AssetSize);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsForgedManifest()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer);
        fixture = fixture with { ManifestBytes = [.. fixture.ManifestBytes, (byte)' '] };
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.CheckLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("signature verification failed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsManifestSignedByUnknownKey()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer);
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("other", other));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.CheckLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("unknown signing key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsManifestVersionMismatch()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer, manifestVersion: "9.9.8", releaseTag: "v9.9.9");
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.CheckLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("version/tag", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsExpiredManifest()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer, expiresAt: ManifestTestNow.AddMinutes(-1));
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.CheckLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("expired", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsManifestThatRequiresNewerSecureUpdater()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer, minimumSecureUpdaterVersion: "2.5.1");
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.CheckLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("requires updater version v2.5.1", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsAssetFilenameSubstitution()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer);
        var assets = fixture.Release["assets"]!.AsArray();
        var application = assets.OfType<JsonObject>()
            .Single(asset => asset["name"]?.ToString() == AppUpdateService.PortableExeName);
        application["name"] = "substituted-update.exe";
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));

        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.CheckLatestAsync(TestContext.Current.CancellationToken));

        Assert.Contains("listed by the signed release manifest", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppUpdateServiceAcceptsConfiguredNextRotationKey()
    {
        using var current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(next, keyId: "next");
        var trust = new ReleaseManifestTrustStore([
            TrustKey("current", current),
            TrustKey("next", next)]);
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, trust);

        var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);

        Assert.True(update.AuthenticityVerified);
        Assert.Equal("next", update.ManifestKeyId);
    }

    [Fact]
    public async Task AppUpdateServiceRejectsTamperedAssetEvenWithCohostedChecksum()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fixture = CreateManifestFixture(signer);
        using var http = new HttpClient(new ManifestHttpHandler(fixture));
        var service = CreateManifestUpdateService(http, TrustStore("current", signer));
        var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);
        fixture.AssetBytes[^1] ^= 0xff;
        var root = CreateTempRoot();
        try
        {
            var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.StageInstallAsync(
                update,
                root,
                Path.Combine(root, AppUpdateService.PortableExeName),
                TestContext.Current.CancellationToken));
            Assert.Contains("checksum mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsChecksumOnlyAndRollbackInstallPlans()
    {
        var root = CreateTempRoot();
        var service = new AppUpdateService(
            new HttpClient(),
            _ => { },
            signatureVerifier: new AcceptingManifestSignatureVerifier());
        var checksumOnly = new AppUpdateInfo(
            true, "v2.5.0", "v2.5.1", "release", "", "", AppUpdateService.PortableExeName,
            "https://example.invalid/app.exe", 1024 * 1024, ExpectedSha256: new string('a', 64));
        var rollback = checksumOnly with
        {
            IsAvailable = false,
            LatestVersion = "v2.4.0",
            AuthenticityVerified = true,
            ReleaseChannel = "stable",
            ManifestKeyId = "current",
            ExpectedWindowsPublisher = "Test Publisher"
        };
        try
        {
            var unsignedError = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.StageInstallAsync(
                checksumOnly, root, Path.Combine(root, AppUpdateService.PortableExeName), TestContext.Current.CancellationToken));
            var rollbackError = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.StageInstallAsync(
                rollback, root, Path.Combine(root, AppUpdateService.PortableExeName), TestContext.Current.CancellationToken));
            Assert.Contains("signed release manifest", unsignedError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not newer", rollbackError.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRequiresExpectedAuthenticodePublisher()
    {
        var bytes = Enumerable.Repeat((byte)0x5a, 1024 * 1024).ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var handler = new DelegateHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        using var http = new HttpClient(handler);
        var rejecting = new RejectingManifestSignatureVerifier();
        var service = new AppUpdateService(http, _ => { }, signatureVerifier: rejecting);
        var update = new AppUpdateInfo(
            true, "v2.5.0", "v2.5.1", "release", "", "", AppUpdateService.PortableExeName,
            "https://example.invalid/app.exe", bytes.Length, ExpectedSha256: hash,
            AuthenticityVerified: true, ReleaseChannel: "stable", ManifestKeyId: "current",
            ExpectedWindowsPublisher: "Expected Publisher");
        var root = CreateTempRoot();
        try
        {
            var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.StageInstallAsync(
                update, root, Path.Combine(root, AppUpdateService.PortableExeName), TestContext.Current.CancellationToken));
            Assert.Contains("unexpected publisher", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Expected Publisher", rejecting.ExpectedPublisher);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AppUpdateService CreateManifestUpdateService(HttpClient http, ReleaseManifestTrustStore trust)
        => new(
            http,
            _ => { },
            trust,
            new AcceptingManifestSignatureVerifier(),
            () => ManifestTestNow,
            currentVersion: () => "v2.5.0");

    private static ReleaseManifestTrustStore TrustStore(string keyId, ECDsa signer)
        => new([TrustKey(keyId, signer)]);

    private static ReleaseManifestTrustKey TrustKey(string keyId, ECDsa signer)
        => new(keyId, Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()));

    private static ManifestFixture CreateManifestFixture(
        ECDsa signer,
        string keyId = "current",
        string manifestVersion = "9.9.9",
        string releaseTag = "v9.9.9",
        DateTimeOffset? expiresAt = null,
        string minimumSecureUpdaterVersion = "2.5.0")
    {
        var assetBytes = Enumerable.Repeat((byte)0x41, 1024 * 1024).ToArray();
        var assetHash = Convert.ToHexStringLower(SHA256.HashData(assetBytes));
        var sbomBytes = Encoding.UTF8.GetBytes("{\"spdxVersion\":\"SPDX-2.3\"}");
        var sbomHash = Convert.ToHexStringLower(SHA256.HashData(sbomBytes));
        var manifest = new ReleaseManifestDocument(
            1,
            manifestVersion,
            $"v{manifestVersion}",
            new string('a', 40),
            "alekk89/llama-cpp-windows-manager",
            "stable",
            ManifestTestNow.AddDays(-1),
            expiresAt ?? ManifestTestNow.AddDays(30),
            minimumSecureUpdaterVersion,
            keyId,
            "Test Publisher",
            [
                new(AppUpdateService.PortableExeName, "application", "application/vnd.microsoft.portable-executable", assetBytes.Length, assetHash),
                new("sbom.spdx.json", "sbom", "application/spdx+json", sbomBytes.Length, sbomHash)
            ],
            new("sbom.spdx.json", sbomHash));
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, jsonOptions);
        var signature = signer.SignData(
            manifestBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        var envelope = new ReleaseManifestSignatureEnvelope(
            1,
            keyId,
            "ECDSA_P256_SHA256",
            Convert.ToBase64String(signature));
        var signatureBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, jsonOptions);
        var release = new JsonObject
        {
            ["tag_name"] = releaseTag,
            ["name"] = releaseTag,
            ["body"] = "Signed release notes",
            ["html_url"] = $"https://github.com/alekk89/llama-cpp-windows-manager/releases/tag/{releaseTag}",
            ["draft"] = false,
            ["prerelease"] = false,
            ["assets"] = new JsonArray(
                ReleaseAsset(AppUpdateService.PortableExeName, assetBytes.Length),
                ReleaseAsset("sbom.spdx.json", sbomBytes.Length),
                ReleaseAsset(AppReleaseManifestVerifier.ManifestAssetName, manifestBytes.Length),
                ReleaseAsset(AppReleaseManifestVerifier.SignatureAssetName, signatureBytes.Length))
        };
        return new ManifestFixture(release, manifestBytes, signatureBytes, assetBytes, sbomBytes, assetHash);
    }

    private static JsonObject ReleaseAsset(string name, long size)
        => new()
        {
            ["name"] = name,
            ["browser_download_url"] = $"https://example.invalid/{name}",
            ["size"] = size
        };

    private sealed record ManifestFixture(
        JsonObject Release,
        byte[] ManifestBytes,
        byte[] SignatureBytes,
        byte[] AssetBytes,
        byte[] SbomBytes,
        string AssetSha256);

    private sealed class ManifestHttpHandler(ManifestFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            byte[] bytes;
            string mediaType;
            if (path.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                bytes = Encoding.UTF8.GetBytes(fixture.Release.ToJsonString());
                mediaType = "application/json";
            }
            else if (path.EndsWith(AppReleaseManifestVerifier.SignatureAssetName, StringComparison.Ordinal))
            {
                bytes = fixture.SignatureBytes;
                mediaType = "application/json";
            }
            else if (path.EndsWith(AppReleaseManifestVerifier.ManifestAssetName, StringComparison.Ordinal))
            {
                bytes = fixture.ManifestBytes;
                mediaType = "application/json";
            }
            else if (path.EndsWith("sbom.spdx.json", StringComparison.Ordinal))
            {
                bytes = fixture.SbomBytes;
                mediaType = "application/spdx+json";
            }
            else
            {
                bytes = fixture.AssetBytes;
                mediaType = "application/octet-stream";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new(mediaType) }
                }
            });
        }
    }

    private sealed class DelegateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    private sealed class AcceptingManifestSignatureVerifier : IAppUpdateSignatureVerifier
    {
        public void Verify(string path, string expectedPublisher, string? expectedSignerPath = null)
        {
        }
    }

    private sealed class RejectingManifestSignatureVerifier : IAppUpdateSignatureVerifier
    {
        public string ExpectedPublisher { get; private set; } = "";

        public void Verify(string path, string expectedPublisher, string? expectedSignerPath = null)
        {
            ExpectedPublisher = expectedPublisher;
            throw new InvalidOperationException("Update is signed by an unexpected publisher.");
        }
    }
}
