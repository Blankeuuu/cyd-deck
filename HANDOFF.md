---
title: CYD-DECK — bieżący handoff interfejsu urządzenia
tags: [cyd-deck, handoff, ui, firmware, release, 1.3.0]
aliases: [STREAMDECK_HANDOFF, CYD UI handoff]
updated: 2026-10-19
status: active
---

## 2026-10-19 — Wydanie 1.3.0: Web Installer, Tray Media Controls, Color Picker & Windows Setup

### Zakres i stan release'u v1.3.0
- **Wersja systemu:** 1.3.0 podbita we wszystkich komponentach (`firmware/include/ui/app_state.h`, `StreamDeck.WindowsAgent.csproj`, `installer/windows-agent.iss`, `tests/ui_layout_test.cpp`).
- **Przeglądarkowy instalator Web Serial (ESP Web Tools):**
  - Katalog: `installer/web/` (`index.html`, `manifest.json`, `bootloader.bin`, `partitions.bin`, `boot_app0.bin`, `firmware-ST7789-1.3.0.bin`).
  - Skrypt uruchomieniowy: `scripts/launch-web-flasher.ps1` (serwuje instalator lokalnie przez Python HTTP server na porcie 8080, co omija ograniczenia bezpieczeństwa Web Serial dla localhost bez konieczności konfiguracji HTTPS).
- **Instalator Windows (Inno Setup 6):**
  - Plik wynikowy: `artifacts/release-v1.3.0/CYD-DECK-Setup-1.3.0-win-x64.exe` (50.54 MB, SHA-256: `58E3ACF7EA97DE82B9501DB0C36C5061C5674E410AEFAF36FDD7DB7A9403CE67`).
  - Archiwum portable: `artifacts/release-v1.3.0/StreamDeck.WindowsAgent-win-x64.zip` (70.47 MB, SHA-256: `8217ABE634D114AE88F266C692287574AE106B4B398CE2E87B98A987BEE0395D`).
  - Czysta dezinstalacja: Wprowadzono hooki `InitializeSetup` oraz `InitializeUninstall` w Inno Setup, które bezszelestnie zabijają proces `StreamDeck.WindowsAgent.exe` (`taskkill /F /IM`), usuwają wpis autostartu w rejestrze `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` i całkowicie czyszczą pliki aplikacji bez błędów blokowania plików.
- **Firmware ESP32 (ST7789 320x240):**
  - Aplikacja (offset `0x10000`): `firmware-ST7789-1.3.0.bin` (SHA-256: `2EACB0410727B4C240A8AFD7ADF9082A7439BE466D10F90A6AEBF9F03505F739`).
  - Pełny obraz scalony (offset `0x0` z bootloaderem i partycjami): `firmware-ST7789-1.3.0-full.bin` (SHA-256: `AEE510C0EF71DB4D89CB118CB410448FD3BDF64ED183EC91EE631A1BC369D808`).
  - Plik sum kontrolnych: `artifacts/release-v1.3.0/SHA256SUMS.txt`.
- **Poprawka stabilności autoryzacji (Bearer Token):**
  - Wyeliminowano sporadyczny błąd odrzucania tokena parowania (`IsBearerToken` dopuszcza znaki `-` oraz `_` w standardzie Base64URL na każdej pozycji tokena; `CreateBearerToken` generuje początkowy znak alfanumeryczny).
- **Zasobnik systemowy (Tray UX):**
  - Bezpośrednie sterowanie multimediami (Play/Pause, Prev, Next, Mute/Unmute, poziom głośności) z menu ikony w zasobniku oraz powiadomienia balonowe o stanie połączenia z urządzeniem.
- **Odświeżone README.md i specyfikacja:**
  - Nowoczesne, bogate README w języku polskim z instrukcjami Web Installera, instalacji Windows i specyfikacją techniczną.
  - Dokumentacja wydania: `docs/releases/1.3.0.md`.

Poniżej historia wcześniejszych wdrożeń.

## 2026-10-17 — poprawki po manualnych testach Windows Agenta

