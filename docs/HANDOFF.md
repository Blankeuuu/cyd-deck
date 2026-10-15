---
title: Handoff CYD-DECK — artwork, audio i przebudowa UI
tags: [cyd-deck, handoff, bugfix, ui, embedded]
aliases: [CYD-DECK handoff, artwork audio UI redesign]
updated: 2026-10-16
status: active
---

# Cel i ograniczenia

Pracuj wyłącznie w C:\Users\Syzmon\Documents\VAULT\projects\stream-deck. Komunikacja i dokumentacja po polsku. Nie ruszaj innych projektów. Repo ma liczne wcześniejsze niezacommitowane zmiany — nie resetuj ich. Nie spawnuj subagentów bez upoważnienia. Firmware fizycznego ESP32-2432S028R: ST7789 320x240, środowisko cyd28_st7789_app, port COM5. Spotify i przeglądarki to główne źródła mediów.

Najnowsza prośba użytkownika: artwork nadal nie działa w Spotify ani przeglądarce, głośność również; cały UI Windows Agenta jest brzydki i pogmatwany, wymaga przebudowy. Następnie użytkownik poprosił o instalację skilla, użycie go i handoff rozmowy. Handoff nie oznacza ukończenia naprawy.

# Kluczowy dowód z żywego środowiska

Działał PID 28688 z agent/src/WindowsAgent/StreamDeck.WindowsAgent/bin/Debug/net10.0-windows10.0.17763.0/win-x64/StreamDeck.WindowsAgent.exe, build 17:41:48, start 17:42:19. Poprzednio poprawiony build artifacts/state-sync-agent/StreamDeck.WindowsAgent.dll pochodzi z 18:14. Poprawki nie były wdrożone — poprzednia odpowiedź raportowała tylko testy i build. Powiedziano to użytkownikowi i rozpoczęto wdrożenie.

Read-only /api/status starego Agenta: connected, CYD-40C86C, USB, LAN PC 192.168.50.187, urządzenie 192.168.50.205; telemetria stale wskazywała uptimeSeconds=15, timestamp 17:42:36, cover.status=1 i rendered=false. To potwierdza utknięcie okładki i telemetrii na starej wersji, ale nie dowodzi jeszcze sukcesu nowych poprawek na sprzęcie.

PID 28688 zatrzymano w celu zwolnienia COM5. Upload rozpoczęty przed przerwaniem tury zatrzymał się na 27%, bez SUCCESS. Konieczne było ponowne pełne wgranie. Aktualny wynik oraz stan Agenta dopisane na końcu tego dokumentu. Nie zakładaj sukcesu na podstawie samego wcześniejszego buildu.

# Już wykonane poprawki w kodzie

Szczegóły: [[wiki/2026-10-15-state-sync-refactor]], [[protocol]], [[wiki/2026-10-15-agent-ui]].

1. CoverProcessor: Skia decode/crop, baseline JPEG; WindowsAgentRuntime wybiera 128x128 quality80; klucz cache zawiera rozmiar/jakość/wersję. USB metadata przed chunks; mediaFrameGate/coverTransferGate; dedup coverId; chunk512 B +20ms; relaxed JSON escaping zapobiega utracie plusów Base64.
2. DeviceProtocolSession: po autoryzacji shortcuts.update -> device.settings -> audio.update -> media/cover -> device.status.request -> connected, dla USB i WS. HelloMessage ma opcjonalne DeviceInfo. Agent przekazuje aktualną konfigurację i exact sink.
3. Firmware: Serial RX8192 przed begin; timeout Pending15s, ten sam hash nie resetuje okładki, spóźniony transfer innego hasha nie renderuje, cover.complete render+RefreshUi+telemetry. RGB565 bytes conversion po JPEG. HTTP cover fetch wyłączony przy USB. Hello i device.info współdzielą pełny payload; USB heartbeat/telemetry obsługiwane także przy WiFi bez WS. NVS zapisuje6slotów skrótów.
4. CoreAudio: eRender/eMultimedia, Refresh w hydration, audio przed media, AudioChanged subskrypcja. Protocol audio.update ma muted (nie isMuted); firmware finalnie czyta muted.
5. AgentState/SettingsWindow: wspólna telemetria, Connected now, Device reaktywny, Updates aktualny IP/firmware i OTA eligibility. Maskowanie bearerToken/token/password także w starych logach. Appearance actual shortcut labels, jasny enabled preview, scroll i układ stron; dark native ListView header gap; Address szerszy; puste Approve wyłączone.
6. CYD: Loading..., No artwork, ukrycie placeholdera po gotowej okładce, dark emerald/navy +white status text. Cache renderowania ogranicza zbędne repaint.

