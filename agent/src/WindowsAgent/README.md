# StreamDeck Windows Agent

`StreamDeck.WindowsAgent` is the runnable Windows shell for the existing agent modules. It starts the media, audio, shortcut and host WebSocket components, shows a tray menu, and exposes a local dashboard bound to `127.0.0.1`.

The dashboard is separate from the ESP32 LAN WebSocket listener. The LAN listener uses the configured agent port, defaults to `8765`, and accepts ESP32 WebSocket connections at `/ws`. The dashboard chooses a loopback-only ephemeral port and never exposes pairing tokens or secrets.

The LAN listener is advertised with mDNS/DNS-SD as `_streamdeck._tcp.local`. The service publishes an SRV record for the configured WebSocket port, TXT records `path=/ws` and `proto=ws`, and A records for the host's IPv4 LAN addresses. This lets firmware discover the agent when the setup flow leaves the manual agent IP blank.

Run with `--mock` to start the same tray, LAN WebSocket and pairing dashboard without touching CoreAudio or Windows media sessions. Mock mode accepts protocol commands and returns success for media, audio and shortcut commands, which is useful for transport and pairing tests before exercising the live Windows integrations.

The Windows Agent builds as a self-contained `win-x64` executable so `StreamDeck.WindowsAgent.exe` can run on a standard Windows user account without a machine-wide ASP.NET Core runtime install. Debug builds are emitted under `bin/Debug/net10.0-windows10.0.17763.0/win-x64/`.

## Portable deployment package

Deployment uses a portable self-contained `win-x64` ZIP. There is no installer, no admin elevation requirement, no Windows service, and no automatic firewall rule.

From the repository root, create a package with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-windows-agent.ps1 -OutputDirectory artifacts/windows-agent
```

Use a dedicated output directory under `artifacts/` (as above) or outside the repository. The script rejects the repository, its parent directories, and other source directories as output destinations because it replaces its own publish folder and ZIP there.

The script runs the existing prepublish quality gate from the documented build path, then publishes `agent/src/WindowsAgent/StreamDeck.WindowsAgent/StreamDeck.WindowsAgent.csproj` as a self-contained Release `win-x64` app and creates:

```text
artifacts/windows-agent/StreamDeck.WindowsAgent-win-x64.zip
```

Use `-SkipQualityGate` only when another build/test stage has already validated the same source revision. The package contains the publish output (excluding PDB debug symbols) and a `DEPLOYMENT.txt` quick guide. Symbols remain available in local build outputs; the portable ZIP must not contain exported configuration, pairing records, logs, cache files, secrets, or user data.

Third-party notices: package verification for the pinned transitive artifact `SixLabors.ImageSharp 2.1.11` must use that exact published `.nupkg`, not the current ImageSharp repository or current NuGet page for newer versions. The inspected 2.1.11 `.nuspec` states `Apache-2.0` and `requireLicenseAcceptance=true`; it is not the current Six Labors Split License. The package script adds `THIRD-PARTY-NOTICES.txt` with the ImageSharp 2.1.11 attribution and a copy of Apache License 2.0, plus exact-version .NET runtime-pack license/notice files. It checks the published deps inventory and refuses to archive unknown or changed dependencies until their notices are reviewed. A legal/compliance review remains necessary before release.

Recommended installation path for a user install is a stable, user-writable directory such as:

```text
%LOCALAPPDATA%\Programs\StreamDeckWindowsAgent
```

Extract the ZIP there and run `StreamDeck.WindowsAgent.exe`. Do not run it from a temporary downloads or extraction directory if you plan to enable startup, because the startup entry points to the executable path.

First use prerequisites:

- The PC and device must be on the same private LAN.
- Start the agent before pairing.
- Approve the pending device in the agent UI when the device first connects.
- If Windows Firewall prompts, allow private-network access for `StreamDeck.WindowsAgent.exe`. If creating a manual allow rule, scope it narrowly to the installed executable and the configured LAN listener port, default `8765`.

The localhost dashboard must stay local-only. It binds to `127.0.0.1` on an ephemeral port and should not be exposed through firewall rules, port forwarding, or reverse proxies. Only the device LAN WebSocket listener needs private-LAN reachability.

Optional per-user startup can be enabled from the tray/settings UI after the app is installed in its stable path. This writes only the current user's `Run` entry; the portable package script does not modify registry startup state.

To update:

1. Exit the agent from the tray menu.
2. Keep a backup copy of the previous extracted install directory.
3. Extract the new ZIP to the same stable install path, replacing application files.
4. Start `StreamDeck.WindowsAgent.exe` again.

To roll back, exit the agent and restore the previous extracted install directory.

To uninstall the portable app, exit the agent and delete the install directory. User configuration, logs, pairing records and cache live outside the portable package under `%LOCALAPPDATA%\StreamDeckAgent`; leave that directory in place to retain data for a future reinstall, or delete it only when intentionally removing local data and pairing state.

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
