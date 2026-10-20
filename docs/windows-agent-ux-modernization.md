---
title: Modernizacja UX Windows Agent
tags: [cyd-deck, winforms, ux, accessibility, state-management]
aliases: [Windows Agent UX modernization]
updated: 2026-10-20
status: in-progress
---

# Cel i granice

Natywny WinForms/.NET 10, bez rewrite i nowych ciężkich zależności. Priorytety: przewidywalność, użyteczność, spójność, estetyka. Protokół 1 i dotychczasowy format konfiguracji pozostają kompatybilne. Edycja nie zapisuje danych użytkownika przed jawną akcją. Testy używają własnych magazynów konfiguracji i parowania; nie uruchamiają prawdziwych instalatorów, resetów lub odtwarzaczy.

# Etapy i właściciele

1. Discovery: analiza bieżącego kodu, wiki i ADR zapisu offline; regresja nieskutecznej pauzy oraz ikony aplikacji.
2. State & Safety: wspólna projekcja połączenia, zapisany port a runtime, uczciwy stan synchronizacji i ochrona operacji destrukcyjnych.
3. Deck Builder: biblioteka definicji po lewej, interaktywny podgląd pośrodku, inspektor po prawej. Wybór slotu niezależny od wyboru definicji; odpięcie nie usuwa akcji. Zachowanie draftu i profili.
4. Design system: centralne tokeny kolorów/odstępów/fontów, stany kontrolek, kontrast i DPI. Polski domyślny, angielski dodatkowy, natywne zasoby .resx.
5. Pozostałe widoki: Media, Monitor, Ustawienia oraz widoczna sekcja Urządzenie z aktualizacjami i logami.
6. QA: testy zachowania, konfiguracji historycznej, błędów transportu, renderów i DPI; build, review integracji i synchronizacja wiki przez MCP.

# Kierunek wizualny

Grafitowe tło #0D0F14, powierzchnia #16181D, powierzchnia podniesiona #1E2028, obramowanie #3C4656, tekst #F4F6F8 i pomocniczy #ADB8C8. Niebieski wyróżnia główną akcję oraz wybór; secondary pozostaje neutralne. Kolor tekstu na akcencie wynika z kontrastu. Segoe UI: body 11 pt (około 14,7 px przy 96 DPI), label/caption co najmniej 10 pt. Odstępy 4/8/12/16/24/32. WinForms AutoScaleMode.Dpi i elastyczne układy zastępują arbitralne stałe wysokości.

Głównym elementem jest podgląd rzeczywistego CYD z sześcioma slotami; otoczenie pozostaje spokojne. Bez dekoracyjnych wykresów, atrap przycisków i niepotwierdzonych komunikatów sukcesu. Widok urządzenia pokazuje USB/Wi-Fi jako odrębne fakty.

# Kontrakty pracy równoległej

- Lead: Shell/SettingsWindow.cs, Desktop, Compact, Controls, pozostałe widoki, lokalizacja wspólna, tokeny, integracja i dokumentacja.
- Deck Worker: Shell/SettingsWindow.Deck.cs, DesktopCanvas.cs, ShortcutEditorDialog.cs, ShortcutIconPicker.cs oraz własne modele/testy i zasoby Deck.
- State Worker: AgentState, AgentStatusFormatter, Runtime transport/synchronizacja i własne testy; bez Shell i bez protokołu.
- Media/Branding Worker: biblioteka Media i jej testy, adapter mediów Host, AppIconFactory/AppIdentity/assets oraz BrandingTests. Analiza firmware bez zmian kontraktu; minimalna poprawka po dowodzie regresji.
- Zasoby: `UiText.Get("Feature.Key")` oraz `UiText.Format(key, args)`; `Localization/Feature.resx` zawiera polski, `Feature.en.resx` angielski. `UiText.Language`, `SetLanguage("pl"/"en")`, `LanguageChanged` stanowią wspólny punkt zmiany języka.
- Nazwy wewnętrzne stron i wartości konfiguracji pozostają stabilne, a tłumaczone są ich etykiety. Zmiany wspólnego API wymagają wiadomości do Leada przed edycją konsumenta.

# Weryfikacja

Wyniki i ograniczenia uzupełniane po rzeczywistych testach. Nie oznaczamy P0/P1/P2 jako zakończonych na podstawie samego buildu lub deklaracji Workera. Testy sprzętowe i DPI monitora fizycznego są odrębne od renderów/fixture'ów.
