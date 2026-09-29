# StreamDeck.Host

Host transport layer for the Windows agent.

- WebSocket JSON endpoint: `/ws`.
- Default binding: `127.0.0.1:8765`.
- Remote LAN clients must be enabled explicitly with `AllowRemoteClients`.
- Device commands are ignored until a `hello` message is authorized by an `IDeviceAuthorizer`.
- `PairingApprovalService` implements `IPairingApprovalService`, keeps unknown devices pending, persists approved/rejected pairings in per-user local state, protects bearer tokens with Windows DPAPI, and sends the new `pairing.token` protocol message once to the active approved session.
- The default authorizer returns pending approval, so the tray/dashboard host must explicitly approve a pending device before commands are accepted.
- The admin dashboard should stay on localhost and remain separate from the ESP32 API surface.
