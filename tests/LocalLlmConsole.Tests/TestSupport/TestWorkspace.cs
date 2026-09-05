using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;

/// <summary>Owns only the directories allocated by one test, including parallel helper calls.</summary>
internal sealed class TestWorkspace
{
    private readonly object _gate = new();
    private readonly string _allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LocalLlmConsole.Tests"))
        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    private string? _root;
    private bool _completed;

    internal string CreateDirectory()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_completed, this);
            _root ??= Path.Combine(_allowedRoot, "test-" + Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    internal void Complete(bool passed, Action<string> report)
    {
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            if (_root is null) return;
            if (!passed)
            {
                report($"Test workspace retained after failure or incomplete execution: {_root}");
                return;
            }

            var resolved = Path.GetFullPath(_root);
            if (!resolved.StartsWith(_allowedRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("test-", StringComparison.Ordinal))
                throw new InvalidOperationException($"Refusing to clean an unowned test workspace: {resolved}");
            try
            {
                if (Directory.Exists(resolved))
                {
                    if ((File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The test workspace root was replaced by a reparse point.");
                    ReleaseDatabasePools(resolved);
                    DeleteWithBoundedRetry(resolved);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report($"Test workspace cleanup failed; artifacts retained at {resolved}: {ex.Message}");
                throw;
            }
        }
    }

    private static void ReleaseDatabasePools(string root)
    {
        // StateStore uses the default pool; a few migration checks use a read-only pool.
        // Never clear other concurrently running tests' pools with ClearAllPools.
        foreach (var database in Directory.EnumerateFiles(root, "*.db", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        }))
        {
            foreach (var readOnly in new[] { false, true })
            {
                var builder = new SqliteConnectionStringBuilder { DataSource = database };
                if (readOnly) builder.Mode = SqliteOpenMode.ReadOnly;
                using var connection = new SqliteConnection(builder.ToString());
                SqliteConnection.ClearPool(connection);
            }
        }
    }

    private static void DeleteWithBoundedRetry(string root)
    {
        // Windows can briefly retain an executable mapping after a supervised child exits.
        // Retry only an actual sharing/access failure, never delay every successful cleanup.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }
}
