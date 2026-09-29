# CYD hardware

Płytka bazowa: ESP32-2432S028R / Cheap Yellow Display, klasyczny ESP32 bez PSRAM. Moduł jest opisany jako ST7789 / ILI9341; sterownika nie wybieramy bez fizycznej obserwacji. Kod konfiguruje oba warianty.

## Pinout

| Funkcja | Sygnał | GPIO | Uwagi |
|---|---|---:|---|
| TFT | MISO | 12 | hardware SPI (HSPI / SPI2) |
| TFT | MOSI | 13 | |
| TFT | SCLK | 14 | |
| TFT | CS | 15 | |
| TFT | DC | 2 | |
| TFT | BL | 21 | aktywne HIGH, PWM |
| TFT | RST | -1 | nieużywany; nie podłączać GPIO4 jako reset bez potwierdzenia |
| XPT2046 | CLK | 25 | software SPI |
| XPT2046 | MOSI | 32 | |
| XPT2046 | MISO | 39 | input-only |
| XPT2046 | CS | 33 | |
| XPT2046 | IRQ | 36 | input-only |
| microSD | CS | 5 | osobny hardware SPI host VSPI / SPI3 |
| microSD | SCLK | 18 | |
| microSD | MISO | 19 | |
| microSD | MOSI | 23 | |
| RGB LED | R | 4 | |
| RGB LED | G | 16 | |
| RGB LED | B | 17 | |
| Speaker | — | 26 | |
| LDR | — | 34 | input-only |
| BOOT | — | 0 | |

GPIO36 i GPIO39 są input-only. TFT korzysta z hardware SPI host 2, SD z drugiego hosta VSPI, a XPT2046 z bit-bang SPI na własnych liniach. Nie zakładamy trzech sprzętowych hostów.

## Display HAL

Dwa środowiska PlatformIO, cyd28_ili9341 oraz cyd28_st7789, budują ten sam diagnostic app i zmieniają wyłącznie wybrany panel w LovyanGFX HAL. Landscape to 320×240. Nominalne offsety startują od zera i nie są fizycznie zweryfikowane. LovyanGFX daje lokalną konfigurację hardware w kodzie projektu i czysty wybór panelu; TFT_eSPI także potrafi sterować CYD, lecz jego setup jest zwykle wybierany przez nagłówki kompilacyjne/globalny User_Setup. Nie należy edytować globalnych plików bibliotek.

## Diagnostic firmware

W katalogu projektu uruchom scripts/setup-tools.ps1 przy pierwszej konfiguracji, potem scripts/build-firmware.ps1. Wrapper skraca ścieżkę roboczą automatycznym, tymczasowym subst i przekazuje wszystkie argumenty PlatformIO. Przykłady: scripts/build-firmware.ps1 run -e cyd28_ili9341; scripts/build-firmware.ps1 run -e cyd28_st7789. Monitor szeregowy: 115200 baud. Kalibracja przy pierwszym starcie rozpoczyna się automatycznie, jeśli NVS nie zawiera wcześniejszych współczynników. Można ją także uruchomić komendą serialową calibrate; komendy kończy się Enterem. Obsługiwane komendy: display, touch, calibrate, sd, light on, light off, light 0..100, help. Ekran i Serial pokazują wariant kompilacji, wymiary, touch raw, stan SD i heap. Wi-Fi celowo nie łączy się: status disconnected i RSSI N/A są oczekiwane; WebSocket/FPS dotyczą późniejszej fazy.

Display test cyklicznie pokazuje RED, GREEN, BLUE, WHITE, BLACK, gradient, tekst, obramowanie, prostokąt, koło i linię. Dotknij ekranu by zmienić kolor; przycisk BACK wraca.

Touch test wypisuje X/Y/Z w Serial. Przy pierwszym uruchomieniu kalibracja startuje od razu; później można ją uruchomić poleceniem Serial calibrate lub przyciskiem CALIBRATE. Dotknij i zwolnij kolejno pięć celów: lewy górny, prawy górny, lewy dolny, prawy dolny, środek. Trzy punkty wyznaczają pełne przekształcenie affine 2D, a czwarty i środkowy walidują błąd maksymalnie 30 px; zatwierdzona kalibracja trafia do NVS. Nieudana rekalibracja zachowuje poprzednią. GPIO36 IRQ jest wejściem bez pull-up/down wewnętrznego (pin ESP32 nie ma takich rezystorów). Przycisk LIGHT ON/OFF przełącza PWM GPIO21; sterowanie serialowe light N ustawia 0..100%, startowa jasność 86%, częstotliwość 5 kHz.

SD storage przy starcie mountuje kartę i tworzy katalogi /covers, /icons, /assets, /cache, /config i /logs. Brak karty albo błąd layoutu nie blokuje startu. Cover cache używa ścieżek /covers/<coverId>.jpg, indeksu /cache/covers.tsv i limitu 256 MB; przy zapisie akceptuje tylko bezpieczne ID, JPEG do 512 KB i commit przez plik tymczasowy w /cache. Uszkodzony pusty albo zbyt duży cover jest usuwany przy sprawdzeniu. SD test nadal zapisuje, odczytuje, weryfikuje i usuwa wyłącznie /.pcdock-hwdiag.tmp z własnym payloadem. Jeśli ta ścieżka już istnieje, test odmawia zapisu i niczego nie usuwa. Nic nie jest formatowane.

## Procedura fizyczna

1. Włóż kartę microSD z zachowanymi danymi, podłącz CYD przez USB.
2. Uruchom scripts/build-firmware.ps1 run -e cyd28_ili9341, następnie scripts/build-firmware.ps1 run -e cyd28_ili9341 -t upload. Otwórz scripts/build-firmware.ps1 device monitor -b 115200 lub monitor Serial w PlatformIO. Przekaż linię [BOOT] i [MEM].
3. Zapisz, czy widać kompletny obraz 320×240 landscape, oraz czytelność i brak przesunięć.
4. Dotknij lewego górnego, prawego dolnego i środka; przekaż linie [TOUCH] raw. Uruchom CALIBRATE, dotknij pięciu krzyżyków w wyświetlonej kolejności, po czym sprawdź narożniki i środek. Zrestartuj i sprawdź, czy NVS zachowała kalibrację.
5. Uruchom DISPLAY TEST. Zanotuj wygląd RED/GREEN/BLUE/WHITE/BLACK, gradient, tekst, prostokąt/koło i obramowanie krawędzi.
6. Uruchom SD TEST i przekaż linię [SD]; oczekuj write/read/delete OK oraz PASS. Karta nie jest formatowana; plik testowy znika po teście.
7. Przełącz LIGHT OFF/ON i zanotuj, czy podświetlenie gaśnie/wraca.
8. Zostaw uruchomione 10 minut. Wklej kilka linii [MEM], zanotuj restarty i zachowanie heap.
9. Wgraj także wariant alternatywny: scripts/build-firmware.ps1 run -e cyd28_st7789, scripts/build-firmware.ps1 run -e cyd28_st7789 -t upload; powtórz kroki 2–8 i przekaż logi oraz opis obu widoków. Nie wybieraj docelowego sterownika bez wyników fizycznych obu wariantów.

Przypadki matematyczne kalibracji (normalne, zamienione i odwrócone osie oraz punkty współliniowe) sprawdzisz bez płytki przez node --test tests/affine.test.mjs.

Kompilacja nie potwierdza stabilnego bootu, kolorów, orientacji, touch, PWM ani działania czytnika SD. Te kryteria wymagają procedury fizycznej.
