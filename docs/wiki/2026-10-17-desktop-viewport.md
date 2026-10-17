---
title: Windows Agent — cztery widoki desktopowe bez przewijania viewportu
tags: [cyd-deck, windows-agent, winforms, apple-design, ui, desktop]
aliases: [Zero Scroll Desktop, Unified Toolbar, Desktop Bento Dashboard]
updated: 2026-10-17
status: active
---

# Windows Agent: stały viewport

## Zakres

Zmiany dotyczą wyłącznie części PC projektu CYD-DECK. Firmware, dane NVS, parowanie i zegar HH:mm w doku urządzenia pozostały bez zmian. Windows Agent jest aplikacją WinForms; układy opisane przez użytkownika za pomocą CSS zostały przełożone na natywne TableLayoutPanel, kontrolki i rysowanie GDI+.

## Architektura interfejsu

- Jeden Unified Toolbar o wysokości 56 px: tytuł widoku, Deck Profile i akcje profilu, jeden status połączenia, Configuration Draft / Saved oraz Save & Sync.
- Sidebar 210 px: Deck Builder, Media Controls, System Monitor, Settings. Device, Pairing, Updates i Logs pozostają dostępne w menu Device & Tools.
- Główny kontener ma AutoScroll=false. Scroll jest dopuszczony w wewnętrznej Shortcut Library.
- Ctrl+S zapisuje i synchronizuje; Ctrl+1…4 przełącza główne widoki. Wszystkie przyciski ikonowe mają AccessibleName / tooltip.
- WinForms nie może opierać wysokości jednowierszowych TableLayoutPanel na domyślnym AutoSize. FitDesktopRows jawnie ustawia procentowy wiersz, aby tekst nie był rysowany poza przyciętym rodzicem.

## Widoki

| Widok | Układ i interakcje |
| --- | --- |
| Deck Builder | Podgląd 4:3 z przełącznikiem Shortcuts Page / Home Screen (4), wewnętrzna biblioteka z wyszukiwaniem i New Shortcut, Tile Inspector 354 px. Unassign, Assign, zmiana slotu i przeciąganie działają w Configuration Draft. |
| Settings | Trzy kompletne kolumny: Desktop Appearance, Display Appearance, Hardware & Connection. Segmented controls, swatches z zaznaczeniem, toggles, jasność, COM i reset draftu. |
| Media Controls | Zaokrąglona okładka pobierana z SMTC, fallback, metadane, sterowanie, timeline interpolowany co 100 ms, współdzielony Volume/Mute. Zmiany suwaka wysyłane co najwyżej co 60 ms. Podgląd CYD Media Page. |
| System Monitor | CPU/RAM/GPU/TEMP w siatce 2×2, pasek i 30 próbek historii. Podsumowanie z etykietą o stałej szerokości i jednowierszową wartością z ellipsis, podgląd monitora CYD. Brak biblioteki skrótów. |

## Configuration Draft i Deck Profile

Dotychczasowe konfiguracje zachowują pierwsze sześć Shortcutów jako pozycje urządzenia. Opcjonalne AssignedShortcutIds w DeckProfileConfiguration opisuje sześć jawnych pozycji, każda z ID Shortcutu albo null. Nie usuwa Shortcutów z katalogu.

Runtime buduje paczkę z przypisanych pozycji. Pusta pozycja ma nieszkodliwy identyfikator __empty_slot_N i nie ma akcji w katalogu wykonawczym. Dzięki temu parser istniejącego firmware nie przesuwa kolejnych slotów. Raporty tych pustych pozycji nie tworzą propozycji importu.

Przy jawnych przypisaniach Auto jest rozwiązywane po stronie PC do istniejącego hero-1 / split-2 / quad-4 / grid-6 na podstawie najwyższego zajętego slotu. Saved Configuration nadal przechowuje Auto, a firmware otrzymuje obsługiwane już pole Shortcuts Layout. Nie dodano pól protokołu ani zmian firmware.

