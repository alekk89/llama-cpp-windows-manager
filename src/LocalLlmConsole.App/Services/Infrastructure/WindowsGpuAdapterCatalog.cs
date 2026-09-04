using System.Runtime.InteropServices;

namespace LocalLlmConsole.Services;

internal static class WindowsGpuAdapterCatalog
{
    internal static IReadOnlyList<GpuMemorySample> Read()
    {
        var adapters = new List<GpuMemorySample>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        if (CreateDXGIFactory1(ref iid, out var factory) < 0) return adapters;
        try
        {
            // IUnknown (3), IDXGIObject (4), IDXGIFactory (5), EnumAdapters1.
            var enumerate = Method<EnumAdapters1>(factory, 12);
            for (uint index = 0; index < 128; index++)
            {
                if (enumerate(factory, index, out var adapter) < 0) break;
                try
                {
                    // IUnknown (3), IDXGIObject (4), IDXGIAdapter (3), GetDesc1.
                    if (Method<GetDesc1>(adapter, 10)(adapter, out var desc) < 0 || (desc.Flags & 2) != 0) continue;
                    adapters.Add(new GpuMemorySample(
                        $"luid_0x{desc.LuidHigh:x8}_0x{desc.LuidLow:x8}",
                        desc.Description,
                        (long)(desc.DedicatedVideoMemory.ToUInt64() / (1024 * 1024)), null, null));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return adapters;
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1(IntPtr adapter, out AdapterDescription description);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubsystemId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
}
