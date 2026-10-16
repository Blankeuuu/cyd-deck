---
title: Protokół CYD-DECK
tags: [cyd-deck, protocol, usb, websocket]
aliases: [CYD-DECK protocol, synchronizacja stanu]
updated: 2026-10-16
status: active
---

# Protokół

Komunikacja realtime między ESP32 i agentem Windows używa JSON po WebSocket lub USB Serial. WebSocket pobiera okładki przez HTTP (`GET /api/covers/{coverId}.jpg`); USB przesyła je w ramkach Base64 opisanych poniżej.

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


## Synchronizacja po hello (2026-10-15)

Wersja koperty pozostaje `protocol: 1`. Hello ESP32 może zawierać opcjonalny obiekt `deviceInfo`, o takim samym schemacie jak `device.info`: firmwareVersion, uptimeSeconds, wifi (connected, ssid, rssi, ip), sd (mounted, ready, capacityBytes, freeBytes), heap (freeBytes, minFreeBytes, largestBlockBytes). Adres Wi-Fi jest raportowany także przy sterowaniu przez USB. Agent akceptuje również starsze hello bez tego obiektu.

Po autoryzacji USB/WebSocket Agent wysyła kolejno `shortcuts.update`, `device.settings`, `audio.update`, bieżące media i okładkę, `device.status.request`, a następnie `connection.state` connected. Pusta konfiguracja skrótów również jest wysyłana. `device.status.request` nie wymaga requestId ani payloadu i wywołuje natychmiastowe `device.info`. Firmware wysyła też telemetrię okresowo. Ostatnie sześć slotów skrótów jest przechowywane w NVS urządzenia.

## Okładki i audio

`coverId` w `media.update` identyfikuje zawartość okładki. Metadane poprzedzają transfer. Agent dekoduje miniaturę SMTC, wykonuje center-crop do 128x128 i koduje baseline JPEG quality 80. Klucz cache obejmuje rozmiar, jakość i wersję kodera.

USB używa `cover.chunk` (512 bajtów obrazu przed Base64, odstęp 20 ms) i `cover.complete`. Zakończenie transferu wywołuje renderowanie i odświeżenie UI; wynik jest widoczny w diagnostyce urządzenia. Ten sam coverId nie resetuje stanu ani nie powoduje kolejnego transferu w tej samej sesji. Firmware odrzuca spóźniony wynik innej okładki. Po 15 s bez postępu przechodzi do fallback zamiast pozostawać w Loading. Bufor Serial RX wynosi 8192 bajty.

`audio.update` zachowuje pola `volumePercent` i `muted` (nie `isMuted`). Agent odczytuje domyślne wyjście CoreAudio eRender/eMultimedia przy synchronizacji i wysyła audio przed mediami oraz po powiadomieniu endpointu.

Logi Agenta maskują wartości bearerToken, token i password, także podczas odczytu historycznych wpisów do UI/eksportu. Nowy firmware i Agent należy wdrożyć razem; zgodność nowego hello ze starym Agentem nie została potwierdzona.


## Diagnostyka responsywności i USB hello

Opcjonalne device.info.performance (również w hello.deviceInfo): loopMaxUs, renderMaxUs, networkMaxUs, slowLoops, wifiDiscoveryCount — nieujemne uint32. Maksima obejmują pracę przy połączonym Agencie od startu firmware, w tym początkowe ładowanie okładki; nie są średnią ani FPS. networkMaxUs obejmuje obsługę usług sieciowych oraz USB. slowLoops liczy iteracje powyżej50ms. Agent waliduje te pola i udostępnia je w lokalnym /api/status. Starszy firmware bez performance pozostaje obsługiwany; nowy firmware wymaga aktualnego Agenta akceptującego to opcjonalne pole.

