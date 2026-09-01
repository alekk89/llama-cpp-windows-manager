using System.Net.Http.Headers;

namespace LocalLlmConsole.Services;

public static class RuntimePackageReleaseClient
{
    private const string OfficialNightlyTagAssetName = "nightly-tag.txt";
    private const string OfficialReleaseTagApiBaseUrl = "https://api.github.com/repos/ggml-org/llama.cpp/releases/tags/";
    private const int MaxNightlyTagBytes = 64;

    public static async Task<RuntimePackageRelease> FetchLatestReleaseAsync(HttpClient client, CancellationToken cancellationToken = default)
        => await FetchLatestReleaseAsync(client, null, cancellationToken);

    public static async Task<RuntimePackageRelease> FetchLatestReleaseAsync(HttpClient client, RuntimePackagePreset? preset, CancellationToken cancellationToken = default)
    {
        if (preset is not null && RuntimePackageSourceCatalog.IsOfficialPackage(preset))
        {
            var recentRelease = await FetchRecentOfficialReleaseAsync(client, preset, cancellationToken);
            if (recentRelease is not null)
                return recentRelease;
        }

        var apiUrl = RuntimePackageSourceCatalog.ReleaseApiUrlFor(preset);
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LocalLlmConsole", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(IsHuggingFaceApiUrl(apiUrl) ? "application/json" : "application/vnd.github+json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (IsHuggingFaceApiUrl(apiUrl))
            return ParseHuggingFaceModelJson(json, preset);

        var release = ParseReleaseJson(json);
        return await ResolveOfficialNightlyReleaseAsync(client, preset, release, cancellationToken);
    }

    private static async Task<RuntimePackageRelease?> FetchRecentOfficialReleaseAsync(
        HttpClient client,
        RuntimePackagePreset preset,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await DownloadJsonAsync(
                client,
                RuntimePackageSourceCatalog.RecentOfficialReleasesApiUrl,
                cancellationToken);
            var releases = JsonNode.Parse(json) as JsonArray;
            if (releases is null)
                return null;

            foreach (var candidate in releases
                         .OfType<JsonObject>()
                         .Where(release => !BooleanValue(release["draft"]))
                         .Select(release => new
                         {
                             Release = release,
                             Build = OfficialBuildNumber(release["tag_name"]?.ToString())
                         })
                         .Where(candidate => candidate.Build >= 0)
                         .OrderByDescending(candidate => candidate.Build))
            {
                RuntimePackageRelease release;
                try
                {
                    release = ParseReleaseJson(candidate.Release.ToJsonString());
                    _ = RuntimePackageAssetSelector.SelectAssets(preset, release);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                return release;
            }
        }
        catch (HttpRequestException)
        {
            // The stable release marker remains a bounded fallback when the recent-release feed is unavailable.
        }
        catch (JsonException)
        {
            // Fall back when GitHub returns an unexpected recent-release payload.
        }

        return null;
    }

    private static async Task<RuntimePackageRelease> ResolveOfficialNightlyReleaseAsync(
        HttpClient client,
        RuntimePackagePreset? preset,
        RuntimePackageRelease release,
        CancellationToken cancellationToken)
    {
        if (preset is not null && !RuntimePackageSourceCatalog.IsOfficialPackage(preset))
            return release;

        var nightlyTagAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.Equals(OfficialNightlyTagAssetName, StringComparison.OrdinalIgnoreCase));
        if (nightlyTagAsset is null)
            return release;

