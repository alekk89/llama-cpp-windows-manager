namespace LocalLlmConsole.Services;

// Raised when the gateway can no longer write to, or read from, the client
// connection. HttpListener exposes no disconnect notification, so a disconnect
// is observed only on the next client read or write.
public sealed class ModelGatewayClientDisconnectedException : Exception
{
    public ModelGatewayClientDisconnectedException(Exception innerException)
        : base("The gateway client disconnected.", innerException)
    {
    }

    public static bool IsClientConnectionFailure(Exception exception)
        => exception is ModelGatewayClientDisconnectedException or HttpListenerException;

    internal static bool IsClientStreamFailure(Exception exception)
        => exception is HttpListenerException or IOException or ObjectDisposedException;
}
