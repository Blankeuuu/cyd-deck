---
title: Przebudowa ustawień na CYD Display
tags: [cyd-deck, firmware, settings, touchscreen, ux, lvgl]
aliases: [CYD settings overhaul, ustawienia CYD, brightness controls, device maintenance]
updated: 2026-10-19
status: active
---

# Cel i decyzja

Ustawienia mają umożliwić szybką regulację ekranu oraz łatwe znalezienie narzędzi dotyku i informacji o urządzeniu na panelu 320×240. Poprzedni widok łączył pięć drobnych wierszy diagnostyki, przyciski 28–32 px i suwak o obszarze dotykowym około 25 px. Jasność była przy dolnej krawędzi, bez wartości procentowej; restart działał natychmiast.

Zachowano ikonę ustawień w doku oraz podział odpowiedzialności z [[../adr/0001-decouple-desktop-and-cyd-display-appearance]] i [[../adr/0002-explicit-save-and-offline-sync]]. Ustawienia systemowe pozostają po angielsku zgodnie z [[../ui-refactor-decisions]]. Dane Windows i katalog skrótów nadal mają źródło prawdy w Agencie.

```text
Settings                         USB online
[ Display ] [  Touch  ] [ Device ]

Brightness                            60%
───────────────●───────────────

[  25%   ] [   60%   ] [  100%  ]
────────────────────────────────────────
        istniejący dok nawigacji
```

# Przepływy

- **Display:** duży suwak 10–100%, bieżąca wartość procentowa oraz poziomy 25%, 60% i 100%. Podświetlony preset odpowiada dokładnej wartości. Przeciąganie nie jest cofane przez odświeżenie stanu. Jasność nadal używa istniejącego `SetBrightness` i `Local Brightness Override`; jawny zapis ustawień Windows może usunąć korektę zgodnie z dotychczasową rewizją.
- **Touch:** stan kalibracji, `Test touch` i `Calibrate`, z wyjaśnieniem testu i pięciu celów. Funkcje pozostają lokalne i korzystają z dotychczasowych intentów.
- **Device:** `Connection & status`, `Restart` i `Factory reset`. Widok statusu ma jawny `Back`, oddzielne etykiety/wartości firmware, PC link, Wi-Fi, IP, SD, uptime i OTA. Długie wartości otrzymują wielokropek.
- **Restart:** potwierdzenie z komunikatem o zachowaniu ustawień i ponownym połączeniu. `Cancel` nie kolejkuje operacji.
- **Factory reset:** zachowano wymagane przytrzymanie 3 s i dodatkowe potwierdzenie. Dialog wyraźnie wskazuje usunięcie Wi-Fi, parowania, kalibracji i ustawień. Pełnoekranowa przyciemniona warstwa przechwytuje dotyk tła. Zmiana strony programowo zamyka dialog.

Nie dodano nowych pól protokołu ani nowego formatu NVS. Nie jest potrzebna zmiana po stronie Agy/Windows Agent.

# Styl i samoocena

Interfejs pozostaje spokojnym panelem sterowania sprzętem. Jego wyróżnikiem jest bezpośrednia regulacja jasności z czytelną wartością i trzema dużymi presetami. Usunięto widoczne drobne statusy z widoku codziennej regulacji; nie dodano dekoracyjnych kart, animacji ani przewijania. Na stałym ekranie 320×240 zastosowano trzy sekcje i jeden dodatkowy poziom szczegółów zamiast zagęszczania całej diagnostyki.

Fonty: istniejące `streamdeck_polish_14/16/20`; wartości i tytuły do 20 px, etykiety interaktywne 16 px, statusy/opisy 14 px. Wszystkie przyciski nowych sekcji i potwierdzeń mają wysokość 44 px. Suwak ma obszar dotykowy 44 px wysokości; mieści się w ekranie i nie wchodzi w dok. To adaptacja zasad dotykowych do CYD, nie deklaracja zgodności urządzenia z platformą iOS ani systemowymi funkcjami dostępności.

| Rola | Ciemny motyw | Jasny motyw |
|---|---|---|
| Tło | `#101216` | `#F3F5F7` |
| Panel dialogu | `#1D1D1F` | `#FFFFFF` |
| Kontrolka | `#272729` | `#E8EAED` |
| Tekst | `#F7F8FA` | `#101216` |
| Tekst pomocniczy | `#B2BAC6` | `#58616E` |
| Aktywna kontrolka | akcent użytkownika, domyślnie `#7EAFE3` | akcent użytkownika, domyślnie `#276EF1` |

