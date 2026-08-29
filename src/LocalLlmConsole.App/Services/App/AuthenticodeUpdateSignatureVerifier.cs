using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace LocalLlmConsole.Services;

public interface IAppUpdateSignatureVerifier
{
    void Verify(string path, string expectedPublisher, string? expectedSignerPath = null);
}

internal enum AuthenticodeTrustState
{
    Unsigned,
    Valid,
    Invalid
}

public sealed class AuthenticodeUpdateSignatureVerifier : IAppUpdateSignatureVerifier
{
    private const uint WtdRevokeWholeChain = 1;
    private const uint WtdRevocationCheckChainExcludeRoot = 0x00000080;
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public void Verify(string path, string expectedPublisher, string? expectedSignerPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPublisher);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Authenticode update verification requires Windows.");
        if (!File.Exists(path))
            throw AppUpdateVerificationException.Publisher($"Signed update file was not found: {Path.GetFileName(path)}");

        var result = GetTrustResult(path);
        if (result != 0)
            throw AppUpdateVerificationException.Publisher($"Update Authenticode verification failed for '{Path.GetFileName(path)}' (0x{result:X8}).");

        using var certificate = ReadSignerCertificate(path)
            ?? throw AppUpdateVerificationException.Publisher($"Update '{Path.GetFileName(path)}' has no Authenticode signer certificate.");
        if (!IsExpectedPublisher(certificate, expectedPublisher))
        {
            throw AppUpdateVerificationException.Publisher(
                $"Update '{Path.GetFileName(path)}' is signed by unexpected publisher '{certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false)}'.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSignerPath) && File.Exists(expectedSignerPath))
        {
            using var expectedCertificate = ReadSignerCertificate(expectedSignerPath);
            if (expectedCertificate is not null && !HasSameCertificate(certificate, expectedCertificate))
            {
                throw AppUpdateVerificationException.Publisher(
                    $"Update '{Path.GetFileName(path)}' is not signed by the same certificate as '{Path.GetFileName(expectedSignerPath)}'.");
            }
        }
    }

    internal static AuthenticodeTrustState InspectTrust(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return AuthenticodeTrustState.Unsigned;
        using var certificate = ReadSignerCertificate(path);
        if (certificate is null) return AuthenticodeTrustState.Unsigned;
        return GetTrustResult(path) == 0 ? AuthenticodeTrustState.Valid : AuthenticodeTrustState.Invalid;
    }

    internal static uint GetTrustResult(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Authenticode verification requires Windows.");

        var pathPointer = IntPtr.Zero;
        var fileInfoPointer = IntPtr.Zero;
        try
        {
            pathPointer = Marshal.StringToCoTaskMemUni(Path.GetFullPath(path));
            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = pathPointer
            };
            fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2,
                RevocationChecks = WtdRevokeWholeChain,
                UnionChoice = 1,
                FileInfo = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = WtdRevocationCheckChainExcludeRoot,
                UiContext = 0
            };

            return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref trustData);
        }
        finally
        {
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPointer);
            if (pathPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    internal static bool IsExpectedPublisher(X509Certificate2 certificate, string expectedPublisher)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPublisher);
        var simpleName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        return string.Equals(simpleName, expectedPublisher, StringComparison.OrdinalIgnoreCase)
            || string.Equals(certificate.Subject, expectedPublisher, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasSameCertificate(X509Certificate2 first, X509Certificate2 second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return CryptographicOperations.FixedTimeEquals(first.RawData, second.RawData);
    }

    private static X509Certificate2? ReadSignerCertificate(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // Authenticode signer extraction requires CreateFromSignedFile.
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return X509CertificateLoader.LoadCertificate(signer.GetRawCertData());
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
