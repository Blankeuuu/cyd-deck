---
title: Audyt firmware Nixie — odporność transferów i reconnect
tags: [cyd-deck, firmware, reliability, networking, ota, memory, touchscreen]
aliases: [Nixie firmware audit, cover transfer reliability, millis rollover, niezawodność firmware]
updated: 2026-10-19
status: active
---

# Zakres i źródła

Audyt i trzy kolejne poprawki wykonano w worktree `nixie`. Audyt zakończono bez flashowania; późniejsze wgranie na wyraźne polecenie użytkownika opisano poniżej. Użytkownik zaakceptował końcowy firmware 2026-10-19 i polecił publikację zmian na `main`.

Wiedzę projektu pobrano przez Obsidian MCP: wyszukiwanie semantyczne po polsku i angielsku, notatki `wiki/projects/stream-deck.md` oraz `projects/stream-deck/docs/wiki/2026-10-15-usb-responsiveness.md`. Historyczne podsumowania skonfrontowano z bieżącym kodem, [[../protocol]], [[../HANDOFF]] i słownikiem `GLOSSARY.md`. Zachowano decyzje: brak PSRAM, jedna dekodowana okładka, priorytet USB, lokalny cache SD i envelope `protocol: 1`.

# Trzy wdrożone poprawki

## 1. Izolacja transferów USB według bieżącej okładki

W `agent_client.cpp` fragment starej okładki mógł otworzyć transfer pliku, a następnie konflikt identyfikatora lub offsetu oznaczał **aktualnie żądaną** okładkę jako `Failed`. Dotyczyło to również błędnego/spóźnionego `cover.complete`.

`acceptsCoverTransfer` dopuszcza wyłącznie poprawny identyfikator bieżącego żądania w stanie `Pending`. Kontrola następuje przed dekodowaniem Base64 i operacjami SD. Zmiana lub wyczyszczenie okładki zamyka i usuwa poprzedni plik tymczasowy. Utrata autoryzowanej sesji USB także zwalnia zasoby transferu. Ramki starej okładki oraz duplikaty po sukcesie lub timeout nie zmieniają aktualnego stanu.

Testy hostowe obejmują zmianę A → B, brak/błędny identyfikator, timeout, sukces i wyczyszczenie metadanych. Sprawdzają bramkę stanu używaną przez oba handlery; nie zastępują próby z UART i rzeczywistą kartą SD.

## 2. Poprawne zliczanie bajtów HTTP → SD

`SdStorage::appendCoverTemp` zwiększa przekazany przez referencję licznik. Klient HTTP ponownie zwiększał ten sam licznik przez `accountBoundedBytes`, przez co kończył odbiór przedwcześnie albo odrzucał zapisany plik.

`appendCountedCoverChunk` sprawdza granice przed zapisem i wymaga dokładnie jednej aktualizacji licznika przez warstwę SD. Nie zwiększa go drugi raz. Test strumienia 1025 bajtów w fragmentach 512 + 512 + 1 odtworzył poprzednie podwójne zliczanie jako RED (`received != fileBytes`), a następnie przeszedł po poprawce. Testy obejmują również nadmiar danych, zerowy fragment, niespójny licznik, limit pliku i błąd zapisu. Zapis SD jest modelowany callbackiem; test nie uruchamia HTTP ani systemu plików ESP32.

## 3. Reconnect i discovery odporne na przepełnienie zegara

Bezpośrednie porównania `now < nextAttemptAt` i `now >= nextMdnsAt` nie zachowywały interwałów w pobliżu przepełnienia `millis()` po około 49,7 dnia. Mogły powodować przedwczesne próby, częste blokujące discovery lub długie odroczenie reconnect.

`RetryTimer` porównuje upływ czasu przez odejmowanie `uint32_t`. Obsługuje natychmiastową pierwszą próbę i reset po zakończeniu priorytetu USB bez używania zera jako specjalnego terminu. Zastosowano go do obu harmonogramów. Testy sprawdzają interwały 2000, 10000 i 30000 ms, granicę terminu, przepełnienie i termin wypadający dokładnie na zero.

# Zgodność z Windows Agent

Nie zmieniono typów wiadomości, payloadów, capabilities, sekwencji handshake ani wersji protokołu. Nie jest potrzebna zmiana Agenta ani koordynacja breaking change z Agy. Ewentualne przyszłe rozszerzenia protokołu wymagają wcześniejszego uzgodnienia z Agy.

# Weryfikacja

