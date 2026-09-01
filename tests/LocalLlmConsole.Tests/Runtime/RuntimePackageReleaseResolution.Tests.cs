using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class RuntimePackageReleaseResolutionTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task OfficialReleaseClientPrefersNewestCompatibleRecentBuildOverStaleMarker()
    {
        const string recentReleasesJson = """
        [
          {
            "tag_name": "b10680",
            "target_commitish": "newer-incomplete",
            "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b10680",
            "published_at": "2026-08-28T20:00:00Z",
            "prerelease": true,
            "draft": false,
            "assets": [
              { "name": "llama-b10680-bin-win-cpu-x64.zip", "browser_download_url": "https://example.com/b10680-cpu.zip", "size": 10 }
            ]
          },
          {
            "tag_name": "b10621",
            "target_commitish": "stale-marker",
            "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b10621",
            "published_at": "2026-08-27T08:00:00Z",
            "prerelease": true,
            "draft": false,
            "assets": [
              { "name": "llama-b10621-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/b10621-cuda.zip", "size": 13 },
              { "name": "cudart-llama-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/b10621-cudart.zip", "size": 3 }
            ]
          },
          {
            "tag_name": "b10679",
            "target_commitish": "newest-compatible",
            "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b10679",
            "published_at": "2026-08-28T19:23:12Z",
            "prerelease": true,
            "draft": false,
            "assets": [
              { "name": "llama-b10679-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/b10679-cuda.zip", "size": 13 },
              { "name": "cudart-llama-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/b10679-cudart.zip", "size": 3 }
            ]
          },
          {
            "tag_name": "b10699",
            "target_commitish": "draft",
            "html_url": "https://github.com/ggml-org/llama.cpp/releases/tag/b10699",
            "published_at": "2026-08-28T21:00:00Z",
            "prerelease": true,
            "draft": true,
            "assets": [
              { "name": "llama-b10699-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/b10699-cuda.zip", "size": 13 },
              { "name": "cudart-llama-bin-win-cuda-13.3-x64.zip", "browser_download_url": "https://example.com/b10699-cudart.zip", "size": 3 }
            ]
          }
        ]
        """;
        var requests = new List<string>();
        using var handler = new CapturingHttpHandler(request =>
        {
            requests.Add(request.RequestUri?.ToString() ?? "");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(recentReleasesJson)
            };
        });
        using var client = new HttpClient(handler);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate =>
            candidate.Id == "official-prebuilt-windows-cuda");

        var release = await RuntimePackageReleaseClient.FetchLatestReleaseAsync(
            client,
            preset,
            TestContext.Current.CancellationToken);
        var selection = RuntimePackageAssetSelector.SelectAssets(preset, release);

        Assert.Equal("b10679", release.TagName);
        Assert.Equal("newest-compatible", release.TargetCommit);
        Assert.Equal("llama-b10679-bin-win-cuda-13.3-x64.zip", selection.PrimaryAsset.Name);
        Assert.Equal([RuntimePackageSourceCatalog.RecentOfficialReleasesApiUrl], requests);
    }
}
