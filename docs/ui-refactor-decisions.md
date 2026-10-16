---
title: Ustalenia refaktoryzacji UI CYD-DECK
tags: [cyd-deck, ui, ux, apple-design]
aliases: [CYD-DECK UI Decisions, Compact Layout, Zero Scroll]
updated: 2026-10-16
status: active
---

## Korekta użytkownika: zegar w doku

Pierwszy przycisk nawigacji ponownie pokazuje bieżący czas `HH:mm`, na wszystkich stronach. Pozostałe elementy to Deck / Media / PC / koło zębate. Szerokość zegara wynika z najszerszej cyfry fontu; test obejmuje wszystkie 1440 godzin/minut doby. Zmiana zastępuje poprzednie ustalenie etykiety Home. Pozostałe poprawki ikon, daty i układu pozostają aktualne.

# Ustalenia refaktoryzacji UI CYD-DECK

Wywiad `grill-with-docs` zakończony. Użytkownik zatwierdził obie rundy i pełny zakres do implementacji, a następnie doprecyzował ekran startowy i osobny przycisk ustawień.

## Runda 1 — zatwierdzone

- CYD Display zachowuje maksymalnie sześć kafelków na stronie, w układzie 3×2; podgląd Windows odzwierciedla układ urządzenia.
- Brak pionowego przewijania całej zakładki obowiązuje przy oknie 1200×760 i skali Windows 100%. Przy większym skalowaniu dopuszczone są podzakładki ustawień. Listy skrótów i logów mogą przewijać własną zawartość.
- Jeden jawny zapis konfiguracji działa także offline; ponowne połączenie synchronizuje ostatnią zapisaną konfigurację. Niezapisana edycja nie trafia na urządzenie. Szczegóły: [ADR 0002](adr/0002-explicit-save-and-offline-sync.md).
- Dok CYD stosuje wizualny efekt półprzezroczystości bez rozmywania tła w czasie rzeczywistym; priorytetem jest szybka reakcja na dotyk.

## Fakty z inspekcji kodu

- Aktualny checkout korzysta z WinForms/.NET 10. Okno ma domyślnie 1080×740 i minimum 900×620.
- Firmware ma sześć slotów i układy hero-1, split-2, quad-4, grid-6 oraz auto.
- Obecny wygląd zapisuje się automatycznie po 300 ms; wymaga dostosowania do ADR 0002.
- W źródłach Agenta nie znaleziono części nazw z opisu użytkownika, m.in. `Quick Media Deck` i `CYD-DECK Control Surface`. Przed wdrożeniem trzeba sprawdzić zgodność uruchomionej wersji z checkoutem.

## Runda 2 — zatwierdzone

Użytkownik zatwierdził wszystkie trzy rekomendacje odpowiedzią „3x tak”.

- Przełączanie zakładek zachowuje Configuration Draft. Zamknięcie okna z niezapisanymi zmianami udostępnia `Save / Discard / Cancel`.
- Jasność zmieniana w `Info` na CYD działa natychmiast jako Local Brightness Override. Jawny zapis ustawień w Windows Agent ponownie stosuje jasność z konfiguracji PC. Chwilowe rozłączenie i ponowne połączenie nie cofa lokalnej korekty; synchronizacja musi odróżniać odtworzenie połączenia od jawnego zastosowania ustawień.
- Pełny katalog skrótów pozostaje dostępny. Podgląd urządzenia pokazuje sześć przypisanych pozycji, których kolejność można zmieniać przeciąganiem. Dodawanie kolejnych stron kafelków nie należy do tej refaktoryzacji.

## Zakres końcowy do potwierdzenia

