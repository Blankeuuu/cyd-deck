# CYD-DECK

<div align="center">

![Platform](https://img.shields.io/badge/Platform-ESP32--2432S028R-blue?style=flat-square)
![Firmware](https://img.shields.io/badge/Firmware-v1.3.0-success?style=flat-square)
![Windows Agent](https://img.shields.io/badge/Windows%20Agent-.NET%2010%20x64-512bd4?style=flat-square)
![UI](https://img.shields.io/badge/Graphics-LVGL%209.3-orange?style=flat-square)
![Web Serial](https://img.shields.io/badge/Web%20Flasher-Ready-teal?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

**Nowoczesny, dotykowy dock biurkowy oparty na płytce ESP32 „Cheap Yellow Display” (CYD) oraz aplikacji Windows Agent.**

[⚡ Instalator przeglądarkowy (Web Installer)](#-szybka-instalacja-firmware-przeglądarka) • [💻 Instalator Windows](#-instalacja-windows-agent) • [📖 Dokumentacja](#-architektura-i-dokumentacja) • [🚀 Zmiany w 1.3.0](docs/releases/1.3.0.md)

</div>

---

## 🌟 O projekcie

**CYD-DECK** przekształca tanią i popularną płytkę **ESP32-2432S028R** (ekran dotykowy 2.8" 320×240) w zaawansowaną stację kontrolną PC. Projekt integruje sterowanie multimediami, monitorowanie podzespołów komputera, uruchamianie skrótów klawiszowych, skryptów i aplikacji, oferując spójny interfejs graficzny inspirowany zasadami Apple HIG i Windows 11 Fluent Design.

Komunikacja bazuje na priorytetowym połączeniu **USB-Serial** z automatycznym fallbackiem na **Wi-Fi / WebSocket**, gwarantując zerowe opóźnienia i brak podatności na zakłócenia sieci.

---

## ✨ Główne możliwości

### 🖥️ Ekran urządzenia (CYD ESP32)
- **Ekran domowy (Home)**: Zegar w doku `HH:mm`, dynamiczny format daty, miniaturowy odtwarzacz z okładką albumu w tle oraz 4 powiększone kafelki szybkiego dostępu (148×44 px).
- **Strona skrótów (Deck)**: Elastyczne układy siatek (1, 2, 4 lub 6 kafelków), symetrycznie wyśrodkowane ikony i etykiety, biblioteka 21 wbudowanych wektorowych glifów oraz obsługa własnych grafik 24×24 px dekodowanych z URL.
- **Odtwarzacz multimedialny (Media)**: Podgląd tytułu, wykonawcy i albumu, pasek postępu odtwarzania z lokalną interpolacją czasu (odświeżanie co 100 ms), sterowanie odtwarzaniem i głośnością oraz okładki 128×128 zapisywane na karcie microSD.
- **Monitor zasobów (PC Stats)**: Wykresy w czasie rzeczywistym dla obciążenia CPU, pamięci RAM oraz silników graficznych GPU (agregacja PDH dla układów zintegrowanych i dedykowanych).
- **Zintegrowane ustawienia**: Kalibracja dotyku, regulacja jasności, wygaszanie ekranu oraz diagnostyka połączenia.

### 🪟 Windows Agent (Aplikacja PC)
- **Integracja z zasobnikiem (Tray)**: Nowoczesne menu podręczne z bezpośrednim sterowaniem multimediami (Play/Pause, utwory, suwak głośności, Mute) oraz powiadomieniami balonowymi o podłączeniu USB.
- **Windows 11 DWM Dark Mode**: Spójny ciemny pasek tytułowy okien dialogowych i zaokrąglone narożniki zgodne z systemem.
- **Nowoczesny Color Picker**: Zaawansowany selektor barw z podglądem na żywo, wprowadzaniem kodów HEX i paletą predefiniowanych akcentów.
- **Odporność USB**: Automatyczne wykrywanie i obsługa wielu portów COM, bezpieczna izolacja zajętych portów oraz obsługa odłączenia przewodu bez błędów w aplikacji.
- **Atomowy zapis**: Bezpieczny zapis konfiguracji i profili offline bez ryzyka utraty stanu urządzenia.

---

## ⚡ Szybka instalacja Firmware (Przeglądarka)

Najprostszym sposobem wgrania oprogramowania na płytkę CYD jest użycie **instalatora w przeglądarce** (wymaga Google Chrome, Microsoft Edge lub Brave):

1. Podłącz płytkę CYD kablem USB do komputera.
2. Wejdź do katalogu projektu i uruchom:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/launch-web-flasher.ps1
   ```
3. W otwartej karcie przeglądarki kliknij **⚡ Zainstaluj CYD-DECK**, wskaż port COM urządzenia i zatwierdź.
4. Instalator wgra bootloader, partycje oraz firmware w około 30 sekund!

> Alternatywnie możesz użyć narzędzia `esptool`:
> ```powershell
> esptool.py --chip esp32 --port COM5 write_flash 0x10000 artifacts/release-v1.3.0/firmware-ST7789-1.3.0.bin
> ```

---

## 💻 Instalacja Windows Agent

Wybierz jedną z dwóch wygodnych metod:

### 1. Instalator automatyczny (Zalecany)
Pobierz **`CYD-DECK-Setup-1.3.0-win-x64.exe`** z [Wydania GitHub](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.3.0).
- Instalacja per-user (nie wymaga uprawnień administratora).
- Zawiera zintegrowany runtime .NET 10 i biblioteki SkiaSharp.
- Tworzy wpisy w menu Start oraz opcjonalny skrót na pulpicie.
- **Czyste i bezpieczne odinstalowanie**: Aplikacja rejestruje się w systemowym aplecie *Aplikacje i funkcje* (Windows Settings), a deinstalator automatycznie zamyka pracujący proces w zasobniku i czyści rejestr autostartu.

### 2. Paczka Portable
Pobierz **`StreamDeck.WindowsAgent-win-x64.zip`**, wypakuj do dowolnego folderu i uruchom `StreamDeck.WindowsAgent.exe`.

---

## 🛠️ Budowanie ze źródeł

### Wymagania
- Windows 10/11 x64, PowerShell 5.1+.
- .NET 10 SDK.
- PlatformIO (zarządzane lokalnie w `.tools/`).

### Kompilacja Agenta Windows
```powershell
dotnet build agent/src/WindowsAgent/StreamDeck.WindowsAgent/StreamDeck.WindowsAgent.csproj
```

### Kompilacja Firmware ESP32
```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app
```

### Uruchomienie testów
```powershell
# Testy natywne C++ firmware i layoutu
powershell -ExecutionPolicy Bypass -File scripts/test-ui-logic.ps1

# Testy integracyjne i jednostkowe Windows Agenta
dotnet run --project agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests/StreamDeck.WindowsAgent.Tests.csproj
```

---

## 📖 Architektura i dokumentacja

- [Architektura systemu](docs/architecture.md)
- [Protokół komunikacji Agent ↔ CYD](docs/protocol.md)
- [Specyfikacja interfejsu UI](docs/ui.md)
- [Opis sprzętu i wyświetlaczy (ST7789 vs ILI9341)](docs/hardware.md)
- [Rozwiązywanie problemów (Troubleshooting)](docs/troubleshooting.md)
- [Instrukcje i historia wydań](docs/releases/)

---

## 📋 Historia wydań

- **[Wydanie 1.3.0](docs/releases/1.3.0.md)** (Bieżące) — Przeglądarkowy instalator Web Serial, integracja zasobnika systemowego z kontrolkami multimediów, DWM Immersive Dark Mode, nowoczesny selektor kolorów, odporność wieloportowa USB i metryki sprzętowe PC.
- **[Wydanie 1.2.0](docs/releases/1.2.0.md)** — Wyśrodkowane etykiety i ikony, wizualna galeria 21 ikon, dekodowanie grafik URL do RGB565A8, instalator Windows.
- **[Wydanie 1.1.0](docs/releases/1.1.0.md)** — Pamięć podręczna SD dla okładek, interpolacja osi czasu 100 ms, odświeżony dok nawigacyjny z zegarem HH:mm.

---

## 📄 Licencja

Projekt udostępniany jest na warunkach licencji [MIT](LICENSE).
Wykorzystane biblioteki zewnętrzne oraz licencje komponentów runtime opisano w pliku `THIRD-PARTY-NOTICES.txt`.
