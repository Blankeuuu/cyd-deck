using System.Text.Json;
using StreamDeck.Audio;
using StreamDeck.Protocol;
using StreamDeck.Configuration;
using StreamDeck.Shortcuts;

namespace StreamDeck.Host;

public sealed class DeviceProtocolSession(
    DeviceCommandDispatcher dispatcher,
    IDeviceAuthorizer authorizer,
    IProtocolSink sink,
    string? bearerToken = null,
    string? remoteAddress = null,
    AppearanceConfiguration? appearance = null,
    DeviceShortcutConfiguration? shortcuts = null,
    Func<DeviceInfoMessage, CancellationToken, ValueTask>? deviceInfoHandler = null,
    Func<AudioSnapshot?>? initialAudioProvider = null,
    Func<ShortcutsReportMessage, CancellationToken, ValueTask>? shortcutsReportHandler = null,
    Func<DeviceShortcutConfiguration>? shortcutsProvider = null,
    Func<AppearanceConfiguration>? appearanceProvider = null,
    Func<IProtocolSink, CancellationToken, ValueTask>? initialMediaSender = null,
    Func<CoverRequestMessage, CancellationToken, ValueTask>? coverRequestHandler = null,
    Action<string>? sessionErrorHandler = null)
{
    private readonly Guid pendingSessionId = Guid.NewGuid();
    private bool authorized;
    private HelloMessage? pendingHello;
    private bool pairingChangeSubscribed;
    private readonly SemaphoreSlim shortcutExecution = new(1, 1);

    public bool IsAuthorized => authorized;

    public async Task HandleTextMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var parseResult = ProtocolSerializer.Parse(message);
        if (!parseResult.IsSuccess)
        {
            if (!authorized)
            {
                await sink.SendTextAsync(
                    ProtocolStateMapper.CreateConnectionState("degraded", parseResult.ErrorMessage),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            sessionErrorHandler?.Invoke(parseResult.ErrorMessage ?? "Malformed message payload.");

            var (safeRequestId, frameType) = TryInspectIncomingEnvelope(message);
            // Never send an ACK in response to an incoming ACK (even malformed ones); prevents ACK-of-ACK loops.
            if (safeRequestId is not null && !string.Equals(frameType, ProtocolConstants.MessageTypes.Ack, StringComparison.OrdinalIgnoreCase))
            {
                await sink.SendTextAsync(
                    ProtocolSerializer.SerializeAck(
                        safeRequestId,
                        success: false,
                        new ProtocolError("invalidPayload", parseResult.ErrorMessage ?? "Malformed message payload.")),
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var envelope = parseResult.Envelope!;
        if (envelope.Type == ProtocolConstants.MessageTypes.Hello)
        {
            await HandleHelloAsync(envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!authorized)
        {
            await HandleUnauthorizedAsync(envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (envelope.Type == ProtocolConstants.MessageTypes.Ack)
        {
            // Incoming ACK (e.g. acknowledging device.settings) is observed/ignored without reply.
            // Prevents ACK-of-ACK ping-pong loops.
            return;
        }

        if (envelope.Type == ProtocolConstants.MessageTypes.CoverRequest)
        {
            if (coverRequestHandler is not null)
                await coverRequestHandler(ProtocolSerializer.DeserializeData<CoverRequestMessage>(envelope), cancellationToken).ConfigureAwait(false);
            return;
        }
        if (envelope.Type == ProtocolConstants.MessageTypes.ShortcutExecute)
        {
            if (!await shortcutExecution.WaitAsync(0, cancellationToken).ConfigureAwait(false)) {
                await sink.SendTextAsync(ProtocolSerializer.SerializeAck(envelope.RequestId!, false,
                    new ProtocolError("busy", "Another Shortcut is executing.")), cancellationToken).ConfigureAwait(false);
                return;
            }
            _ = Task.Run(async () => {
                try { await dispatcher.DispatchAsync(envelope, sink, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch { /* Transport is gone; execution is uncertain and must never be retried. */ }
                finally { shortcutExecution.Release(); }
            }, CancellationToken.None);
            return;
        }

        if (envelope.Type == ProtocolConstants.MessageTypes.DeviceInfo)
        {
            if (deviceInfoHandler is not null)
            {
                await deviceInfoHandler(ProtocolSerializer.DeserializeData<DeviceInfoMessage>(envelope), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (envelope.Type == ProtocolConstants.MessageTypes.ShortcutsReport)
        {
            if (shortcutsReportHandler is not null)
            {
                await shortcutsReportHandler(ProtocolSerializer.DeserializeData<ShortcutsReportMessage>(envelope), cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await dispatcher.DispatchAsync(envelope, sink, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleHelloAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
    {
        var hello = ProtocolSerializer.DeserializeData<HelloMessage>(envelope);
        var presentedBearerToken = bearerToken ?? hello.BearerToken;
        var result = authorizer is PairingApprovalService pairingService
            ? await pairingService.AuthorizeAsync(hello, presentedBearerToken, remoteAddress, pendingSessionId, cancellationToken).ConfigureAwait(false)
            : await authorizer.AuthorizeAsync(hello, presentedBearerToken, cancellationToken).ConfigureAwait(false);
        authorized = result.Status == DeviceAuthorizationStatus.Authorized;

        if (authorized)
        {
            await sink.SendTextAsync(ProtocolStateMapper.CreateAgentHello(), cancellationToken).ConfigureAwait(false);
            if (ShouldEchoVerifiedUsbBearerToken(hello, presentedBearerToken))
            {
                await sink.SendTextAsync(ProtocolStateMapper.CreatePairingToken(hello.BearerToken!), cancellationToken).ConfigureAwait(false);
            }

            await SendInitialStateAsync(cancellationToken).ConfigureAwait(false);
            await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("connected", null), cancellationToken).ConfigureAwait(false);
            return;
        }

        var state = result.Status == DeviceAuthorizationStatus.PendingApproval ? "reconnecting" : "degraded";
        await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState(state, result.Detail), cancellationToken).ConfigureAwait(false);

        if (result.Status == DeviceAuthorizationStatus.PendingApproval)
        {
            pendingHello = hello;
            SubscribeForPairingApproval();
            await TryCompletePendingPairingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void SubscribeForPairingApproval()
    {
        if (pairingChangeSubscribed || authorizer is not PairingApprovalService pairingService)
        {
            return;
        }

        pairingChangeSubscribed = true;
        pairingService.PairingChanged += HandlePairingChanged;
    }

    private void HandlePairingChanged(object? sender, EventArgs args)
    {
        _ = TryCompletePendingPairingAsync(CancellationToken.None);
    }

    private bool ShouldEchoVerifiedUsbBearerToken(HelloMessage hello, string? presentedBearerToken)
    {
        return string.Equals(remoteAddress, "USB", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(hello.BearerToken)
            && string.Equals(presentedBearerToken, hello.BearerToken, StringComparison.Ordinal);
    }

    private async Task TryCompletePendingPairingAsync(CancellationToken cancellationToken)
    {
        if (pendingHello is null || authorizer is not PairingApprovalService pairingService)
        {
            return;
        }

        var token = pairingService.TryConsumeUndeliveredToken(pendingHello.DeviceId, pendingSessionId);
        if (token is null)
        {
            if (pairingService.IsRejected(pendingHello.DeviceId))
            {
                pairingService.PairingChanged -= HandlePairingChanged;
                pairingChangeSubscribed = false;
                pendingHello = null;
            }

            return;
        }

        pairingService.PairingChanged -= HandlePairingChanged;
        pairingChangeSubscribed = false;
        authorized = true;
        pendingHello = null;

        await sink.SendTextAsync(ProtocolStateMapper.CreatePairingToken(token), cancellationToken).ConfigureAwait(false);
        await sink.SendTextAsync(ProtocolStateMapper.CreateAgentHello(), cancellationToken).ConfigureAwait(false);
        await SendInitialStateAsync(cancellationToken).ConfigureAwait(false);
        await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("connected", null), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendInitialStateAsync(CancellationToken cancellationToken)
    {
        var currentShortcuts = shortcutsProvider?.Invoke() ?? shortcuts ?? new DeviceShortcutConfiguration([]);
        await sink.SendTextAsync(ProtocolStateMapper.CreateShortcutsUpdate(currentShortcuts), cancellationToken).ConfigureAwait(false);
        var currentAppearance = appearanceProvider?.Invoke() ?? appearance;
        if (currentAppearance is not null)
            await sink.SendTextAsync(ProtocolStateMapper.CreateDeviceSettings(CreateRequestId("settings"), currentAppearance), cancellationToken).ConfigureAwait(false);

        var audioSnapshot = initialAudioProvider?.Invoke();
        if (audioSnapshot is not null)
        {
            await sink.SendTextAsync(ProtocolStateMapper.CreateAudioUpdate(audioSnapshot), cancellationToken).ConfigureAwait(false);
        }
        if (initialMediaSender is not null) await initialMediaSender(sink, cancellationToken).ConfigureAwait(false);
        await sink.SendTextAsync(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.DeviceStatusRequest, null, new { }), cancellationToken).ConfigureAwait(false);
    }

    private static string CreateRequestId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private async Task HandleUnauthorizedAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(envelope.RequestId))
        {
            await sink.SendTextAsync(
                ProtocolSerializer.SerializeAck(
                    envelope.RequestId,
                    success: false,
                    new ProtocolError("unauthorized", "Device is not paired or authorized.")),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await sink.SendTextAsync(
            ProtocolStateMapper.CreateConnectionState("degraded", "Device is not paired or authorized."),
            cancellationToken).ConfigureAwait(false);
    }

    private static (string? RequestId, string? Type) TryInspectIncomingEnvelope(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                string? type = null;
                if (doc.RootElement.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                {
                    type = typeProp.GetString();
                }

                string? safeReqId = null;
                if (doc.RootElement.TryGetProperty("requestId", out var reqProp) && reqProp.ValueKind == JsonValueKind.String)
                {
                    var req = reqProp.GetString();
                    if (!string.IsNullOrWhiteSpace(req) && req.Length <= 64 && req.All(c => c >= 0x21 && c <= 0x7e))
                    {
                        safeReqId = req;
                    }
                }

                return (safeReqId, type);
            }
        }
        catch
        {
            // Unparsable JSON
        }

        return (null, null);
    }
}
