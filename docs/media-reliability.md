---
title: Niezawodność multimediów i synchronizacja osi czasu (GSMTC / CYD-DECK)
tags: [cyd-deck, media, covers, gsmtc, timeline, architecture]
aliases: [Media Reliability Guide, GSMTC Synchronization, Album Art Fixes]
updated: 2026-10-22
status: active
---

# Niezawodność multimediów i synchronizacja osi czasu w CYD-DECK

## Kandydat lifecycle buforów okładek — 2026-10-22

Przy trwałym `No artwork` fizyczne urządzenie miało 124760 B wolnego heapu, ale
największy ciągły blok 32756 B — mniej niż wymagane 32768 B obrazu RGB565.
SD nadal raportowała gotowość, a restart samego Agenta nie usunął awarii.

Firmware używa teraz dwóch slotów obrazu rezerwowanych jednorazowo przed startem
AgentClient zamiast alokacji i zwalniania 32 KiB przy każdej okładce. Bufory
pozostają na stercie przez cały runtime; mały obiekt globalny nie powiększa BSS
o 64 KiB. `CoverPixelBuffers` oddziela obraz widoczny od stagingu;
UI przyjmuje wyłącznie wynik właściwego ID/generacji. Błąd dekodowania lub stary
wynik nie nadpisuje widocznego bufora, a slot roboczy można użyć ponownie.
Pozostałe alokacje dekodera/taska nadal mogą zawieść; nie jest to gwarancja działania
przy dowolnym wyczerpaniu heapu. Hostowy test produkcyjnego modułu przechodzi
160 zmian i scenariusze błędów/stale/powrotu okładki. Wstrzyknięty allocator
potwierdza dokładnie dwie alokacje, brak dalszych wywołań przy zmianach oraz
rollback i retry po nieudanej początkowej rezerwacji. Rzeczywisty JPEG, karta SD,
FreeRTOS i TFT wymagają walidacji fizycznej. Stan buildu i wdrożenia:
[raport QA](firmware-reliability-qa.md).

## Uzupełnienie firmware i transportu — 2026-10-20

Na gałęzi `fix/firmware-e2e-reliability` dodano ograniczone ponawianie tego samego
coverId po Failed, ponowne transfery na jawny cache miss, odblokowanie odbiornika
USB podczas artwork, sieciowy worker HTTP, worker SD read/decode i kontrolę SHA-256. Raport rendered
odróżnia dekodowanie od zakończenia ramki TFT. Audio ma lokalny stan pending,
korelowaną odpowiedź i opcjonalną revision. Nie zmieniono envelope protocol:1.

Poniższe wcześniejsze wpisy „Firmware UNCHANGED” dotyczą poprzedniego etapu hosta,
nie tej gałęzi. Nie wgrano zmian na fizyczny CYD ani nie scalono ich do main.
Szczegóły, rzeczywiste wyniki oraz jawne braki w pokryciu sprzętowym i SD:
[raport QA](firmware-reliability-qa.md), [aktualizacja protokołu](protocol.md).

Dokument opisuje architekturę obsługi sesji multimedialnych Windows (WinRT GSMTC), mechanizmy stabilizacji osi czasu odtwarzacza, procedury odzyskiwania miniatur okładek albumów oraz zasady współbieżności w **CYD-DECK**.

---

## 1. Status realizacji i weryfikacja

| Obszar | Status | Stan faktyczny |
|---|---|---|
| **Kod hosta i bibliotek mediów** | **IMPLEMENTED** | Implementacja i poprawki przeszły przegląd oraz testy regresyjne; publikacja wydania i walidacja na sprzęcie pozostają osobne. |
| **Testy jednostkowe i integracyjne hosta** | **IMPLEMENTED** | Niezależnie zweryfikowane przez Leada: `StreamDeck.Media.Tests` (**22/22 PASS**), `StreamDeck.Covers.Tests` (**15/15 PASS**), `RuntimeMediaRegressionTests` (**3/3 PASS**), review probe (`Probe.csproj`) **PASS**. |
| **Protokół komunikacji** | **UNCHANGED** | Zachowano pełną zgodność z `protocol: 1`. Brak zmian w specyfikacji ramek binarnych i JSON. |
| **Firmware urządzenia** | **UNCHANGED** | Brak zmian w kodzie firmware ESP32; brak sztucznego ograniczania (heurystycznego clampu) pozycji przy przewijaniu wstecz. |
| **Weryfikacja na żywym sprzęcie (CYD ESP32)** | **PENDING** | Testy przeprowadzono w środowisku hosta Windows z użyciem kontrolowanych mocków i fixture'ów. Weryfikacja na fizycznym panelu ST7789 przez USB CDC / Wi-Fi oczekuje na testy manualne. |
| **Obserwacje aplikacji zewnętrznych (Spotify/Brave)** | **KONTROLOWANE MOCKI** | Odporność na przejściowe blokady I/O i puste strumienie przetestowano w środowisku testowym. Nie potwierdzono bezpośredniej obserwacji sharing violations w procesie Spotify Desktop ani 0-bajtowych strumieni w Brave na żywym systemie (scenariusze oparte na specyfikacji GSMTC). |

