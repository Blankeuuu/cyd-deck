---
title: CYD — spójny akcent Media i jednoznaczne dane Windows PC
tags: [cyd-deck, firmware, media, pc-monitoring, ux, reliability]
aliases: [Media accent mismatch, Windows PC metrics, CPU RAM CYD, nieaktualne pomiary]
updated: 2026-10-19
status: active
---

# Wymaganie użytkownika

Pasek postępu na Media miał niezgodny kolor akcentu. Karta PC nie wyjaśniała, czy pokazuje dane komputera, czy CYD. Użytkownik jednoznacznie wykluczył statystyki CYD z tej karty.

# Media

Pasek nie miał jawnego koloru `LV_PART_INDICATOR` i dziedziczył domyślny kolor LVGL. Regresja `media-accent-red.exe` odtworzyła różnicę między kolorem wskaźnika a `state.appearance.accent`. Jawne ustawienie neutralnej ścieżki, pełnego krycia i akcentu wskaźnika usunęło błąd.

Testy pokrywają motyw jasny/ciemny, zmianę akcentu bez przebudowy strony oraz wyjście i ponowne wejście. Kolor wskaźnika odpowiada suwakowi głośności. Podgląd wykrył dodatkowo niewłaściwy kontrast ikony Play przy ciemnym akcencie w jasnym motywie; jej kolor dobiera teraz `contrastingText` do rzeczywistego wypełnienia przycisku.

# Windows PC

Nowy widok ma jawny nagłówek `Windows PC`, stan `Live / Waiting / Stale / Offline`, dwa duże panele `CPU usage` i `Memory (RAM)` oraz podpis źródła `Windows Agent`. RAM pokazuje procent i zajętą/całkowitą pamięć. Fonty 14/16/20 px i istniejąca paleta zachowują spójność z [[2026-10-19-cyd-settings-overhaul]]. Układ 320×240 nie wymaga przewijania; panele kończą się nad dokiem rozpoczynającym się na y=202.

Nie są tworzone żadne widgety heap, SD, uptime CYD ani diagnostyki OTA na stronie PC. Diagnostyka urządzenia pozostaje w ustawieniach; dane wewnętrzne wymagane przez telemetrię firmware nadal istnieją.

## Wyłącznie rzeczywiste pomiary

Przegląd ujawnił wyliczanie zastępczej temperatury w firmware (`38 + CPU * 35 / 100`) oraz zastępczego GPU i temperatury w `ProtocolStateMapper.CreateSystemStats` Agenta. Takie wartości nie są pomiarami. Nowa karta prezentuje tylko CPU i RAM z `system.stats`; pola GPU/temperatury nie są wyświetlane. Nie ingerowano w kod ani działający build Agy/Windows w innym worktree.

Prawdziwe zero wyświetla się jako `0%`. Pole puste, `null`, nieprawidłowe lub poza zakresem daje brak pomiaru, nie podtrzymanie poprzedniej liczby. Po pięciu sekundach bez świeżej próbki albo po utracie połączenia wartości są zastępowane przez `--`, a paski zerowane. Powrót danych przywraca bieżące wartości. Świeżość korzysta z odejmowania `uint32_t`, także przy przepełnieniu `millis()`.

`memoryUsedBytes` i `memoryTotalBytes` są odczytywane jako liczby 64-bitowe z kontrolą przepełnienia, a nie przez 32-bitowy `long` ESP32. Pozwala to poprawnie pokazywać np. 10 / 16 GB RAM. Dane wymagają `0 <= used <= total`, dodatniego total oraz zgodności z pojemnością lokalnego modelu. Brak pojemności nie ukrywa poprawnego procentu RAM.

## Zgodność i dalszy zakres dla Agy

Envelope i pola `system.stats` pozostają bez zmian. Nie dodano nowych wymagań protokołu ani formatu NVS, więc nie wprowadzono breaking change. Parser przyjmuje istniejące pola CPU/RAM; opcjonalne GPU/temperatura są ignorowane na karcie.

Przed przywróceniem GPU lub temperatury do widoku należy po stronie Agenta usunąć syntetyczne fallbacki i zagwarantować pochodzenie danych z rzeczywistego providera. Obecny kontrakt ma nullable `GpuPercent` i `TempCelsius`, więc sam brak pomiaru nie wymaga nowego typu wiadomości. Ewentualne dodatkowe pola źródła/dostępności trzeba uzgodnić z Agy przed rozszerzeniem walidowanego schematu.

# Weryfikacja

- Media: test RED → GREEN, z `-Wall -Wextra -Werror`; po poprawce także build ST7789 PASS.
- Pełne `scripts/test-ui-logic.ps1`: PASS, w tym nowe testy parsowania, układu, danych nieaktualnych/brakujących, rzeczywistego zera, pojemności RAM ponad 2 GB i zawijania zegara. Testy faktycznego drzewa UI i protokołu także osobno PASS z `-Werror`.
- Rzeczywiste metryki fontów sprawdzają nowe etykiety i podpisy.
- `scripts/render-ui-preview.ps1 -OutputDirectory artifacts/pc-media-preview`: PASS. Produkcyjny renderer LVGL i fonty wygenerowały podglądy obu motywów, PC live/waiting/offline i Media z różnymi akcentami. Obejrzano PC w obu motywach i stany braku danych oraz ponownie Media po korekcie kontrastu Play.
- Końcowe buildy `cyd28_st7789_app` i `cyd28_ili9341_app`: PASS bez ostrzeżeń. RAM statyczny 84168 B; flash aplikacji odpowiednio 1683533 B i 1683573 B / 2031616 B.
- Wgranie ST7789 na COM5: PASS. Zapisano aplikację pod `0x10000`, esptool potwierdził hash i zrestartował ESP32 przez RTS. Ustawienia i parowanie zachowano; Agent został ponownie uruchomiony.
- Kontrola po restarcie o 17:23:27 czasu polskiego: `CYD-40C86C`, `connected` przez USB, uptime 27 s, SD gotowa. Agent miał bieżącą telemetrię PC (CPU około 21%, RAM około 85%). Nie jest to zdalny odczyt wartości narysowanych na CYD; ocenę fizycznego widoku i dotyku pozostawiono użytkownikowi.

Logi robocze: `.tools/media-accent-red-result.log`, `.tools/media-accent-build.log`, `.tools/pc-media-host-final.log`, `.tools/pc-media-build-final.log`, `.tools/pc-media-render.log`, `.tools/pc-media-flash.log`. Podglądy są lokalnymi artefaktami w `artifacts/pc-media-preview`.

Źródła projektu sprawdzono przez Obsidian MCP (polskie i angielskie wyszukiwanie), w tym notatkę `projects/stream-deck/agent/src/SystemMonitoring/README.md`, a następnie skonfrontowano z aktualnym mapperem Agenta i parserem firmware. Zastosowano skille diagnostyczne i dotychczasowe zasady `apple-design`/`frontend-design`; uproszczenie danych opiera się na czytelnej hierarchii i zasadzie prezentowania rzeczywistych pomiarów, bez ozdobnych wskaźników.

# Akceptacja użytkownika — 2026-10-19

Po wgraniu na ST7789 i własnej ocenie użytkownik potwierdził całość: „uzupelnij wiki i zpushuj zmiany do maina jest super!”. Ustawienia, spójność akcentu Media oraz nowy widok Windows PC są zaakceptowane do publikacji. Wcześniejsze zastrzeżenie o braku oceny fizycznej dotyczy etapu przed tą odpowiedzią.