USB discovery probe to hello z pustym data. Firmware odpowiada urządzeniowym hello na taki probe. Hello z role=agent jest odpowiedzią handshake i NIE wywołuje kolejnego hello urządzenia. To zapobiega pętli wielokrotnej autoryzacji, synchronizacji i transferu tej samej okładki.

Przy autoryzowanym USB oraz przez krótki czas negocjacji USB nie uruchamia się discovery mDNS, WebSocket.loop ani HTTP cover fetch. Wi-Fi STA pozostaje połączone dla IP/OTA; po utracie USB wraca obsługa transportu sieciowego. RX8192, TX2048; odbiornik UART obrabia najwyżej2048bajtów lub2ms przed oddaniem sterowania pętli UI (kompletna ramka może wymagać dodatkowego czasu parsowania/dekodowania).


## Cache SD steruje transferem USB

Capability cover-cache-request w hello ESP32 uzgadnia tryb żądania przy cache miss. Dla takiego urządzenia media.update zawiera coverId, ale Agent nie przesyła automatycznie obrazu. CYD odczytuje plik SD; tylko gdy go brakuje lub dekoder go odrzuci, wysyła cover.request: {coverId: SHA256}. Żądanie nie wymaga requestId; Agent autoryzuje sesję, waliduje64hex i odpowiada wyłącznie dla aktualnego coverId. Odpowiedź zachowuje cover.chunk/cover.complete. Bez capability pozostaje legacy push. Pierwsza synchronizacja nie czeka na binaria: metadane i connected poprzedzają obsługę ewentualnego requestu z urządzenia.

Heartbeat USB5s, timeout30s. Lokalny timeline interpoluje pozycję co100ms; pakiety media.timeline nadal korygują kotwicę bez zwiększania ruchu. Envelope protocol1; nową parę Agent/firmware wdrożono razem.

## Rozszerzenia UI — 2026-10-16

Wersja koperty pozostaje `protocol: 1`. Opcjonalne pola zachowują zgodność: starszy firmware pomija nowe ustawienia, nowszy przyjmuje ich brak.

- `device.settings.data.applyBrightness` (boolean): jawne zastosowanie jasności również przy tej samej wartości; hydration wysyła false.
- `device.settings.data.brightnessRevision` (32 znaki hex): identyfikator jawnego zapisu ustawień, także offline. Zmiana rewizji usuwa lokalną korektę jasności; ta sama rewizja przy reconnect ją zachowuje. Ostatnia rewizja jest zapisana w NVS.
- `device.settings`: walidator uwzględnia teraz `tileColorScheme`, `standbyStyle`, `shortcutsLayout` oraz powyższe pola.
- `system.stats.data.hostDate`: angielska data z Windows (`dddd, d MMMM`, InvariantCulture), używana na ekranie Default i StandBy; istniejący `hostLocalMinutes` nadal synchronizuje zegar.
- `shortcuts.update.data.shortcuts[].color`: opcjonalny kolor kafelka `#RRGGBB`; brak oznacza dziedziczenie Display Appearance. Katalog aktywnego profilu jest przesyłany istniejącym mechanizmem; firmware używa pierwszych sześciu wpisów, a Default pierwszych czterech.

Default oraz przycisk ustawień są lokalną nawigacją firmware. Zachowano dotychczasowe identyfikatory konfiguracji `media`, `shortcuts`, `pc`, `info`; nie dodano nowej akcji zdalnej ani dodatkowej strony kafelków. Nawigacja pozostaje dostępna niezależnie od legacy pageVisibility.

NVS skrótów używa wersji 2; starszy zapis wersji 1 jest odczytywany bez utraty identyfikatorów i podpisów, z domyślnym dziedziczeniem kolorów. Cache okładek SD i kolejność transferu USB nie zmieniły się.

Transport potwierdza przyjęcie `device.settings` przez dotychczasowy ACK. Etykieta Windows `sent to CYD` opisuje wysłanie przez transport; nie stanowi dodatkowego potwierdzenia fizycznego renderowania.
