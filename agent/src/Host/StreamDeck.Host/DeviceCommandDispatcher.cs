using StreamDeck.Protocol;

namespace StreamDeck.Host;

public sealed class DeviceCommandDispatcher(
    IHostMediaController mediaController,
    IHostAudioController audioController,
    IHostShortcutExecutor shortcutExecutor,
    IHostSystemCommandExecutor systemCommandExecutor,
    TimeProvider? timeProvider = null,
    Action<string>? diagnostics = null)
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task DispatchAsync(ProtocolEnvelope envelope, IProtocolSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(sink);

        switch (envelope.Type)
        {
            case "diagnostic.event":
                var trace = ProtocolSerializer.DeserializeData<DeviceDiagnosticEvent>(envelope);
                // Accept only bounded identifiers, never arbitrary device log text.
                static string Safe(string? value) => new((value ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(64).ToArray());
                diagnostics?.Invoke($"device stage={Safe(trace.Stage)} correlation={Safe(trace.Correlation)} deviceMs={trace.DeviceMs} transport={Safe(trace.Transport)}");
                break;
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
                if (audioController.Snapshot is { } audio)
                    await sink.SendTextAsync(ProtocolStateMapper.CreateAudioUpdate(audio, envelope.RequestId), cancellationToken).ConfigureAwait(false);
                break;

            case ProtocolConstants.MessageTypes.AudioToggleMute:
                await AckCommandAsync(envelope, sink, () => audioController.ToggleMuteAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                if (audioController.Snapshot is { } muteSnapshot)
                    await sink.SendTextAsync(ProtocolStateMapper.CreateAudioUpdate(muteSnapshot, envelope.RequestId), cancellationToken).ConfigureAwait(false);
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

    private async Task AckCommandAsync(
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
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        diagnostics?.Invoke($"request={envelope.RequestId} stage=command_received type={envelope.Type}");
        string errorCode = "commandFailed";
        try
        {
            diagnostics?.Invoke($"request={envelope.RequestId} stage=execution_started");
            success = await command().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            success = false;
            errorCode = error switch {
                HostCommandException commandError => commandError.Code,
                FileNotFoundException => "targetNotFound",
                System.ComponentModel.Win32Exception { NativeErrorCode: 2 or 3 } => "targetNotFound",
                System.ComponentModel.Win32Exception { NativeErrorCode: 5 } => "accessDenied",
                System.ComponentModel.Win32Exception => "actionUnavailable",
                UnauthorizedAccessException => "accessDenied",
                NotSupportedException => "actionUnavailable",
                ArgumentException => "invalidConfiguration",
                _ => "executionFailed"
            };
        }
        diagnostics?.Invoke($"request={envelope.RequestId} stage=execution_completed success={success} elapsedMs={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}");

        if (success)
        {
            await sink.SendTextAsync(ProtocolSerializer.SerializeAck(envelope.RequestId, success: true), cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendFailureAckAsync(envelope.RequestId, errorCode, "Host command failed or is not available.", sink, cancellationToken)
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

public sealed record DeviceDiagnosticEvent(string Stage, string Correlation, long DeviceMs, string Transport);

public sealed class HostCommandException(string code) : Exception("Host command could not be completed.")
{
    public string Code { get; } = code;
}