Poprzednie rundy: lock SMTC source dictionary (798/800 crash ->0/800), STA smoke runner DragDrop, shortcuts reorder/catalog update. Nie wdrażaj ponownie bez analizy.

# Testy potwierdzone przed ostatnim zgłoszeniem

- Agent build:0errors/0warnings, pełne smoke PASS (84przełączenia zakładek).
- Covers12, Host38, Protocol76, Audio6: PASS.
- Real CoreAudio read-only:100%, muted=False.
- C++ network_protocol, usb_protocol, ui_layout, ui_logic PASS z -Wall -Wextra -Werror.
- Firmware build SUCCESS0warnings, RAM72828 B, Flash1481121 B przed ponownym wdrożeniem. Nowy rebuild może mieć inny rozmiar.
- Brak potwierdzenia fizycznego renderu i działania głośności po wdrożeniu. Nie deklaruj naprawy na sprzęcie bez danych.

# Zainstalowany i zastosowany skill

Na prośbę użytkownika zainstalowano oficjalny anthropics/skills, skills/frontend-design, jako katalog C:\Users\Syzmon\.codex\skills\cyd-ui-design. Wewnętrzna nazwa YAML pozostaje frontend-design. Instalacja helperem skill-installer zakończyła się Installed. Przeczytano SKILL.md i zastosowano jego proces do poniższego kierunku. Automatyczne wykrycie zainstalowanego skilla powinno być dostępne od kolejnej tury. Istnieją także lokalne diagnosing-bugs i frontend-design; instrukcje diagnostyki przeczytane. Nie wykonano jeszcze redesignu w kodzie.

## Kierunek UI wynikający ze skilla

Platforma: Windows WinForms, fizyczny kontroler6przycisków. Główny cel: skonfigurować układ CYD i szybko zobaczyć, czy urządzenie działa. Rozpoznawalnym elementem ma być rzeczywisty podgląd siatki3x2, z prawdziwymi skrótami i ich pozycjami.

Tokeny: ink #20242B, canvas #F4F6F8, surface #FFFFFF, muted #657080, divider #DEE3E9, action #2864B4. Dark theme analogiczny semantycznie; jeden akcent dla aktywnej nawigacji i głównej akcji. Bez all-caps ozdobników i ciężkich ramek wszędzie. Typografia Segoe UI Variable/Segoe UI (Windows), title22, section12semibold, body10–11; tekst lewe wyrównanie. To decyzja projektowa, nie wdrożone kolory.

Układ:

    Sidebar ~200px      Content
    CYD-DECK            Tytuł + jednozdaniowy opis
    Overview            [Connected · USB · device name]
    Shortcuts           [rzeczywisty podgląd CYD 3x2]
    Appearance          [Configure shortcuts] [Appearance]
    Device              krótki firmware/IP/last seen
    — Maintenance —
    Pairing / Updates / Logs

Overview obecnie powtarza status w ogromnej karcie i wielowierszowej tabeli, którą trzeba scrollować nawet przy900x620. Zredukować do jednego statusu, skrótów do dwóch głównych zadań i kilku faktów; detale techniczne do Device. Shortcuts obecnie ma9akcji w stopce — przenieść rzadkie import/export i dodatkowe operacje do More/context menu, zostawić główne dodawanie/edycję/test/zmianę pozycji. Rozdzielić codzienne konfigurowanie od parowania i diagnostyki grupami sidebar. Konsekwentna terminologia, sentence case, jasny empty/error state z następną czynnością. Unikać marketingowych dashboard cards i gradientów.

Sprawdzenie względem briefu: nie zmieniaj samego koloru istniejącej tabeli; pierwszeństwo mają skróty i podgląd rzeczywistego urządzenia. Skromny shell Windows pasuje do narzędzia sprzętowego. Zachowaj klawiaturę, DPI, contrast, dostępny scroll. Zrób zrzuty900x620 i1200x800, light/dark; krytykuj i popraw przed deklaracją sukcesu.

# Kolejne kroki

