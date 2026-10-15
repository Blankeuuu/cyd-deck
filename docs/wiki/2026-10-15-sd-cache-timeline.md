---
title: CYD-DECK — cache SD na żądanie i płynny timeline
tags: [cyd-deck, sd-cache, artwork, timeline, usb, heartbeat]
aliases: [SD cache miss, cover.request, smooth timeline, płynny pasek postępu]
updated: 2026-10-16
status: active
---


# Cache SD i płynny timeline — 2026-10-15 około23:42

## Przyczyny ponownych transferów

Cache SD był implementowany, ale Agent wysyłał obraz bez sprawdzania, czy CYD ma go już na karcie. Dodatkowo heartbeat USB miał15000ms, a timeout także15000ms; timeout był sprawdzany przed wysłaniem heartbeat. To powodowało niepotrzebne ponowne handshake, zerowanie deduplikacji i kolejne transfery.

## Zmiany

- Firmware reklamuje capability cover-cache-request. Agent przy tej capability wysyła metadata z coverId, bez binariów. CYD próbuje renderCachedCover z SD; przy braku lub błędzie pliku wysyła cover.request z SHA256 coverId. Agent wysyła wtedy cover.chunk/cover.complete, tylko dla aktualnego hasha; powtórne żądania tego samego transferu są deduplikowane. Starszy firmware bez capability zachowuje poprzedni tryb push.
- Cache lokalny pozostaje na SD w dotychczasowym katalogu i jest odczytywany także po restarcie. Nie kasowano karty ani konfiguracji. Usunięto podwójne sprawdzanie hasCover przed openCoverRead (jedno wystarcza i ogranicza zapis indeksu).
- Heartbeat USB5000ms, timeout30000ms; Wi-Fi zachowuje swój interwał. Regresja sprawdza odpowiedni margines.
- Timeline: lokalna interpolacja pozycji na podstawie ostatniej kotwicy z PC, odświeżanie UI co100ms tylko na widocznej stronie Media podczas odtwarzania i aktywnego Agenta. Skala paska0..10000 zamiast0..100. Pauza zamraża pozycję, korekta z PC/seek ustawia nową kotwicę, pozycja nie przekracza duration. Obsłużono wrap uint32 zegara. Nie zwiększano częstotliwości ramek USB.

## Pliki

Firmware: agent_client.cpp, app_state.h, app.cpp, ui_manager.cpp, usb_agent_protocol.h. Agent: ProtocolConstants.cs, Messages.cs, ProtocolSerializer.cs, DeviceProtocolSession.cs, UsbDeviceRuntime.cs. Testy: ui_logic_test.cpp, ui_layout_test.cpp, usb_protocol_test.cpp, HydrationProtocolTests.cs i WindowsAgent.Tests/Program.cs.

## Weryfikacja

Test layoutu przed zmianą był RED dla oczekiwanej precyzji paska2250 przy22,5%. Po zmianie C++ timeline/layout/USB PASS z -Werror; obejmuje pauzę, koniec utworu, wrap zegara. Protocol80/80 PASS. Pełne WindowsAgent smoke PASS, w tym cache hit bez chunków, pojedynczy transfer na miss, odrzucenie starego requestu, deduplikacja oraz legacy push. Build.NET i ST7789 bez ostrzeżeń.

Firmware wgrano na COM5, hash danych potwierdzony i restart RTS (artifacts/cache-timeline-flash.log). Uruchomiono widoczny artifacts/cache-agent/StreamDeck.WindowsAgent.exe --usb-port COM5, PID13864 w chwili kontroli. Pierwszy odczyt fizycznego CYD: connected/USB, SDready, cover.status=2/rendered=true, audio100%/muted=false. Końcowy test persistent cache i40s stabilności zapisany w artifacts/cache-sd-live-verification.json; jego wynik dopisany niżej. Dotyk i subiektywna płynność timeline wymagają oceny użytkownika.


## Potwierdzony wynik sprzętowy

Kontrolny restart CYD z już zapisanym obrazem: cover.status=2/rendered=true, SDready, **0 transferów okładek od startu Agenta**, stabilne USB i stały licznik discovery przez30s. Artefakt: artifacts/cache-sd-warm-reboot-verification.json. Wcześniejszy pierwszy test miał1transfer (cache miss); nie jest dowodem powtarzania pobierania. Użytkownik potwierdził „jest super” i polecił pozostawić Agenta uruchomionego oraz opublikować wydanie z instalatorem.
