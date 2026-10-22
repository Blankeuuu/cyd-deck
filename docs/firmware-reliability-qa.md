---
title: CYD-DECK — naprawy niezawodności i plan akceptacji fizycznej
tags: [cyd-deck, firmware, reliability, qa, recovery, shortcuts]
aliases: [CYD end-to-end recovery, Firmware reliability QA]
updated: 2026-10-22
status: active
---

# Zakres i status

## Opóźnienie Spotify — pomiar 2026-10-22

### Uzupełnienie: potwierdzony Spotifast i RED

Użytkownik potwierdził Spotifast, a rzeczywiste źródło GSMTC ma ID
`spotifast.exe` (zainstalowana wersja 0.13.3). Wcześniejszy filtr diagnostyczny
`Contains("spotify")` błędnie pomijał ten odtwarzacz; wynik no_spotify_session
nie oznaczał braku aktywnej muzyki. Hipoteza Brave nie potwierdziła się.
Harness używa teraz produkcyjnego `DefaultMediaSessionSelector`.

Bez restartu Agenta: pierwsza rzeczywista komenda Next → nowy snapshot
10809,2 ms (FAIL); Agent wysłał zmianę około 14 ms po zdarzeniu. Druga próba
z odczytem RAW co 200 ms: docelowy hash metadanych był gotowy po 617,4 ms,
`monitor.snapshot` po 10045,0 ms, `Media sent` po 10056,6 ms. Dane były
dostępne przed opóźnionym eventem Spotifast. Plik dowodowy:
`artifacts/spotifast-latency-evidence-2026-10-22.json`.

Naprawa w toku: bounded fallback odczytu metadanych co 500 ms, również przy
pauzie, z regresją braku eventu i ochroną przed kolejką/churnem artwork.
Nie deklaruje się jeszcze wdrożonej poprawki ani wyniku GREEN.

### Wcześniejszy pomiar i jego ograniczenia

Użytkownik zgłosił około 10 s od zmiany utworu w Spotify do zmiany na CYD.
Pomiar `artifacts/measure-cover-usb-latency.py` wykonał prawdziwe ramki na COM5
bez resetu ESP32: pięć odpowiedzi `device.info` 47–63 ms, pięć różnych
`media.update` ze znanymi okładkami SD do `cache_hit` 265–297 ms. Próg 2000 ms,
10/10 prób PASS. Wynik: `artifacts/cover-usb-latency-2026-10-22.json`.
To nie jest pomiar Spotify→TFT ani test cache miss/transferu nowego obrazu.
Zatrzymanie Agenta do zwolnienia COM5 mogło usunąć wcześniejszy backlog Agenta;
wynik nie wyklucza narastającego opóźnienia przed takim restartem.

Uruchomiono także standalone `artifacts/latency-native-probe/`, używający
produkcyjnego źródła GSMTC/MediaSessionMonitor, UTC/monotonicznych znaczników
i hashy metadanych bez tytułów. Read-only baseline działał, ale nie uchwycił
zmiany utworu. Próba `--seconds 25 --skip-next` została zatrzymana przez guard
`BLOCKED reason=no_spotify_session count=0`; nie wysłano komendy do odtwarzacza.
Nie odtworzono 10 s i nie przypisano przyczyny konkretnemu modułowi.
Użytkownik nie mógł w tym czasie wykonać kontrolowanych zmian ręcznie.

Kod produkcyjny oraz firmware pozostały bez zmian w tym etapie; nie flashowano
ani nie resetowano ESP32. Tymczasową diagnostykę procesową Agenta wyłączono,
normalny Agent przywrócono (PID36644, Responding=true w chwili kontroli).
Hash konfiguracji nadal
`0DEBE79EF38379EA12A8E0064CCD319B800855FFB06BA7F5FB9E01E8025FAAAA`.
Następny pomiar musi uchwycić aktywną sesję Spotify: source event→snapshot→
`usb_tx media.update`→decode/render. Nie deklaruje się naprawy opóźnienia.

## Trwałe No artwork i fragmentacja — 2026-10-22

Użytkownik zgłosił utratę wszystkich okładek po kolejnych zmianach, także tych
wcześniej zapisanych na SD oraz po zmianie aplikacji muzycznej. Log Agenta nadal
pokazywał `artwork=True` i wielokrotne transfery tych samych ID. Odczyt fizycznego
CYD bez resetowania (DTR/RTS false), uptime 670 s: SD mounted/ready, cover Failed,
rendered=false, heap free 124760 B, minimum 75848 B, największy blok 32756 B.
Pojedynczy obraz 128×128 RGB565 wymaga 32768 B. Restart samego Agenta nie pomógł;
urządzenie nadal raportowało Failed, a stats/audio działały. Pomiar silnie wskazuje
brak ciągłego bloku na kolejną okładkę, ale nie ustala źródła całej fragmentacji.
Nie zarejestrowano bezpośredniego śladu nieudanej alokacji wewnątrz dekodera.

