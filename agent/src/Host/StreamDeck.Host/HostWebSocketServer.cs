using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace StreamDeck.Host;

public sealed class HostWebSocketServer(
    TransportOptions options,
    Func<IProtocolSink, string?, string?, DeviceProtocolSession> sessionFactory)
{
    private readonly HttpListener listener = new();

    public HostWebSocketServer(
        TransportOptions options,
        Func<IProtocolSink, string?, DeviceProtocolSession> sessionFactory)
        : this(options, (sink, token, _) => sessionFactory(sink, token))
    {
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        listener.Prefixes.Add(CreatePrefix(options));
        listener.Start();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleContextAsync(context, cancellationToken), CancellationToken.None);
            }
        }
        finally
        {
            listener.Close();
        }
    }

    public static string CreatePrefix(TransportOptions options)
    {
        var host = options.BindAddress.Equals(IPAddress.Any) ? "+" : options.BindAddress.ToString();
        return $"http://{host}:{options.Port}/";
    }

    public static bool IsAllowedRemoteEndPoint(EndPoint? remoteEndPoint, TransportOptions options)
    {
        if (options.AllowRemoteClients)
        {
            return true;
        }

        return remoteEndPoint is IPEndPoint ipEndPoint && IPAddress.IsLoopback(ipEndPoint.Address);
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!IsAllowedRemoteEndPoint(context.Request.RemoteEndPoint, options)
            || !string.Equals(context.Request.Url?.AbsolutePath, options.WebSocketPath, StringComparison.Ordinal)
            || !context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.Close();
            return;
        }

        var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        using var _ = webSocketContext.WebSocket;
        var sink = new WebSocketProtocolSink(webSocketContext.WebSocket);
        var session = sessionFactory(sink, context.Request.QueryString["token"], context.Request.RemoteEndPoint?.ToString());
        await ReceiveLoopAsync(webSocketContext.WebSocket, session, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(WebSocket webSocket, DeviceProtocolSession session, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(options.MaxMessageBytes, 16 * 1024)];
        var builder = new StringBuilder();

        while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Only text JSON messages are supported.", cancellationToken).ConfigureAwait(false);
                return;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (builder.Length > options.MaxMessageBytes)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message is too large.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            await session.HandleTextMessageAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
            builder.Clear();
        }
    }
}
