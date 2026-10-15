---
title: CYD-DECK — lagi USB, pętla hello i ikona Mute
tags: [cyd-deck, usb, performance, mdns, handshake, lvgl]
aliases: [USB lag, hello storm, responsive touch, opóźnienia dotyku]
updated: 2026-10-15
status: active
---


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


## Parowanie przywrócone za zgodą użytkownika — 2026-10-15 około23:29

Użytkownik wyraźnie zatwierdził przesłanie istniejącego tokenu Windows do CYD przez COM5 (odpowiedź „tak”). Przywrócono zatwierdzony token z chronionego rekordu Windows na fizycznym CYD-40C86C przez USB. Potwierdzono zgodność i długość43znaki; wartości sekretu nie wypisywano ani nie zapisywano do logów. Rekordu Windows i pozostałych ustawień nie kasowano. Wcześniejsza blokada zgody nie jest już aktualna.

Uruchomiono widoczny artifacts/visible-agent/StreamDeck.WindowsAgent.exe --usb-port COM5; potwierdzono PID29148, Responding=true, okno CYD-DECK. /api/status port64697 tej sesji: connected, deviceIdCYD-40C86C, remoteAddressUSB, świeża telemetria23:29:38, audio100%/muted=false. Procesy/porty są historycznym stanem kontroli.

UWAGA: ta próbka raportuje cover.status=3/rendered=false; przywrócenie połączenia nie jest potwierdzeniem poprawnego obrazu. Końcowy test stabilności zapisano w artifacts/usb-perf-after-pairing.json. Ocena responsywności podczas dotyku i ponowny test okładki pozostają potrzebne. Nie deklaruj zaliczenia progu50ms — maksima obejmują początkową synchronizację (loop~515ms, network~509ms, render~35ms), a wcześniejszy baseline zawierał~101s blokadę.