Kolor tekstu aktywnych przycisków jest wybierany przez istniejące `contrastingText`. Obliczone kontrasty bazowe: tekst na kontrolce ciemnej 14,03:1; tekst pomocniczy na ciemnym tle 9,58:1; tekst na kontrolce jasnej 15,55:1; tekst pomocniczy na jasnym tle 5,74:1; domyślne aktywne kontrolki 8,15:1 (dark) i 4,58:1 (light). Akcje resetujące używają `#FF918B` na ciemnej kontrolce (6,87:1) lub `#A72323` na jasnej (5,99:1). Potwierdzenie kasowania ma biały tekst na `#A72323` (7,22:1). To obliczenia kolorów źródłowych; TFT/renderer RGB565 kwantyzuje składowe.

Wykorzystano skille `apple-design` i `frontend-design`. Podstawa lokalnych referencji HIG: `settings.md › Best practices` — „Minimize the number of settings you offer”; `layout.md › Best practices` — grupowanie powiązanych funkcji; `accessibility.md › Mobility` i `buttons.md › Best practices` — wielkość i odstępy celów dotykowych; `sliders.md › Best practices` — wartość liczbowa obok regulacji; `alerts.md › Best practices` — potwierdzenia rzadkich, nieodwracalnych operacji. Do panelu CYD zastosowano zasady, bez kopiowania konwencji okien macOS/iOS.

Przegląd renderów: główne widoki oraz oba potwierdzenia mieszczą się w 320×240; sprawdzone etykiety nie są obcinane. Podglądy rzeczywistego renderera LVGL pokazują spójny stan zaznaczenia i wyraźne oddzielenie niebezpiecznej operacji. Ocena projektu: **Good**, z pozostającą oceną dotyku i czytelności na fizycznym panelu przez użytkownika.

# Weryfikacja

- `scripts/test-ui-logic.ps1`: PASS. Rozszerzono test rzeczywistego drzewa widgetów z hostowym fake LVGL o zmianę sekcji, geometrię, jasność/przeciąganie/presety, odtworzenie ustawień Windows, statusy online/offline, kalibrację, modalne potwierdzenia i anulowanie. Dwadzieścia cykli przez wszystkie sekcje w obu motywach zachowuje stałą liczbę obiektów; niezmieniony stan nie powoduje kolejnych zapisów koloru tła.
- Test rzeczywistych danych fontów obejmuje nowe etykiety sekcji, akcje i komunikaty dialogów, sprawdzając ich szerokość względem dostępnego miejsca.
- `scripts/render-ui-preview.ps1`: PASS. Nowy harness `tests/ui_render_preview.cpp` renderuje produkcyjny `ui_manager.cpp` i fonty przez prawdziwe LVGL 9.3 do bufora RGB565. Powstało 12 obrazów: Display, Touch, Device, Status, Restart, Reset w jasnym i ciemnym motywie. Nie emuluje to XPT2046, UART, SD ani backlight.
- Build `cyd28_st7789_app`: PASS bez ostrzeżeń; RAM statyczny 84200 B, flash 1682481 B / 2031616 B.
- Build `cyd28_ili9341_app`: PASS bez ostrzeżeń; RAM statyczny 84200 B, flash 1682521 B.
- Wgranie ST7789 na COM5: PASS, zgodnie z wcześniejszą zgodą użytkownika na flashowanie i ocenę urządzenia. Zapisano wyłącznie aplikację pod `0x10000`, potwierdzono hash i zrestartowano ESP32 przez RTS; ustawienia i parowanie zachowano.
- Przywrócono aktualnego Agenta z worktree `windows-app`, z jego wcześniejszym argumentem `--usb-port COM5`. Odczyt `/api/status` potwierdził `CYD-40C86C`, `connected` przez USB i `sd.ready=true`; kolejna próbka przy uptime 90 s miała `cover.status=2` i `cover.rendered=true`. Agent pozostał uruchomiony. Ocena dotyku i wyglądu na fizycznym ekranie pozostaje po stronie użytkownika.

Podglądy: `artifacts/settings-preview/*.png` (lokalny, ignorowany katalog). Logi: `.tools/settings-host-final.log`, `.tools/settings-st7789-build.log`, `.tools/settings-ili9341-build.log`, `.tools/settings-render.log`, `.tools/settings-flash.log`.

# Akceptacja użytkownika — 2026-10-19

Po ocenie ustawień na fizycznym CYD użytkownik potwierdził kierunek („Dobrze”), a po kolejnej poprawce Media/PC zaakceptował całość słowami „jest super” i polecił uzupełnienie wiki oraz publikację na `main`. Wcześniejsze wzmianki o oczekiwaniu na ocenę opisują stan przed tą akceptacją. Końcowy firmware obejmuje także [[2026-10-19-pc-media-clarity]]. Akceptacja wizualna nie zastępuje osobnych prób awaryjnych OTA ani długotrwałych testów sprzętu.
