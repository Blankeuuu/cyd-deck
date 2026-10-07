# Audyt wymagań

Źródło: `../prompt.txt`. Lista zachowuje pełny zakres specyfikacji. Status „niezweryfikowane” nie oznacza ukończenia. Wyniki fizyczne wymagają testów na płytce; build nie potwierdza zachowania sprzętu. Funkcje przyszłe z sekcji 75 są świadomie poza obecną implementacją.

## Release blockers przed publikacją

Projekt nie jest jeszcze potwierdzony jako publish-ready. Blokery wymagające bezpośredniej weryfikacji przed wydaniem:

- Hardware CYD: display 320×240 landscape, kolory/offset/driver, touch XPT2046 i kalibracja, microSD mount/read/write/fallback oraz AP/provisioning. Kroki: `docs/hardware.md`, `docs/troubleshooting.md`, flash środowisk diagnostycznych z `firmware/platformio.ini`, uruchom `scripts/check-display-ui-diagnostics.ps1`, `scripts/check-display-color-path.ps1`, `scripts/test-affine.ps1`, `scripts/test-sd-storage.ps1`; zebrać zdjęcia/serial log z fizycznej płytki.
- Windows manual/end-to-end: tray, dashboard, start-with-Windows, firewall/bindings, pairing, WebSocket/HTTP LAN, media/audio/shortcuts z realnym ESP32. Kroki: `agent/src/WindowsAgent/README.md`, `docs/configuration.md`, `docs/protocol.md`, `scripts/publish-windows-agent.ps1`; uruchomić opublikowanego agenta na Windows 11 i wykonać pełny scenariusz z urządzeniem.
- Legal/release review: sprawdzić licencje bibliotek, ikon/fontów/assets i zgodność publikacji binariów/dokumentacji. Kroki: przejrzeć manifesty `.csproj`, `firmware/platformio.ini`, `README.md`, `docs/` oraz planowane artefakty release.