- Po poprawce 1: test protokołu sieciowego, pełne `scripts/test-ui-logic.ps1` i build `cyd28_st7789_app` — PASS.
- Po poprawce 2: regresja RED → GREEN i build `cyd28_st7789_app` — PASS.
- Po poprawce 3: pełne `scripts/test-ui-logic.ps1`, test protokołu z `-Wall -Wextra -Werror`, `scripts/test-affine.ps1`, `scripts/test-sd-storage.ps1` i build `cyd28_st7789_app` — PASS.
- Końcowy ST7789: RAM statyczny 84096 B, flash aplikacji 1679885 B / 2031616 B. To raport linkera, nie pomiar wolnej sterty w runtime.
- Build końcowy `cyd28_ili9341_app` — PASS; RAM statyczny 84096 B, flash aplikacji 1679925 B. Oba końcowe buildy i kompilacje testów nie zgłosiły ostrzeżeń.
- Podczas audytu nie wykonywano testów fizycznego dotyku, UART, sieci, SD i OTA ani flashowania. Późniejsza kontrola po wgraniu jest opisana poniżej; nie zastępuje oceny dotyku i wyglądu przez użytkownika.

Logi robocze znajdują się lokalnie w ignorowanym `.tools/`: `build-cover-isolation.log`, `build-http-counter.log`, `build-retry-timer.log`, `build-ili9341-final.log`, `host-final.log`, `affine-final.log`, `sd-final.log`, `nixie-http-red-result.log`.

# Wgranie po zgodzie użytkownika

Na polecenie „zflashuj sprzet ja ocenie” wgrano `cyd28_st7789_app` na COM5. PlatformIO zakończyło upload `SUCCESS`, potwierdziło hash zapisanych danych i zrestartowało ESP32 przez RTS. Log: `.tools/nixie-flash.log`. Nie kasowano NVS ani parowania.

Agenta Windows zatrzymano wyłącznie na czas operacji i ponownie uruchomiono z tego samego pliku `artifacts/desktop-layout-fix-20261017/StreamDeck.WindowsAgent.exe` w głównym checkout. Kontrola lokalnego `/api/status` o 16:31:24 czasu polskiego: `connected`, `CYD-40C86C`, transport `USB`, uptime 26 s, `sd.ready=true`, `cover.status=2`, `cover.rendered=true`. Agent pozostał uruchomiony. Wizualna ocena ekranu i dotyku należy do użytkownika; nie deklarowano ich fizycznej akceptacji.

# Pozostałe priorytety

Końcowy wygląd i działanie zostały zaakceptowane przez użytkownika odpowiedzią „jest super” oraz poleceniem uzupełnienia wiki i pushu na `main`. Obejmuje to późniejsze [[2026-10-19-cyd-settings-overhaul]] i [[2026-10-19-pc-media-clarity]]. Akceptacja nie oznacza wykonania długotrwałych prób awaryjnych sieci/SD/OTA opisanych poniżej.

1. **Responsywność sieci:** `maybeFetchCover` nadal synchronicznie łączy TCP, czyta nagłówki i cały plik; timeout pojedynczej operacji wynosi 2500 ms. `MDNS.queryService` i połączenie WebSocket także pracują na wątku UI. Potrzebny odbiór etapami z limitem bajtów i czasu oraz wspólnym terminem dla nagłówków, a następnie pomiar `loopMaxUs/networkMaxUs` na sprzęcie.
2. **OTA i informacja zwrotna:** przypięty ArduinoOTA wywołuje blokujące `_runUpdate` z `handle`. Callbacki zmieniają `state.ota`, ale nie uruchamiają renderowania; użytkownik może nie widzieć postępu w trakcie transferu. Dodać kontrolowane odświeżanie UI i zweryfikować poprawną aktualizację, zły obraz, przerwanie połączenia i ponowienie. Próby flash/OTA wymagają zgody użytkownika.
3. **Pamięć LVGL:** `lvgl_port.cpp` alokuje bufor przez `sizeof(lv_color_t)` (3 bajty w przypiętym LVGL 9), choć wyświetlacz renderuje RGB565 (2 bajty): 9600 B zamiast 6400 B dla deklarowanych 320 × 10 pikseli. Jawne użycie rozmiaru RGB565 pozwoli odzyskać 3200 B pamięci wewnętrznej/DMA, ale zmniejszy rzeczywistą partię renderowania z 15 do 10 linii; wymaga pomiaru responsywności i sprawdzenia kolorów oraz flush na obu panelach.
4. **Dotyk:** odczyt XPT2046 uśrednia pięć próbek i używa jednego progu nacisku. Zmierzyć fałszywe zwolnienia dotyku i drgania współrzędnych podczas przeciągania; dopiero na podstawie próbek dobrać medianę i histerezę. Nie zmieniać progów bez danych z panelu.
5. **Pozostałe ścieżki cover/network:** port odkryty przez mDNS służy WebSocket, lecz fetch HTTP używa `config_.port`. Po niepowodzeniu tej samej okładki `updateCoverFetch` pozostawia `Unchanged`, więc brak kontrolowanego retry dla identycznego identyfikatora. `hasCover` zapisuje indeks przy odczycie, a fetch HTTP może sprawdzać cache ponownie przed rendererem; ograniczyć zapisy SD po pomiarze.
6. **Dokumentacja architektury:** `docs/architecture.md` nadal opisuje etap Hardware Diagnostic Mode, mimo obecnej implementacji LVGL i Agenta. Zaktualizować ją przed następną większą przebudową.
