using System.Windows.Threading;

namespace LocalLlmConsole;

internal static class WindowCloseScheduler
{
    public static void Schedule(Dispatcher dispatcher, Action close)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(close);
        dispatcher.BeginInvoke(close, DispatcherPriority.ApplicationIdle);
    }
}
