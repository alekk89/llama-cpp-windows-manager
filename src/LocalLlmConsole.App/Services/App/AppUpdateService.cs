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
    string ExpectedSha256 = "",
    bool AuthenticityVerified = false,
    string ReleaseChannel = "",
    string ManifestKeyId = "",
    string ManifestCommit = "",
    DateTimeOffset? ManifestExpiresAtUtc = null,
    string ExpectedWindowsPublisher = "");

public sealed record AppUpdateInstallPlan(
    string ScriptPath,
    string SourceExe,
    string TargetExe,
    string NoticePath,
    string ObsoleteExe = "",
    string SourceCli = "",
    string TargetCli = "");

public sealed record InstalledUpdateNotice(string Version, string ReleaseName, string ReleaseNotes, DateTimeOffset InstalledAt);

public sealed partial class AppUpdateService : IDisposable
{
    public const string RepositoryUrl = "https://github.com/alekk89/llama-cpp-windows-manager";
    public const string PortableExeName = "LlamaCppWindowsManager.exe";
    public const string ControlCliExeName = "llwmctl.exe";
    private const string ObsoletePortableExeName = "LlamaCppConsole.exe";

    private const string UserAgent = "llama-cpp-windows-manager-updater";
    private readonly HttpClient _http;
    private readonly Action<ProcessStartInfo> _startProcess;
    private readonly AppReleaseManifestVerifier _manifestVerifier;
    private readonly IAppUpdateSignatureVerifier _signatureVerifier;
    private readonly bool _ownsHttpClient;
    private readonly string _currentVersion;
    private readonly Queue<AppUpdateVerificationDiagnostic> _diagnostics = [];
    private readonly object _diagnosticSync = new();

