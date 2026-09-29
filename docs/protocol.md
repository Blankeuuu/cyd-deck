# Protokół

Komunikacja realtime między ESP32 i agentem Windows używa JSON po WebSocket. HTTP jest zarezerwowane dla binarnych zasobów, np. `GET /api/covers/{coverId}.jpg`; okładki nie są wysyłane jako Base64 w JSON.

## Envelope

Każda wiadomość ma wersjonowaną kopertę:

```json
{
  "protocol": 1,
  "type": "media.command",
  "requestId": "42",
  "data": {
    "command": "next"
  }
}
```

Pola:

- `protocol`: obecnie zawsze `1`.
- `type`: nazwa typu wiadomości.
- `requestId`: widoczny ASCII `1..64` znaków. Wymagany dla komend i `ack`; opcjonalny dla strumieni stanu.
- `data`: payload zależny od typu.
- `success` i `error`: tylko dla `ack`.

## ESP32 -> PC

- `hello`: `{ "deviceId": "...", "name": "...", "role": "esp32", "capabilities": [] }`
- `media.command`: `{ "command": "playPause|play|pause|previous|next|stop" }`
- `audio.setVolume`: `{ "volumePercent": 0..100 }`
- `audio.toggleMute`: bez wymaganego payloadu
- `shortcut.execute`: `{ "shortcutId": "..." }`
- `system.command`: `{ "command": "sleep|lock|restartAgent" }`
- `device.info`: telemetry `{ "firmwareVersion": "...", "uptimeSeconds": 0, "wifi": { "connected": true, "ssid": "...", "rssi": -53, "ip": "..." }, "agentHost": "...", "sd": { "mounted": true, "ready": true, "capacityBytes": 0, "freeBytes": 0 }, "heap": { "freeBytes": 0, "minFreeBytes": 0, "largestBlockBytes": 0 } }`. `ssid`, `rssi`, `ip`, `agentHost`, capacity/free may be null when unavailable.
- `ping`: `{ "sentAtUnixMs": 0 }`

Firmware emits `device.info` after authenticated connection and periodically while connected. It contains diagnostics only; it does not include Wi-Fi credentials or pairing tokens.

Komendy oczekują `ack` z tym samym `requestId`.

## PC -> ESP32

- `hello`: `{ "deviceId": "...", "name": "...", "role": "agent", "capabilities": [] }`
- `media.update`: tytuł, artysta, album, `playing`, `positionMs`, `durationMs`, `coverId`, `accent`
- `media.timeline`: `positionMs`, `durationMs`, `updatedAtUnixMs`
- `media.playback`: `{ "playing": true }`
- `audio.update`: `{ "volumePercent": 0..100, "muted": false }`
- `system.stats`: CPU i RAM
- `device.settings`: `{ "version": 1, "theme": "dark|light", "accent": "#RRGGBB", "background": "#RRGGBB", "buttonStyle": "flat|rounded|pill", "pageOrder": ["media", "shortcuts", "pc", "info"], "pageVisibility": ["media", "shortcuts", "pc", "info"], "brightness": 10..100, "dimAfterSec": 0..86400, "dimBrightness": 1..100, "screenOffAfterSec": 0..86400, "wakeOnTouch": true }`. Settings are sent from the persisted Agent configuration after authorized connection/reconnection and can also be applied live.
- `shortcuts.update`: lista `{ id, label, actionType }`. Agent jest źródłem prawdy i wysyła zapisany katalog po autoryzacji i reconnect; firmware pokazuje maksymalnie pierwsze 6 pozycji w kolejności Agenta, a każda aktualizacja zastępuje cały lokalny widok (brakujące sloty są czyszczone). Urządzenie nie wysyła katalogu/preloadu do Agenta; odsyła do wykonania wyłącznie `shortcutId`, nie komendę powłoki.
- `device.restart`: normalny restart ESP32, bez czyszczenia konfiguracji.
- `device.factoryReset`: usuwa konfigurację użytkownika urządzenia i wraca do provisioning. W Windows wymaga potwierdzenia w UI; uruchomienie przyciskiem CYD wymaga przytrzymania przez 3 s i osobnego lokalnego potwierdzenia.
- `connection.state`: `{ "state": "connected|degraded|reconnecting", "detail": null }`
- `pairing.token`: `{ "token": "..." }` - jednorazowa wiadomość po zatwierdzeniu parowania; ESP32 zapisuje bearer token lokalnie i używa go przy kolejnych handshake WebSocket. Tokenu nie logować.
- `ack`: `{ "success": true }` albo `{ "success": false, "error": { "code": "...", "message": "..." } }`
- `pong`: `{ "sentAtUnixMs": 0, "receivedAtUnixMs": 0 }`

## Walidacja

Parser odrzuca deterministycznie:

- błędny JSON;
- brak lub nieobsługiwaną wersję `protocol`;
- brak albo nieznany `type`;
- wymagany, pusty albo niedozwolony `requestId`;
- `success`/`error` poza `ack`;
- `ack` bez `success`, albo nieudany `ack` bez `error`.
- payload komend ESP32 → PC niezgodny ze schematem:
  - brak wymaganego `data` tam, gdzie typ go wymaga;
  - zły typ pola, np. string zamiast liczby;
  - wartość poza zakresem, np. `volumePercent` poza `0..100`;
  - wartość spoza enuma, np. nieznany `media.command`;
  - dodatkowe pola w payloadzie komendy.
