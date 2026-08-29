using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace LocalLlmConsole;

internal static class TrayPopupActivationService
{
    public static void ActivateForOutsideDismissal(ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!menu.IsOpen) return;
            if (PresentationSource.FromVisual(menu) is HwndSource source)
                _ = SetForegroundWindow(source.Handle);
            _ = menu.Focus();
        });
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
