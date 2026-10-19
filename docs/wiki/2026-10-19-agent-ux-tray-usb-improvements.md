---
title: Windows Agent — integracja zasobnika, spójność dialogów i odporność USB
tags: [cyd-deck, windows-agent, tray, media, usb, winforms, dark-mode, bugfix]
aliases: [Tray Media Controls, DWM Immersive Dark Dialogs, USB Multi-Port Fault Tolerance, Hotkey Clear]
updated: 2026-10-19
status: active
---

# Udoskonalenia Windows Agent (Agy) — 2026-10-19

W ramach niniejszej sesji inżynierskiej zaimplementowano trzy spójne ulepszenia Windows Agenta, skupiając się na stabilności, ergonomii użytkownika (UX) oraz bezbłędnej komunikacji ze sprzętem CYD bez wprowadzania niekompatybilnych zmian w protokole.

---

## 1. Integracja z zasobnikiem systemowym (Tray) i szybkie sterowanie multimediami

### Problem i motywacja
- Ikona w zasobniku systemowym posiadała nieaktualny tooltip z przestarzałą nazwą (`PC Dock - ...`), a menu kontekstowe nie udostępniało szybkiego podglądu ani sterowania aktualnie odtwarzanymi mediami i głośnością bez konieczności otwierania głównego okna.
- Użytkownik nie otrzymywał systemowych powiadomień balonowych (BalloonTip) o zmianach stanu połączenia z fizycznym urządzeniem CYD (np. przy rozłączeniu kabla USB).

### Zrealizowane rozwiązanie
- **Plik**: `agent/src/WindowsAgent/StreamDeck.WindowsAgent/Tray/WindowsAgentApplicationContext.cs`
- Zaktualizowano branding tooltipa zasobnika: `CYD-DECK ({DeviceName}) - {Status}`.
- Dodano dynamiczną sekcję multimedialną do menu kontekstowego zasobnika:
  - Aktualny tytuł i wykonawca: `🎵 {Title} · {Artist}` (z obsługą skracania długich tytułów). Kliknięcie pozycji natychmiast aktywuje okno aplikacji i przełącza widok na stronę `Media`.
  - Kontrolka odtwarzania: `▶ Play` / `⏸ Pause` (stan adaptacyjny do bieżącego odtwarzania).
  - Przełączanie utworów: `⏮ Previous Track` oraz `⏭ Next Track`.
  - Wyciszanie i wskaźnik poziomu głośności: `🔊 Mute ({vol}%)` / `🔇 Unmute ({vol}%)`.
  - Bezpieczne wywołania asynchroniczne (`SafeExecuteMediaCommandAsync`, `SafeToggleMuteAsync`) zabezpieczone przed wyjątkami w wątku UI i logujące ewentualne błędy diagnostyczne.
- Dodano powiadomienia balonowe zasobnika (`ShowBalloonTip`) przy podłączeniu (`{DeviceName} connected via USB/Wi-Fi`) i odłączeniu (`Device disconnected`), z automatyczną supresją podczas pierwszego uruchomienia aplikacji.

---

## 2. Spójność wizualna, DWM Immersive Dark Titlebar i czyszczenie skrótów klawiszowych

### Problem i motywacja
- Okna dialogowe edycji skrótów (`ShortcutEditorDialog`) oraz wyboru ikon (`ShortcutIconPicker`) miały na stałe zaszyte ciemne kolory i nie reagowały na motyw jasny (`clientLightTheme`).
- W systemach Windows 10/11 pasek tytułowy okien dialogowych pozostawał standardowy jasny, tworząc niespójność estetyczną przy ciemnym motywie.
- W edytorze skrótu klawiszowego (typ `Send hotkey`) brakowało możliwości łatwego wyczyszczenia zarejestrowanej kombinacji (pozycja 207 z `TODO.txt`).

### Zrealizowane rozwiązanie
- **Pliki**:
  - `agent/src/WindowsAgent/StreamDeck.WindowsAgent/Shell/SettingsWindow.cs`
  - `agent/src/WindowsAgent/StreamDeck.WindowsAgent/Shell/ShortcutEditorDialog.cs`
  - `agent/src/WindowsAgent/StreamDeck.WindowsAgent/Shell/ShortcutIconPicker.cs`
- Wprowadzono obsługę Windows DWM Immersive Dark Mode (`DwmSetWindowAttribute(Handle, 20, ...)` dla paska tytułowego oraz atrybut 33 dla zaokrąglonych rogów w Windows 11).
- Zaimplementowano pełne dostosowanie kolorystyki dialogów do aktualnego motywu jasnego/ciemnego (tło, teksty, obramowania, pola wprowadzania, przyciski).
- Dodano przycisk `Clear` (`clearHotkeyButton` z etykietą dostępności `Clear hotkey combination`) obok pola docelowego kombinacji klawiszy przy wybranym typie `Send hotkey`. Metoda `ClearHotkey()` czyści bufor klawiszy i natychmiast odświeża podgląd.
- Zapewniono pełną nawigację klawiaturą, skróty Enter (Save) / Escape (Cancel) oraz precyzyjne AccessibleName dla technologii asystujących.

