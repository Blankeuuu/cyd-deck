---
title: Niezawodność multimediów i synchronizacja osi czasu (GSMTC / CYD-DECK)
tags: [cyd-deck, media, covers, gsmtc, timeline, architecture]
aliases: [Media Reliability Guide, GSMTC Synchronization, Album Art Fixes]
updated: 2026-10-20
status: active
---

# Niezawodność multimediów i synchronizacja osi czasu w CYD-DECK

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
- **Unieważnianie cache**: Klucz miniatury jest unieważniany zarówno przy zmianie identyfikatora utworu, jak i przy zmianie referencji miniatury dla tego samego utworu. Odpytywanie samej osi czasu nie wyzwala ponownego odczytu strumienia grafiki.

---

## 4. Wyniki testów automatycznych

Wszystkie testy regresyjne i integracyjne wykonano w środowisku roboczym:

1. **Review Probe Leada (`Probe.csproj`)**: `PASS` — weryfikacja wznowienia postępu osi czasu po przejściowym błędzie metadanych (pozycja = 2 s).
2. **Pakiet `StreamDeck.Media.Tests`**: `22 / 22 PASS` (m.in. ekstrapolacja Chromium, walidacja parametrów, obsługa pauzy i odłączania sesji).
3. **Pakiet `StreamDeck.Covers.Tests`**: `15 / 15 PASS` (m.in. liniowy backoff ponowień, unieważnianie indeksu, odporność na błędy I/O).
4. **Regresje runtime (`StreamDeck.WindowsAgent.Tests --runtime-media-regression`)**: `3 / 3 PASS` (ten sam utwór z nową miniaturą, brak nadmiarowych odczytów przy poll osi czasu, powrót do sprawności po błędzie I/O).

---

## 5. Podsumowanie i plan weryfikacji sprzętowej

Zmiany hosta przeszły wskazane testy regresyjne. Przed włączeniem zmian do oficjalnego wydania wymagany jest test manualny na fizycznym urządzeniu ESP32:
1. Weryfikacja płynności paska postępu na ekranie CYD Display podczas odtwarzania wideo w serwisie YouTube.
2. Sprawdzenie pobierania i buforowania okładek albumów na karcie microSD przy dynamicznej zmianie utworów w aplikacji Spotify Desktop.
