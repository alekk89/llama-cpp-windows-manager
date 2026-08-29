namespace LocalLlmConsole.Services;

public enum AppUpdateFailureKind
{
    Trust,
    Publisher,
    Manifest,
    Asset
}

public sealed class AppUpdateVerificationException : InvalidOperationException
{
    public AppUpdateVerificationException(
        AppUpdateFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public AppUpdateFailureKind FailureKind { get; }

    public string DiagnosticCode => FailureKind switch
    {
        AppUpdateFailureKind.Publisher => "LLWM-UPDATE-PUBLISHER",
        AppUpdateFailureKind.Manifest => "LLWM-UPDATE-MANIFEST",
        AppUpdateFailureKind.Asset => "LLWM-UPDATE-ASSET",
        _ => "LLWM-UPDATE-TRUST"
    };

    public static AppUpdateVerificationException Trust(string message, Exception? innerException = null)
        => new(AppUpdateFailureKind.Trust, message, innerException);

    public static AppUpdateVerificationException Publisher(string message, Exception? innerException = null)
        => new(AppUpdateFailureKind.Publisher, message, innerException);

    public static AppUpdateVerificationException Manifest(string message, Exception? innerException = null)
        => new(AppUpdateFailureKind.Manifest, message, innerException);

    public static AppUpdateVerificationException Asset(string message, Exception? innerException = null)
        => new(AppUpdateFailureKind.Asset, message, innerException);
}
