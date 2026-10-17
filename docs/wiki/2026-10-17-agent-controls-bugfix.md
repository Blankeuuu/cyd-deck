---
title: Windows Agent — naprawa menu, kolorów, kontrolek i telemetrii GPU
tags: [cyd-deck, windows-agent, winforms, bugfix, gpu, temperature, apple-design]
aliases: [ContextMenuStrip ObjectDisposedException, Tile color must be RRGGBB, GPU Engine telemetry]
updated: 2026-10-17
status: active
---

# Naprawy po testach manualnych

## Błędy krytyczne: odtworzenie i poprawka

`dotnet run --project agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests -c Release -- --bug-repro` przed poprawkami odtwarzał dwa błędy: `AgentConfigurationException` przy kolorze `Red` oraz dokładny `ObjectDisposedException` z nazwą `System.Windows.Forms.ContextMenuStrip` przy zamykaniu Device & Tools. Po poprawkach oba przypadki przechodzą.

Menu Device & Tools jest własnością SettingsWindow i przeżywa zmianę zakładki i motywu. Nie jest niszczone w `Closed`, ponieważ to zdarzenie występuje przed zakończeniem wewnętrznego zamykania ToolStripDropDown. Menu zwalniane jest przy Dispose okna. Menu Tray odpinane jest od NotifyIcon przed zwolnieniem; aktualizacja nie dotyka zniszczonej instancji.

`ConfigurationColorNormalizer` normalizuje kolory przy deserializacji/importowaniu, przed atomowym zapisem oraz przed synchronizacją Configuration Draft. `Red` → `#FF0000`, `#f00` → `#FF0000`, `#80FF0000` → `#FF0000`; pusty/default/transparent → null. Null dla Deck Tile oznacza dziedziczenie bieżącego motywu, bez wpisywania stałego koloru. Nieznana nazwa nadal podlega walidacji i jest odrzucana. ColorDialog zawsze używa `$"#{color.R:X2}{color.G:X2}{color.B:X2}"`; nie stosuje ColorTranslator.ToHtml, które może zwracać nazwę. Ścieżka błędu profilu ma postać `$.profiles.Default.shortcuts[0].color`.

## Kontrolki i layout

- RoundedButton: GDI+, AntiAlias, promień 8 px, hover/pressed/focus, bez natywnej prostokątnej ramki. Zachowuje Button, klawiaturę i AccessibleName. Osobny overlay Paint służy ikonom oraz swatchom.
- SegmentedGroup / RoundedSegment: jedno zaokrąglone tło i aktywna pigułka, osobne palety jasna/ciemna; natywna logika RadioButton zachowana.
- RoundedInput: pola i dropdowny osadzone w zaokrąglonym kontenerze 32 px, tekst wyśrodkowany pionowo. Nie opakowuje wewnętrznych edytorów NumericUpDown. Zachowuje kolejność kontrolek w FlowLayoutPanel.
- Title, Icon, Slot, Type i Display mają jednolitą wysokość. Default poszerzony; Save Shortcut/Delete mają równe proporcje i wypełniają wiersz.
- Toolbar ma szerszy obszar COM/Ready. Test sprawdza szerokość rzeczywistego tekstu, bez ellipsis.
- Shortcut Library ma zaokrąglone karty i Assign. Przewijanie pozostaje wyłącznie wewnątrz biblioteki.
- Wykresy mają wspólny dolny obszar 64 px, lokalny zakres wartości i przezroczysty gradient pod linią. Stały RAM nie przenosi wykresu na środek karty. Progress i wykres tworzą wspólną dolną grupę.
- Usunięto kropkowaną ramkę focus z podglądu CYD. Media/Monitor preview nie przejmują fokusu klawiatury.

## Prawdziwa telemetria Windows

Dotychczasowy WindowsSystemMetricsProvider bezwarunkowo zwracał null dla GPU/TEMP. Nowy WindowsHardwareMetricsReader czyta natywnie PDH `GPU Engine(*)\Utilization Percentage` przez language-neutral PdhAddEnglishCounterW. Sumuje procesy korzystające z tego samego LUID/physical engine, a następnie wybiera najbardziej obciążony engine. Nie sumuje niezależnych silników do fałszywego obciążenia ponad 100%.

Temperatura jest badana asynchronicznie co 5 s, z ograniczonym czasem zapytań WMI: root/LibreHardwareMonitor → root/OpenHardwareMonitor → ACPI thermal zones. Temperatura ACPI jest opisana jako ACPI thermal zone, nie jako pomiar CPU. Brak dostępu lub czujnika daje czytelny status i tooltip z instrukcją. Nie instalowano sterownika, nie podnoszono uprawnień całego Agenta i nie generowano estymacji udającej pomiar.

GPU, CPU i UI nie czekają na sondowanie temperatury. Query PDH jest zamykane podczas zatrzymania Runtime. HostSystemTelemetrySnapshot przenosi dostępność i źródło pomiaru. Brak pomiaru czyści jego historię; usunięto wcześniejsze sztuczne wartości GPU i temperatury wyliczane z CPU.

Na komputerze użytkownika GPU odczytywało około 4–5%, a uruchomiony Agent raportował 5,38%. Dostępne źródła WMI nie zwróciły temperatury; UI pokazuje `Start LibreHardwareMonitor` i instrukcję dotyczącą WMI/uprawnień. Ten status nie oznacza, że odczyt temperatury został uzyskany.

Źródła pierwotne: [Microsoft: GPU engine utilization](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/), [PDH counter arrays](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw), [language-neutral counters](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw), [oficjalny przykład WMI LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitor.Windows.Forms/TestScripts/basicwmi.py).

## Weryfikacja i wdrożenie

- Reprodukcja obu crashy przed poprawką: FAIL. Po poprawce: PASS; menu otwierane/zamykane osiem razy z przełączaniem zakładek i zapisywaniem motywu.
- DesktopViewportTests: PASS w 1200×780 i 1280×800, jasny/ciemny motyw, brak głównego scrolla, Default bez clippingu, Title/Icon 32 px, pełne COM/Ready, draft/sloty/Volume/Mute.
- SystemMonitoring.Tests: PASS (obciążenie CPU, agregacja GPU, pochodzenie odczytu).
- Configuration.Tests: PASS (walidacja i atomowy zapis).
- WindowsAgent smoke: log `artifacts/polish-smoke-final.log`. Release publish bez błędów/ostrzeżeń: `artifacts/polish-publish.log`.
- Agent: `artifacts/desktop-polish-20261017/StreamDeck.WindowsAgent.exe`, PID 23504, dashboard http://127.0.0.1:63766. Wystartował przez ScheduledTask Interactive, zadanie usunięto. Poprzedni proces już nie działał; nie odrzucono żadnego otwartego draftu.
- USB connected, firmware 1.2.0, cover.status=2/rendered=true, audio 100%/muted=false. Dowód bez sekretów: `artifacts/polish-live-verification.json`.
- Firmware niezmienione, bez flasha. Nie wykonywano nowego release/commita/pusha.
