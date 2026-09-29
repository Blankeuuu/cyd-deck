# Troubleshooting

## pio unavailable
Zainstaluj PlatformIO Core i uruchom build z katalogu firmware. Pierwszy build pobiera platformę ESP32 i LovyanGFX.

## Brak obrazu lub błędne kolory
Zapisz log bootu oraz obserwacje testu ekranu, a potem spróbuj drugiego środowiska. Zanotuj offset, ucięte krawędzie, orientację i kolejność kolorów.

## Touch
Surowe X/Y/Z są widoczne w ekranie TOUCH TEST i Serial 115200. Kalibracja pięcioma punktami zapisuje zakres osi w NVS. Jeśli wartości nie reagują, sprawdź połączenie i logi przed zmianą pinów.

## microSD

Test nie formatuje karty. Jeśli mount się nie powiedzie, zachowaj log [SD]. Istniejący plik /.pcdock-hwdiag.tmp powoduje bezpieczne pominięcie zapisu.

Przy poprawnym mount firmware tworzy /covers, /icons, /assets, /cache, /config i /logs. Jeśli któryś z tych pathów istnieje jako zwykły plik, layout SD jest traktowany jako niedostępny, ale urządzenie nadal startuje. Cache okładek zapisuje najpierw plik tymczasowy w /cache, a dopiero potem przenosi go do /covers. Pusty, zbyt duży albo nieprawidłowo zapisany cover jest odrzucany lub usuwany przy następnym sprawdzeniu.

## OTA

Firmware aplikacji używa własnej tablicy partycji `firmware/partitions/streamdeck_ota.csv` z dwoma slotami aplikacji po `0x1F0000`. Offset NVS pozostaje `0x9000`, więc zwykły flash bez kasowania całej pamięci zachowuje zapisane ustawienia. Zmiana tablicy partycji wymaga jednak pierwszego flasha przez USB, bo obecnie działający firmware na układzie `huge_app.csv` nie ma drugiego slotu aplikacji do bezpiecznego OTA.

OTA startuje dopiero po zakończonym onboardingu i po połączeniu z Wi-Fi. Usługa nasłuchuje na porcie ArduinoOTA `3232`, nie reklamuje osobnego mDNS i wymaga hasła setupu pokazywanego podczas pierwszego onboardingu. Status jest widoczny na stronie `PC STATUS` oraz w Serial jako linie `[OTA]`.

Pierwsza weryfikacja po wymaganym flashu USB:

1. Zbuduj aplikację: `scripts/build-firmware.ps1 run -e cyd28_st7789_app`.
2. Wgraj raz po USB, bez kasowania całego flasha: `scripts/build-firmware.ps1 run -e cyd28_st7789_app -t upload`. Jeśli auto-reset CH340 nadal nie wejdzie w bootloader, ten krok wymaga fizycznego BOOT/RST i nie da się go zastąpić OTA ze starej partycji.
3. Po restarcie sprawdź, że urządzenie pomija onboarding, łączy się z Wi-Fi i na `PC STATUS` pokazuje `OTA ready`.
4. Ustal IP CYD z routera, DHCP albo Serial.
5. Wgraj ten sam build przez OTA: `scripts/upload-firmware-ota.ps1 -DeviceIp <CYD_IP> -SetupPassword <setup-password>`.
6. Oczekuj progresu `[OTA]`, automatycznego rebootu, ponownego połączenia Wi-Fi i powrotu statusu `OTA ready`.

Jeśli upload OTA zgłasza błąd autoryzacji, użyto złego hasła setupu. Skrypt nie zapisuje hasła; przekazuje je tylko do `espota.py` jako parametr `-a` dla bieżącego uruchomienia.
