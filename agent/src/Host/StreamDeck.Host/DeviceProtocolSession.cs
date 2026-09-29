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
    Func<ShortcutsReportMessage, CancellationToken, ValueTask>? shortcutsReportHandler = null)
{
    private readonly Guid pendingSessionId = Guid.NewGuid();
    private bool authorized;
    private HelloMessage? pendingHello;
    private bool pairingChangeSubscribed;

    public async Task HandleTextMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        var parseResult = ProtocolSerializer.Parse(message);
        if (!parseResult.IsSuccess)
        {
            await sink.SendTextAsync(
                ProtocolStateMapper.CreateConnectionState("degraded", parseResult.ErrorMessage),
                cancellationToken).ConfigureAwait(false);
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
        var result = authorizer is PairingApprovalService pairingService
            ? await pairingService.AuthorizeAsync(hello, bearerToken, remoteAddress, pendingSessionId, cancellationToken).ConfigureAwait(false)
            : await authorizer.AuthorizeAsync(hello, bearerToken, cancellationToken).ConfigureAwait(false);
        authorized = result.Status == DeviceAuthorizationStatus.Authorized;

        if (authorized)
        {
            await sink.SendTextAsync(ProtocolStateMapper.CreateAgentHello(), cancellationToken).ConfigureAwait(false);
            await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("connected", null), cancellationToken).ConfigureAwait(false);
            await SendInitialStateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var state = result.Status == DeviceAuthorizationStatus.PendingApproval ? "reconnecting" : "degraded";
        await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState(state, result.Detail), cancellationToken).ConfigureAwait(false);

        if (result.Status == DeviceAuthorizationStatus.PendingApproval)
        {
            pendingHello = hello;
            SubscribeForPairingApproval();
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
        await sink.SendTextAsync(ProtocolStateMapper.CreateConnectionState("connected", null), cancellationToken).ConfigureAwait(false);
        await SendInitialStateAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SendInitialStateAsync(CancellationToken cancellationToken)
    {
        if (appearance is not null)
        {
            await sink.SendTextAsync(ProtocolStateMapper.CreateDeviceSettings(CreateRequestId("settings"), appearance), cancellationToken).ConfigureAwait(false);
        }

        if (shortcuts is not null)
        {
            await sink.SendTextAsync(ProtocolStateMapper.CreateShortcutsUpdate(shortcuts), cancellationToken).ConfigureAwait(false);
        }

        var audioSnapshot = initialAudioProvider?.Invoke();
        if (audioSnapshot is not null)
        {
            await sink.SendTextAsync(ProtocolStateMapper.CreateAudioUpdate(audioSnapshot), cancellationToken).ConfigureAwait(false);
        }
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
}