Kandydat zastępuje powtarzany `calloc/free` dwoma trwale rezerwowanymi slotami w
`ui/cover_pixel_buffers.h`, używanymi w `renderCachedCover`. Worker dostaje ticket
z ID i generacją, dekoduje do slotu roboczego, a UI publikuje tylko aktualny,
poprawny wynik. Widoczny slot nie jest nadpisywany; błąd/stary wynik zwalnia slot
roboczy do ponownego użycia. Oba obrazy zajmują 65536 B na stercie, rezerwowane
jednorazowo przed startem AgentClient; zmiany okładek nie wykonują kolejnych
alokacji. Pierwsza rezerwacja może się nie udać i jest ponawiana; błąd drugiego
bufora zwalnia pierwszy. Wstępny wariant statyczny zastąpiono tym rozwiązaniem
po sprawdzeniu banku `.dram0_0_seg` (124580 B) w linkerze frameworka ESP32.
Mały obiekt poola ma 176 B na hoście x64; rozmiar i budżet firmware potwierdza
build, a heap po starcie wymaga testu urządzenia.
File/sprite opuszczają zakres przed `vTaskDelete`. Stack taska i dekoder JPEG nadal
mogą wymagać innych alokacji; nie deklaruje się usunięcia każdej przyczyny braku RAM.

Koordynator: `scripts/test-cover-pixel-buffers.ps1` PASS (160 zmian, decode failure,
stale result, zachowanie widocznego obrazu i ponowne `cached-A`, błędy pierwszej
i drugiej rezerwacji z rollbackiem oraz retry),
`scripts/test-ui-logic.ps1` PASS. Porównanie starej strategii to kontrolowany model
odmowy dużej alokacji w pierwszym harnessie nie było uruchomieniem starego
JPEG/FreeRTOS na sprzęcie. Końcowy test nowej strategii używa produkcyjnego
modułu i wstrzykniętego allocatora: dokładnie 2 rzeczywiste wywołania przed
160 zmianami i żadnych kolejnych mimo ustawienia odmowy alokacji. Bez SD/TFT.
Pomiar: `artifacts/cover-fragmentation-observation-2026-10-22.json`; raport:
`artifacts/cover-persistent-failure-2026-10-22.md`.
Końcowy `platformio run -e cyd28_st7789_app -j 1` PASS (91,23 s): RAM statyczny
85936 B / 327680 B, flash 1717621 B / 2031616 B. Symbol poola w ELF ma 160 B
na ESP32 (`nm`, rozmiar 0xa0). Pierwszy, zastąpiony wariant statyczny faktycznie
przekroczył region DRAM o 26880 B. Obraz końcowy:
`artifacts/cover-pool-ST7789-2026-10-22.bin`, 1724192 B,
SHA256 `513272AC18892C635B230A77973F5C2B30E16CE7442EA02851D71D744483E5AF`.
Wgrano wyłącznie aplikację na COM5/0x10000: esptool `Hash of data verified`,
restart po flashu. SD/NVS i konfiguracji nie czyszczono. Pierwszy hello po starcie:
SD mounted/ready, wolny heap 94436 B, największy blok 47092 B. Po połączeniu
Agenta, przy uptime 143 s: cover status=2/rendered=true, freeHeap=92212 B,
minHeap=72392 B, largestBlock=45044 B. Zapis bezpiecznych pól:
`artifacts/cover-pool-postflash-2026-10-22.json`. To potwierdza pojedynczy render,
nie wielogodzinną stabilność ani serię różnych utworów.
Pierwsza dodatkowa sonda z role=agent nie otrzymała odpowiedzi (protokół
celowo nie odpowiada na taką rolę, aby zapobiec pętli hello); poprawne discovery
z pustą rolą dało powyższy wynik bez resetu. Zwykły Agent ponownie działał jako
PID37368, Responding=true; konfiguracja zachowała SHA256
`0DEBE79EF38379EA12A8E0064CCD319B800855FFB06BA7F5FB9E01E8025FAAAA`.

Play/Pause: użytkownik doprecyzował ikonę `Ⅱ` podczas grania. To prawidłowa
następna akcja Pause, nie napis o stanie Paused. Zakończono diagnozę bez zmian
produkcyjnych i wycofano tymczasowe modyfikacje harnessu Media. Zwykły Agent
przywrócono po diagnostyce (PID 34124 w chwili kontroli); hash config.json pozostał
identyczny jak przed sesją. Pomiary diagnozy wykonano przed wgraniem nowego obrazu.

## Okładki i cache SD — kontynuacja 2026-10-21

