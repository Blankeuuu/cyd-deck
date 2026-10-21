using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using StreamDeck.Audio;
using StreamDeck.Host;
using StreamDeck.Protocol;
using StreamDeck.Configuration;
using StreamDeck.Shortcuts;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed class WindowsAgentWebSocketServer(
    TransportOptions options,
    DeviceCommandDispatcher dispatcher,
    PairingApprovalService pairingService,
    AgentState state,
    AppearanceConfiguration? appearance = null,
    DeviceShortcutConfiguration? shortcuts = null,
    string? coverCacheDirectory = null,
    Func<AudioSnapshot?>? initialAudioProvider = null,
    Func<ShortcutsReportMessage, CancellationToken, ValueTask>? shortcutsReportHandler = null,
    Func<DeviceShortcutConfiguration>? shortcutsProvider = null,
    Func<AppearanceConfiguration>? appearanceProvider = null,
    Func<IProtocolSink, CancellationToken, ValueTask>? initialMediaSender = null) : IAsyncDisposable
{
    private WebApplication? app;
    private readonly object activeSinkGate = new();
    private TrackingProtocolSink? activeSink;

    public int Port { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(options.BindAddress, options.Port, listen => listen.Protocols = HttpProtocols.Http1);
        });

        app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/api/covers/{coverId}.jpg", GetCoverAsync);
        app.Map(options.WebSocketPath, HandleWebSocketAsync);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        Port = app.Urls.Select(TryParsePort).First(port => port > 0);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            app = null;
        }
    }

    public async Task<bool> SendToActiveDeviceAsync(string message, CancellationToken cancellationToken = default)
    {
        TrackingProtocolSink? sink;
        lock (activeSinkGate)
        {
            sink = activeSink;
        }

        if (sink is null)
        {
            return false;
        }

        await sink.SendTextAsync(message, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SendConfigurationAsync(string shortcuts, string settings, CancellationToken token)
    {
        TrackingProtocolSink? sink;
        lock (activeSinkGate) sink = activeSink;
        if (sink is null) return false;
        await sink.SendTextAsync(shortcuts, token).ConfigureAwait(false);
        await sink.SendTextAsync(settings, token).ConfigureAwait(false);
        lock (activeSinkGate) return ReferenceEquals(sink, activeSink);
    }
    public async Task<bool> DisconnectActiveDeviceAsync(CancellationToken cancellationToken = default)
    {
        TrackingProtocolSink? sink;
        lock (activeSinkGate)
        {
            sink = activeSink;
        }

        if (sink is null)
        {
            return false;
        }

        await sink.CloseAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!IsAllowedRemoteAddress(context.Connection.RemoteIpAddress)
            || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var remoteAddress = FormatRemoteAddress(context);
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var sink = new TrackingProtocolSink(webSocket, new WebSocketProtocolSink(webSocket), state, remoteAddress, SetActiveSink);
        var session = new DeviceProtocolSession(dispatcher, pairingService, sink, context.Request.Query["token"], remoteAddress, appearance ?? new AppearanceConfiguration(), shortcuts ?? new DeviceShortcutConfiguration([]), (info, token) => { state.UpdateDeviceInfoForSession(sink.SessionId, info); return ValueTask.CompletedTask; }, initialAudioProvider, shortcutsReportHandler, shortcutsProvider, appearanceProvider, initialMediaSender,
            sessionErrorHandler: message => AgentDiagnosticLog.Write("WebSocket protocol frame rejected: " + message));

        try
        {
            await ReceiveLoopAsync(webSocket, session, sink, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            ClearActiveSink(sink);
            if (sink.DeviceId is not null)
            {
                state.MarkDeviceDisconnected(sink.DeviceId, sink.SessionId);
            }
        }
    }

    private bool IsAllowedRemoteAddress(IPAddress? remoteAddress)
    {
        return options.AllowRemoteClients || (remoteAddress is not null && IPAddress.IsLoopback(remoteAddress));
    }

    private static string? FormatRemoteAddress(HttpContext context)
    {
        return context.Connection.RemoteIpAddress is null
            ? null
            : $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}";
    }

    private async Task ReceiveLoopAsync(
        WebSocket webSocket,
        DeviceProtocolSession session,
        TrackingProtocolSink sink,
        CancellationToken cancellationToken)
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

            var message = builder.ToString();
            sink.TrackIncoming(message);
            LogIncomingDiagnostics(message);
            try
            {
                await session.HandleTextMessageAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException)
            {
                LogSessionHandlerException(ex);
                throw;
            }
            builder.Clear();
        }
    }

    private static void LogIncomingDiagnostics(string message)
    {
        var incomingType = TryReadMessageType(message);
        var parseResult = ProtocolSerializer.Parse(message);
        var parsedType = parseResult.Envelope?.Type;
        var type = string.IsNullOrWhiteSpace(parsedType) ? incomingType : parsedType;
        var result = parseResult.IsSuccess ? "success" : "failure";
        var errorCode = parseResult.ErrorCode?.ToString() ?? "none";

        Console.Error.WriteLine(
            $"[WindowsAgent.WebSocket] incoming type={FormatDiagnosticValue(type)} parse={result} errorCode={errorCode}");
    }

    private static string? TryReadMessageType(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void LogSessionHandlerException(Exception exception)
    {
        Console.Error.WriteLine(
            $"[WindowsAgent.WebSocket] session handler exception type={exception.GetType().Name}");
    }

    private static string FormatDiagnosticValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static int TryParsePort(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Port : 0;
    }

    private async Task<IResult> GetCoverAsync(string coverId, CancellationToken cancellationToken)
    {
        if (!IsValidCoverId(coverId) || string.IsNullOrWhiteSpace(coverCacheDirectory))
        {
            return Results.NotFound();
        }

        var path = Path.Combine(coverCacheDirectory, "covers", $"{coverId}.jpg");
        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Results.File(bytes, "image/jpeg", enableRangeProcessing: false);
    }

    private static bool IsValidCoverId(string coverId)
    {
        return coverId.Length == 64 && coverId.All(Uri.IsHexDigit);
    }

    private sealed class TrackingProtocolSink(
        WebSocket webSocket,
        IProtocolSink inner,
        AgentState state,
        string? remoteAddress,
        Action<TrackingProtocolSink> setActiveSink) : IProtocolSink
    {
        private readonly InitialConfigurationTracker initialSync = new(state);
        private HelloMessage? hello;
        private bool connected;

        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public string? DeviceId => hello?.DeviceId;

        public void TrackIncoming(string message)
        {
            var parseResult = ProtocolSerializer.Parse(message);
            if (!parseResult.IsSuccess || parseResult.Envelope?.Type != ProtocolConstants.MessageTypes.Hello)
            {
                return;
            }

            hello = ProtocolSerializer.DeserializeData<HelloMessage>(parseResult.Envelope);
        }

        public async ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
        {
            if (ProtocolSerializer.Parse(message).Envelope?.Type == ProtocolConstants.MessageTypes.ShortcutsUpdate) MarkAuthorizedConnection();
            await initialSync.SendAsync(message, () => inner.SendTextAsync(message, cancellationToken), state.IsActiveDeviceSession(SessionId)).ConfigureAwait(false);
            TrackOutgoing(message);
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Device forgotten", cancellationToken).ConfigureAwait(false);
            }
        }

        private void MarkAuthorizedConnection()
        {
            if (hello is null || connected) return;
            connected = true;
            setActiveSink(this);
            state.MarkDeviceConnected(hello.DeviceId, hello.Name, remoteAddress, DeviceTransport.WebSocket, sessionId: SessionId);
            if (hello.DeviceInfo is not null) state.UpdateDeviceInfoForSession(SessionId, hello.DeviceInfo);
        }
        private void TrackOutgoing(string message)
        {
            if (hello is null)
            {
                return;
            }

            var parseResult = ProtocolSerializer.Parse(message);
            if (!parseResult.IsSuccess || parseResult.Envelope?.Type != ProtocolConstants.MessageTypes.ConnectionState)
            {
                return;
            }

            var connection = ProtocolSerializer.DeserializeData<ConnectionStateMessage>(parseResult.Envelope);
            if (string.Equals(connection.State, "connected", StringComparison.Ordinal))
            {
                MarkAuthorizedConnection();
            }
        }
    }

    private void SetActiveSink(TrackingProtocolSink sink)
    {
        lock (activeSinkGate)
        {
            activeSink = sink;
        }
    }

    private void ClearActiveSink(TrackingProtocolSink sink)
    {
        lock (activeSinkGate)
        {
            if (ReferenceEquals(activeSink, sink))
            {
                activeSink = null;
            }
        }
    }
}