1. Sprawdź dopisany na końcu wynik uploadu i aktualny PID/path Agenta. Jeśli wdrożenie przerwano, najpierw przywróć firmware i uruchom aktualny Agent.
2. Odtwórz live problem z tym samym Spotify/YouTube. Hipotezy pokazane użytkownikowi: niewdrożone poprawki, utrata danych USB, błąd dekodowania ESP32. Nie potwierdzono jeszcze dominującej przyczyny poza używaniem starego buildu.
3. Sprawdzaj /api/status co kilka sekund: czy uptime/timestamp rosną i cover.rendered=true. Brakuje telemetry audio percent — dodaj wąską diagnostykę odczytanej i odebranej wartości, jeśli potrzeba rozdzielenia transportu od UI. Brak real playback nie jest dowodem awarii SMTC.
4. Jeśli cover wciąż nie renderuje, sprawdź count/offset/total, SD commit, wynik renderCachedCover i JPEG decoder. Zbuduj repro na real payload/cover; testy samych helperów mogą nie pokrywać pełnego łańcucha.
5. Przebuduj UI zgodnie z powyższym planem, zachowując działające funkcje. Główny plik Shell/SettingsWindow.cs (~3500wierszy), ShortcutEditorDialog.cs. Można wydzielić komponenty, nie zmieniaj frameworku bez potrzeby.
6. Build/test -> wdroż aktualny Agent i firmware -> live diagnostyka -> krótki raport. Nie powtarzaj zakończenia na samym isolated build.

# Komendy i środowisko

Cwd: korzeń stream-deck. exec_command wymagał sandbox_permissions=require_escalated (domyślny helper wcześniej zawodził). Używaj rg, unikaj recursive Get-ChildItem przez .tools (liczne access denied). Sekrety redaguj przed wyjściem do narzędzi. Nie drukuj surowych hello ani całej konfiguracji z tokenami.

Build Agent izolowany:

    dotnet build agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests/StreamDeck.WindowsAgent.Tests.csproj --no-restore -v quiet -p:OutDir=C:/Users/Syzmon/Documents/VAULT/projects/stream-deck/artifacts/state-sync-agent/
    dotnet artifacts/state-sync-agent/StreamDeck.WindowsAgent.Tests.dll
    dotnet artifacts/state-sync-agent/StreamDeck.WindowsAgent.Tests.dll --live-audio
    dotnet artifacts/state-sync-agent/StreamDeck.WindowsAgent.Tests.dll --live-media

Firmware build/upload:

    powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app -j 8
    powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app -t upload --upload-port COM5 -j 8

Nie używaj -t nobuild tutaj: po przerwaniu brakowało firmware.bin i target zawiódł. Skrypt tworzy subst, po przerwaniu zostały X:/Y: wskazujące ten projekt; sprawdź subst przed ewentualnym sprzątaniem. Artefakty .pio-app/cyd28_st7789_app/.

Uruchom Agent:

    Start-Process -FilePath <absolute-project-path>/artifacts/state-sync-agent/StreamDeck.WindowsAgent.exe -ArgumentList '--usb-port','COM5' -WindowStyle Hidden

SingleInstanceGuard: stare procesy trzeba zamknąć przed startem nowego, inaczej nowy tylko aktywuje stary. Po uruchomieniu potwierdź ExecutablePath i świeże logi. Dashboard nasłuchuje losowego portu127.0.0.1 (Get-NetTCPConnection -OwningProcess PID), GET /api/status. WS port8765.

