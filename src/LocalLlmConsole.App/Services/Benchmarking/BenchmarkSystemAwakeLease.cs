using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LocalLlmConsole.Services;

public sealed class BenchmarkSystemAwakeLease : IDisposable
{
    private const uint ContextVersion = 0;
    private const uint SimpleString = 1;
    private readonly SafeFileHandle? _handle;
    private bool _disposed;

    private BenchmarkSystemAwakeLease(SafeFileHandle? handle) => _handle = handle;

    public static BenchmarkSystemAwakeLease Acquire(bool enabled)
    {
        if (!enabled || !OperatingSystem.IsWindows()) return new BenchmarkSystemAwakeLease(null);
        var reason = Marshal.StringToHGlobalUni("llama.cpp Windows Manager benchmark in progress");
        try
        {
            var context = new ReasonContext
            {
                Version = ContextVersion,
                Flags = SimpleString,
                SimpleReasonString = reason
            };
            var handle = PowerCreateRequest(ref context);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new InvalidOperationException($"PowerCreateRequest failed: {Marshal.GetLastWin32Error()}");
            }
            if (!PowerSetRequest(handle, PowerRequestType.SystemRequired))
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new InvalidOperationException($"PowerSetRequest failed: {error}");
            }
            return new BenchmarkSystemAwakeLease(handle);
        }
        finally
        {
            Marshal.FreeHGlobal(reason);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle is null) return;
        _ = PowerClearRequest(_handle, PowerRequestType.SystemRequired);
        _handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public nint SimpleReasonString;
    }

    private enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(SafeFileHandle powerRequest, PowerRequestType requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(SafeFileHandle powerRequest, PowerRequestType requestType);
}
