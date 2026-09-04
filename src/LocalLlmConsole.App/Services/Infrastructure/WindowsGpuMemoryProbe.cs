using System.Runtime.InteropServices;

namespace LocalLlmConsole.Services;

internal interface IGpuMemoryProbe : IDisposable
{
    IReadOnlyList<GpuMemorySample> Read();
}

// Windows WDDM counters cover NVIDIA, AMD and Intel. Match by adapter LUID, never by
// display order or a truncated Win32_VideoController.AdapterRAM value.
internal sealed class WindowsGpuMemoryProbe : IGpuMemoryProbe
{
    private readonly IReadOnlyList<GpuMemorySample> _adapters;
    private IntPtr _query;
    private IntPtr _dedicated;
    private IntPtr _shared;

    public WindowsGpuMemoryProbe()
    {
        _adapters = WindowsGpuAdapterCatalog.Read();
        if (PdhOpenQueryW(null, UIntPtr.Zero, out _query) != 0) return;
        _ = PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", UIntPtr.Zero, out _dedicated);
        _ = PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Shared Usage", UIntPtr.Zero, out _shared);
        _ = PdhCollectQueryData(_query);
    }

    public IReadOnlyList<GpuMemorySample> Read()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return _adapters;
        return Combine(_adapters, ReadCounter(_dedicated), ReadCounter(_shared));
    }

    internal static IReadOnlyList<GpuMemorySample> Combine(
        IReadOnlyList<GpuMemorySample> adapters,
        IReadOnlyDictionary<string, long> dedicated,
        IReadOnlyDictionary<string, long> shared)
    {
        return adapters.Select(adapter => adapter with
        {
            DedicatedUsedMiB = Sum(adapter.DeviceId, dedicated),
            SharedUsedMiB = Sum(adapter.DeviceId, shared)
        }).ToArray();
    }

    private static long? Sum(string luid, IReadOnlyDictionary<string, long> counters)
    {
        var readings = counters.Where(pair => pair.Key.StartsWith(luid + "_phys_", StringComparison.OrdinalIgnoreCase)
                                             && pair.Value >= 0).Select(pair => pair.Value).ToArray();
        return readings.Length == 0 ? null : (long)Math.Ceiling(readings.Sum(value => (double)value) / (1024 * 1024));
    }

    private static Dictionary<string, long> ReadCounter(IntPtr counter)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (counter == IntPtr.Zero) return result;
        const uint format = 0x400; // PDH_FMT_LARGE
        uint bytes = 0;
        if (PdhGetFormattedCounterArrayW(counter, format, ref bytes, out _, IntPtr.Zero) != 0x800007D2
            || bytes == 0 || bytes > 16 * 1024 * 1024) return result; // PDH_MORE_DATA
        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, format, ref bytes, out var count, buffer) != 0) return result;
            var stride = Marshal.SizeOf<CounterItem>();
            if ((long)count * stride > bytes) return result;
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * stride));
                if (item.Value.Status <= 1 && item.Value.Value >= 0 && Marshal.PtrToStringUni(item.Name) is { } name)
                    result[name] = item.Value.Value;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) _ = PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue
    {
        public uint Status;
        public long Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
}