Save Shortcut w inspektorze zatwierdza edycję w Configuration Draft; końcowy zapis i synchronizację wykonuje Save & Sync w toolbarze. Edycja przetrwa zmianę widoku. Zapis jest atomowy i nadal blokowany w razie błędu odczytu istniejącej konfiguracji.

## Dane i ograniczenia

- Brak rzeczywistych czujników GPU lub temperatury daje „Not reported by hardware”, zamiast prezentowania oszacowania jako pomiaru.
- Aktualny firmware nie raportuje Local Brightness Override. Ustawienia pokazują „Not reported”; nie zgadują jego wartości.
- Clock 24h format dotyczy podglądów Windows Agent. Fizyczny CYD Display zachowuje HH:mm; w tej pracy nie zmieniano firmware ani formatu czasu w protokole.
- Podglądy są natywnymi reprezentacjami tego samego stanu i geometrii urządzenia, a nie zrzutami framebuffer TFT. Test rendery używają kontrolowanych danych; nie są fotografią żywego urządzenia.

## Weryfikacja

- Build/publish Release: bez błędów i ostrzeżeń.
- StreamDeck.WindowsAgent.Tests: PASS, łącznie z DesktopViewportTests.
- StreamDeck.Configuration.Tests: PASS.
- DesktopViewportTests obejmuje wszystkie cztery widoki w 1200×780 i 1280×800, brak scrollbara strony, granice bezpośrednich rodziców kontrolek, trzy kolumny Settings, metryki etykiet Now Playing/Uptime, wewnętrzny scroll biblioteki, Assign/Unassign, draft, zapis, zachowanie pozycji, Volume/Mute i dwa motywy.
- Rendery: artifacts/ui-review/desktop-*.png. Logi: artifacts/desktop-smoke.log, desktop-config-tests.log, desktop-publish.log.
- Weryfikacja dotyczy standardowego rozmiaru i skali środowiska testowego; nie zastępuje sprawdzenia fizycznego monitora o innym DPI.

## Wdrożenie lokalne

Uruchomiona wersja: artifacts/desktop-agent-20261017/StreamDeck.WindowsAgent.exe, PID 7796, dashboard http://127.0.0.1:52335. Poprzedni Agent PID 20024 został zastąpiony po kontroli braku niezapisanych zmian. Start wykonano poprzez jednorazowe ScheduledTask Interactive, następnie zadanie usunięto.

USB connected, firmware 1.2.0, cover.status=2/rendered=true, volume=100. Dowód bez sekretów: artifacts/desktop-live-verification.json. Nie publikowano release ani instalatora i nie wykonywano nowego pusha w ramach tej pracy.

## Pliki i granice modułów

- SettingsWindow.Desktop.cs: toolbar, sidebar, wspólne kontrolki i Settings.
- SettingsWindow.Deck.cs: biblioteka, przypisania i Tile Inspector.
- SettingsWindow.MediaMonitor.cs: media, audio, ładowanie okładek i monitor.
- DesktopCanvas.cs: podglądy CYD Display i interakcje kafelków.
- SettingsWindow.cs / Compact.cs: integracja, cykl życia, dotychczasowy jawny zapis; usunięto zastąpione implementacje Deck/Settings.
- WindowsAgentRuntime.cs: wybór slotów i rozwiązywanie Auto przy hydration/sync.
- AgentState.cs: 30 próbek i informacja o dostępności rzeczywistych pomiarów.
- AgentConfiguration.cs / Validator: format zegara podglądu i walidowane przypisania profilu.

## Zastosowane wytyczne Apple Design

- layout.md › Best practices: „Group related items to help people find the information they want.” — stałe kolumny i odrębne zadania kart.
- typography.md › Ensuring legibility: „Use font sizes that most people can read easily.” — hierarchia tytuł / wartość / opis; żadnego pionowego łamania etykiet podsumowania.
- color.md › Best practices: „Avoid using the same color to mean different things.” — akcent dla wyboru/akcji, osobny stan draftu, stan nieprzekazywany samym kolorem.
- sidebars.md, toolbars.md, settings.md, segmented-controls.md i accessibility.md — jedna nawigacja, jeden toolbar, zwarta forma ustawień i nazwy dostępności.
