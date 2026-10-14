namespace LocalLlmConsole.Services;

public static class ProfileBundleFileService
{
    public static async Task<ProfileBundle> ReadAsync(string path)
    {
        if (new FileInfo(path).Length > ProfileBundleCodec.MaximumBytes)
            throw new InvalidOperationException("Profile bundle exceeds 4 MiB.");
        return ProfileBundleCodec.Read(await File.ReadAllTextAsync(path));
    }

    public static Task WriteAsync(string path, ProfileBundle bundle)
        => File.WriteAllTextAsync(path, ProfileBundleCodec.Write(bundle), new UTF8Encoding(false));
}
