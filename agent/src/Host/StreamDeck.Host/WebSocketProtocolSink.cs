using System.Net.WebSockets;
using System.Text;

namespace StreamDeck.Host;

public sealed class WebSocketProtocolSink(WebSocket webSocket) : IProtocolSink
{
    private readonly SemaphoreSlim writeGate = new(1, 1);
    public async ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        } finally { writeGate.Release(); }
    }
}