- Firmware: cztery stałe zakładki `Deck / Monitor / Media / Info` w dolnym doku. Kalibracja, test dotyku, jasność i szczegóły techniczne wyłącznie w Info. Media zachowuje okładki, timeline i audio; Monitor prezentuje CPU, RAM, GPU i temperaturę. StandBy używa angielskiej daty.
- Angielski obejmuje napisy systemowe i domyślne etykiety. Nazwy utworów, artystów oraz własne nazwy użytkownika pozostają treścią użytkownika i nie są tłumaczone automatycznie.
- Windows: jeden kompaktowy toolbar do 64 px, jeden wskaźnik połączenia i jeden mechanizm zapisu. Deck Builder ma podgląd oraz Tile Inspector obok siebie. Settings używa kompaktowych kart obok siebie i adaptacji przy większym DPI. Usuwane są rzeczywiste duplikaty odnalezione w kodzie.
- Desktop Appearance i Display Appearance pozostają niezależne zgodnie z ADR 0001. Stan mediów, cache okładek oraz transport USB zachowują działające mechanizmy; zmiany synchronizacji są ograniczone do potrzeb uzgodnionego cyklu zapisu i lokalnej korekty jasności.
- Weryfikacja: kompilacja Agenta i firmware, testy zmienionych zachowań, kontrola układu przy 1200×760/100% i adaptacji DPI, sprawdzenie wymiany Serial JSON. Wynik kompilacji nie jest traktowany jako potwierdzenie jakości na fizycznym ekranie.

Pełny zakres został zatwierdzony przez użytkownika odpowiedzią „tak”.

## Zmiana użytkownika po zatwierdzeniu

Ekran startowy `Default` prezentuje duży zegar, kompaktowy odtwarzacz oraz pierwsze cztery przypisane skróty. Główna nawigacja to `Default / Shortcut / Media / PC`; diagnostyka jest dostępna przez mniejszy, oddzielony odstępem przycisk z ikoną koła zębatego. Zastępuje to wcześniejszy układ czterech zakładek z tekstowym Info. Ekran startowy nie jest dodatkową stroną kafelków ani siódmym slotem skrótu.

## Implementacja

Kompaktowe widoki Windows znajdują się w `SettingsWindow.Compact.cs`; usunięto zastąpione implementacje stron ze starego pliku. Zapis obejmuje profile, skróty i niezależne konfiguracje wyglądu. Ustawienia USB są dostępne w Settings; jawne `--usb-port` ma pierwszeństwo przed zapisanym wyborem. Auto-connect dotyczy USB; nasłuch Wi-Fi pozostaje dostępny.

Sześć pozycji urządzenia jest pierwszymi sześcioma wpisami aktywnego profilu. Profil przechowuje cały katalog; pozostałe wpisy są oznaczone jako Catalog. Podgląd stosuje wybrany układ auto/hero/split/quad/grid. Przeciąganie i przyciski kolejności zmieniają draft.

## Tokeny i zasady wizualne

- Windows: tło `#0D0F14`, karta `#16181D`, powierzchnia kontrolek `#1E2028`, tekst `#F4F6F8`, tekst pomocniczy `#9CA4B0`; obramowanie 1 px `#292B2F`. Wariant jasny korzysta z istniejących semantycznych tokenów Light.
- CYD: tło `#101216`, karty `#1D1D1F`, tekst `#F7F8FA`, tekst pomocniczy `#B2BAC6`, domyślny akcent `#7EAFE3`. Kolory użytkownika zachowują ochronę kontrastu; tekst kafelków dobierany jest na podstawie względnej luminancji.
- Typografia Windows: Inter, jeśli dostępny w systemie, w przeciwnym razie Segoe UI; tekst formularzy 10 pt, tytuł zakładki 17 pt. CYD używa istniejących fontów LVGL 14/16/20 px i Montserrat 48 dla zegara; glyphy polskie pozostają do treści użytkownika.
- Glass stosowany jest do funkcjonalnego doku CYD; bez rozmywania w czasie rzeczywistym. Brak dekoracyjnych animacji przełączania stron.
- Układ regularny: sidebar 188 px | toolbar 60 px | podgląd + inspektor albo trzy karty ustawień.
- Układ kompaktowy / większe DPI: ustawienia w podzakładkach, z dzieleniem dłuższych formularzy na General/More. Powrót do większego okna odtwarza kompletne karty.