Przejęto ORCA Run `run_74e90ec96345`. Na polecenie użytkownika dalsze zadania
wykonują workery Codex `gpt-6-luna`, effort `low`; wcześniejszy wymóg Antigravity
dotyczył poprzedniego etapu. Przejęte poprawki Agenta i firmware zweryfikowano
ponownie, bez zmniejszania cache SD.

- Agent zachowuje `coverId` dla tego samego utworu przy nowej referencji lub
  chwilowym braku miniatury. LRU 32 identyfikatorów przyspiesza powrót do utworu.
- Render SD nie przelicza całego SHA-256 z opóźnieniem na każdy blok 512 B.
  Strumień nadal weryfikuje hash przed commitem; render sprawdza rozmiar,
  nagłówek JPEG i powodzenie dekodowania. Poprawny JPEG z innymi bajtami nie
  jest już odrzucany wyłącznie na podstawie niezgodności hasha przy renderze.
- Pełny indeks 512 wpisów usuwa teraz pojedynczy najmniej używany plik zamiast
  odrzucać nowe okładki. Test produkcyjnego `SdStorage` z kontrolowanym FS
  potwierdza 513. zapis, ochronę dotkniętego wpisu, trafienie istniejącego ID
  i zachowanie danych przy błędzie usunięcia. Stub SHA w tym harnessie nie
  testuje kryptografii ani streamingu.
- Porównanie tego samego utworu w firmware uwzględnia title, artist i album
  po normalizacji do buforów, także dla długich nazw. Regresje wykonują
  produkcyjne przejścia stanów. Raporty: `artifacts/cover-sd-capacity-fix.md`
  i `artifacts/cover-track-boundary-review.md`.

Niezależna weryfikacja: regresje artwork 6/6, runtime media 3/3, Covers 15/15
oraz `scripts/test-ui-logic.ps1` PASS; Agent Release 0 ostrzeżeń i 0 błędów.
Pełny `WindowsAgent.Tests` poza sandboxem zatrzymał się na budżecie wydajności
`DesktopViewportTests`: warm `ShowPage(Shortcuts)` p50 103,92 ms przy wymaganiu
poniżej 50 ms. Nie jest to pełny PASS; nie ustalono przyczyny tego przekroczenia
w ramach naprawy okładek. Testy kontrolowane nie mierzą czasu wyświetlenia TFT.

Powtórzenie pełnego smoke po zakończeniu kompilacji: **PASS**, exit 0,
`PASS StreamDeck.WindowsAgent smoke tests`; hash rzeczywistego config.json
przed/po identyczny. Log: `artifacts/cover-smoke-final.log`. Pierwsze przekroczenie
budżetu pozostaje wynikiem wcześniejszego przebiegu, a nie końcowym statusem suite.

Końcowy `scripts/test-sd-storage-cache.ps1` PASS po poprawce wykrywania Ziga;
`scripts/test-ui-logic.ps1` PASS po wszystkich zmianach logiki. Build ST7789
SUCCESS (788,58 s): RAM 85712/327680 B, flash 1716533/2031616 B.
Obraz `artifacts/cover-cache-ST7789-2026-10-21.bin`: 1723104 B, SHA-256
`5A9573366DDFB672BC5A8A4CCB3C7B67A075F2B723C774D5B34B5DF999BA027E`.

Nowy Agent uruchomiony z `artifacts/reliability-cover-agent/CYD-DECK.exe`
z `--usb-port COM5`; PID 30244 odpowiadał w chwili kontroli. Hash konfiguracji
przed i po zmianie instancji był identyczny. Nie zastąpiono instalacji ani
autostartu. Pierwsza próba flashowania została zablokowana przez automatyczny
przegląd bezpieczeństwa; użytkownik następnie jawnie zatwierdził wdrożenie.

**Wdrożenie 2026-10-21:** zapisano zatwierdzony obraz 1723104 B wyłącznie pod
`0x10000` na COM5. Esptool 4.9.0 potwierdził `Hash of data verified`, exit 0,
i wykonał restart RTS. Nie kasowano NVS ani nie formatowano SD. Odczyt hello
bez dodatkowego resetu: CYD-40C86C, firmwareVersion 1.4.0, SD mounted=true,
ready=true. Heap przy hello: free 161524 B, minimum 150968 B, largest 110580 B.
Agent ponownie uruchomiony, PID 5172 Responding true; hash config.json nadal
`0DEBE79EF38379EA12A8E0064CCD319B800855FFB06BA7F5FB9E01E8025FAAAA`.
Odczyt hello przed uruchomieniem Agenta miał cover status=0/rendered=false,
co nie jest wynikiem testu wyświetlenia okładki po połączeniu.
Fizyczna ocena braku migotania, cache hit i czasu wyświetlenia TFT pozostaje
do potwierdzenia przez użytkownika. Bez merge, push i release.