- Aktualnie uruchomiony `artifacts/desktop-polish-20261017/StreamDeck.WindowsAgent.exe`, PID 23504, dashboard `http://127.0.0.1:63766`. USB connected, firmware 1.2.0, cover=2/rendered=true, audio=100/muted=false. GPU z PDH: 5,38%; temperatura niedostępna przez dostępne WMI, UI: Start LibreHardwareMonitor.
- Odtworzono i naprawiono ObjectDisposedException dla ContextMenuStrip (nie niszczyć w Closed; ownership do Dispose okna) oraz błąd named color Red (normalizacja podczas importu/load/save/sync). Default=null pozostaje dziedziczeniem motywu. Poprawiono ścieżki błędów profili.
- Nowy `SettingsWindow.Controls.cs`: rounded buttons, segmented controls i kontenery pól; zachowane klawiatura i dostępność. Default w całości, pełne COM/Ready, inputy 32 px, równe Save Shortcut/Delete i zaokrąglona biblioteka.
- Wykresy przy dole kart, stała wysokość 64 px i gradient area fill. Brak dashed border podglądu.
- `WindowsHardwareMetricsReader.cs`: PDH GPU Engine z właściwą agregacją iGPU/dGPU; asynchroniczne WMI Libre/OpenHardwareMonitor + ACPI. Dodano System.Management 10.0.0. Nie instalowano sterowników ani zewnętrznego monitora. Nie pokazuj estymacji jako rzeczywistej temperatury.
- Regression `--bug-repro` przeszedł po FAIL przed poprawką; DesktopViewportTests, SystemMonitoring.Tests i Configuration.Tests PASS. Logi: `polish-smoke-final.log`, `polish-system-tests.log`, `polish-config-tests.log`, `polish-publish.log`. Screenshoty nadal `artifacts/ui-review/desktop-*.png`.
- Szczegóły i źródła API: [Wiki napraw](docs/wiki/2026-10-17-agent-controls-bugfix.md). Dowód uruchomienia bez sekretów: `artifacts/polish-live-verification.json`.
- Firmware i zegar CYD niezmienione. Jednorazowe zadanie startu usunięte, Agent pozostawiony włączony. Zmiany nie zostały jeszcze commitowane/pushowane; zachowaj cały obecny worktree.

Poniżej wcześniejsze wdrożenia.

## 2026-10-17 — Windows Agent: cztery widoki desktopowe

**Bieżąca praca dotyczy wyłącznie PC. Firmware i zegar HH:mm w doku nie zostały zmienione.**

- Uruchomiono `artifacts/desktop-agent-20261017/StreamDeck.WindowsAgent.exe`, PID 7796, dashboard `http://127.0.0.1:52335`. USB connected, firmware 1.2.0, cover.status=2/rendered=true, volume=100. Agent pozostaje włączony.
- Sidebar 210 px: Deck Builder / Media Controls / System Monitor / Settings. Device, Pairing, Updates i Logs w menu Device & Tools.
- Jeden Unified Toolbar 56 px; główny AutoScroll=false. Scrolluje wyłącznie wewnętrzna Shortcut Library.
- Podglądy Shortcuts / Home Screen 4:3, galeria ikon zachowana, Assign/Unassign i drag/drop, Tile Inspector 354 px, trzy kolumny Settings, żywe media/audio i monitor 2×2 z 30 próbkami.
- Nowe pliki UI: `SettingsWindow.Desktop.cs`, `.Deck.cs`, `.MediaMonitor.cs`, `DesktopCanvas.cs`. Usunięto stare zastąpione Deck/Settings z Compact.cs.
- `DeckProfileConfiguration.AssignedShortcutIds` opcjonalne: sześć ID/null. Brak pola oznacza stare pierwsze sześć pozycji. Puste sloty kodowane jako bezczynne `__empty_slot_N`, ignorowane przez import raportów. Auto rozwiązywane w Runtime na istniejące layouty, aby nie przesuwać pozycji. Nie dodano pól protokołu firmware.
- `DesktopAppearanceConfiguration.Clock24Hour` dotyczy podglądów PC. CYD pozostaje w formacie HH:mm. Brak raportu Local Brightness Override jest jawnie pokazany jako Not reported.
- Release build/publish bez błędów/ostrzeżeń. WindowsAgent smoke PASS, Configuration tests PASS. Nowe DesktopViewportTests sprawdzają 4 widoki w 1200×780 i 1280×800, clipping kontenerów, Assign/Unassign, trwałość draftu, zapis, Volume/Mute, jasny i ciemny motyw.
- Rendery `artifacts/ui-review/desktop-*.png`; logi `desktop-smoke.log`, `desktop-config-tests.log`, `desktop-publish.log`. Dane testowe nie są zdjęciem TFT. Weryfikacja nie obejmowała fizycznej zmiany DPI monitora.
- Szczegóły: [Wiki desktop viewport](docs/wiki/2026-10-17-desktop-viewport.md). Dowód żywego połączenia: `artifacts/desktop-live-verification.json`.
- Przed zastąpieniem Agenta sprawdzono brak niezapisanych zmian. Jednorazowe ScheduledTask usunięto. Nie wykonano flasha, nowego pusha, instalatora ani release. Przy kolejnej pracy zachowaj wszystkie niezacommitowane zmiany.

