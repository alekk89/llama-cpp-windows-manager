using System.Text.RegularExpressions;

namespace LocalLlmConsole.Services;

/// <summary>
/// Reads SHA-256 digests from checksum companion files in GNU (<c>hash  [*]name</c>),
/// BSD (<c>SHA256 (name) = hash</c>), or single-digest form. Shared by runtime package
/// and application update verification.
/// </summary>
public static class Sha256ChecksumFile
{
    private static readonly Regex BsdChecksumLine = new(
        @"^SHA-?256\s*\((?<name>.+)\)\s*=\s*(?<hash>[a-f0-9]{64})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GnuChecksumLine = new(
        @"^(?:sha256:)?(?<hash>[a-f0-9]{64})(?:\s+\*?(?<name>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the lower-case digest for <paramref name="assetName"/>, or an empty string.
    /// Named entries must match the asset file name exactly (case-insensitively); directory
    /// prefixes are ignored. A nameless digest is accepted only when it is the file's sole
    /// entry. Without an asset name the first entry is returned.
    /// </summary>
    public static string FindSha256(string? checksumText, string? assetName = "")
    {
        var entries = Parse(checksumText).ToArray();
        if (string.IsNullOrWhiteSpace(assetName))
            return entries.FirstOrDefault().Hash ?? "";

        var expectedName = assetName.Trim();
        foreach (var (hash, name) in entries)
        {
            if (name is not null && string.Equals(FileNameOf(name), expectedName, StringComparison.OrdinalIgnoreCase))
                return hash;
        }

        return entries is [{ Name: null } sole] ? sole.Hash : "";
    }

    private static IEnumerable<(string Hash, string? Name)> Parse(string? checksumText)
    {
        foreach (var rawLine in (checksumText ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var match = BsdChecksumLine.Match(line);
            if (!match.Success) match = GnuChecksumLine.Match(line);
            if (!match.Success) continue;
            var name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : null;
            yield return (match.Groups["hash"].Value.ToLowerInvariant(), string.IsNullOrEmpty(name) ? null : name);
        }
    }

    private static string FileNameOf(string path)
    {
        var normalized = path.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }
}