        ValidateOfficialNightlyTagUrl(nightlyTagAsset.DownloadUrl);
        var nightlyTag = (await DownloadNightlyTagAsync(client, nightlyTagAsset.DownloadUrl, cancellationToken)).Trim();
        if (!Regex.IsMatch(nightlyTag, "^b[0-9]+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"Release {release.TagName} published an invalid official nightly build tag.");

        var nightlyJson = await DownloadJsonAsync(
            client,
            OfficialReleaseTagApiBaseUrl + Uri.EscapeDataString(nightlyTag),
            cancellationToken);
        var nightlyRelease = ParseReleaseJson(nightlyJson);
        if (!nightlyRelease.TagName.Equals(nightlyTag, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Official nightly build lookup for {nightlyTag} returned release {nightlyRelease.TagName}.");
        return nightlyRelease;
    }

    private static async Task<string> DownloadJsonAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LocalLlmConsole", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<string> DownloadNightlyTagAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LocalLlmConsole", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxNightlyTagBytes)
            throw new InvalidOperationException("The official llama.cpp nightly build tag was unexpectedly large.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxNightlyTagBytes + 1];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
            if (read == 0) break;
            totalRead += read;
        }

        if (totalRead > MaxNightlyTagBytes)
            throw new InvalidOperationException("The official llama.cpp nightly build tag was unexpectedly large.");
        return Encoding.UTF8.GetString(buffer, 0, totalRead);
    }

    private static void ValidateOfficialNightlyTagUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.StartsWith(
                "/ggml-org/llama.cpp/releases/download/",
                StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.EndsWith('/' + OfficialNightlyTagAssetName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Release marker {OfficialNightlyTagAssetName} did not use the official llama.cpp download location.");
        }
    }

    public static RuntimePackageRelease ParseReleaseJson(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("GitHub release response was empty.");
        var tag = root["tag_name"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(tag))
            throw new InvalidOperationException("GitHub release response did not include a release tag.");

        var assetsNode = root["assets"] as JsonArray;
        if (assetsNode is null || assetsNode.Count == 0)
            throw new InvalidOperationException($"Release {tag} did not include downloadable assets.");

        var assets = new List<RuntimePackageAsset>();
        foreach (var assetNode in assetsNode.OfType<JsonObject>())
        {
            var name = assetNode["name"]?.ToString() ?? "";
            var url = assetNode["browser_download_url"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            assets.Add(new RuntimePackageAsset(name, url, LongValue(assetNode["size"]), AssetSha256(assetNode)));
        }

        if (assets.Count == 0)
            throw new InvalidOperationException($"Release {tag} did not include usable downloadable assets.");

        var verifiedAssets = AttachChecksumCompanions(assets);
        return new RuntimePackageRelease(
            tag,
            root["target_commitish"]?.ToString() ?? "",
            root["html_url"]?.ToString() ?? $"{RuntimePackageSourceCatalog.ReleasesUrl}/tag/{tag}",
            DateTimeOffset.TryParse(root["published_at"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishedAt)
                ? publishedAt
                : DateTimeOffset.MinValue,
            verifiedAssets);
    }

    public static RuntimePackageRelease ParseHuggingFaceModelJson(string json, RuntimePackagePreset? preset = null)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("Hugging Face model response was empty.");
        var modelId = root["id"]?.ToString() ?? root["modelId"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException("Hugging Face model response did not include a model id.");

        var sha = root["sha"]?.ToString() ?? "";
        var revision = string.IsNullOrWhiteSpace(sha) ? "main" : sha;
        var tag = string.IsNullOrWhiteSpace(sha) ? "hf-latest" : $"hf-{sha[..Math.Min(12, sha.Length)]}";
        var assetsNode = root["siblings"] as JsonArray;
        if (assetsNode is null || assetsNode.Count == 0)
            throw new InvalidOperationException($"Hugging Face model {modelId} did not include downloadable files.");

        var assets = new List<RuntimePackageAsset>();
        foreach (var assetNode in assetsNode.OfType<JsonObject>())
        {
            var name = assetNode["rfilename"]?.ToString() ?? "";
            if (!IsDownloadableHuggingFaceFile(name)) continue;
            assets.Add(new RuntimePackageAsset(
                name,
                $"{HuggingFaceModelPageUrl(modelId)}/resolve/{Uri.EscapeDataString(revision)}/{EscapeHuggingFacePath(name)}?download=true",
                LongValue(assetNode["size"]),
                AssetSha256(assetNode)));
        }

        if (assets.Count == 0)
            throw new InvalidOperationException($"Hugging Face model {modelId} did not include usable downloadable files.");

        var pageUrl = preset is null || string.IsNullOrWhiteSpace(preset.ReleasePageUrl)
            ? HuggingFaceModelPageUrl(modelId)
            : preset.ReleasePageUrl;
        return new RuntimePackageRelease(
            tag,
            sha,
            pageUrl,
            DateTimeOffset.TryParse(root["lastModified"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var publishedAt)
                ? publishedAt
                : DateTimeOffset.MinValue,
            assets);
    }

    private static IReadOnlyList<RuntimePackageAsset> AttachChecksumCompanions(IReadOnlyList<RuntimePackageAsset> assets)
        => assets
            .Select(asset => string.IsNullOrWhiteSpace(asset.ChecksumUrl)
                ? asset with { ChecksumUrl = ChecksumUrlFor(assets, asset.Name) }
                : asset)
            .ToArray();

    private static string ChecksumUrlFor(IReadOnlyList<RuntimePackageAsset> assets, string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)) return "";
        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            assetName + ".sha256",
            assetName + ".sha256.txt",
            assetName + ".sha256sum",
            Path.ChangeExtension(assetName, ".sha256")
        };
        return assets.FirstOrDefault(asset => expectedNames.Contains(asset.Name))?.DownloadUrl ?? "";
    }

    private static string AssetSha256(JsonObject assetNode)
    {
        foreach (var value in new[]
        {
            assetNode["digest"]?.ToString(),
            assetNode["sha256"]?.ToString(),
            assetNode["checksum"]?.ToString(),
            assetNode["lfs"]?["sha256"]?.ToString()
        })
        {
            var sha256 = RuntimePackageAssetVerifier.NormalizeSha256(value ?? "");
            if (!string.IsNullOrWhiteSpace(sha256)) return sha256;
        }

        return "";
    }

    private static bool IsHuggingFaceApiUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/api/models/", StringComparison.OrdinalIgnoreCase);

    private static string HuggingFaceModelPageUrl(string modelId)
        => $"https://huggingface.co/{modelId.Trim('/')}";

    private static string EscapeHuggingFacePath(string path)
        => string.Join("/", (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static bool IsDownloadableHuggingFaceFile(string name)
        => !string.IsNullOrWhiteSpace(name)
            && !name.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("README.md", StringComparison.OrdinalIgnoreCase);

    private static long LongValue(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<long>(out var result)) return result;
        return long.TryParse(node?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static bool BooleanValue(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) && result;

    private static long OfficialBuildNumber(string? tag)
        => tag is not null
            && tag.Length > 1
            && tag[0] is 'b' or 'B'
            && long.TryParse(tag.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var build)
                ? build
                : -1;
}