Poniżej historia wcześniejszych wdrożeń.

## Wdrożenie zakończone sukcesem: Wydanie 1.2.0 (Wyśrodkowanie przycisków, Galeria ikon, Instalator Windows)

### Zakres i obowiązujące decyzje

Pracuj wyłącznie w `C:\Users\Syzmon\Documents\VAULT\projects\stream-deck`. Komunikacja i dokumentacja po polsku; interfejs produktu po angielsku. Zastosowano zasady `apple-design` do ciemnego motywu galerii ikon, ramek i podglądów.

**Zegar w doku zachowany.** Pierwszy przycisk nawigacji stale pokazuje `HH:mm` na wszystkich stronach urządzenia CYD; pozostałe przyciski to `Deck`, `Media`, `PC` oraz koło zębate diagnostyki.

### Faktyczny stan sprzętu i uruchomionego Agenta

- CYD ESP32-2432S028R / ST7789 320×240 na porcie **COM5**.
- Wdrożony najnowszy firmware 1.2.0 z wyśrodkowaniem ikon i etykiet przycisków, galerią 21 ikon, obsługą custom obrazów 24×24 RGB565A8, unieważnianiem cache oraz NVS v3.
- SHA256 wdrożonego firmware 1.2.0: `A008CE38DFF7566EB3B4DB9F5CB9E93A2702DCFAC70DC3CBD0AA08C720E72AEE`. Wgrano na `0x10000` narzędziem `esptool` (460800 baud) bez czyszczenia NVS.
- Uruchomiony Windows Agent: PID **20024**, dashboard `http://127.0.0.1:52075`, port agenta `8765`. Uruchomiony poprzez ScheduledTask Interactive z uprawnieniami zalogowanego użytkownika.
- Weryfikacja po wdrożeniu: telemetria `connected`, transport `USB`, `cover.status=2/rendered=true`, `firmwareVersion: 1.2.0`, `volume=100/muted=false`, `sd.mounted=true`.
- Przygotowany instalator Inno Setup: `artifacts/release-v1.2.0/CYD-DECK-Setup-1.2.0-win-x64.exe` oraz portable `StreamDeck.WindowsAgent-win-x64.zip`.
- Testy .NET: **PASS** (`StreamDeck.WindowsAgent.Tests`).
- Testy natywne C++: **PASS** (`scripts/test-ui-logic.ps1` ze sprawdzaniem asercji wyśrodkowania i FW 1.2.0).

### Zrealizowane funkcjonalności

1. **Wizualna galeria ikon (`ShortcutIconPicker`)**:
   - 21 wbudowanych symboli: `audio`, `star`, `check`, `gear`, `home`, `lock`, `mute`, `volume`, `camera`, `prev`, `play`, `pause`, `next`, `right`, `folder`, `browser`, `hotkey`, `terminal`, `mic`, `shortcut`, `app`.
   - Pełna zgodność wizualna między podglądem w Windows Agent a wyświetlaniem na CYD (wspólne maski alpha z tego samego fontu Font Awesome).
   - Nowoczesny dark mode inspirowany Apple HIG: subtelne obramowania kafelków, akcent `#5299E0` dla wybranego elementu, przycisk `Use icon` z akcentem, `Load preview` i `Cancel` z ciemnym tłem powierzchni.
