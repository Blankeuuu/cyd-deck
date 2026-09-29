# Stream Deck / PC Dock

Monorepo istniejącego docka PC opartego na ESP32-2432S028R (CYD) oraz Windows Agent. Projekt obejmuje firmware LVGL, komunikację WebSocket/HTTP, konfigurację w Windows i diagnostykę urządzenia.

Dokumentacja: [architektura](docs/architecture.md), [UI](docs/ui.md), [protokół](docs/protocol.md), [konfiguracja i recovery](docs/configuration.md), [hardware](docs/hardware.md), [troubleshooting](docs/troubleshooting.md).

## Wymagania

Windows PowerShell, Python 3 z launcherem py i USB-UART driver dla płytki. PlatformIO, framework ESP32 i LovyanGFX instalują się lokalnie w .tools/; globalny Arduino/TFT_eSPI setup nie jest modyfikowany.

Pierwsza instalacja lokalnych narzędzi:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-tools.ps1
```

## Firmware

Z katalogu projektu:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1
```

Skrypt tworzy tymczasowe mapowanie wolnej litery dysku, by skrócić ścieżki kompilatora Windows, po czym przywraca katalog roboczy i zmienną PLATFORMIO_CORE_DIR oraz usuwa tylko własne mapowanie. Argumenty PlatformIO można przekazać dalej, np.:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_ili9341
```

Wgrywanie i monitor:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_ili9341 -t upload
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 device monitor -b 115200
```

Sprawdź oba warianty wyświetlacza i przekaż obserwacje z testu fizycznego; nie zakładamy kontrolera na podstawie nazwy płytki. Pełna procedura znajduje się w [docs/hardware.md](docs/hardware.md).

## Symulacja Wokwi

Uruchom `powershell -ExecutionPolicy Bypass -File scripts/test-wokwi.ps1`, aby zbudować podgląd firmware, przygotować pełny obraz flash i sprawdzić start UI w Wokwi. Wymaga zainstalowanego Wokwi CLI i zmiennej środowiskowej `WOKWI_CLI_TOKEN` z Wokwi CI; tokenu nie zapisuj w repozytorium. Symulator używa ESP32 DevKit i modelu ILI9341: sprawdza boot oraz renderowanie UI, ale nie emuluje dotyku XPT2046, microSD, rzeczywistego panelu ST7789 ani testów fizycznych.

## Testy hostowe

Czysty affine helper używany przez firmware ma hostowe testy C++. Opcjonalny Zig host compiler instaluje się przez:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-tools.ps1 -IncludeHostTestCompiler
powershell -ExecutionPolicy Bypass -File scripts/test-affine.ps1
```

Testy pokrywają normalne, zamienione i odwrócone osie, przypadki zdegenerowane, walidację czwartego/piątego punktu i zachowanie poprzedniej kalibracji po błędzie.

## Windows Agent i aplikacja

Wymaga .NET SDK 10. Build: `dotnet build agent/src/WindowsAgent/StreamDeck.WindowsAgent/StreamDeck.WindowsAgent.csproj`. Agent działa w trayu, a centralne Settings Window zawiera Overview, Shortcuts, Appearance, Device, Updates i Logs. Zmiany wyglądu są synchronizowane z autoryzowanym CYD; pełny OTA workflow i weryfikacja na hardware wymagają osobnych testów.

Więcej informacji: [docs/configuration.md](docs/configuration.md) oraz [docs/protocol.md](docs/protocol.md).