---

## 3. Odporność na błędy portów USB i rozłączenie przewodu (CYD Communication)

### Problem i motywacja
- Podczas automatycznego wykrywania portów USB (`UsbDeviceRuntime.RunReconnectLoopAsync`), jeśli którykolwiek z portów kandydatów był zajęty przez inny program (np. `UnauthorizedAccessException` na porcie COM3 innego urządzenia), cała pętla wykrywania rzucała wyjątek, blokując sprawdzenie kolejnych kandydatów (np. COM5 z podłączonym CYD).
- W strumieniu `SerialPortUsbDuplexStream` nagłe odłączenie fizycznego przewodu USB podczas odczytu lub zapisu mogło skutkować niekontrolowanymi wyjątkami `InvalidOperationException` / `IOException` / `UnauthorizedAccessException`.

### Zrealizowane rozwiązanie
- **Pliki**:
  - `agent/src/WindowsAgent/StreamDeck.WindowsAgent/Runtime/UsbDeviceRuntime.cs`
  - `agent/src/WindowsAgent/StreamDeck.WindowsAgent/Runtime/SerialPortUsbDuplexStream.cs`
- W `UsbDeviceRuntime` otoczono próbkowanie każdego pojedynczego portu kandydata blokiem `try-catch`, zapisując powód niepowodzenia do logu diagnostycznego (`AgentDiagnosticLog.Write($"USB port {port} probe failed: ...")`) i płynnie przechodząc do kolejnego dostępnego portu.
- W `SerialPortUsbDuplexStream`:
  - W pętli `ReadAsync` przechwycono błędy rozłączenia (`InvalidOperationException`, `IOException`, `UnauthorizedAccessException`) i obsłużono je jako czyste zakończenie strumienia (zwrócenie `0` bajtów / EOF), co pozwala sesji protokołu na eleganckie zamknięcie i natychmiastowe przejście do procedury reconnectu.
  - W `WriteAsync` dodano precyzyjną weryfikację `serialPort.IsOpen` oraz bezpieczne opakowanie błędów zapisu z diagnostyką portu.
- Protokół komunikacyjny pozostał w 100% niezmieniony (`protocol: 1`), co gwarantuje pełną kompatybilność z firmware Nixie na mikrokontrolerze ESP32.

---

## Wyniki weryfikacji i testów automatycznych

1. **Test jednostkowy odporności USB na błędy otwarcia portu**:
   - Dodano test `TestUsbRuntimeAdvancesFromPortOpenFailureToValidEsp32HelloAsync` w `agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests/Program.cs`.
   - Test symuluje rzucenie `UnauthorizedAccessException` na COM3 i weryfikuje bezbłędne przejście do COM5 oraz nawiązanie sesji parowania.
2. **Test przycisku czyszczenia skrótu**:
   - Weryfikacja działania `ClearHotkey()` i wyczyszczenia wartości w teście headless w `StreamDeck.WindowsAgent.Tests`.
3. **Kompletny zestaw testów automatycznych**:
   - `StreamDeck.WindowsAgent.Tests`: **PASS** (wszystkie smoke testy: widoki, dialogi, kontrolery, WebSocket, USB runtime).
   - `StreamDeck.Protocol.Tests`: **PASS** (80/80 testów zaliczonych).
   - `StreamDeck.Shortcuts.Tests`: **PASS** (22/22 testów zaliczonych).
   - `StreamDeck.Configuration.Tests`: **PASS** (8/8 testów zaliczonych).
   - `StreamDeck.Audio.Tests`: **PASS** (6/6 testów zaliczonych).
   - `StreamDeck.Covers.Tests`: **PASS** (12/12 testów zaliczonych).
   - `StreamDeck.Host.Tests`: **PASS** (38/38 testów zaliczonych).
   - `StreamDeck.Media.Tests`: **PASS** (12/12 testów zaliczonych).
   - `SystemMonitoring.Tests`: **PASS** (5/5 testów zaliczonych).
   - Kompilacja projektu: **0 błędów, 0 ostrzeżeń**.

---

## Pozostałe priorytety w backlogu

1. **Automatyczna kopia zapasowa konfiguracji** (`TODO.txt` sekcja 3): Zapis pliku `.bak` przed atomową podmianą konfiguracji.
2. **Nagłówki kolumn ListView w Dark Theme**: Dostosowanie rysowania nagłówków ListView w `SettingsWindow` dla eliminacji jasnej ramki Windows w ciemnym motywie.
3. **Pasek postępu i dialog aktualizacji OTA**: Lepsza wizualizacja pobierania i wgrywania firmware przez sieć w zakładce Updates.
