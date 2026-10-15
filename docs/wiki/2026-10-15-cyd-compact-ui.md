---
title: CYD-DECK — kompaktowy interfejs i jasność przy połączeniu z PC
tags: [cyd-deck, ui, lvgl, backlight, artwork]
aliases: [compact UI, rounded artwork, disable auto dim, pasek głośności]
updated: 2026-10-15
status: active
---

# Stan wejściowy

Zdjęcia IMG_5298.heic i IMG_5299.heic z Downloads użytkownika pokazują działającą okładkę 128×128 oraz zbyt masywny suwak. Symbol wyciszenia nie ma glifu w użytym foncie. Podglądy: artifacts/ui-reference/IMG_5298.png i IMG_5299.png. Oryginały nie zostały zmienione.

Późniejszy handoff raportuje usunięcie podwójnej inkrementacji usbCoverWritten_, render okładek Available/rendered=true, poprawne audio, top nav zamiast swipe oraz USB hello recovery. Wcześniejsze wpisy o niesprawnych okładkach dotyczą poprzedniego etapu. Bieżąca runda zachowuje te naprawy.

# Zmiany

| Element | Nowy układ / zachowanie |
| --- | --- |
| Nawigacja | Cztery zakładki 74×26, klikanie bez gestów swipe. |
| Okładka | Natywne 128×128 przy12,40; radius12, rzeczywiste clip_corner i brak ramki zmniejszającej zawartość. |
| Odtwarzanie | Wyrównane metadane i postęp; transport48×32 przy y140. |
| Głośność | Tor174×6 przy76,203; rozszerzony hit area16px, mały uchwyt, procent osobno po prawej. |
| Mute | 48×32 przy12,190; tekst Mute/Off zastępuje brakujący glif. Off ma jasny tekst na ciemnym tle. |
| Kolory | Tor i uchwyt używają aktualnego theme/accent z konfiguracji urządzenia. |
| Podświetlenie | Agent connected przez USB lub WS blokuje dim i screen-off oraz przywraca zwykłą ustawioną jasność. Po rozłączeniu timeout liczy się od nowa. Samo Wi-Fi nie blokuje wygaszania. |

Zmodyfikowano firmware/src/ui/ui_manager.cpp, firmware/include/ui/app_state.h, tests/ui_logic_test.cpp, tests/ui_layout_test.cpp i tests/fakes/lvgl.h. Protokół i Windows Agent bez zmian w tej rundzie.

# Weryfikacja

Test polityki idle przed poprawką zakończył się błędem na oczekiwaniu resetIdleTimer przy połączeniu z PC. Po poprawce testy ui_logic i ui_layout przechodzą, kompilowane z -Wall -Wextra -Werror. Obejmują wybudzenie z dim/off, timeout po rozłączeniu, clipping okładki, wymiary i hit area suwaka, tekst Off oraz istniejącą nawigację. Kompilacja firmware ST7789 zakończona SUCCESS bez ostrzeżeń; wynik finalnego uploadu dopisany poniżej.

Kontrola wizualna nowych narożników i komfort dotyku wymagają zdjęcia/testu nowego firmware na CYD. Nie należy utożsamiać atrap LVGL z weryfikacją pikseli na ST7789.

# Kontynuacja

Aktualne handoffy: [HANDOFF.md](../../HANDOFF.md), [STREAMDECK_HANDOFF.md](../../STREAMDECK_HANDOFF.md); historia: [docs/HANDOFF.md](../HANDOFF.md). Sprawdzić0/50/100% i Mute, brak swipe podczas regulacji, light/dark i długie tytuły, dłuższe połączenie z PC bez dim oraz timeout po odłączeniu.


## Wynik wdrożenia — 2026-10-15 około 22:29

- Końcowa kompilacja i upload ST7789 przez COM5: SUCCESS, brak błędów i ostrzeżeń w logu artifacts/compact-ui-upload.log, czas135,20s. RAM72860 B (22,2%), Flash1483261 B (73,0%). Ten build zawiera także końcową poprawkę kontrastu Off i usunięcie ramki okładki.
- Uruchomiono aktualny Agent artifacts/state-sync-agent/StreamDeck.WindowsAgent.exe z --usb-port COM5, PID28960 w chwili kontroli. Potwierdzono ExecutablePath. Dashboard55901 jest portem tej sesji, może zmienić się po restarcie.
- Świeża telemetria fizycznego CYD-40C86C o22:28:42: connected/USB, uptime57s, SDready, cover.status=2/rendered=true, audio.volumePercent=100/muted=false. Render okładki działa po zmianie clippingu. FreeHeap96176 B, największy blok45044 B.
- Testy C++ ui_logic i ui_layout PASS z -Werror; git diff --check dla tej rundy bez błędów. Nie modyfikowano .NET, więc nie powtarzano jego pełnego zestawu testów.
- Do potwierdzenia przez użytkownika: wizualne narożniki, wygoda hit area, brak dim przy dłuższym połączeniu i powrót do skonfigurowanych timeoutów offline. Telemetria nie jest zdjęciem ekranu ani testem dotyku.
