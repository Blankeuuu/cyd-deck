using System.Net.WebSockets;
using Xunit;

namespace StreamDeck.Host.Tests;

public sealed class WebSocketSerializationTests
{
    [Fact]
    public async Task ConcurrentStateAndAckNeverWriteOverlappingWebSocketFrames()
    {
        using var socket = new GatedSocket();
        var sink = new WebSocketProtocolSink(socket);
        var token = TestContext.Current.CancellationToken;
        var first = sink.SendTextAsync("state", token).AsTask();
        await socket.Started.Task.WaitAsync(token);
        var second = sink.SendTextAsync("ack", token).AsTask();
        try { Assert.Equal(1, socket.MaxActive); }
        finally { socket.Release.TrySetResult(); }
        await Task.WhenAll(first, second);
    }

    private sealed class GatedSocket : WebSocket
    {
        private int active;
        public int MaxActive { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token) => SendGatedAsync(token);
        public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token) => new(SendGatedAsync(token));
        private async Task SendGatedAsync(CancellationToken token) {
            MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref active));
            Started.TrySetResult();
            try { await Release.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