| Sekcja | Wymaganie | Stan / dowód |
|---|---|---|
| 1 | SPRZĘT | Niezweryfikowane |
| 2 | PINOUT CYD | Niezweryfikowane |
| 3 | DISPLAY CONTROLLER | Niezweryfikowane |
| 4 | TOUCH — XPT2046 | Niezweryfikowane |
| 5 | MICROSD | Niezweryfikowane |
| 6 | STRUKTURA SD | Niezweryfikowane |
| 7 | COVER CACHE | Niezweryfikowane |
| 8 | COVER ART FORMAT | Niezweryfikowane |
| 9 | BRAK PSRAM — KRYTYCZNE | Niezweryfikowane |
| 10 | MEMORY DIAGNOSTICS | Niezweryfikowane |
| 11 | STACK FIRMWARE | Niezweryfikowane |
| 12 | REPOZYTORIUM | Niezweryfikowane |
| 13 | WINDOWS AGENT | Niezweryfikowane |
| 14 | MEDIA — NIE UŻYWAJ SPOTIFY API JAKO PODSTAWY | Niezweryfikowane |
| 15 | MEDIA DATA | Niezweryfikowane |
| 16 | MEDIA EVENTS | Niezweryfikowane |
| 17 | MEDIA COMMANDS | Niezweryfikowane |
| 18 | COVER PIPELINE NA PC | Częściowo automatycznie zweryfikowane: `dotnet test agent/src/Covers.Tests/StreamDeck.Covers.Tests/StreamDeck.Covers.Tests.csproj --nologo` (6/6) sprawdza brak thumbnail, crop/resize do 140×140 JPEG, SHA-256 coverId/nazwę pliku i cache hit bez ponownego zapisu. Scope: unit test PC pipeline; nie potwierdza HTTP, SD ani wyświetlenia na ESP32. |
| 19 | DOMINUJĄCY KOLOR OKŁADKI | Częściowo automatycznie zweryfikowane: `dotnet test agent/src/Covers.Tests/StreamDeck.Covers.Tests/StreamDeck.Covers.Tests.csproj --nologo` (6/6) sprawdza wybór nasyconego akcentu zamiast bieli/czerni/szarości oraz `null` dla okładki nienasyconej. Scope: algorytm PC; nie potwierdza runtime themingu na ESP32. |
| 20 | AUDIO WINDOWS | Niezweryfikowane |
| 21 | PC MONITORING | Niezweryfikowane |
| 22 | KOMUNIKACJA | Niezweryfikowane |
| 23 | PROTOKÓŁ | Częściowo automatycznie zweryfikowane: `dotnet test agent/src/Networking/StreamDeck.Protocol.Tests/StreamDeck.Protocol.Tests.csproj --nologo` (74/74) sprawdza wersjonowaną kopertę `protocol: 1`, camelCase payload, `requestId`, `ack`, walidację błędów i odrzucanie nieobsługiwanej wersji/typu. Scope: serializacja/walidacja bibliotekowa; nie potwierdza WebSocket LAN ani zgodności firmware runtime. |
| 24 | TYPY WIADOMOŚCI | Częściowo automatycznie zweryfikowane: `dotnet test agent/src/Networking/StreamDeck.Protocol.Tests/StreamDeck.Protocol.Tests.csproj --nologo` (74/74) sprawdza parsowanie wymaganych typów wiadomości i odrzucanie niepoprawnych payloadów komend ESP32. Scope: kontrakty JSON w kodzie PC; nie potwierdza end-to-end z urządzeniem. |
| 25 | COVER HTTP | Niezweryfikowane |
| 26 | DISCOVERY | Niezweryfikowane |
| 27 | WI-FI SETUP | Niezweryfikowane |
| 28 | FACTORY RESET | Niezweryfikowane |
| 29 | PAIRING / SECURITY | Niezweryfikowane |
| 30 | PAIRING FLOW | Niezweryfikowane |
| 31 | SHORTCUT SECURITY | Częściowo automatycznie zweryfikowane: `dotnet test agent/src/Shortcuts/StreamDeck.Shortcuts.Tests/StreamDeck.Shortcuts.Tests.csproj --nologo` (22/22) sprawdza, że konfiguracja wysyłana do device nie zawiera ścieżek/URL wykonawczych, odrzucenie ścieżek UNC dla aplikacji/skryptów oraz nieznane ID `run-anything-from-lan` nie wywołuje executorów. Scope: logika walidacji/dispatch w agencie; nie potwierdza zabezpieczeń całego LAN/pairingu runtime. |
| 32 | OBSŁUGIWANE SHORTCUT ACTIONS | Częściowo automatycznie zweryfikowane: `dotnet test agent/src/Shortcuts/StreamDeck.Shortcuts.Tests/StreamDeck.Shortcuts.Tests.csproj --nologo` (22/22) sprawdza dispatch dla launch application, open URL, hotkey, script/PowerShell i predefined adapter. Scope: mocked executory; nie potwierdza realnego uruchamiania aplikacji/skryptów w Windows tray. |
| 33 | SYSTEM TRAY | Niezweryfikowane |
| 34 | LOCAL CONFIG DASHBOARD | Niezweryfikowane |
| 35 | UI ESP32 | Niezweryfikowane |
| 36 | DESIGN RULES | Niezweryfikowane |
| 37 | EKRANY | Niezweryfikowane |
| 38 | MEDIA SCREEN — NAJWAŻNIEJSZY | Niezweryfikowane |
| 39 | MEDIA PLAYER ELEMENTS | Niezweryfikowane |
| 40 | DŁUGIE TYTUŁY | Niezweryfikowane |
| 41 | VOLUME UI | Niezweryfikowane |
| 42 | MEDIA ANIMATIONS | Niezweryfikowane |
| 43 | SHORTCUTS SCREEN | Niezweryfikowane |
| 44 | SHORTCUT CONFIG | Niezweryfikowane |
| 45 | PC SCREEN | Niezweryfikowane |
| 46 | SYSTEM COMMANDS | Niezweryfikowane |
| 47 | TOUCH UX | Niezweryfikowane |
| 48 | TOUCH CALIBRATION | Niezweryfikowane |
| 49 | TOUCH DIAGNOSTICS | Niezweryfikowane |
| 50 | BACKLIGHT | Niezweryfikowane |
| 51 | AUTO DIM | Niezweryfikowane |
| 52 | RGB LED | Niezweryfikowane |
| 53 | SPEAKER | Niezweryfikowane |
| 54 | CONNECTION SCREEN | Niezweryfikowane |
| 55 | RECONNECT | Niezweryfikowane |
| 56 | HEARTBEAT | Niezweryfikowane |
| 57 | UPDATE RATE | Niezweryfikowane |
| 58 | LOCAL PROGRESS INTERPOLATION | Niezweryfikowane |
| 59 | FONTS | Niezweryfikowane |
| 60 | ICONS | Niezweryfikowane |
| 61 | PAGE INDICATOR | Niezweryfikowane |
| 62 | DYNAMIC THEME | Niezweryfikowane |
| 63 | SD ASSET MANAGEMENT | Niezweryfikowane |
| 64 | SD FAILURE | Niezweryfikowane |
| 65 | LOGGING | Niezweryfikowane |
| 66 | HARDWARE DIAGNOSTIC MODE | Niezweryfikowane |
| 67 | DISPLAY TEST | Niezweryfikowane |
| 68 | SD TEST | Niezweryfikowane |
| 69 | MOCK MODE | Niezweryfikowane |
| 70 | PC AGENT TESTS | Częściowo automatycznie zweryfikowane: istnieją i przechodzą testy `StreamDeck.Protocol.Tests` (74), `StreamDeck.Covers.Tests` (6), `StreamDeck.Shortcuts.Tests` (22), konsolowy harness `StreamDeck.Configuration.Tests` (`dotnet run -c Release --project agent/src/Configuration/StreamDeck.Configuration.Tests/StreamDeck.Configuration.Tests.csproj`, 8 PASS), `node --test tests/wokwi-config.test.mjs` (5) oraz konsolowy smoke harness `dotnet run -c Release --project agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests/StreamDeck.WindowsAgent.Tests.csproj` (`PASS StreamDeck.WindowsAgent smoke tests`, z uruchomieniem lokalnego HTTP/WebSocket). Scope: wybrane testy automatyczne; nie potwierdza ręcznej obsługi tray ani połączenia z fizycznym CYD i nie jest kompletną macierzą publish-readiness. |
| 71 | CONFIGURATION | Częściowo automatycznie zweryfikowane: `dotnet run -c Release --project agent/src/Configuration/StreamDeck.Configuration.Tests/StreamDeck.Configuration.Tests.csproj` wykonał 8 PASS; testy w `Program.cs` sprawdzają defaulty (`port 8765`, monitoring 1000 ms, cover 140/80), roundtrip shortcutów bez token/secret, walidację zakresów/nulli i atomowy replace. Scope: store/JSON config; nie potwierdza panelu UI ani sekretów systemowych. |
| 72 | FIREWALL | Niezweryfikowane |
| 73 | AGENT BINDINGS | Niezweryfikowane |
| 74 | START WITH WINDOWS | Niezweryfikowane |
| 75 | FUTURE FEATURES | Niezweryfikowane |
| 76 | CODE QUALITY | Niezweryfikowane |
| 77 | NON-BLOCKING FIRMWARE | Niezweryfikowane |
| 78 | STATE MODEL | Niezweryfikowane |
| 79 | RESPONSIVENESS | Niezweryfikowane |
| 80 | COVER PLACEHOLDER | Niezweryfikowane |
| 81 | MEDIA IDENTITY | Niezweryfikowane |
| 82 | README | Niezweryfikowane |
| 83 | HARDWARE.MD | Niezweryfikowane |
| 84 | ARCHITECTURE DOCUMENT | Niezweryfikowane |
| 85 | DEVELOPMENT PHASES | Niezweryfikowane |
| 86 | BUILD AFTER EVERY PHASE | Niezweryfikowane |
| 87 | PHYSICAL HARDWARE BOUNDARY | Niezweryfikowane |
| 88 | ACCEPTANCE CRITERIA — HARDWARE | Niezweryfikowane |
| 89 | ACCEPTANCE CRITERIA — MEDIA | Niezweryfikowane |
| 90 | ACCEPTANCE CRITERIA — AUDIO | Niezweryfikowane |
| 91 | ACCEPTANCE CRITERIA — SHORTCUTS | Niezweryfikowane |
| 92 | ACCEPTANCE CRITERIA — NETWORK | Niezweryfikowane |
| 93 | PERFORMANCE TARGET | Niezweryfikowane |
| 94 | PRIORYTETY PROJEKTU | Niezweryfikowane |
| 95 | CZEGO NIE CHCĘ | Niezweryfikowane |
| 96 | WAŻNE: MICROSD JEST CZĘŚCIĄ PROJEKTU | Niezweryfikowane |
| 97 | FIRST BOOT EXPERIENCE | Niezweryfikowane |
| 98 | START NOW | Niezweryfikowane |