## Regresja fizyczna i dalsza naprawa — 2026-10-21

Po pierwszym wdrożeniu użytkownik zgłosił `--:--`, brak metryk PC oraz
`Disconnected`/`ACK timeout` przy Previous i Play/Pause. Instrumentacja fizycznego
COM5 potwierdziła wspólną przyczynę: `shortcuts.report` zawierał identyfikatory
pustych Deck Tiles `__empty_slot_…`, niezgodne z walidatorem stabilnych ID.
Agent wysyłał `connection.state=degraded`, firmware tracił autoryzację i ponawiał
hello/hydration co sekundę. Przez to sporadycznie docierały `system.stats`
(największa zaobserwowana luka ok. 52 s), którymi przesyłany jest też zegar.
Jeden fizyczny `media-17` został wykonany przez Windows z sukcesem po 1161 ms,
ale CYD nie raportował odbioru ACK. Nie wolno interpretować tego jako awarii touch.

Poprawka firmware zachowuje pozycje pustych kafelków, usuwa ich wykonywalne ID
przy synchronizacji oraz pomija historyczne placeholdery z NVS w raporcie
i wykonaniu. Regresja `sparseProfile.ids[0][0] == '\0'` była RED przed poprawką,
po poprawce `scripts/test-ui-logic.ps1` PASS. Nie zmieniono zapisanej konfiguracji
Windows ani nie kasowano danych NVS.

Nadzór: ORCA Run `run_74e90ec96345`, workery Antigravity. Poprawki pomocnicze:
EOF kończy sesję USB zamiast pozornego idle; metadane/miniatura nie blokują
natywnego wyniku komendy mediów. Media.Tests 25/25 i Media.CommandTests 9/9 PASS,
w tym zablokowana asynchroniczna oraz synchroniczna część mappera.
Poprawiony firmware wgrano na COM5 (1722192 bajty, hash zapisu PASS), SHA-256:
`40B4C7F30DC17DEA29FC26AFDAD1BC9C0B708CB54E23A2D9DAF886AD617DD90A`.
Z tym samym instrumentowanym Agentem potwierdzono 44 kolejne próbki stats,
maksymalną przerwę 1,113 s oraz zero błędów ramek i przejść do degraded.
Użytkownik potwierdził fizycznie: czas, PC oraz media działają poprawnie.

W próbie 28 fizycznych komend po tej poprawce wszystkie miały odebrany ACK.
Dotyk→wysłanie: p50 2 ms, p95 3 ms, max 4 ms. Dotyk→ACK: p50 93 ms,
p95 1128 ms, max 1348 ms. Percentyle metodą nearest-rank, próbka mała;
wartości pochodzą z jednego zegara deviceMs, bez odejmowania zegara Windows.
Próbę wykonano jeszcze PRZED uruchomieniem poprawki MediaSessionMonitor,
dlatego nie dowodzi ona opóźnień po rozdzieleniu native result od metadanych.
Zanonimizowane próbki: `artifacts/physical-media-latency-2026-10-21.json`.

### Lokalny Debug mode

Na prośbę użytkownika górny pasek wyników komend (`Done`, błędy i timeouty)
jest domyślnie ukryty. **Settings → Device → Debug: ON/OFF** steruje wyłącznie
prezentacją diagnostyczną; ACK, wynik wykonania i logowanie błędów pozostają.
Stan zapisuje się jako bool `appearance/debug` w NVS, z kompatybilnym default OFF.
W trybie ON ustawienia pokazują dodatkowo transport, wolny heap i ostatni wynik.
Nie jest to sieciowe `diagnostics.configure`, nie dodaje pól do `device.info`.

Weryfikacja manualna po wgraniu: w OFF naciśnięcia playera nie pokazują paska;
w ON pasek pokazuje wynik/błąd, po wyłączeniu znika natychmiast. Sprawdzić oba
motywy i zachowanie ON/OFF po następnym zwykłym uruchomieniu urządzenia.
Nie wykonywać resetu fabrycznego w celu testowania zapisu ustawienia.

Debug mode wgrano na COM5: 1722944 bajty, esptool hash PASS i restart RTS,
SHA-256 `4CD2C5212DBAC5367FB97791CF5FE441288A7500EE8EA36BCCFF5C767D5E1FF0`.
Końcowy build: RAM 85712/327680 B, flash 1716373/2031616 B.
`test-ui-logic.ps1` i realne rendery LVGL dark/light PASS. Review obrazu wykrył
nieczytelny toast w jasnym motywie; dodatkowa asercja była RED, po dobraniu
kolorów panelu/tekstu do motywu jest GREEN. Finalne podglądy znajdują się
w `artifacts/reliability-preview-final`, dokładny obraz w `artifacts/reliability-build`.
Użytkownik potwierdził działanie Debug mode na fizycznym CYD: „Tak debug dziala”.
Nie jest to potwierdzenie pełnej macierzy awarii ani trwałości ustawienia po restarcie.