Logi: %LOCALAPPDATA%/StreamDeckAgent/windows-agent.log. Stare logi zawierają również wpisy testowe usb-probe-device i synthetic hello — odróżniaj je od real CYD-40C86C. Zrzuty: artifacts/ui-review/*.png. Edycje Python zawsze encoding=utf-8, newline=\n.


# Stan końcowy handoffu — 2026-10-15 około 18:43

- Ponowny pełny upload cyd28_st7789_app przez COM5 zakończony SUCCESS; hash potwierdzony, restart RTS. Log artifacts/handoff-device-upload.log, czas268,25s. Pierwszy przerwany upload nie jest aktualnym stanem.
- Uruchomiono nowego Agenta PID28772 z artifacts/state-sync-agent/StreamDeck.WindowsAgent.exe, --usb-port COM5. Potwierdzono ExecutablePath. Poprzedni PID28688 nie działa.
- Lokalny dashboard aktualnego Agenta: http://127.0.0.1:59459/api/status (port może zmienić się po restarcie).
- Real CYD-40C86C connected/USB; firmware1.0.0, uptime37s, WiFi192.168.50.205, SDready, świeży deviceInfoUpdatedAtUtc18:42:53.97674+02:00.
- WAŻNE: cover.status=3, rendered=false po wdrożeniu. To nowy, konkretny sygnał niepowodzenia; artwork nadal wymaga diagnostyki transfer/SD/decode. Nie twierdź, że samo wdrożenie naprawiło problem. Nie potwierdzono fizycznego procentu głośności ani wizualnej oceny urządzenia.
- Redesign UI nie został wdrożony. Skill zainstalowany i zastosowany do planu w tym dokumencie. Następna tura powinna zacząć od odtworzenia status3 i śledzenia przyczyny, następnie wdrożyć uproszczony interfejs.

# Status realizacji — 2026-10-15 około 21:10 (ZAKOŃCZONE PEŁNYM SUKCESEM)

1. **Artwork (Rozwiązany):**
   - Zidentyfikowano i usunięto podwójną inkrementację `usbCoverWritten_ += decodedLength;` w `src/network/agent_client.cpp`, która uszkadzała nagłówki JPEG i powodowała `cover.status = 3` (Failed).
   - W `ProtocolSerializer.cs` dodano obsługę `audio` w ramce `device.info`, dzięki czemu handshake i synchronizacja nie są odrzucane.
   - W logach na żywo z fizycznego ESP32: `USB artwork transfer completed`, `cover.status = 2` (Available) oraz `rendered = true`.
2. **Audio & Mute (Rozwiązane):**
   - Wdrożono `audio.update` i `IntentType::ToggleMute`.
   - Na stronie Media CYD dodano dotykowy suwak głośności ze sprzężeniem do Windows Master Volume oraz przycisk wyciszania Mute.
   - Telemetria live: `"audio": { "volumePercent": 100, "muted": false }`.
3. **Przebudowa UI Windows Agent (Rozwiązane):**
   - Przeprojektowano `SettingsWindow.cs` zgodnie ze skillem `frontend-design`.
   - Pasek boczny podzielony na sekcję główną i serwisową `MAINTENANCE` (`Pairing`, `Updates`, `Logs`).
   - Strona `Overview`: na pierwszym planie rzeczywisty interaktywny podgląd siatki 3×2 fizycznego CYD z klikalnymi kafelkami przenoszącymi do edycji skrótu.
   - Nowe zrzuty UI wygenerowane w `artifacts/ui-review/overview-minimum.png` i `overview-wide.png`.
4. **Weryfikacja na sprzęcie:**
   - Firmware wgrany na moduł ESP32-2432S028R na porcie `COM5`.
   - Wszystkie testy C++ (`test-ui-logic.ps1`) oraz testy .NET (`StreamDeck.WindowsAgent.Tests.csproj`) zakończone kodem 0 (PASS).

# Status realizacji — 2026-10-15 około 22:15 (TOP NAV BAR I USB RECOVERY)

1. **Top Nav Bar zamiast gestów swipe:**
   - Zastąpiono zawodne na ekranie rezystancyjnym gesty swipe stałym paskiem 4 zakładek na górze (`Y=0..32`): `[ Media ] [ Skróty ] [ PC ] [ Info ]`.
   - Wymiary zakładek 74×28 px, symetryczne marginesy 6 px.
   - Aktywna zakładka wyróżniona kolorem akcentu i białym tekstem, z ochroną przed zbędnymi zapisami SPI (`!lv_color_eq`).
   - W `app.cpp` wyłączono rozpoznawanie gestów swipe w `readTouch()`, dzięki czemu przeciąganie poziomego suwaka głośności nie zmienia przypadkowo strony.
2. **Dopracowanie paska głośności i Mute:**
   - Zmniejszono Mute do 48×42 px.
   - Suwak wydłużony do 240 px z centralną etykietą `lvl%`.
3. **Stabilizacja ponownego łączenia USB (Handshake Recovery):**
   - W `agent_client.cpp` dodano obsługę natychmiastowego odsyłania ramki `hello` po nadejściu zapytania od Agenta oraz 15-sekundowy timeout braku aktywności USB resetujący stan autoryzacji przy odłączeniu PC.
   - W `UsbDeviceRuntime.cs` dodano wysyłanie ramki odpytującej `hello` podczas wykrywania portu.
4. **Dokument Handoff:**
   - Zapisano pełny handoff w `%TEMP%\STREAMDECK_HANDOFF.md` (zgodnie z wytycznymi).



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
