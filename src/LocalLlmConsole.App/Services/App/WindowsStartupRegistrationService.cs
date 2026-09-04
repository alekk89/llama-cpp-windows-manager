namespace LocalLlmConsole.Services;

public sealed record WindowsStartupRegistrationResult(
    bool Success,
    string StatusMessage);

public sealed class WindowsStartupRegistrationService
{
    public const string StartupValueName = "LlamaCppWindowsManager";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly Func<string?> _readStartupCommand;
    private readonly Action<string> _writeStartupCommand;
    private readonly Action _deleteStartupCommand;
    private readonly Func<string> _executablePath;

    public WindowsStartupRegistrationService()
        : this(
            ReadRegistryStartupCommand,
            WriteRegistryStartupCommand,
            DeleteRegistryStartupCommand,
            CurrentExecutablePath)
    {
    }

    public WindowsStartupRegistrationService(
        Func<string?> readStartupCommand,
        Action<string> writeStartupCommand,
        Action deleteStartupCommand,
        Func<string> executablePath)
    {
        _readStartupCommand = readStartupCommand ?? throw new ArgumentNullException(nameof(readStartupCommand));
        _writeStartupCommand = writeStartupCommand ?? throw new ArgumentNullException(nameof(writeStartupCommand));
        _deleteStartupCommand = deleteStartupCommand ?? throw new ArgumentNullException(nameof(deleteStartupCommand));
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
    }

    public AppSettings Reconcile(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            return settings with { StartWithWindows = IsEnabled() };
        }
        catch
        {
            return settings;
        }
    }

    public bool IsEnabled()
        => string.Equals(_readStartupCommand()?.Trim(), StartupCommand(), StringComparison.OrdinalIgnoreCase);

    public WindowsStartupRegistrationResult Apply(bool startWithWindows)
    {
        try
        {
            if (startWithWindows)
                _writeStartupCommand(StartupCommand());
            else if (IsEnabled())
                _deleteStartupCommand();

            return new WindowsStartupRegistrationResult(true, "");
        }
        catch (Exception ex)
        {
            return new WindowsStartupRegistrationResult(
                false,
                $"Start with Windows could not be updated: {ex.Message}");
        }
    }

    public string StartupCommand()
    {
        var path = _executablePath();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Application executable path could not be resolved.");

        return StartupCommandForExecutable(path);
    }

    public static string StartupCommandForExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is required.", nameof(executablePath));

        return $"\"{executablePath.Replace("\"", "", StringComparison.Ordinal)}\"";
    }

    private static string? ReadRegistryStartupCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(StartupValueName) as string;
    }

    private static void WriteRegistryStartupCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup registry key could not be opened.");
        key.SetValue(StartupValueName, command, RegistryValueKind.String);
    }

    private static void DeleteRegistryStartupCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(StartupValueName, throwOnMissingValue: false);
    }

    private static string CurrentExecutablePath()
        => Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? "";
}