## Pierwszy kandydat

Gałąź: `fix/firmware-e2e-reliability`, baza `477c9a0`. Zmiany nie są scalone do
chronionego main ani wydane. Na polecenie użytkownika 2026-10-21 wgrano firmware
ST7789 przez COM5: 1722048 bajtów, weryfikacja zapisu PASS, restart RTS.
SHA-256 obrazu: `B407B225A144DCB51490BF4F9624D39F6B14E7D139308639C516119FAC6E8EC2`.
Uruchomiono najnowszego Agenta z katalogu build Release (0 błędów i ostrzeżeń),
bez nadpisania zainstalowanej aplikacji. Odebrano hello CYD-40C86C i SD ready;
Agent zakończył transfer okładki, ale nie potwierdzono jeszcze jej widoczności na TFT.
Nie kasowano NVS ani nie formatowano SD. Końcowy build firmware i testy LVGL PASS.

Nie ma jeszcze podstaw do stwierdzenia pełnej naprawy urządzenia end-to-end.
Testy automatyczne i odczyty Windows opisane niżej nie zastępują fizycznego testu
dotyku, karty SD, TFT, reconnect i wielokrotnych zmian utworów.

## Diagnozy, rozwiązania i granice pewności

| Problem | Ustalona przyczyna / pewność | Zmiana | Weryfikacja i ryzyko |
|---|---|---|---|
| Artwork po błędzie | Potwierdzony testem: Failed + ten sam coverId pozostawał Unchanged; Agent odrzucał ponowne wysłanie ID po własnym complete | `cover_art.h`: 3 ograniczone retry 1/2/4 s; `agent_client.cpp`: autonomiczne próby; `UsbDeviceRuntime.cs`: jawne żądanie może ponowić transmisję | Test najpierw FAIL, potem PASS; próby i rollover zegara sprawdzone. Nie potwierdzono wielogodzinnego odtwarzania na TFT |
| Artwork po pierwszym połączeniu | Potwierdzona inspekcją luka: hydration wysyłała media przez IProtocolSink, pomijając aktualizację currentCoverId wymaganą do przyjęcia requestu | `UsbDeviceRuntime.cs`: aktualizacja ID także w SendTextAsync | Regresje runtime; sprzęt pending |
| Przycisk podczas artwork | Potwierdzona struktura await: odbiornik USB czekał na obsługę cover.request i cały transfer | `UsbDeviceRuntime.cs`: osobne zadanie z anulowaniem i deduplikacją w trakcie transferu | Kontrolowany test zatrzymuje wysyłkę chunku i potwierdza przejście ACK. To nie pomiar touch→GSMTC |
| Wi-Fi ACK + telemetria | Deterministyczny test odtworzył równoczesne SendAsync na tym samym WebSocket | `WebSocketProtocolSink.cs`: blokada całej ramki | Test współbieżności FAIL→PASS; fizyczna ścieżka Wi-Fi pending |
| Tap głośności / cofanie | Potwierdzony brak modelu pending: raport Windows i lokalne żądanie współdzieliły reportedVolume; ochrona kontrolki kończyła się po release | `app_state.h`, `app.cpp`, `ui_manager.cpp`, AudioSnapshot/service, Messages, mapper i dispatcher: pending, observed, requestId i revision | Prawdziwe LVGL: tap bez drag 20/80/10/100%, oba motywy, stare update. CoreAudio odczytano; nie wykonano fizycznego tap→CoreAudio |
| Previous wymaga wielu naciśnięć | Blokowanie odbiornika przez artwork jest potwierdzone. Nie udowodniono, że to jedyna przyczyna zgłoszenia | ACK tracker, znaczniki intencji, odblokowanie odbiornika; brak automatycznego podwójnego Previous | Media i CommandTests PASS. Semantyka „restart bieżącego utworu” nadal zależy od odtwarzacza; kalibracja/hitbox fizycznie pending |
| PC nie pokazuje metryk | Potwierdzone syntetyczne GPU/TEMP. Potencjalne przeterminowanie za artwork i zależność publikacji od tekstowego HostStatus; nie odtworzono pełnego błędu fizycznej zakładki | Mapper usuwa fallbacki; sampler uwzględnia gotowy USB; parser + dashboard CPU/RAM/GPU/TEMP | Mapper regression FAIL→PASS; parser/null/64-bit/stale PASS, render obejrzany. Rzeczywisty odbiór stats na nowym firmware pending |
| Shortcut skrypt | Rzeczywisty test Windows: runner nie czekał na zakończenie; PowerShell exit 7 dawał pozorny sukces | `WindowsProcessShortcutRunners.cs`: jawny cmd dla bat/cmd, argumenty w cudzysłowach, await exit, błąd kodu niezerowego | Testy bat/cmd Unicode + spacje i PS failure FAIL→PASS; dodano rzeczywisty PS success z argumentami i brak pliku. Ograniczenia batch opisane niżej |
| Wynik Shortcut/media | Firmware ignorował ACK | `pending_commands.h`, `agent_client.cpp`, dispatcher i adapter: korelacja, timeout, kody błędów i feedback | Host tests obejmują korelowane audio, busy i media podczas długiego Shortcut. Nie ponawia się automatycznie akcji |
| Wi-Fi artwork / SD read | Potwierdzony synchroniczny HTTP i JPEG/SD read na wątku LVGL | `agent_client.cpp`: worker sieciowy, kolejka 4×512 B; `app.cpp`: pojedynczy worker SD read/SHA/decode, osobny bufor i podmiana wyłącznie w UI | Build i testy stanów; rzeczywiste zadania FreeRTOS wymagają testu ESP32. Append/commit pozostają synchroniczne |
| Integralność/cache | Same znaczniki JPEG i długość nie potwierdzały zgodności coverId | `UsbCoverSender.cs`: hash i limit; `sd_storage.*`: streaming SHA-256, walidacja, rename z backupem/rollbackiem | Build; awarie SD i przerwanie zasilania nieprzetestowane. Nie formatuje się SD ani nie usuwa całego cache |
| „Rendered” | Wcześniej oznaczało dekodowanie/przypisanie, nie ramkę TFT | `lvgl_port.*`, `app.cpp`, `agent_client.cpp`: zakończenie ramki przy widocznym Home/Media; opcjonalny coverId w raporcie | Kompilacja; transmisja do sterownika nie dowodzi poprawnego obrazu na fizycznym panelu |