---

## 2. Diagnoza problemów i architektura rozwiązań

Zakres zgłoszenia obejmował trzy problemy. Testy potwierdzają naprawę kontrolowanych scenariuszy hosta; poniższe powiązania z konkretnymi aplikacjami wymagają weryfikacji na żywej sesji:
1. **Zacinanie i pętle osi czasu (*rubberbanding*)**: Pozycja cofała się lub zapętlała (np. `01s → 02s → 01s → 02s → 06s`) na skutek asynchronicznego wyścigu pomiędzy pollingiem osi czasu a odświeżaniem metadanych.
2. **Brak postępu odtwarzacza w przeglądarkach (YouTube/Chromium)**: Pozycja pozostawała zatrzymana na `00:00`, ponieważ silnik Chromium w WinRT GSMTC nie emituje ciągłych zdarzeń zmiany czasu, aktualizując jedynie znaczniki referencyjne.
3. **Utrata okładek albumów**: Testy odtwarzają przejściowe błędy I/O, puste strumienie i uszkodzony cache hosta. Nie potwierdzono, że błędy Spotify/Brave uszkadzały indeks na karcie microSD.

---

## 3. Szczegóły implementacji Windows Agenta

### 3.1. Ekstrapolacja czasu WinRT GSMTC (`LastUpdatedTime` i `PlaybackRate`)
W celu prawidłowego wyświetlania postępu odtwarzaczy opartych o silnik Chromium (np. YouTube w przeglądarce):
- `WindowsGlobalMediaSession` odczytuje natywne pola `LastUpdatedTime` oraz `PlaybackRate` udostępniane przez system Windows.
- Pozycja wyliczana jest w oparciu o upływ czasu zegara systemowego:
  $$\text{Pozycja} = \text{Position} + (\text{UtcNow} - \text{LastUpdatedTime}) \times \text{PlaybackRate}$$
- Wartości są chronione przed anomaliami: skrajne wartości nieliczbowe (`NaN`, `Infinity`) są odrzucane, wartości ujemne sprowadzane do zera, a czas ograniczany do `EndTime`.

### 3.2. Usunięcie wyścigów współbieżności w `MediaSessionMonitor`
- **Śledzenie aktywnych operacji mapowania (`_activeRefreshCount`)**: Zastąpiono porównywanie sekwencji jawnym licznikiem w bloku `try ... finally`. W przypadku przejściowego wyjątku (`IOException`) monitor zachowuje ostatni poprawny stan (*last-good snapshot*), a pętla odpytywania osi czasu nie ulega permanentnej blokadzie i natychmiast wznawia działanie.
- **Ochrona przed wyparciem nowego utworu (*poll displacement race*)**: W trakcie pobierania danych GSMTC przez `TryRefreshTimeline` może nadejść zdarzenie zmiany utworu. Przed zatwierdzeniem nowego stanu timeline monitor atomowo weryfikuje generację sesji, wersję snapshotu oraz brak aktywnych operacji mapowania. W razie wykrycia zmiany utworu odpytywanie rezygnuje z commitu, zapobiegając nadpisaniu nowych metadanych starym stanem.
- **Serializacja publikacji**: Publikacja zdarzeń `SnapshotChanged` odbywa się pod blokadą `_publishGate` z monotoniczną numeracją wersji. Wykluczono możliwość emisji zdarzeń po zwolnieniu zasobów (`DisposeAsync`).

### 3.3. Niezawodność pamięci podręcznej okładek (Covers Cache)
- **Liniowy backoff ponowień**: W przypadku przejściowej blokady I/O agent wykonuje do 3 prób odczytu strumienia miniatury z odstępem liniowym: $100\text{ ms} \times \text{próba}$ (100 ms, 200 ms).
- **Odrzucanie pustych strumieni**: Strumienie o długości 0 bajtów są bezpiecznie ignorowane bez zapisu do pamięci podręcznej i bez uszkadzania indeksu binarnego.
- **Tożsamość okładki**: `MediaArtworkCoordinator` identyfikuje utwór po aplikacji, tytule, artyście i albumie. Nowa referencja miniatury tego samego utworu nie kasuje znanego `coverId`; chwilowy brak miniatury lub błąd jej odczytu zachowuje ostatnią okładkę. Zmiana referencji może odświeżyć ją w tle, najwyżej raz na 30 s. Odpytywanie samej osi czasu nie wyzwala ponownego odczytu strumienia grafiki.
- **Powrót do utworu**: ograniczony LRU 32 wpisów przechowuje `coverId` i akcent, dzięki czemu powrót może natychmiast opublikować znany identyfikator. To cache identyfikatorów Agenta; pliki JPEG urządzenia znajdują się osobno na SD.

