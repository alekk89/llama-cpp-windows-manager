namespace LocalLlmConsole.Services;

public sealed class ActiveRuntimeSessionStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _workspaceRoot;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private sealed record RecoveryDocument(int Version, IReadOnlyList<ActiveRuntimeSession> Sessions);

    public ActiveRuntimeSessionStore(string workspaceRoot)
    {
        _workspaceRoot = workspaceRoot;
    }

    public string SessionPath => Path.Combine(_workspaceRoot, "state", "active-runtime-session.json");
    public string SessionsPath => Path.Combine(_workspaceRoot, "state", "active-runtime-sessions.json");
    public string BackupPath => SessionsPath + ".bak";

    public async Task<IReadOnlyList<ActiveRuntimeSession>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await TryReadSessionsFileAsync(SessionsPath, cancellationToken);
        if (sessions is not null) return sessions;

        sessions = await TryReadSessionsFileAsync(BackupPath, cancellationToken);
        if (sessions is not null) return sessions;

        var legacy = await TryReadLegacyFileAsync(cancellationToken);
        return legacy is null ? [] : [legacy];
    }

    public async Task<ActiveRuntimeSession?> TryReadAsync(CancellationToken cancellationToken = default)
    {
        var sessions = await ReadAllAsync(cancellationToken);
        return sessions.FirstOrDefault(session => session.IsSelected) ?? sessions.FirstOrDefault();
    }

    public async Task SaveAsync(ActiveRuntimeSession session, CancellationToken cancellationToken = default)
        => await SaveAllAsync([session], cancellationToken);

    public async Task SaveAllAsync(IReadOnlyList<ActiveRuntimeSession> sessions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        await _writeGate.WaitAsync(cancellationToken);
        var temporaryPath = SessionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionsPath)!);
            var protectedSessions = sessions.Select(Protect).ToArray();
            var json = JsonSerializer.Serialize(new RecoveryDocument(CurrentVersion, protectedSessions), JsonOptions);
            await WriteDurablyAsync(temporaryPath, json, cancellationToken);

            if (File.Exists(SessionsPath))
            {
                var backupPath = await ContainsOnlyProtectedSecretsAsync(SessionsPath, cancellationToken)
                    ? BackupPath
                    : null;
                if (backupPath is not null && File.Exists(backupPath)) File.Delete(backupPath);
                File.Replace(temporaryPath, SessionsPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, SessionsPath);
            }

            DeleteBestEffort(SessionPath);
        }
        finally
        {
            DeleteBestEffort(temporaryPath);
            _writeGate.Release();
        }
    }

    public void Clear()
    {
        DeleteBestEffort(SessionPath);
        DeleteBestEffort(SessionsPath);
        DeleteBestEffort(BackupPath);

        var stateDirectory = Path.GetDirectoryName(SessionsPath)!;
        if (!Directory.Exists(stateDirectory)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         stateDirectory,
                         Path.GetFileName(SessionsPath) + ".*.tmp",
                         SearchOption.TopDirectoryOnly))
            {
                DeleteBestEffort(path);
            }
        }
        catch
        {
            // Best effort only; stale sessions are revalidated on next startup.
        }
    }

    private static ActiveRuntimeSession Protect(ActiveRuntimeSession session)
        => session with
        {
            LaunchSettings = session.LaunchSettings with
            {
                ModelApiKey = SecretProtector.ProtectSetting(session.LaunchSettings.ModelApiKey),
                ModelApiKeyBackup = ""
            }
        };

    private static ActiveRuntimeSession Unprotect(ActiveRuntimeSession session)
        => session with
        {
            LaunchSettings = session.LaunchSettings with
            {
                ModelApiKey = SecretProtector.UnprotectSetting(session.LaunchSettings.ModelApiKey),
                ModelApiKeyBackup = ""
            }
        };

    private static async Task<IReadOnlyList<ActiveRuntimeSession>?> TryReadSessionsFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            try
            {
                var document = JsonSerializer.Deserialize<RecoveryDocument>(json);
                if (document is not null && document.Version == CurrentVersion)
                    return document.Sessions.Select(Unprotect).ToArray();
            }
            catch (JsonException)
            {
                // Fall through to the pre-versioned list format.
            }

            var legacySessions = JsonSerializer.Deserialize<List<ActiveRuntimeSession>>(json);
            return legacySessions?.Select(Unprotect).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ActiveRuntimeSession?> TryReadLegacyFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SessionPath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(SessionPath, cancellationToken);
            var session = JsonSerializer.Deserialize<ActiveRuntimeSession>(json);
            return session is null ? null : Unprotect(session);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteDurablyAsync(string path, string content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task<bool> ContainsOnlyProtectedSecretsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(json);
            return !ContainsUnprotectedSecret(document.RootElement);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsUnprotectedSecret(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals(nameof(AppSettings.ModelApiKey)) ||
                     property.NameEquals(nameof(AppSettings.ModelApiKeyBackup))) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrEmpty(value) && !value.StartsWith("dpapi:v1:", StringComparison.Ordinal))
                        return true;
                }

                if (ContainsUnprotectedSecret(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (ContainsUnprotectedSecret(item)) return true;
        }

        return false;
    }

    private static void DeleteBestEffort(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort only; stale sessions are revalidated on next startup.
        }
    }
}