## Weryfikacja automatyczna

Wykonane komendy (ich nazwy sprawdzono w repo):

```powershell
dotnet build agent/src/WindowsAgent/StreamDeck.WindowsAgent/StreamDeck.WindowsAgent.csproj -c Release
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app
powershell -ExecutionPolicy Bypass -File scripts/test-ui-logic.ps1
powershell -ExecutionPolicy Bypass -File scripts/render-ui-preview.ps1 -OutputDirectory artifacts/reliability-preview
dotnet run --project agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests/StreamDeck.WindowsAgent.Tests.csproj
dotnet test agent/src/Host/StreamDeck.Host.Tests/StreamDeck.Host.Tests.csproj
dotnet test agent/src/Shortcuts/StreamDeck.Shortcuts.Tests/StreamDeck.Shortcuts.Tests.csproj
dotnet test agent/src/Media/StreamDeck.Media.Tests/StreamDeck.Media.Tests.csproj
dotnet test agent/src/Covers.Tests/StreamDeck.Covers.Tests/StreamDeck.Covers.Tests.csproj
dotnet test agent/src/Networking/StreamDeck.Protocol.Tests/StreamDeck.Protocol.Tests.csproj
dotnet run --project agent/src/Media/StreamDeck.Media.CommandTests/StreamDeck.Media.CommandTests.csproj
dotnet run --project agent/src/Audio/StreamDeck.Audio.Tests/StreamDeck.Audio.Tests.csproj
dotnet run --project agent/tests/SystemMonitoring.Tests/SystemMonitoring.Tests.csproj
```

Host: 42/42 po dodaniu testu serializacji WebSocket; Shortcuts: 43/43,
Media: 22/22, Covers: 15/15, Protocol: 81/81 PASS.
Configuration: 8 PASS; Deck: 39 asercji PASS; State: 30/30 PASS.
Media CommandTests: 8 PASS, Audio: 6 PASS, SystemMonitoring: 5 PASS.
Firmware logic/layout/font tests: exit 0. Rzeczywisty LVGL: cztery tapy i ochrona
przed starszym audio w dark/light PASS. Windows Agent smoke tests: PASS.
Render obejrzany: `artifacts/reliability-preview/dark-pc-live.png` (wartości są
fixture, nie wynik pomiaru Windows). Preview generowane lokalnie i ignorowane Git.

Istniejący test menu zatrzymał się na modalnym błędzie File.Replace w pliku
fixture. Nie ustalono, który proces blokował plik. `DesktopBugRegressionTests.cs`
ma teraz unikalny katalog tymczasowy, przekazuje ten sam izolowany store do
runtime, zamienia dialogi na jawne błędy i pompuje zdarzenia STA podczas zapisu
z terminem 10 s. Ograniczony `--bug-repro` przeszedł po tej zmianie; nie zmieniano
produkcyjnego UI ani nie uznano zawieszonego przebiegu za PASS.

