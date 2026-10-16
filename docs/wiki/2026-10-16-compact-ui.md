---
title: Kompaktowy UI CYD-DECK i rozdzielenie zapisu od synchronizacji
tags: [cyd-deck, winforms, lvgl, ui, state-sync, usb]
aliases: [Compact Bento UI, Default Screen, Offline Save, Brightness Revision]
updated: 2026-10-16
status: active
---

## Korekta użytkownika: zegar w doku

Pierwszy przycisk nawigacji ponownie pokazuje bieżący czas `HH:mm`, na wszystkich stronach. Pozostałe elementy to Deck / Media / PC / koło zębate. Szerokość zegara wynika z najszerszej cyfry fontu; test obejmuje wszystkie 1440 godzin/minut doby. Zmiana zastępuje poprzednie ustalenie etykiety Home. Pozostałe poprawki ikon, daty i układu pozostają aktualne.

# Kompaktowy interfejs CYD-DECK

## Aktualizacja: Home, dok i ikony skrótów

- Aktualny dok: **HH:mm (zegar bieżący) / Deck / Media / PC / koło zębate**. Szerokości wynikają z metryk fontu i poziomego paddingu 10 px; odstępy wynoszą 6 px. Etykiety nie używają wielokropków. Zegar w doku pozostaje przywrócony i widoczny na wszystkich stronach.
- Zegar Home: duża godzina i wyśrodkowana data w formacie `Fri, Oct 16`, pole 132 px i kolor `#A1A1AA` w ciemnym motywie.
- Skróty Home i Deck mają osobne etykiety tekstu i ikon. `cyd_icons_20` zawiera 21 symboli Font Awesome (w tym dodane `camera` i `mic`/`microphone`). Zachowano preferencje `icon` / `label` / `both`; tryb Label celowo ukrywa ikonę.
- Generator fontu: `scripts/icon-font-generator`, bitmapy 4 bpp, 3649 B. Generuje font C oraz JSON podglądów `shortcut-icons.json`. Licencja: `firmware/src/ui/fonts/FONT-AWESOME-LICENSE.txt`.
- Wizualna galeria ikon i import URL (Windows Agent):
  - Modalna galeria 21 ikon (`ShortcutIconPicker`) w siatce 5×5 z ciemnym motywem Apple HIG i podglądem na żywo.
  - Możliwość wklejenia bezpośredniego URL do obrazu (PNG, JPEG, WebP, GIF, BMP) o rozmiarze do 4096×4096 px i pobrania go (limit 2 MB, SkiaSharp). Obraz jest automatycznie skalowany z zachowaniem proporcji i przezroczystością do 24×24 pikseli w formacie `RGB565A8` (1728 B) i kodowany jako Base64URL bez paddingu (2304 znaki).
  - W razie błędu pobierania poprzednia ikona i URL są zachowywane.
  - Firmware wspiera wyświetlanie własnych miniatur 24×24 jako dzieci `lv_image` wewnątrz etykiet kafelków na Home i Deck z unieważnianiem cache według rewizji.
  - Persystencja NVS `shortcuts` v3 wspiera wsteczny odczyt v1/v2 i alokację sterty dla porównania zawartości bez przeciążania stosu.
- Windows: kompaktowa szerokość selektora profilu i przycisku zapisu; podgląd kafelka respektuje ten sam Display Mode co CYD. Zmiany pozostają w stanie draftu do momentu jawnego `Save` / `Save & Sync`.
- Protokół Serial JSON pozostaje `protocol:1`, z opcjonalnym polem `iconData` (Base64URL) mieszczącym się w limicie ramki 16 KB przy 6 ikonach.


## Nawigacja CYD Display

Default łączy zegar, kompaktowy odtwarzacz i cztery pierwsze skróty aktywnego profilu. Dolny dok mieści Home, Deck, Media i PC; oddzielona ikona koła zębatego prowadzi do diagnostyki. Zmiana nie zwiększa liczby slotów: dedykowana strona skrótów nadal ma sześć pozycji.

## Zapis i synchronizacja

Configuration Draft istnieje wyłącznie w Windows Agent. Przełączanie stron go zachowuje. Jeden Save zapisuje atomowo konfigurację lokalnie; Save & Sync dodatkowo próbuje wysłać ją do CYD. Wynik wysłania nie jest utożsamiany z fizycznym renderowaniem. Reconnect odtwarza ostatnią Saved Configuration.

