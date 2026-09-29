using StreamDeck.Protocol;

namespace StreamDeck.Host;

public sealed class DeviceCommandDispatcher(
    IHostMediaController mediaController,
    IHostAudioController audioController,
    IHostShortcutExecutor shortcutExecutor,
    IHostSystemCommandExecutor systemCommandExecutor,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task DispatchAsync(ProtocolEnvelope envelope, IProtocolSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(sink);

        switch (envelope.Type)
        {
            case ProtocolConstants.MessageTypes.MediaCommand:
                await AckCommandAsync(
                    envelope,
                    sink,
                    () => mediaController.ExecuteAsync(ProtocolSerializer.DeserializeData<MediaCommandMessage>(envelope).Command, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                break;

            case ProtocolConstants.MessageTypes.AudioSetVolume:
                await AckCommandAsync(
                    envelope,
                    sink,
                    () => audioController.SetVolumeAsync(ProtocolSerializer.DeserializeData<AudioSetVolumeMessage>(envelope).VolumePercent, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                break;

            case ProtocolConstants.MessageTypes.AudioToggleMute:
                await AckCommandAsync(envelope, sink, () => audioController.ToggleMuteAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                break;

            case ProtocolConstants.MessageTypes.ShortcutExecute:
                await AckCommandAsync(
                    envelope,
                    sink,
                    () => shortcutExecutor.ExecuteAsync(ProtocolSerializer.DeserializeData<ShortcutExecuteMessage>(envelope).ShortcutId, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                break;

            case ProtocolConstants.MessageTypes.SystemCommand:
                await AckCommandAsync(
                    envelope,
                    sink,
                    () => systemCommandExecutor.ExecuteAsync(ProtocolSerializer.DeserializeData<SystemCommandMessage>(envelope).Command, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                break;

            case ProtocolConstants.MessageTypes.Ping:
                var ping = ProtocolSerializer.DeserializeData<PingMessage>(envelope);
                await sink.SendTextAsync(
                    ProtocolSerializer.Serialize(
                        ProtocolConstants.MessageTypes.Pong,
                        requestId: null,
                        new PongMessage(ping.SentAtUnixMs, timeProvider.GetUtcNow().ToUnixTimeMilliseconds())),
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                if (!string.IsNullOrWhiteSpace(envelope.RequestId))
                {
                    await SendFailureAckAsync(envelope.RequestId, "unsupportedMessage", $"Message type '{envelope.Type}' is not handled by the host.", sink, cancellationToken)
                        .ConfigureAwait(false);
                }

                break;
        }
    }

    private static async Task AckCommandAsync(
        ProtocolEnvelope envelope,
        IProtocolSink sink,
        Func<Task<bool>> command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.RequestId))
        {
            return;
        }

        bool success;
        try
        {
            success = await command().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            success = false;
        }

        if (success)
        {
            await sink.SendTextAsync(ProtocolSerializer.SerializeAck(envelope.RequestId, success: true), cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendFailureAckAsync(envelope.RequestId, "commandFailed", "Host command failed or is not available.", sink, cancellationToken)
            .ConfigureAwait(false);
    }

    private static ValueTask SendFailureAckAsync(
        string requestId,
        string code,
        string message,
        IProtocolSink sink,
        CancellationToken cancellationToken)
    {
        return sink.SendTextAsync(
            ProtocolSerializer.SerializeAck(requestId, success: false, new ProtocolError(code, message)),
            cancellationToken);
    }
}