Podstawa decyzji: lokalne referencje apple-design `layout.md › Best practices`, `typography.md › Ensuring legibility`, `tab-bars.md › Best practices`, `settings.md › General settings` oraz `liquid-glass.md › The two layers`. Zasady HIG są adaptowane do Windows/WinForms i ograniczeń TFT 320×240.

### Obliczony kontrast palety bazowej

- `#F4F6F8` na `#16181D`: 16.39:1.
- `#9CA4B0` na `#16181D`: 7.06:1.
- `#F7F8FA` na `#1D1D1F`: 15.84:1.
- `#B2BAC6` na `#1D1D1F`: 8.60:1.
- `#101216` na `#7EAFE3`: 8.15:1.

## Doprecyzowanie Default — bieżący widok

Na prośbę użytkownika pierwsza pozycja doku pokazuje czas HH:mm zamiast nazwy Default, na wszystkich stronach. Kliknięcie nadal otwiera Home Screen.

W obrębie Default górny rząd zawiera dwie karty 148×84 px: odtwarzacz po lewej (x=8) i zegar po prawej (x=164). Muzyka używa bieżącej okładki jako tła z proporcjonalnym center-crop (`LV_IMAGE_ALIGN_COVER`), zaokrągleniem 16 px i czarną warstwą o opacity 168/255. Białe podpisy zachowują kontrast co najmniej około 7:1 również na białej okładce; placeholder bez obrazu pozostaje ciemny.

Cztery skróty są w siatce 2×2, po 148×44 px (około 84% większa powierzchnia od poprzednich 68×52 px). Przestrzeń kończy się na y=194; dok zaczyna się na y=202. Zegar ma Montserrat 40 px, a rzeczywista szerokość 18:54 w źródle fontu wynosi 99.31 px przy dostępnych 132 px. Data ma osobne linie dnia tygodnia i daty.

Układ:

```text
┌────────────────┬────────────────┐
│ Artwork +      │     18:54      │
│ title / artist │     Friday     │
│           ▶    │   16 October   │
├────────────────┼────────────────┤
│   Shortcut 1   │   Shortcut 2   │
├────────────────┼────────────────┤
│   Shortcut 3   │   Shortcut 4   │
└────────────────┴────────────────┘
 [18:54 | Shortcut | Media | PC]  ⚙
```

Okładka współdzieli istniejący descriptor/bufor RGB565 z Media. Zmiana widoku nie inicjuje nowego transferu ani dekodowania. Dotknięcie karty otwiera Media; osobny przycisk Play/Pause zachowuje sterowanie odtwarzaniem. Pozostałe strony i Agent nie są przebudowywane w tej iteracji.

## Aktualizacja: Home, dok i ikony skrótów

- Aktualny dok: **Home / Deck / Media / PC / koło zębate**. Szerokości wynikają z metryk fontu i poziomego paddingu 10 px; odstępy wynoszą 6 px. Etykiety nie używają wielokropków.
- Zegar Home: duża godzina i wyśrodkowana data w formacie `Fri, Oct 16`, pole 132 px i kolor `#A1A1AA` w ciemnym motywie. Zastępuje wcześniejszą godzinę w doku i długą datę.
- Skróty Home i Deck mają osobne etykiety tekstu i ikon. `cyd_icons_20` zawiera 19 symboli Font Awesome, w tym keyboard, globe, terminal, lock, star i app. Zachowano preferencje `icon` / `label` / `both`; tryb Label celowo ukrywa ikonę.
- Generator fontu: `scripts/icon-font-generator`, bitmapy 4 bpp, 3312 B. Licencja: `firmware/src/ui/fonts/FONT-AWESOME-LICENSE.txt`.
- Windows: kompaktowa szerokość selektora profilu i przycisku zapisu; podgląd kafelka respektuje ten sam Display Mode co CYD.
- Protokół Serial JSON pozostaje `protocol:1`, bez nowych pól. Testy układu i rzeczywistych metryk fontów przechodzą, włącznie ze wszystkimi skróconymi dniami i miesiącami.
