namespace LocalLlmConsole.Services;

public readonly record struct ContainedPath(string Root, string Target);

public static class PathContainmentGuard
{
    public static ContainedPath ResolveDescendant(string root, string target, string outsideRootMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(outsideRootMessage);

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullTarget = Path.GetFullPath(target);
        var relative = Path.GetRelativePath(fullRoot, fullTarget);
        if (string.IsNullOrWhiteSpace(relative)
            || string.Equals(relative, ".", StringComparison.Ordinal)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidOperationException(outsideRootMessage);

        return new ContainedPath(fullRoot, fullTarget);
    }

    public static void RejectReparsePointAncestors(
        ContainedPath path,
        bool includeExistingTarget,
        string reparsePointMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reparsePointMessage);

        var current = includeExistingTarget && (File.Exists(path.Target) || Directory.Exists(path.Target))
            ? path.Target
            : Path.GetDirectoryName(path.Target);
        while (!string.IsNullOrWhiteSpace(current) && IsInsideOrEqual(path.Root, current))
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(reparsePointMessage);
            }

            if (PathsEqual(current, path.Root)) return;
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool IsInsideOrEqual(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return string.Equals(relative, ".", StringComparison.Ordinal)
               || (!string.Equals(relative, "..", StringComparison.Ordinal)
                   && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                   && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                   && !Path.IsPathRooted(relative));
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