### 3.4. Cache SD i ponowne wyświetlanie okładki — kandydat 2026-10-21

Firmware otwiera `/covers/<coverId>.jpg` bezpośrednio po identyfikatorze. Indeks
`/cache/covers.tsv` służy do ewidencji i usuwania starych plików. Zmniejszenie
limitu 256 MiB nie usuwa przyczyny powtórnego pobierania ani opóźnień dekodowania.

Usunięto pełny przebieg SHA-256 i `vTaskDelay(1)` dla każdego bloku 512 B podczas
każdego odczytu cache. Transfer strumieniowy nadal sprawdza SHA-256 przed commitem;
render sprawdza rozmiar, nagłówek JPEG i wynik dekodowania. Render nie wykrywa już
każdej zmiany bajtów, jeżeli zmodyfikowany plik pozostaje poprawnym JPEG.
Dekodowanie nadal działa w osobnym zadaniu, a publikacja obrazu w wątku LVGL.

Testy kontrolowane Agenta: `--artwork-decoupling-regression` 6/6,
`--runtime-media-regression` 3/3 i `StreamDeck.Covers.Tests` 15/15 PASS.
Build Windows Agent Release: 0 błędów i 0 ostrzeżeń. Wyniki te nie potwierdzają
czasu renderowania ani trwałości cache na karcie użytkownika. Stan końcowego
builda firmware, testów SD i wdrożenia podaje [raport QA](firmware-reliability-qa.md).

---

## 4. Wyniki testów automatycznych

Wszystkie testy regresyjne i integracyjne wykonano w środowisku roboczym:

1. **Review Probe Leada (`Probe.csproj`)**: `PASS` — weryfikacja wznowienia postępu osi czasu po przejściowym błędzie metadanych (pozycja = 2 s).
2. **Pakiet `StreamDeck.Media.Tests`**: `22 / 22 PASS` (m.in. ekstrapolacja Chromium, walidacja parametrów, obsługa pauzy i odłączania sesji).
3. **Pakiet `StreamDeck.Covers.Tests`**: `15 / 15 PASS` (m.in. liniowy backoff ponowień, unieważnianie indeksu, odporność na błędy I/O).
4. **Regresje runtime (`StreamDeck.WindowsAgent.Tests --runtime-media-regression`)**: `3 / 3 PASS` (ten sam utwór z nową miniaturą, brak nadmiarowych odczytów przy poll osi czasu, powrót do sprawności po błędzie I/O).

---

## 5. Podsumowanie i plan weryfikacji sprzętowej

### Weryfikacja integracji — 2026-10-22

`MediaSessionMonitor` odświeża metadane również co 500 ms, niezależnie od
zdarzeń natywnych i stanu pauzy. Zdarzenia i polling używają wspólnego schedulera
serializującego i scalającego odświeżenia. Fallback stabilizuje referencję
niepustej miniatury tego samego utworu; zdarzenia i zmiany utworu nadal przekazują
nową referencję. `DisposeAsync` zatrzymuje polling i oczekuje pracy w tle.

Przed commitem uruchomiono: Media.Tests **25/25**, Media.CommandTests **12/12**,
Covers.Tests **15/15**, Protocol.Tests **81/81**, pełne WindowsAgent smoke tests
(w tym artwork regression **6/6**) i osobne runtime media regression **3/3** —
wszystkie PASS. Windows Agent Release: **0 błędów, 0 ostrzeżeń**.
Trzy testy Media.Tests wymagały oczekiwania na asynchroniczne odświeżenie lub
zwolnienia pierwszego mappera przed oczekiwaniem na kolejne zadanie; zachowano
asercje końcowe i dodano zwalnianie monitorów w tych testach.

Testy hosta `test-ui-logic.ps1`, `test-cover-pixel-buffers.ps1` oraz
`test-sd-storage-cache.ps1`: PASS. Build `platformio run -e cyd28_st7789_app -j 1`
w katalogu `firmware`: PASS (819,37 s); RAM statyczny 85 936 B,
flash 1 717 661 B. Nie przeprowadzono testów sprzętowych.
Wiki: **BLOCKED** — w sesji integrującej brak narzędzi Obsidian MCP;
powyższe fakty wymagają synchronizacji z istniejącą dokumentacją projektu
przez MCP i kontrolnego odczytu.

Zmiany hosta przeszły wskazane testy regresyjne. Przed włączeniem zmian do oficjalnego wydania wymagany jest test manualny na fizycznym urządzeniu ESP32:
1. Weryfikacja płynności paska postępu na ekranie CYD Display podczas odtwarzania wideo w serwisie YouTube.
2. Sprawdzenie pobierania i buforowania okładek albumów na karcie microSD przy dynamicznej zmianie utworów w aplikacji Spotify Desktop.
