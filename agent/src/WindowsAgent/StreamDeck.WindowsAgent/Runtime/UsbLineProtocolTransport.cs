using System.Text;
using StreamDeck.Host;

namespace StreamDeck.WindowsAgent.Runtime;

public interface IUsbDuplexStream : IAsyncDisposable
{
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
}

public interface IUsbIncomingObserver
{
    void TrackIncoming(string message);
}

public sealed class UsbLineProtocolTransport(
    IUsbDuplexStream stream,
    DeviceProtocolSession session,
    IProtocolSink sink,
    int maxFrameBytes = 16 * 1024) : IAsyncDisposable
{
    private readonly byte[] readBuffer = new byte[512];
    private readonly MemoryStream frame = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private bool drainingOverflow;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                continue;
            }
            for (var index = 0; index < read; index++)
            {
                var value = readBuffer[index];
                if (value == '\n')
                {
                    await CompleteFrameAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (drainingOverflow) continue;
                if (value == '\r') continue;
                if (frame.Length >= maxFrameBytes)
                {
                    frame.SetLength(0);
                    drainingOverflow = true;
                    await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("degraded", "USB frame is too large."), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                frame.WriteByte(value);
            }
        }
    }

    public async ValueTask SendFrameAsync(string message, CancellationToken cancellationToken = default)
    {
        var bytes = StrictUtf8.GetBytes(message + "\r\n");
        if (bytes.Length > maxFrameBytes + 2) throw new InvalidOperationException("USB frame is too large.");
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task CompleteFrameAsync(CancellationToken cancellationToken)
    {
        if (drainingOverflow)
        {
            drainingOverflow = false;
            frame.SetLength(0);
            return;
        }
        if (frame.Length == 0) return;
        string message;
        try
        {
            message = StrictUtf8.GetString(frame.GetBuffer(), 0, checked((int)frame.Length));
        }
        catch (DecoderFallbackException)
        {
            frame.SetLength(0);
            await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("degraded", "USB frame is not valid UTF-8."), cancellationToken).ConfigureAwait(false);
            return;
        }
        frame.SetLength(0);
        if (sink is IUsbIncomingObserver observer)
        {
            observer.TrackIncoming(message);
        }

        await session.HandleTextMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        frame.Dispose();
        writeLock.Dispose();
        await stream.DisposeAsync().ConfigureAwait(false);
    }
}

public sealed class UsbProtocolSink(Func<string, CancellationToken, ValueTask> send) : IProtocolSink
{
    public ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default) => send(message, cancellationToken);
}
