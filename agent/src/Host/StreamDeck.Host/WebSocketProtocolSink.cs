using System.Net.WebSockets;
using System.Text;

namespace StreamDeck.Host;

public sealed class WebSocketProtocolSink(WebSocket webSocket) : IProtocolSink
{
    public async ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }
}
