namespace LocalLlmConsole.Services;

public static class HttpListenerAcceptLoop
{
    private const int MaximumConsecutiveErrors = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    public static async Task RunAsync(
        HttpListener listener,
        Action<HttpListenerContext, CancellationToken> queueRequest,
        Action<Exception> reportListenerError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(queueRequest);
        ArgumentNullException.ThrowIfNull(reportListenerError);

        await RunCoreAsync(
            listener.GetContextAsync,
            () => listener.IsListening,
            listener.Stop,
            queueRequest,
            reportListenerError,
            RetryDelay,
            cancellationToken);
    }

    internal static async Task RunCoreAsync<TContext>(
        Func<Task<TContext>> acceptContext,
        Func<bool> isListening,
        Action stopListener,
        Action<TContext, CancellationToken> queueRequest,
        Action<Exception> reportListenerError,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acceptContext);
        ArgumentNullException.ThrowIfNull(isListening);
        ArgumentNullException.ThrowIfNull(stopListener);
        ArgumentNullException.ThrowIfNull(queueRequest);
        ArgumentNullException.ThrowIfNull(reportListenerError);
        if (retryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));

        var consecutiveErrors = 0;
        while (!cancellationToken.IsCancellationRequested && isListening())
        {
            TContext context;
            try
            {
                context = await acceptContext();
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && isListening())
            {
                reportListenerError(ex);
                if (++consecutiveErrors >= MaximumConsecutiveErrors)
                {
                    stopListener();
                    throw new InvalidOperationException(
                        $"The HTTP listener stopped after {MaximumConsecutiveErrors} consecutive accept failures.",
                        ex);
                }
                await Task.Delay(retryDelay, cancellationToken);
                continue;
            }

            queueRequest(context, cancellationToken);
            consecutiveErrors = 0;
        }
    }
}
