using System.Threading;

namespace LocalLlmConsole.Services;

public static class FileSystemSafetyService
{
    public static bool IsSafeChildDirectory(string rootDirectory, string targetDirectory)
    {
        var full = Path.GetFullPath(targetDirectory);
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = Path.GetRelativePath(root, full);
        return !string.IsNullOrWhiteSpace(relative)
            && !string.Equals(relative, ".", StringComparison.Ordinal)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative)
            && !HasReparsePointAncestor(root, full)
            && (!Directory.Exists(full) || !ContainsDirectoryReparsePoint(full));
    }

    public static void ClearDirectoryContents(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            ClearDeleteBlockingAttributes(file);
            File.Delete(file);
        }
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            DeleteDirectoryTree(directory);
    }

    public static long DirectorySize(string root, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return 0;
        long size = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        size = checked(size + new FileInfo(file).Length);
                    }
                    catch (OverflowException)
                    {
                        return long.MaxValue;
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"Skipping file during directory size calculation: {file}. {ex.Message}");
                    }
                }

                foreach (var directory in Directory.EnumerateDirectories(current, "*", options))
                    pending.Push(directory);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Skipping directory during size calculation: {current}. {ex.Message}");
            }
        }
        return size;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static void DeleteDirectoryTree(string folder)
    {
        var full = Path.GetFullPath(folder);
        ResetDirectoryTreeAttributes(full);

        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Directory.Delete(full, recursive: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(150 * attempt);
                ResetDirectoryTreeAttributes(full);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(150 * attempt);
                ResetDirectoryTreeAttributes(full);
            }
        }

        Directory.Delete(full, recursive: true);
    }

    public static bool ContainsDirectoryReparsePoint(string root)
    {
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return true;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            };
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateDirectories(pending.Pop(), "*", options))
                {
                    try
                    {
                        if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return true;
                        pending.Push(entry);
                    }
                    catch
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    public static bool HasReparsePointAncestor(string root, string target)
    {
        try
        {
            var current = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
            while (!string.IsNullOrWhiteSpace(current)
                && Path.GetRelativePath(root, current) is var relative
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                && !Path.IsPathRooted(relative))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                if (string.Equals(Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
                    return false;
                current = Path.GetDirectoryName(current);
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static void ResetDirectoryTreeAttributes(string root)
    {
        if (!Directory.Exists(root)) return;

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            ClearDeleteBlockingAttributes(current);

            foreach (var file in Directory.EnumerateFiles(current, "*", SafeDeleteEnumerationOptions()))
                ClearDeleteBlockingAttributes(file);

            foreach (var directory in Directory.EnumerateDirectories(current, "*", SafeDeleteEnumerationOptions()))
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Refusing to delete through a symlinked directory or junction.");

                ClearDeleteBlockingAttributes(directory);
                pending.Push(directory);
            }
        }
    }

    private static void ClearDeleteBlockingAttributes(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            var cleaned = attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
            if (cleaned != attributes)
                File.SetAttributes(path, cleaned);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not clear delete-blocking attributes for {path}: {ex.Message}");
            // Deletion will surface any remaining access issue with the original path.
        }
    }

    private static EnumerationOptions SafeDeleteEnumerationOptions() => new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0
    };
}