Przebieg smoke podczas pełnej kompilacji C++ i renderowania nie spełnił istniejącego
budżetu desktopowego: Shortcuts warm p50=77,75 ms przy wymaganiu <50 ms. Nie
podwyższono progu; pełny smoke musi zostać powtórzony po zakończeniu ciężkich buildów.
Wcześniejszy przebieg (przed końcowymi poprawkami) miał smoke PASS i warm p50
Shortcuts 36,65 ms. Te pomiary nie dotyczą latencji CYD.

## Pomiar i kryteria opóźnień

Kontrolowany test Windows, pierwszy przebieg: **3,71 ms** od zakolejkowania
transferu do przejścia kontrolnego ACK podczas wstrzymanego chunku. Jest to jeden
wynik symulacji, bez przewodu USB, LVGL i GSMTC; nie stanowi p50/p95 urządzenia.

Rzeczywiste odczyty bez zmiany stanu: CoreAudio 28%, mute=false; jedna sesja GSMTC
z dostępną miniaturą; osiem próbek GPU 0,1–3,1%, temperatura niedostępna.
Nie zapisuje się tytułu utworu ani argumentów prywatnych skrótów.

Wyliczalne ograniczenie transportu: przy 115200 baud i 8N1 każdy bajt zajmuje
około 0,0868 ms na przewodzie. Payload 512 B daje 684 znaki Base64, plus JSON/CRLF.
Cała rozpoczęta ramka musi dokończyć zapis; nie należy obiecywać ACK szybszego niż
pozostały czas tej ramki i wykonanie odtwarzacza.

Proponowane **cele akceptacji**, a nie zmierzone wyniki sprzętu:

- touch→intent→command_sent: p95 ≤ 50 ms; każda próbka >100 ms wymaga analizy
  loopMaxUs/networkMaxUs/renderMaxUs oraz czasu SD;
- wysłanie→ACK: raportować p50/p95/max dla każdego typu komendy, co najmniej
  100 próbek; podczas artwork wzrost p95 względem idle nie większy niż czas
  jednej zmierzonej ramki artwork + 50 ms pracy pętli;
- execution_started→completed GSMTC mierzyć osobno; zmiana metadanych nie jest
  warunkiem sukcesu Previous, bo odtwarzacz może wrócić do początku utworu;
- po correlated audio.update CYD i obserwowane CoreAudio powinny różnić się
  najwyżej o 1 punkt procentowy wynikający z kwantyzacji;
- 100 zmian utworów: każda dostępna okładka kończy stanem potwierdzonej ramki
  albo jawnym błędem po ograniczonym budżecie, bez restartu i nieskończonych retry;
- stats: brak stanu Stale podczas poprawnego regularnego dostarczania pakietów;
  po braku pakietu próg pozostaje 5 s. Nie wydłużono go w celu ukrycia awarii.

Po pomiarze fizycznym te cele trzeba ocenić względem rzeczywistego panelu, karty,
sesji i rozmiaru ramek; obecnie brak danych do potwierdzenia ich osiągnięcia.

## Checklista sprzętowa — wszystkie pozycje PENDING

Najpierw potrzebna jest jawna zgoda na wgranie konkretnego firmware i uruchomienie
testowej wersji Agenta. Nie otwierano COM5 obok działającego Agenta, nie resetowano
ESP32 ani nie zmieniano aktywnej konfiguracji użytkownika.

| Próba | Co zapisać / oczekiwanie |
|---|---|
| Nowa okładka, cache hit i miss | coverId oraz requested/cache_hit/cache_miss, saved, decode_success i rendered |
| Uszkodzony JPEG | Jawny decode_failed, ograniczone retry; wstrzyknięcie tylko do odrębnego testowego cache |
| Niepełny USB, brak complete | Brak commitu; timeout, backoff, odzyskanie tego samego coverId |
| Zmiana utworu podczas transferu i powrót | Stary transfer anulowany/odrzucony; poprawna bieżąca okładka |
| Ten sam album wiele razy i nowa miniatura tego samego utworu | Brak zbędnego transferu dla niezmiennego ID; nowe bajty dają nowe ID |
| Home ↔ Media podczas transferu | Wspólny descriptor, prawidłowy obraz na obu stronach, brak ponownego pobierania przez nawigację |
| Błąd SD | Tylko odrębna testowa karta/fixture, żadnego formatowania; brak fałszywego rendered |
| USB disconnect/reconnect, także podczas zmiany utworu | Automatyczne wznowienie bez resetu i bez ręcznego przełączania utworu |
| Tap 20/80%, drag 100→10 i 10→100, seria tapów | Pomiar lokalnego żądania, ACK i CoreAudio; bez cofania gałki |
| Zewnętrzna klawiatura Windows, mute/unmute | Aktualizacja CYD i rzeczywisty odczyt audio |
| Opóźnione audio.update / przeciążenie USB | Odrzucenie starych rewizji, zachowanie ostatniego żądania i wysłanie finalnej wartości |
| PlayPause/Next/Previous | touch→send→execution→ACK; odróżnić restart utworu od przejścia do poprzedniego |
| PC Live/Stale/Unavailable/Disconnected | Porównanie z tym samym snapshotem Windows; brak wymyślonej temperatury |
| Stress | Artwork + volume + media + PC + bezpieczny Shortcut; brak zgubionych poleceń, pomiar kolejek i p95 |
| Wi-Fi | Powtórzyć media/audio/artwork/stats/Shortcut oraz zerwanie połączenia i HTTP error |
| Długa sesja / zmiana aplikacji | Min. 100 zmian i powrót do poprzednich okładek, obserwacja heap i kolejki HTTP |

