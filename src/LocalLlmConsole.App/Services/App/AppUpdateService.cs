using System.IO.Compression;
using System.Net.Http.Headers;

namespace LocalLlmConsole.Services;

public sealed record AppUpdateInfo(
    bool IsAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseName,
    string ReleaseNotes,
    string HtmlUrl,
    string AssetName,
    string AssetUrl,
    long AssetSize,
    string ChecksumAssetName = "",
    string ChecksumAssetUrl = "",
    string ExpectedSha256 = "");

public sealed record AppUpdateInstallPlan(
    string ScriptPath,
    string SourceExe,
    string TargetExe,
    string NoticePath,
    string ObsoleteExe = "",
    string SourceCli = "",
    string TargetCli = "");

public sealed record InstalledUpdateNotice(string Version, string ReleaseName, string ReleaseNotes, DateTimeOffset InstalledAt);

public sealed partial class AppUpdateService
{
    public const string RepositoryUrl = "https://github.com/alekk89/llama-cpp-windows-manager";
    public const string PortableExeName = "LlamaCppWindowsManager.exe";
    public const string ControlCliExeName = "llwmctl.exe";
    private const string ObsoletePortableExeName = "LlamaCppConsole.exe";

    private const string UserAgent = "llama-cpp-windows-manager-updater";
    private readonly HttpClient _http;
    private readonly Action<ProcessStartInfo> _startProcess;

