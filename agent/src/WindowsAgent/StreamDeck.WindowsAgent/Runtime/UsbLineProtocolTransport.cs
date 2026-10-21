using System.Text;
using System.Text.Json;
using StreamDeck.Host;
using StreamDeck.Protocol;

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
                AgentDiagnosticLog.Trace("usb_rx_eof stage=stream_eof");
                break;
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
                    AgentDiagnosticLog.Trace($"usb_rx_error stage=frame_overflow maxBytes={maxFrameBytes}");
                    AgentDiagnosticLog.Write("USB frame rejected: frame exceeds byte limit.");
                    if (!session.IsAuthorized)
                    {
                        await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("degraded", "USB frame is too large."), cancellationToken).ConfigureAwait(false);
                    }
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
            TraceSendFrame(message, bytes.Length);
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
            AgentDiagnosticLog.Trace("usb_rx_error stage=utf8_decode error=invalid_utf8");
            AgentDiagnosticLog.Write("USB frame rejected: invalid UTF-8.");
            if (!session.IsAuthorized)
            {
                await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("degraded", "USB frame is not valid UTF-8."), cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        frame.SetLength(0);

        TraceIncomingFrame(message);

        if (sink is IUsbIncomingObserver observer)
        {
            observer.TrackIncoming(message);
        }

        await session.HandleTextMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private static void TraceSendFrame(string message, int byteCount)
    {
        if (Environment.GetEnvironmentVariable("CYD_DECK_DIAGNOSTICS") != "1") return;

        string? frameType = null;
        string? requestId = null;
        string? state = null;
        string? detail = null;

        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                frameType = typeProp.GetString();
            }
            if (root.TryGetProperty("requestId", out var reqProp) && reqProp.ValueKind == JsonValueKind.String)
            {
                requestId = reqProp.GetString();
            }
            if (frameType == "connection.state" && root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Object)
            {
                if (dataProp.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String)
                {
                    state = stateProp.GetString();
                }
                if (dataProp.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    detail = detailProp.GetString();
                }
                else if (dataProp.TryGetProperty("reason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
                {
                    detail = reasonProp.GetString();
                }
                else if (dataProp.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                {
                    detail = msgProp.GetString();
                }
            }
        }
        catch
        {
            frameType = "parse_error";
        }

        if (frameType == "connection.state")
        {
            AgentDiagnosticLog.Trace($"usb_tx type={Sanitize(frameType)} bytes={byteCount} req={Sanitize(requestId)} state={Sanitize(state)} detail={Sanitize(detail)}");
        }
        else
        {
            AgentDiagnosticLog.Trace($"usb_tx type={Sanitize(frameType)} bytes={byteCount} req={Sanitize(requestId)}");
        }
    }

    private static void TraceIncomingFrame(string message)
    {
        if (Environment.GetEnvironmentVariable("CYD_DECK_DIAGNOSTICS") != "1") return;

        var parseResult = ProtocolSerializer.Parse(message);
        if (!parseResult.IsSuccess)
        {
            AgentDiagnosticLog.Trace($"usb_rx_error stage=protocol_parse error={Sanitize(parseResult.ErrorMessage)} bytes={message.Length}");
        }
        else
        {
            var envelope = parseResult.Envelope;
            AgentDiagnosticLog.Trace($"usb_rx type={Sanitize(envelope?.Type)} bytes={message.Length} req={Sanitize(envelope?.RequestId)}");
        }
    }

    private static string Sanitize(string? input) =>
        string.IsNullOrEmpty(input) ? "none" : new string(input.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ' ').Take(64).ToArray());

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