## Macierz Shortcut — zakres dowodów

| Typ | Automatyczne dowody | Kontrola manualna pozostająca |
|---|---|---|
| EXE | Walidacja pełnej/cytowanej/bare ścieżki, mapowanie argumentów i dispatch przez katalog | Rzeczywisty program ze spacjami/Unicode/argumentami, już uruchomiony, niedostępny EXE |
| URL | Walidacja HTTP(S), mapowanie i executor przez fake adapter | Domyślna przeglądarka, parametry URL i brak skojarzenia; nie otwierano jej automatycznie |
| Hotkey | Katalog, walidacja klawiszy, mapowanie i istniejące testy Agenta | Ctrl+C, Ctrl+Shift+S, Alt+Tab, media, fokus, odrzucone kombinacje; Win+L tylko po osobnej akceptacji |
| bat/cmd | Rzeczywiste wykonanie na Windows: Unicode w ścieżce i argument ze spacjami, plik dowodowy | Interakcja z CYD/transportem i błędy uprawnień |
| PowerShell | Rzeczywisty exit 7 jest błędem; rzeczywisty test sukcesu z argumentem ze spacjami i ścieżką Unicode; brak pliku odrzucany przed startem | Prywatny skrypt użytkownika i jego polityka wykonania |
| System adapter | Istniejący toggle microphone i walidacja/dispatch | Rzeczywisty mute mikrofonu, brak endpointu i uprawnień; nie zmieniano mikrofonu użytkownika |
| Konfiguracja | Deck/State/Configuration tests: draft, zapis, assigned IDs i synchronizacja | Porównanie prywatnego zapisanego profilu z fizycznymi kafelkami |

Nie uznaje się całej macierzy za potwierdzoną wykonawczo. Błędy skryptów mają kod
wyjścia, a ACK akcji aplikacji/URL/hotkey potwierdza adapter Windows, nie efekt w
docelowej aplikacji. Batch odrzuca w ścieżce/argumentach znaki `"`, `%`, CR/LF/NUL,
aby nie wykonywać niejawnej ekspansji cmd; zwraca invalidConfiguration. `!` nie
jest rozwijane (`/v:off`). Skrypty nie dostają obejścia ExecutionPolicy.

## Pozostałe ograniczenia

- Nie wykonano fizycznego flashowania, więc nie potwierdzono naprawy hardware.
- SD read/SHA/decode i sieć przeniesiono do workerów. LVGL i podmiana widocznego
  bufora pozostają w wątku aplikacji. Szczytowo dekoder wymaga dwóch buforów 32 KiB
  i stosu 6 KiB; nie potwierdzono jeszcze marginesu heap na urządzeniu użytkownika.
  Commit i pojedynczy append pozostają synchroniczne: trzeba zmierzyć p95/max na
  karcie użytkownika i w razie przekroczeń rozszerzyć właściciela zapisu SD.
- Wi-Fi discovery i WebSockets korzystają z dotychczasowych bibliotek; nie
  wykazano ich maksymalnej latencji na fizycznym ESP32.
- SD checksum potwierdza odebrane bajty strumienia przed commitem. W kandydacie
  okładek z 2026-10-21 worker nie hashuje ponownie pliku przy każdym cache hit;
  sprawdza rozmiar, nagłówek JPEG i wynik dekodowania.
  Prawdziwe błędy zapisu/rename i utrata zasilania nie były wstrzykiwane.
- Starszy Agent bez revision ma słabszą ochronę kolejności audio.
- Długi Shortcut może skończyć się po 5-sekundowym timeoutcie CYD. Wynik jest
  niepewny; nie wolno automatycznie go powtarzać. Sesja odpowiada busy na kolejny
  Shortcut do czasu zakończenia pierwszego.
- Nie przeprowadzono pełnego audytu wykonawczego prywatnej biblioteki użytkownika;
  nie należy utożsamiać testów katalogu z potwierdzeniem wszystkich jego akcji.

Zmiany są kandydatem do przeglądu i testu sprzętowego, a nie deklaracją ukończenia
wszystkich kryteriów niezawodności zlecenia.