Profil zawiera cały katalog skrótów. Pierwsze sześć wpisów przypisuje się do CYD, pierwsze cztery do Default. Kolejność można zmieniać przeciąganiem kafelków oraz przyciskami Move up / Move down. Więcej wpisów pozostaje w katalogu PC.

## Jasność lokalna i zapis offline

Lokalny suwak w ustawieniach CYD natychmiast zmienia jasność. `brightnessRevision` identyfikuje jawny zapis ustawień w Agencie: nowa rewizja resetuje lokalną korektę, ta sama przy reconnect ją zachowuje. Firmware przechowuje ostatnią rewizję w NVS. Flaga `applyBrightness` pozwala ponownie zastosować tę samą wartość przy aktywnym połączeniu.

## Walidacja protokołu

Rozszerzenie modelu C# nie wystarcza: `ProtocolSerializer.ValidateDeviceSettings` ma whitelistę właściwości. Trzeba aktualizować model, mapper i whitelistę jednocześnie oraz testować `Parse(CreateDeviceSettings(...)).IsSuccess`. W refaktoryzacji naprawiono nieuwzględnione `tileColorScheme`, `standbyStyle` i `shortcutsLayout`.

## Layout Windows i testowanie

Przy 1200×760/100% pełne karty Settings mieszczą się obok siebie bez przewijania strony. Przy ograniczonej przestrzeni przechodzą w podzakładki, a dłuższe formularze są dzielone na General/More. Adaptację trzeba odroczyć do zakończenia początkowego layoutu: tymczasowa zerowa szerokość kontenera nie może trwale włączyć widoku kompaktowego.

Minimum okna pozostaje fizycznie 900×620 po zmianie DPI. Regresje obejmują układ bez scrollowania, zachowanie draftu, zapis offline, duplikację profilu, telemetrię na żywo i stress nawigacji. Rendery zapisują się w `artifacts/ui-review`.

## Weryfikacja sprzętu

ST7789/COM5: zapis firmware zakończony weryfikacją hash. Agent po wdrożeniu raportował połączenie USB, wyrenderowaną okładkę i głośność 100%. Wygląd TFT i skuteczność fizycznego dotyku wymagają testu użytkownika.

## Powiązane dokumenty

- [[../ui-refactor-decisions|Ustalenia UI]]
- [[../adr/0002-explicit-save-and-offline-sync|ADR 0002]]
- [[../protocol|Protokół]]

## Historyczne wdrożenie: czas w doku i okładka jako tło

We wcześniejszym wdrożeniu pierwsza zakładka doku wyświetlała bieżący czas z Windows zamiast słowa Default. Aktualny dok używa Home, a data jest jednowierszowa. Zegar w samym Home Screen jest po prawej, odtwarzacz po lewej, a cztery skróty zajmują większą siatkę 2×2. Pełna geometria i kontrast: [[../ui-refactor-decisions#Doprecyzowanie Default — bieżący widok]].

`uiSetCoverImage` aktualizuje zarówno obraz Media, jak i tło Home Screen. Oba korzystają z tego samego bufora: nie należy ponownie pobierać ani dekodować okładki tylko z powodu przełączenia strony. Etykiety doku dla Home nie mogą nadpisywać tekstu zegara w końcowej pętli `uiRefresh`.

### Wdrożenie najnowszego Default

Wgrano na COM5, esptool potwierdził hash. Firmware: RAM statyczny 73472 B (22.4%), flash 1662157 B (81.8%). Test układu Default przechodzi; obejmuje aktualizację okładki podczas obecności wyłącznie widgetów Home, czas w doku na innych stronach, dwuwierszową datę i granice kafelków względem doku.

Odczyt po wdrożeniu: USB Connected, uptime 70 s, cover status 2 / rendered true, audio 100% / muted false, slowLoops 2 i wifiDiscoveryCount 1. Agent PID 7788 odpowiadał z oknem CYD-DECK i pozostał włączony. Dowód bez sekretów: `artifacts/home-verification.json`. Stan wizualny wymaga oceny na TFT, niezależnie od poprawnej telemetrii.
