---
title: CYD-DECK — naprawa okładek i synchronizacji stanu
tags: [cyd-deck, bugfix, usb, websocket, smtc, firmware]
aliases: [State Sync Refactor, artwork loading, synchronizacja stanu]
updated: 2026-10-15
status: active
---

# Zakres

Zmiany dotyczą wyłącznie projektu stream-deck. Naprawy wykonano po analizie transportu, protokołu, SMTC, CoreAudio, telemetrii i obu interfejsów. Pierwszy etap obejmował build i testy bez wdrożenia. Następnie wgrano firmware oraz uruchomiono nowego Agenta; aktualny wynik sprzętowy opisano poniżej.

## Zmodyfikowane moduły

| Obszar | Pliki | Zmiana |
| --- | --- | --- |
| Okładki Windows | CoverProcessor.cs, WindowsAgentRuntime.cs, UsbCoverSender.cs, UsbDeviceRuntime.cs | Cache uwzględnia rozmiar/jakość; baseline JPEG 128x128 quality 80; serializacja metadata przed chunkami, deduplikacja hash, chunki 512 B. |
| Synchronizacja | DeviceProtocolSession.cs, WindowsAgentWebSocketServer.cs, WindowsAgentRuntime.cs, UsbDeviceRuntime.cs | Po autoryzacji USB/WS skróty, appearance, audio, media, żądanie telemetrii; dopiero potem connected. |
| Protokół | Messages.cs, ProtocolConstants.cs, ProtocolSerializer.cs | Opcjonalne hello.deviceInfo i device.status.request; starsze hello nadal akceptowane. |
| Telemetria/UI Windows | AgentState.cs, AgentStatusFormatter.cs, SettingsWindow.cs | Connected now, wspólny aktualny firmware/IP, reaktywne Device/Updates, dostępność OTA zależna od aktualnego IP i połączenia. |
| Audio | WindowsAgentRuntime.cs, app_state.h, agent_client.cpp, ui_manager.cpp | Odświeżenie endpointu, audio przed media, natychmiastowe powiadomienia CoreAudio, pole muted. |
| UI i logi Windows | SettingsWindow.cs, AgentDiagnosticLog.cs | Jasny podgląd rzeczywistych skrótów, przewijanie backlight, układ kolejności stron, ciemne nagłówki, szerszy Address, wyłączone puste Approve, redakcja sekretów. |
| Firmware | cover_art.h, agent_client.cpp, app.cpp, app_state.h, ui_manager.cpp | Timeout/fallback i ochrona gotowej okładki, render po complete, RGB565, Serial RX 8192, hello i okresowa telemetria po obu transportach, zapis skrótów NVS, kontrast statusów i Loading. |
| Regresja | CoverProcessorTests.cs, DeviceProtocolSessionTests.cs, HydrationProtocolTests.cs, WindowsAgent.Tests, tests/*protocol_test.cpp, ui_layout_test.cpp, ui_logic_test.cpp, fakes/lvgl.h | Kolejność, cache i format JPEG, starsze hello, sekrety, przewijanie, stany okładki i kontrast. |

## Protokół

Koperta pozostaje protocol 1; szczegóły w [[../protocol]]. coverId pełni rolę hasha zawartości. media.update poprzedza cover.chunk/cover.complete; complete uruchamia render. Powtórzone media tego samego hasha nie resetują okładki. device.status.request wymusza device.info. Hello może przenosić pełny deviceInfo w istniejącym zagnieżdżonym schemacie wifi/sd/heap. Audio używa istniejącego pola muted, nie isMuted. Nie dodano osobnego artwork_ready ani USB OTA.

## Weryfikacja automatyczna

- Windows Agent: kompilacja 0 błędów, 0 ostrzeżeń; pełne smoke tests PASS (w tym 84 przełączenia zakładek).
- Covers: 12 testów PASS; PNG/WebP konwertowane do baseline JPEG, kontrola SOF0/braku SOF2 oraz rozdzielenia cache dla rozmiarów.
- Host: 38 testów PASS; Protocol: 76 testów PASS; Audio: 6 testów PASS.
- Odczyt rzeczywistego endpointu CoreAudio bez zmiany głośności: 100%, muted=False, PASS.
- C++: network_protocol, usb_protocol, ui_layout, ui_logic PASS; kompilowane z -Wall -Wextra -Werror.
- Zrzuty UI: artifacts/ui-review/pairing-minimum.png i appearance-scrolled.png; sprawdzono nagłówki i dostępność suwaków nad dolnym paskiem.
- Firmware ST7789: kompilacja SUCCESS, 0 błędów i 0 ostrzeżeń w logu; RAM 72 828 B (22,2%), Flash 1 481 121 B (72,9%).
- Build Agenta: artifacts/state-sync-agent/. Firmware ST7789: .pio-app/cyd28_st7789_app/firmware.bin; log końcowej kompilacji: artifacts/state-sync-firmware-final.log.

## Weryfikacja sprzętowa

Nowe buildy wdrożono razem 2026-10-15 około 18:43. Połączenie i świeża telemetria są potwierdzone; okładka nadal zgłasza błąd. Pozostałe funkcje mają status AWAITING HARDWARE VERIFICATION. Sprawdzić Spotify/YouTube (zmiana utworu, pauza, powtórzone metadata), reconnect USB/Wi-Fi, skróty po restarcie, live audio/mute, kolory i paginację, telemetrię oraz OTA. Automatyczne testy nie potwierdzają fizycznego renderowania JPEG ani migotania ST7789. Nie uruchamiano OTA ani mutujących testów głośności Windows. Nie dodano osobnego testowego pliku progressive JPEG; dekodowanie wejścia odbywa się przez Skia, wyjście zweryfikowano jako baseline.


## Wdrożenie i rzeczywisty wynik — 2026-10-15, około 18:43

Przed wdrożeniem działał starszy Agent PID 28688 (build 17:41:48), podczas gdy poprawiony build powstał później. Odczyt starego Agenta wskazywał telemetrię zatrzymaną na uptime 15 s oraz cover.status=1, rendered=false. Sam wcześniejszy wynik kompilacji nie oznaczał wdrożenia napraw.

- Pełne wgranie cyd28_st7789_app przez COM5 zakończone SUCCESS; hash danych potwierdzony, restart RTS. Log: artifacts/handoff-device-upload.log, czas 268,25 s. Wcześniejszy upload przerwany na 27% został zastąpiony tym pełnym wgraniem.
- Nowy Agent: artifacts/state-sync-agent/StreamDeck.WindowsAgent.exe, argument --usb-port COM5, potwierdzony PID 28772 i ExecutablePath. PID i port dashboardu są historycznym stanem tej kontroli, mogą zmienić się po restarcie.
- CYD-40C86C: connected przez USB, firmware 1.0.0, uptime 37 s, Wi-Fi IP 192.168.50.205, SD ready, świeża telemetria z 18:42:53.97674+02:00.
- **Okładki nadal niesprawne:** cover.status=3, rendered=false. Wdrożenie nie rozwiązało całego problemu. Następna diagnoza musi rozdzielić odbiór chunków, zapis SD oraz dekodowanie/render JPEG.
- **Głośność na CYD niepotwierdzona:** poprawny odczyt CoreAudio na PC nie potwierdza odebranej i wyświetlonej wartości na urządzeniu.
- **Pełny redesign UI niewdrożony:** użytkownik nadal ocenia aplikację jako nieczytelną i pogmatwaną. Wcześniejsze lokalne poprawki UI nie spełniają całego wymagania.

## Skill i dalszy kierunek UI

Zainstalowano frontend-design z repozytorium anthropics/skills jako C:\Users\Syzmon\.codex\skills\cyd-ui-design. Instrukcje zastosowano do planu przebudowy, zapisanego w [[projects/stream-deck/docs/HANDOFF|handoffie]]. Plan: rzeczywisty podgląd sześciu przycisków jako główny element, krótszy Overview, grupy nawigacji oddzielające konfigurację od utrzymania, ograniczenie liczby akcji w stopce i menu dla rzadkich operacji. To plan, nie wykonana implementacja.

## Następna weryfikacja

1. Odtworzyć cover.status=3 na aktualnym firmware i Agencie przy Spotify oraz przeglądarce; zapisać konkretny etap niepowodzenia bez sekretów.
2. Porównać odczyt CoreAudio, wysłane audio.update, stan odebrany przez firmware i wyświetlany procent.
3. Wdrożyć uproszczony UI, sprawdzić zrzuty 900×620 i 1200×800 oraz light/dark, klawiaturę i DPI.
4. Ponownie przetestować fizyczny CYD. OTA, migotanie i pełna regresja sprzętowa pozostają otwarte.


## Nowszy etap — około 22:29

Wcześniejsze zgłoszenie cover.status=3 zostało rozwiązane w późniejszej rundzie (podwójna inkrementacja odbiornika USB). Po wdrożeniu kompaktowego UI telemetria potwierdza cover.status=2/rendered=true i audio100%/muted=false. Aktualny opis: [[projects/stream-deck/docs/wiki/2026-10-15-cyd-compact-ui]]. Pełny kontekst i bieżące zadania: [[projects/stream-deck/HANDOFF]].