2. **Import obrazków z URL**:
   - Bezpośrednie pobieranie HTTP/HTTPS obrazów PNG, JPEG, WebP, GIF, BMP (do 2 MB).
   - Skalowanie do bufora 24×24 z przezroczystością i zachowaniem proporcji przez SkiaSharp.
   - Kodowanie do 1728 bajtów `RGB565A8` (1152 B RGB565 little-endian + 576 B alpha plane) jako 2304 znaki Base64URL (bez znaku `+` i bez paddingu, aby uniknąć escape'ów JSON `\u002B`).
   - W przypadku błędu sieci/URL poprzednio wybrana ikona i URL są bezpiecznie zachowywane w pickerze.
3. **Obsługa na CYD (ESP32 / LVGL)**:
   - Dynamiczne renderowanie custom obrazka jako `lv_image` wewnątrz etykiety ikony kafelka na ekranach Home i Deck.
   - Odświeżanie cache LVGL oparte o pole `iconRevision`.
   - Zapis w NVS v3 z bezpieczną alokacją na stercie w celu ochrony stosu pętli głównej i omijaniem rewizji przy porównaniu niezmienionych ustawień.
   - Zachowany zegar `HH:mm` w doku.

### Narzędzia i ograniczenia środowiska

- PowerShell, workdir zawsze repo. Zwykły sandbox potrafi nie widzieć python/zig; `require_escalated` z konkretnym uzasadnieniem działa.
- Kompilator native: `.tools/windows-host-compiler/ziglang/zig.exe` (inne instalacje Zig miały Access denied).
- Firmware: `powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app -j 8`.
- Flash: `.tools/pio-venv/Scripts/python.exe .tools/platformio-core/packages/tool-esptoolpy/esptool.py --chip esp32 --port COM5 --baud 460800 --before default_reset --after hard_reset write_flash -z 0x10000 .pio-app/cyd28_st7789_app/firmware.bin`.
- Agent uruchamiać ScheduledTask z principal bieżącego użytkownika, LogonType Interactive, RunLevel Limited. Direct Start-Process wcześniej dawał niewidoczny window station.
- HEIC: stary `.tools/heif-py` ma Access denied. Zainstalowano lokalnie `.tools/heif-preview` (Pillow/pillow-heif) dla Python 3.12 z `.tools/pio-venv`. Konwersja zdjęcia została wykonana; oryginał nietknięty.
- Pliki ostatnio zapisane Pythonem mają CRLF; `git -c core.whitespace=cr-at-eol diff --check` nie raportował realnych problemów. Wyłączenie autocrlf w diff może wyprodukować tysiące pozornych trailing whitespace; unikaj pełnego takiego outputu.
- Nie wypisuj tokenów z hello, parowania ani konfiguracji. Nie publikuj teraz release/commita na podstawie starych próśb — bieżąca prośba to handoff.

Poniżej historia poprzednich wdrożeń.

## Korekta użytkownika: zegar w doku

Wdrożono na COM5; esptool potwierdził hash. Agent PID 10252 pozostaje włączony, dashboard http://127.0.0.1:64212. Stan: connected, transport USB. Dowód: artifacts/nav-clock-verification.json. SHA256: 450BF4B8F99D5739832E0558F6A1D025083B67E75116134B0702E5ACCDFA9E38.

Pierwszy przycisk nawigacji ponownie pokazuje bieżący czas `HH:mm`, na wszystkich stronach. Pozostałe elementy to Deck / Media / PC / koło zębate. Szerokość zegara wynika z najszerszej cyfry fontu; test obejmuje wszystkie 1440 godzin/minut doby. Zmiana zastępuje poprzednie ustalenie etykiety Home. Pozostałe poprawki ikon, daty i układu pozostają aktualne.

## Bieżące wdrożenie: Home / Deck i ikony skrótów

Wgrano na COM5, hash zapisu potwierdzony przez esptool. Agent pozostaje włączony: PID 24992, `artifacts/nav-agent/StreamDeck.WindowsAgent.exe`, dashboard `http://127.0.0.1:62155`. USB Connected, CYD-40C86C, cover status 2/rendered true. Bezpieczny zapis telemetrii: `artifacts/nav-verification.json`.

- Dok: **Home / Deck / Media / PC / koło zębate**, szerokości z metryk fontu + padding 10 px, odstępy 6 px. Home zastępuje czas jako nazwę pierwszej zakładki.
- Home: zegar po prawej, data `Fri, Oct 16` w jednej linii, szerokość 132 px. Odtwarzacz z tłem okładki po lewej; cztery skróty poniżej.
- Naprawiono ikony Home i Deck: osobny font `cyd_icons_20` (19 glyphów, bitmapy 3312 B) i oddzielne etykiety ikony/tekstu. Obsługa hotkey, browser, terminal, lock, star, folder, app, media itd. Tryby `icon`, `label`, `both` są respektowane; Label celowo nie wyświetla ikony.
- Windows: kompaktowy selektor profilu i przycisk zapisu, podgląd respektuje Display Mode. Protokół pozostaje `protocol:1`, bez zmian pól.
- Testy: ui_layout (w tym powtarzane przełączanie stron), font_glyph (rzeczywiste szerokości wszystkich kombinacji krótkiej daty i symbole), WindowsAgent smoke PASS. Agent Release 0 błędów/ostrzeżeń; firmware ST7789 bez błędów/ostrzeżeń, RAM 73512 B, flash 1666993 B.
- SHA256 firmware: `3A5101509CC558A04EF5CBB1EBFF9DD1DE235ECC1FE61A3036376D2EBAF9C8F5`. Parowanie, konfiguracja i SD zachowane; przed restartem brak niezapisanych zmian. Jednorazowe zadanie startu usunięto.
- Telemetria potwierdza stan połączenia i renderowania okładki; fizyczne wyrównanie ikon i ergonomia wymagają oceny użytkownika na TFT.

Poniżej historia poprzednich wdrożeń; powyższa sekcja określa aktualny stan.

## Najnowsze doprecyzowanie Default

Wgrane na COM5 i połączone po USB. Agent pozostawiony włączony: PID 7788, `artifacts/ui-refactor-final/StreamDeck.WindowsAgent.exe`, dashboard `http://127.0.0.1:65163`. Okładka status 2/rendered true. Aktualny zapis weryfikacji: `artifacts/home-verification.json`.

Dok wyświetla HH:mm zamiast Default. Na Home Screen muzyka z okładką jako tłem jest po lewej, zegar po prawej; pod nimi cztery większe kafelki 148×44 w układzie 2×2. Zmiana ograniczona do firmware i testów widoku. Szczegóły: [Ustalenia UI — Default](docs/ui-refactor-decisions.md#doprecyzowanie-default--bieżący-widok).

## 2026-10-16 — kompaktowy UI, Default i jawny zapis (bieżące)

- Wdrożono zmianę uzgodnioną przez `grill-with-docs` i dodatkową prośbę użytkownika: CYD startuje na Default z zegarem 48 px, małym odtwarzaczem i czterema skrótami. Dok: Default / Shortcut / Media / PC + osobna ikona ustawień.
- Kalibracja, test dotyku, jasność, restart/reset i dane techniczne są w ustawieniach pod kołem zębatym. Wszystkie systemowe napisy są angielskie; treści użytkownika nie są tłumaczone.
- Windows: `SettingsWindow.Compact.cs`, toolbar 60 px, podgląd + edytowalny Tile Inspector, trzy kompletne karty Settings w 1200×760/100%, podzakładki przy mniejszej przestrzeni. Profile New/Duplicate/Delete i katalog zachowują dane offline. Usunięto ponad 1000 linii zastąpionych widoków.
- Jeden jawny Save/Save & Sync. Draft przetrwa nawigację; zamknięcie okna oferuje Save/Discard/Cancel. Import pełnej konfiguracji jest również roboczy do Save. Zapis lokalny jest atomowy, wynik wysłania odrębny od zapisu.
- Opcjonalny kolor pojedynczego kafelka i migracja NVS shortcuts v1 → v2. Rewizja jasności odróżnia nowy zapis, także offline, od reconnect. Wspólny `protocol:1` zachowany.
- Firmware ST7789 zbudowany bez ostrzeżeń i wgrany na COM5, hash po zapisie zweryfikowany przez esptool. RAM statyczny 73456 B (22.4%), flash 1591069 B (78.3%).
- Testy: Protocol 80/80, Shortcuts 22/22, Configuration PASS, WindowsAgent smoke PASS, C++ ui_logic/ui_layout/network_protocol/usb_protocol PASS. Rendery wszystkich zakładek w `artifacts/ui-review/compact-*.png`; dodatkowo symulacja skalowania 150% (nie zastępuje testu zmiany DPI na monitorze).
- Po wdrożeniu: USB Connected, CYD-40C86C, cover status 2/rendered true, audio 100%/muted false. Agent pozostaje uruchomiony; jednorazowe zadanie Harmonogramu usunięto po starcie. Nie usunięto parowania, cache SD ani konfiguracji użytkownika.
- Dokumenty: [Ustalenia UI](docs/ui-refactor-decisions.md), [ADR 0002](docs/adr/0002-explicit-save-and-offline-sync.md), [Protokół](docs/protocol.md), [Wiki refaktoryzacji](docs/wiki/2026-10-16-compact-ui.md).

Poniższe starsze sekcje opisują wcześniejsze wdrożenia. Fizyczny wygląd i ergonomia nowego Default oczekują oceny użytkownika; telemetria i testy nie są zdjęciem wyświetlacza.



# Kontynuacja projektu

Pracuj wyłącznie w projekcie stream-deck. Pełny historyczny kontekst: [docs/HANDOFF.md](docs/HANDOFF.md). Fizyczna płytka ESP32-2432S028R, ST7789 320×240; COM5. Nie resetuj istniejących zmian w repozytorium. Komunikacja po polsku.


# Dopracowanie UI według IMG_5298/IMG_5299 — 2026-10-15

## Źródło i zakres

Zdjęcia użytkownika: C:\Users\Syzmon\Downloads\IMG_5298.heic i IMG_5299.heic. Podglądy PNG do oceny zapisano w artifacts/ui-reference/. Oryginały pozostawiono bez zmian. Zdjęcia potwierdzają działające okładki i pokazują zbyt masywny suwak oraz pusty glif Mute. Poprzednie wpisy o cover.status=3 są historyczne; późniejszy handoff raportuje naprawę podwójnej inkrementacji odbiornika i cover.status=2/rendered=true.

## Zmiany w kodzie

- firmware/src/ui/ui_manager.cpp: górne zakładki 74×26 zamiast 74×28, zachowane kliknięcia i wyłączone swipe. Okładka nadal natywna 128×128; radius12, brak obramowania i clip_corner=true rzeczywiście przycinają obraz do narożników rodzica. Pozycja okładki12,40. Tekst i transport mają równe odstępy i mniejsze przyciski48×32.
- Głośność: Mute48×32 przy12,190. Zamiast brakującego glifu wyciszenia etykiety Mute/Off; aktywne Off ma biały tekst na ciemnym tle. Tor174×6 przy76,203, mały uchwyt, rozszerzony obszar dotyku16px. Procent w osobnej etykiecie44×20 przy264,198 — nie nachodzi na tor. Kolor toru/uchwytu stosuje bieżący accent i theme, zamiast domyślnego niebieskiego LVGL.
- firmware/include/ui/app_state.h: aktywne agentConnected blokuje dim i screen-off, przywraca zwykłą ustawioną jasność i resetuje licznik idle. Po rozłączeniu PC timeout startuje od nowa. Samo Wi-Fi bez Agenta nie blokuje wygaszania. Ustawienia czasu dim/off pozostają zapisane i obowiązują offline.
- tests/ui_logic_test.cpp: test blokady połączenia, wybudzenia z dim/off i powrotu do timeoutu offline. Test przed zmianą był RED na asercji resetIdleTimer, po poprawce PASS.
- tests/ui_layout_test.cpp i tests/fakes/lvgl.h: regresja clippingu, wymiarów toru, obszaru dotyku, czytelnego Off oraz dotychczasowej nawigacji/layoutu. Celowane buildy C++ z -Wall -Wextra -Werror: PASS.

## Zachowane wcześniejsze naprawy

Top Nav zamiast swipe, native128 artwork, obsługa volume/mute Windows, hello probe i USB recovery15s. Nie cofano wcześniejszych niezacommitowanych zmian. Protokół bez zmian w tej rundzie. Nie zmieniano Windows Agenta.

## Weryfikacja i kontynuacja

Wynik kompilacji/wdrożenia dopisany poniżej. Sprawdzić fizycznie: narożniki okładek, wygodę dotyku suwaka0/50/100%, Mute/Off, brak przypadkowej zmiany strony, motywy dark/light, długie tytuły, pełną jasność przy aktywnym Agencie oraz dim/off dopiero po rozłączeniu. Zdjęcia referencyjne pokazują stary layout; nie stanowią potwierdzenia nowych zmian.


## Wynik wdrożenia — 2026-10-15 około 22:29

- Końcowa kompilacja i upload ST7789 przez COM5: SUCCESS, brak błędów i ostrzeżeń w logu artifacts/compact-ui-upload.log, czas135,20s. RAM72860 B (22,2%), Flash1483261 B (73,0%). Ten build zawiera także końcową poprawkę kontrastu Off i usunięcie ramki okładki.
- Uruchomiono aktualny Agent artifacts/state-sync-agent/StreamDeck.WindowsAgent.exe z --usb-port COM5, PID28960 w chwili kontroli. Potwierdzono ExecutablePath. Dashboard55901 jest portem tej sesji, może zmienić się po restarcie.
- Świeża telemetria fizycznego CYD-40C86C o22:28:42: connected/USB, uptime57s, SDready, cover.status=2/rendered=true, audio.volumePercent=100/muted=false. Render okładki działa po zmianie clippingu. FreeHeap96176 B, największy blok45044 B.
- Testy C++ ui_logic i ui_layout PASS z -Werror; git diff --check dla tej rundy bez błędów. Nie modyfikowano .NET, więc nie powtarzano jego pełnego zestawu testów.
- Do potwierdzenia przez użytkownika: wizualne narożniki, wygoda hit area, brak dim przy dłuższym połączeniu i powrót do skonfigurowanych timeoutów offline. Telemetria nie jest zdjęciem ekranu ani testem dotyku.


# Mute i lagi przy USB — 2026-10-15

Zdjęcie IMG_5300.heic (podgląd artifacts/ui-reference/IMG_5300.png) pokazało zawinięty napis Mute. Użytkownik wskazał, że najbardziej lagują przełączanie zakładek i dotyk.

## Dowody i poprawki

- Zmierzono na CYD networkMaxUs=101117918 i loopMaxUs=101233976 — ponad101s blokady obsługi sieci w pętli UI. Biblioteka mDNS używa synchronicznego zapytania z timeout3000ms; Wi-Fi discovery i WebSocket były obsługiwane mimo aktywnego kabla. Nie przypisano całych101s samemu mDNS; pomiar obejmuje całą obsługę sieci.
- Zawieszono discovery/WebSocket i fetch HTTP okładek przy autoryzowanym USB oraz podczas krótkiej negocjacji po probe. Wi-Fi STA pozostaje dostępne dla OTA/IP, po utracie USB wraca transport sieciowy.
- W logach znaleziono ponowny transfer tej samej okładki niemal co sekundę, bez zmiany sygnatury utworu. Przyczyna: firmware odpowiadało hello na odpowiedź hello Agenta; Agent ponownie wykonywał pełną synchronizację, a sink kasował deduplikację. Odpowiedź urządzenia jest teraz wysyłana wyłącznie dla pustego discovery probe; role=agent nie uruchamia kolejnego hello.
- Dodano TX UART2048 przed Serial.begin (RX8192 zachowane) oraz limit odbioru2048bajtów/2ms na iterację, z zachowaniem fragmentu linii między iteracjami. Pełne parsowanie/SD/dekodowanie obrazu nadal kosztuje czas, nie deklaruj wszystkich operacji poniżej2ms.
- Mute ma ikonę speaker/mute ze standardowego lv_font_montserrat_16, który rzeczywiście zawiera glif F026. Usunięto napis łamiący się na dwie linie; teksty pozostają czytelne w light/dark.
- Podczas przeciągania suwaka procent aktualizuje się lokalnie, a opóźniona ramka Windows nie cofa uchwytu pod palcem.
- Opcjonalna telemetria performance i walidacja/DTO w Agencie; aktualny Agent artifacts/usb-perf-agent/. Miar nie interpretować jako FPS ani gwarantowanego czasu każdego dotknięcia.

## Testy

Protocol78/78 PASS, pełne WindowsAgent smoke PASS, build.NET0errors/0warnings. C++ layout/dragging PASS, rzeczywiste glify mute/speaker PASS, USB probe vs role=agent PASS; buildy testów z -Werror. Zmieniono standardowy skrypt test-ui-logic.ps1, aby linkował font Montserrat16 do testu glifów.

Artefakty pomiarowe: artifacts/usb-perf-baseline.json (próg50ms RED), usb-perf-baseline-blocked.json (101s), usb-perf-usb-priority.json (po wstrzymaniu transportu Wi-Fi, przed naprawą pętli hello). Wynik końcowej wersji i testu na sprzęcie dopisany poniżej. Nie deklaruj sukcesu na podstawie samego buildu ani pierwszej próbki obejmującej tylko startup.


## Stan po końcowym wgraniu — oczekiwanie na zgodę na naprawę parowania

Firmware z naprawą hello, zawieszeniem discovery/WebSocket podczas USB, ikoną Mute i TX2048 wgrano na COM5; hash potwierdzony, reset RTS. Log artifacts/usb-responsive-final-flash.log. Kompilacja.NET i firmware bez ostrzeżeń. Tymczasowe znaczniki DEBUG-usb-lag usunięte ze źródeł.

Końcowe sprawdzenie ujawniło dodatkowy problem: zapisany token CYD ma29znaków, podczas gdy istniejący zatwierdzony rekord Windows ma43; porównanie w pamięci wykazało brak zgodności. Rekord parowania na Windows pochodzi z29września i nie został zmieniony w tej rundzie. Przyczyna zmiany tokenu na urządzeniu nie została potwierdzona. Nie drukowano wartości tokenów. Z tego powodu Agent odbiera hello, ale odrzuca autoryzację — nie traktuj tego jako sukcesu końcowego benchmarku.

Automatyczna kontrola uprawnień odrzuciła wysłanie odszyfrowanego, istniejącego tokenu Windows na CYD, wymagając wyraźnej zgody na konkretny transfer sekretu. Zadano użytkownikowi pytanie asynchroniczne o zgodę na przywrócenie parowania przez COM5. Nie ponawiaj tej czynności bez odpowiedzi. Nie zastępuj jej obejściem. Agent pozostawiony uruchomiony z artifacts/usb-perf-agent/StreamDeck.WindowsAgent.exe (PID28300 w chwili kontroli), bez logów tymczasowych; urządzenie może pozostać nieautoryzowane do przywrócenia parowania.

Po zgodzie przywrócić istniejące zaufane parowanie bez ekspozycji sekretu, uruchomić Agenta i powtórzyć40–60s pomiar z klikaniem zakładek. Sprawdzić stałą liczbę discovery przy USB i brak ponownych transferów przy tej samej okładce. Wcześniejszy pomiar po samym priorytecie USB miał maxsieci~524ms (zawierał startup i powtarzane artwork), a przed zmianami~101s. Końcowy pomiar po poprawce hello i TX pozostaje otwarty. Nie przedstawiaj progowego testu50ms jako zaliczonego.


## Naprawa otwierania okna Windows — 2026-10-15 około 23:22

Proces28300 działał, ale natywne okno CYD-DECK miało IsWindowVisible=false, mimo wpisów ShowMainWindow completed. Przyczyną był start z WindowStyle Hidden i rozjazd natywnej widoczności z właściwością WinForms Visible. W SettingsWindow.ShowAndActivateWindow dodano ShowWindow(Handle, SW_SHOW), aby jawne otwarcie przywracało również HWND bez utraty stanu maksymalizacji.

Build0errors/0warnings; uruchomiono widocznie artifacts/visible-agent/StreamDeck.WindowsAgent.exe --usb-port COM5 (WindowStyle Normal, użytkownik prosił o okno). Potwierdzono PID20288, Responding=true, MainWindowTitle=CYD-DECK, MainWindowHandle=23136676. Te identyfikatory są historycznym wynikiem kontroli. Parowania ani tokenów nie zmieniano; zgoda na przywrócenie parowania nadal wymaga osobnej odpowiedzi użytkownika.


## Parowanie przywrócone za zgodą użytkownika — 2026-10-15 około23:29

Użytkownik wyraźnie zatwierdził przesłanie istniejącego tokenu Windows do CYD przez COM5 (odpowiedź „tak”). Przywrócono zatwierdzony token z chronionego rekordu Windows na fizycznym CYD-40C86C przez USB. Potwierdzono zgodność i długość43znaki; wartości sekretu nie wypisywano ani nie zapisywano do logów. Rekordu Windows i pozostałych ustawień nie kasowano. Wcześniejsza blokada zgody nie jest już aktualna.

Uruchomiono widoczny artifacts/visible-agent/StreamDeck.WindowsAgent.exe --usb-port COM5; potwierdzono PID29148, Responding=true, okno CYD-DECK. /api/status port64697 tej sesji: connected, deviceIdCYD-40C86C, remoteAddressUSB, świeża telemetria23:29:38, audio100%/muted=false. Procesy/porty są historycznym stanem kontroli.

UWAGA: ta próbka raportuje cover.status=3/rendered=false; przywrócenie połączenia nie jest potwierdzeniem poprawnego obrazu. Końcowy test stabilności zapisano w artifacts/usb-perf-after-pairing.json. Ocena responsywności podczas dotyku i ponowny test okładki pozostają potrzebne. Nie deklaruj zaliczenia progu50ms — maksima obejmują początkową synchronizację (loop~515ms, network~509ms, render~35ms), a wcześniejszy baseline zawierał~101s blokadę.


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


# Wydanie1.1.0 — publikacja na polecenie użytkownika

Użytkownik potwierdził działanie („jest super”), polecił pozostawić Agenta uruchomionego oraz opublikować najnowszy kod z instalatorem na GitHub. Test kontrolnego restartu z ciepłym cache:0transferów USB, coverAvailable/rendered=true, stały licznik discovery i stabilne połączenie. Wynik: artifacts/cache-sd-warm-reboot-verification.json.

Wersja aplikacji i firmware w źródłach1.1.0. Dodano installer/windows-agent.iss, scripts/build-windows-installer.ps1 oraz ikonę EXE z istniejącego AppIconFactory. Paczka self-contained zawiera runtime.NET10.0.13 i właściwe notices. Instalator per-user, menu Start, opcjonalny pulpit, zachowanie konfiguracji/parowania przy odinstalowaniu. Installer unsigned. Publikowane artefakty: installerEXE, portableZIP, aplikacjaST7789BIN, scalony obraz i paczka firmware, SHA256SUMS.

Działający Agent pozostaje uruchomiony: artifacts/cache-agent/StreamDeck.WindowsAgent.exe --usb-port COM5, PID13276 w chwili kontroli. Nie instalowano na tej maszynie nowego produktu ani nie restartowano urządzenia podczas budowy wydania. Historia niżej/starsze PID-y nie opisują obecnej sesji. Instrukcja: docs/releases/1.1.0.md; release: https://github.com/Blankeuuu/stream-deck/releases/tag/v1.1.0.