    public AppUpdateService(HttpClient http, Action<ProcessStartInfo> startProcess)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, CurrentVersionLabel().TrimStart('v')));
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static string CurrentVersionLabel()
    {
        var value = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "v0.0.0";
        return value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value : $"v{value}";
    }

    public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        var releaseUrl = $"{RepositoryUrl.TrimEnd('/')}/releases/latest";
        if (TryParseGitHubRepository(RepositoryUrl, out var owner, out var repo))
            releaseUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, releaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return AppUpdateReleaseParser.NoUpdateAvailable(CurrentVersionLabel(), "No GitHub release feed is published yet.");
        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject()
            ?? throw new InvalidOperationException("GitHub did not return a release object.");
        return AppUpdateReleaseParser.ParseLatestRelease(json, CurrentVersionLabel());
    }

    public async Task<AppUpdateInstallPlan> StageInstallAsync(AppUpdateInfo update, string workspaceRoot, string? currentExecutablePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(update.AssetUrl))
            throw new InvalidOperationException("The latest GitHub release does not include a portable llama.cpp Windows Manager asset.");
        var hasInlineChecksum = !string.IsNullOrWhiteSpace(update.ExpectedSha256);
        if (hasInlineChecksum && string.IsNullOrWhiteSpace(AppUpdateAssetVerifier.NormalizeSha256(update.ExpectedSha256)))
            throw new InvalidOperationException("The latest GitHub release includes an invalid SHA-256 checksum. Refusing to stage an unverifiable update.");
        if (!hasInlineChecksum && string.IsNullOrWhiteSpace(update.ChecksumAssetUrl))
            throw new InvalidOperationException("The latest GitHub release asset is missing a SHA-256 companion file. Refusing to stage an unverifiable update.");

        var requestedTargetExe = string.IsNullOrWhiteSpace(currentExecutablePath)
            ? Path.Combine(AppContext.BaseDirectory, PortableExeName)
            : Path.GetFullPath(currentExecutablePath);
        var obsoleteExe = Path.GetFileName(requestedTargetExe).Equals(ObsoletePortableExeName, StringComparison.OrdinalIgnoreCase)
            ? requestedTargetExe
            : "";
        var targetExe = requestedTargetExe;
        if (!AppUpdateReleaseParser.IsPortableExeName(Path.GetFileName(targetExe)))
            targetExe = Path.Combine(Path.GetDirectoryName(targetExe) ?? AppContext.BaseDirectory, PortableExeName);

        var safeVersion = RegexSafeFileName(update.LatestVersion);
        var updateRoot = Path.Combine(workspaceRoot, "cache", "app-updates");
        var stageRoot = Path.Combine(updateRoot, safeVersion);
        Directory.CreateDirectory(stageRoot);

        var assetPath = Path.Combine(stageRoot, RegexSafeFileName(update.AssetName));
        await DownloadAssetAsync(update.AssetUrl, assetPath, cancellationToken);
        await AppUpdateAssetVerifier.VerifyChecksumAssetAsync(_http, update, assetPath, cancellationToken);
        var stagedFiles = await Task.Run(() =>
        {
            var executable = PreparePortableExe(assetPath, stageRoot);
            ValidateUpdateSignature(executable, targetExe);
            var controlCli = FindStagedControlCli(executable);
            if (!string.IsNullOrWhiteSpace(controlCli))
                ValidateUpdateSignature(controlCli, targetExe);
            return (Executable: executable, ControlCli: controlCli);
        }, cancellationToken);
        var stagedExe = stagedFiles.Executable;
        var stagedCli = stagedFiles.ControlCli;
        var targetCli = string.IsNullOrWhiteSpace(stagedCli)
            ? ""
            : Path.Combine(Path.GetDirectoryName(targetExe) ?? AppContext.BaseDirectory, ControlCliExeName);

        var pendingNotice = Path.Combine(stageRoot, "installed-update.json");
        await File.WriteAllTextAsync(pendingNotice, JsonSerializer.Serialize(new InstalledUpdateNotice(
            update.LatestVersion,
            update.ReleaseName,
            TrimReleaseNotes(update.ReleaseNotes),
            DateTimeOffset.UtcNow)), cancellationToken);

        var noticePath = PendingNoticePath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(noticePath)!);
        var scriptPath = Path.Combine(stageRoot, "Install-LlamaCppWindowsManagerUpdate.ps1");
        await File.WriteAllTextAsync(scriptPath, UpdaterScript(), new UTF8Encoding(false), cancellationToken);
        return new AppUpdateInstallPlan(scriptPath, stagedExe, targetExe, noticePath, obsoleteExe, stagedCli, targetCli);
    }

    public void StartInstaller(AppUpdateInstallPlan plan, int currentProcessId)
    {
        var psi = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(plan.TargetExe) ?? AppContext.BaseDirectory
        };
        foreach (var arg in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-Scope", "Process", "-File", plan.ScriptPath,
            "-ParentPid", currentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-SourceExe", plan.SourceExe,
            "-TargetExe", plan.TargetExe,
            "-ObsoleteExe", plan.ObsoleteExe,
            "-SourceCli", plan.SourceCli,
            "-TargetCli", plan.TargetCli,
            "-NoticeSource", Path.Combine(Path.GetDirectoryName(plan.ScriptPath) ?? "", "installed-update.json"),
            "-NoticeTarget", plan.NoticePath,
            "-WorkingDirectory", Path.GetDirectoryName(plan.TargetExe) ?? AppContext.BaseDirectory
        })
        {
            psi.ArgumentList.Add(arg);
        }

        _startProcess(psi);
    }

    public static async Task<InstalledUpdateNotice?> TryConsumeInstalledNoticeAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var path = PendingNoticePath(workspaceRoot);
        if (!File.Exists(path)) return null;
        try
        {
            var notice = JsonSerializer.Deserialize<InstalledUpdateNotice>(await File.ReadAllTextAsync(path, cancellationToken));
            File.Delete(path);
            return notice;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not consume installed update notice {path}: {ex.Message}");
            try { File.Delete(path); }
            catch (Exception deleteEx)
            {
                Trace.TraceWarning($"Could not delete installed update notice {path}: {deleteEx.Message}");
            }
            return null;
        }
    }

    private async Task DownloadAssetAsync(string assetUrl, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string PreparePortableExe(string assetPath, string stageRoot)
    {
        if (Path.GetExtension(assetPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ValidateStagedExe(assetPath);
            return assetPath;
        }

        if (!Path.GetExtension(assetPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The update asset must be a portable .exe or .zip release artifact.");

        var extractRoot = Path.Combine(stageRoot, "extracted");
        if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, recursive: true);
        Directory.CreateDirectory(extractRoot);
        ArchiveSafetyService.ValidateZipArchiveEntries(assetPath, extractRoot);
        ZipFile.ExtractToDirectory(assetPath, extractRoot);
        var stagedExe = Directory.EnumerateFiles(extractRoot, PortableExeName, SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The update archive does not contain {PortableExeName}.");
        ValidateStagedExe(stagedExe);
        return stagedExe;
    }

    private static void ValidateStagedExe(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 1024 * 1024)
            throw new InvalidOperationException("The downloaded update does not look like a valid app executable.");
    }

    private static string FindStagedControlCli(string stagedExe)
    {
        var directory = Path.GetDirectoryName(stagedExe);
        if (string.IsNullOrWhiteSpace(directory)) return "";
        var path = Path.Combine(directory, ControlCliExeName);
        if (!File.Exists(path)) return "";
        if (new FileInfo(path).Length < 64 * 1024)
            throw new InvalidOperationException("The downloaded update contains an invalid llwmctl executable.");
        return path;
    }

    private static void ValidateUpdateSignature(string stagedExe, string targetExe)
    {
        var current = TryReadSigningCertificate(targetExe);
        if (current is null) return;

        var staged = TryReadSigningCertificate(stagedExe)
            ?? throw new InvalidOperationException("The installed app is signed, but the downloaded update is not signed.");
        if (!string.Equals(current.Thumbprint, staged.Thumbprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The downloaded update is not signed by the same certificate as the installed app.");
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2? TryReadSigningCertificate(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
#pragma warning disable SYSLIB0057 // This API extracts an Authenticode signer from a PE file; X509CertificateLoader only accepts certificate files.
            using var certificate = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseGitHubRepository(string url, out string owner, out string repo)
    {
        owner = "";
        repo = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        owner = parts[0];
        repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }

    private static string RegexSafeFileName(string value)
        => string.Join("_", (value ?? "update").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string TrimReleaseNotes(string notes)
        => string.IsNullOrWhiteSpace(notes) ? "No release notes were provided." : notes.Trim().Length <= 4000 ? notes.Trim() : notes.Trim()[..4000] + "\n\n...";

    private static string PendingNoticePath(string workspaceRoot)
        => Path.Combine(workspaceRoot, "cache", "app-updates", "installed-update.json");

}