    public AppUpdateService(
        HttpClient http,
        Action<ProcessStartInfo> startProcess,
        ReleaseManifestTrustStore? trustStore = null,
        IAppUpdateSignatureVerifier? signatureVerifier = null,
        Func<DateTimeOffset>? utcNow = null,
        bool ownsHttpClient = false,
        Func<string>? currentVersion = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
        _ownsHttpClient = ownsHttpClient;
        _currentVersion = (currentVersion ?? CurrentVersionLabel)();
        _manifestVerifier = new AppReleaseManifestVerifier(
            _http,
            trustStore ?? ReleaseManifestTrustStore.FromAssembly(typeof(AppUpdateService).Assembly),
            utcNow,
            () => _currentVersion);
        _signatureVerifier = signatureVerifier ?? new AuthenticodeUpdateSignatureVerifier();
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, _currentVersion.TrimStart('v')));
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    public static string CurrentVersionLabel()
    {
        var value = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "v0.0.0";
        return value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value : $"v{value}";
    }

    public IReadOnlyList<AppUpdateVerificationDiagnostic> VerificationDiagnostics()
    {
        lock (_diagnosticSync) return _diagnostics.ToArray();
    }

    public async Task<AppUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var update = await CheckLatestCoreAsync(cancellationToken);
            RecordDiagnostic("LLWM-UPDATE-CHECK", "success", update.IsAvailable ? "verified-update-available" : "no-update");
            return update;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RecordDiagnostic(UpdateErrorCode(ex), "rejected", ex.GetType().Name);
            throw;
        }
    }

    private async Task<AppUpdateInfo> CheckLatestCoreAsync(CancellationToken cancellationToken)
    {
        var releaseUrl = $"{RepositoryUrl.TrimEnd('/')}/releases/latest";
        if (TryParseGitHubRepository(RepositoryUrl, out var owner, out var repo))
            releaseUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, releaseUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return AppUpdateReleaseParser.NoUpdateAvailable(_currentVersion, "No GitHub release feed is published yet.");
        response.EnsureSuccessStatusCode();

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))?.AsObject()
            ?? throw AppUpdateVerificationException.Trust("GitHub did not return a release object.");
        var preliminary = AppUpdateReleaseParser.ParseLatestRelease(json, _currentVersion);
        if (!preliminary.IsAvailable) return preliminary;
        var verifiedManifest = await _manifestVerifier.DownloadAndVerifyAsync(json, cancellationToken);
        return AppUpdateReleaseParser.ParseVerifiedLatestRelease(json, _currentVersion, verifiedManifest);
    }

    public async Task<AppUpdateInstallPlan> StageInstallAsync(AppUpdateInfo update, string workspaceRoot, string? currentExecutablePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await StageInstallCoreAsync(update, workspaceRoot, currentExecutablePath, cancellationToken);
            RecordDiagnostic("LLWM-UPDATE-STAGED", "success", "asset-and-publisher-verified");
            return plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            RecordDiagnostic(UpdateErrorCode(ex), "rejected", ex.GetType().Name);
            throw;
        }
    }

    private async Task<AppUpdateInstallPlan> StageInstallCoreAsync(AppUpdateInfo update, string workspaceRoot, string? currentExecutablePath, CancellationToken cancellationToken)
    {
        if (!update.IsAvailable)
            throw AppUpdateVerificationException.Trust("The selected release is not newer than the installed application.");
        if (!update.AuthenticityVerified)
            throw AppUpdateVerificationException.Manifest("Stable updates require a verified signed release manifest. Checksum-only update installation is not allowed.");
        if (!string.Equals(update.ReleaseChannel, "stable", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(update.ManifestKeyId) ||
            string.IsNullOrWhiteSpace(update.ExpectedWindowsPublisher))
        {
            throw AppUpdateVerificationException.Manifest("Verified stable-update trust metadata is incomplete.");
        }
        if (string.IsNullOrWhiteSpace(update.AssetUrl))
            throw AppUpdateVerificationException.Asset("The latest GitHub release does not include a portable llama.cpp Windows Manager asset.");
        var hasInlineChecksum = !string.IsNullOrWhiteSpace(update.ExpectedSha256);
        if (hasInlineChecksum && string.IsNullOrWhiteSpace(AppUpdateAssetVerifier.NormalizeSha256(update.ExpectedSha256)))
            throw AppUpdateVerificationException.Asset("The latest GitHub release includes an invalid SHA-256 checksum. Refusing to stage an unverifiable update.");
        if (!hasInlineChecksum && string.IsNullOrWhiteSpace(update.ChecksumAssetUrl))
            throw AppUpdateVerificationException.Asset("The latest GitHub release asset is missing a SHA-256 companion file. Refusing to stage an unverifiable update.");

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
        await DownloadAssetAsync(update.AssetUrl, assetPath, update.AssetSize, cancellationToken);
        if (update.AssetSize <= 0 || new FileInfo(assetPath).Length != update.AssetSize)
            throw AppUpdateVerificationException.Asset($"Update asset size mismatch for '{update.AssetName}'.");
        await AppUpdateAssetVerifier.VerifyChecksumAssetAsync(_http, update, assetPath, cancellationToken);
        var stagedFiles = await Task.Run(() =>
        {
            var executable = PreparePortableExe(assetPath, stageRoot);
            _signatureVerifier.Verify(executable, update.ExpectedWindowsPublisher, requestedTargetExe);
            var controlCli = FindStagedControlCli(executable);
            if (!string.IsNullOrWhiteSpace(controlCli))
                _signatureVerifier.Verify(controlCli, update.ExpectedWindowsPublisher, executable);
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

    private void RecordDiagnostic(string code, string outcome, string message)
    {
        lock (_diagnosticSync)
        {
            _diagnostics.Enqueue(new AppUpdateVerificationDiagnostic(
                DateTimeOffset.UtcNow,
                code,
                outcome,
                message.Length <= 256 ? message : message[..256]));
            while (_diagnostics.Count > 32) _diagnostics.Dequeue();
        }
    }

    private static string UpdateErrorCode(Exception exception)
    {
        if (exception is AppUpdateVerificationException verificationException)
            return verificationException.DiagnosticCode;

        var message = exception.Message;
        if (message.Contains("publisher", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Authenticode", StringComparison.OrdinalIgnoreCase))
            return "LLWM-UPDATE-PUBLISHER";
        if (message.Contains("signature", StringComparison.OrdinalIgnoreCase)
            || message.Contains("signing key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("manifest", StringComparison.OrdinalIgnoreCase))
            return "LLWM-UPDATE-MANIFEST";
        if (message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)
            || message.Contains("checksum", StringComparison.OrdinalIgnoreCase)
            || message.Contains("size mismatch", StringComparison.OrdinalIgnoreCase))
            return "LLWM-UPDATE-ASSET";
        return "LLWM-UPDATE-TRUST";
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
        catch (OperationCanceledException)
        {
            throw;
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

    private async Task DownloadAssetAsync(string assetUrl, string destination, long expectedBytes, CancellationToken cancellationToken)
    {
        if (expectedBytes <= 0)
            throw AppUpdateVerificationException.Asset("The signed update manifest did not provide a valid asset size.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, assetUrl);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } reportedBytes && reportedBytes != expectedBytes)
                throw AppUpdateVerificationException.Asset($"Update asset size mismatch. Expected {expectedBytes:N0} bytes, server reported {reportedBytes:N0} bytes.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            try
            {
                await BoundedStreamCopyService.CopyToAsync(input, output, expectedBytes, cancellationToken: cancellationToken);
            }
            catch (InvalidDataException ex)
            {
                throw AppUpdateVerificationException.Asset(ex.Message);
            }
        }
        catch
        {
            try { File.Delete(destination); }
            catch (Exception deleteEx)
            {
                Trace.TraceWarning($"Could not delete failed update download {destination}: {deleteEx.Message}");
            }
            throw;
        }
    }

    private static string PreparePortableExe(string assetPath, string stageRoot)
    {
        if (Path.GetExtension(assetPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ValidateStagedExe(assetPath);
            return assetPath;
        }

        if (!Path.GetExtension(assetPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw AppUpdateVerificationException.Asset("The update asset must be a portable .exe or .zip release artifact.");

        var extractRoot = Path.Combine(stageRoot, "extracted");
        if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, recursive: true);
        Directory.CreateDirectory(extractRoot);
        ArchiveSafetyService.ValidateZipArchiveEntries(assetPath, extractRoot);
        ZipFile.ExtractToDirectory(assetPath, extractRoot);
        var stagedExe = Directory.EnumerateFiles(extractRoot, PortableExeName, SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw AppUpdateVerificationException.Asset($"The update archive does not contain {PortableExeName}.");
        ValidateStagedExe(stagedExe);
        return stagedExe;
    }

    private static void ValidateStagedExe(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length < 1024 * 1024)
            throw AppUpdateVerificationException.Asset("The downloaded update does not look like a valid app executable.");
    }

    private static string FindStagedControlCli(string stagedExe)
    {
        var directory = Path.GetDirectoryName(stagedExe);
        if (string.IsNullOrWhiteSpace(directory)) return "";
        var path = Path.Combine(directory, ControlCliExeName);
        if (!File.Exists(path)) return "";
        if (new FileInfo(path).Length < 64 * 1024)
            throw AppUpdateVerificationException.Asset("The downloaded update contains an invalid llwmctl executable.");
        return path;
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
