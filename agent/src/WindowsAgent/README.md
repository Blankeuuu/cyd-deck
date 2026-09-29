# StreamDeck Windows Agent

`StreamDeck.WindowsAgent` is the runnable Windows shell for the existing agent modules. It starts the media, audio, shortcut and host WebSocket components, shows a tray menu, and exposes a local dashboard bound to `127.0.0.1`.

The dashboard is separate from the ESP32 LAN WebSocket listener. The LAN listener uses the configured agent port, defaults to `8765`, and accepts ESP32 WebSocket connections at `/ws`. The dashboard chooses a loopback-only ephemeral port and never exposes pairing tokens or secrets.

The LAN listener is advertised with mDNS/DNS-SD as `_streamdeck._tcp.local`. The service publishes an SRV record for the configured WebSocket port, TXT records `path=/ws` and `proto=ws`, and A records for the host's IPv4 LAN addresses. This lets firmware discover the agent when the setup flow leaves the manual agent IP blank.

Run with `--mock` to start the same tray, LAN WebSocket and pairing dashboard without touching CoreAudio or Windows media sessions. Mock mode accepts protocol commands and returns success for media, audio and shortcut commands, which is useful for transport and pairing tests before exercising the live Windows integrations.

The Windows Agent builds as a self-contained `win-x64` executable so `StreamDeck.WindowsAgent.exe` can run on a standard Windows user account without a machine-wide ASP.NET Core runtime install. Debug builds are emitted under `bin/Debug/net10.0-windows10.0.17763.0/win-x64/`.

The tray and dashboard status are updated from observed WebSocket protocol flow: incoming `hello`, outgoing `connection.state=connected`, and WebSocket close. Pending pairing state comes from the host-owned `PairingApprovalService`.

Private-LAN access for the ESP32 still needs to be allowed by Windows Firewall for the agent executable and port. The app does not add broad firewall rules automatically.

Pairing integration uses the host-owned app-facing API:

```csharp
public interface IPairingApprovalService
{
    event EventHandler? PairingChanged;
    Task<IReadOnlyList<PendingDevice>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task<bool> ApproveAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<bool> RejectAsync(string deviceId, CancellationToken cancellationToken = default);
}

public sealed record PendingDevice(string DeviceId, string Name, string? RemoteAddress, DateTimeOffset FirstSeenUtc);
```
