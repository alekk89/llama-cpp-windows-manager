
namespace LocalLlmConsole.Services;

public static class CacheMaintenanceService
{
    public static bool IsSafeCacheRoot(string workspaceRoot, string cacheRoot)
    {
        var workspace = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var cache = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return FileSystemSafetyService.IsSafeChildDirectory(workspace, cache);
    }

    public static long Size(string cacheRoot)
        => FileSystemSafetyService.DirectorySize(cacheRoot);

    public static Task<long> SizeAsync(string cacheRoot, CancellationToken cancellationToken = default)
        => Task.Run(
            () => FileSystemSafetyService.DirectorySize(cacheRoot, cancellationToken),
            cancellationToken);

    public static void ClearSafeCacheRoot(string workspaceRoot, string cacheRoot)
    {
        if (!IsSafeCacheRoot(workspaceRoot, cacheRoot))
            throw new InvalidOperationException("Refusing to clear a cache folder outside the app workspace or through a reparse point.");

        FileSystemSafetyService.ClearDirectoryContents(cacheRoot);
        Directory.CreateDirectory(cacheRoot);
    }
}
