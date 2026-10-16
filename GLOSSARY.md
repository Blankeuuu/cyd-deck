---
title: Słownik domeny CYD-DECK
tags: [cyd-deck, domain-modeling, glossary]
aliases: [CYD-DECK Glossary, słownik CYD]
updated: 2026-10-16
status: active
---

# CYD-DECK

System łączący fizyczny panel dotykowy ESP32 (320×240) z aplikacją towarzyszącą na systemie Windows do sterowania multimediami, wywoływania skrótów oraz monitorowania parametrów komputera w estetyce Apple Human Interface Guidelines.

## Language

**CYD Display**:
Fizyczny terminal dotykowy ESP32 z ekranem 320×240 prezentujący widżety, zegar i kafelki skrótów oraz przekazujący intencje dotykowe użytkownika do komputera.
_Avoid_: Stream Deck, PC Dock, slave screen, hardware client

**Windows Agent**:
Aplikacja działająca na systemie Windows, będąca jedynym źródłem prawdy (*single source of truth*) dla konfiguracji, wykonująca akcje systemowe oraz strumieniująca czas systemowy, telemetrię i stan multimediów do **CYD Display**.
_Avoid_: PC Dock Agent, Host app, Client, Desktop Server

**Shortcut**:
Zdefiniowana w **Windows Agent** akcja (uruchomienie aplikacji, otwarcie adresu URL, skrót klawiszowy, skrypt lub akcja wbudowana) przechowywana w katalogu na komputerze.
_Avoid_: Macro, Command, Action item

**Deck Tile**:
Dotykowy kafelek w siatce na ekranie **CYD Display** (oraz w podglądzie WYSIWYG), wizualizujący przypisany **Shortcut** wyłącznie za pomocą wycentrowanej ikony i zwięzłej etykiety.
_Avoid_: Key, Slot button, Hardware button

**Shortcuts Layout**:
Schemat rozplanowania i rozmiaru **Deck Tiles** na stronie skrótów (`auto`, `hero-1`, `split-2`, `quad-4`, `grid-6`), określający czy ekran prezentuje jeden pełnoekranowy kafelek, dwa duże kafelki po pół ekranu, cztery duże kafelki 2×2, czy sześć kafelków 3×2.
_Avoid_: Button size mode, Grid density

**Tile Inspector**:
Panel boczny w **Windows Agent** prezentujący oraz pozwalający edytować techniczne parametry wybranego **Deck Tile** (ścieżki wykonywalne, kombinacje klawiszy, argumenty skryptów, identyfikator), odciążając wizualnie samą siatkę kafelków.
_Avoid_: Inline tile details, Shortcut properties dump

**Home Screen**:
Ekran startowy CYD Display, nazywany Default, łączący zegar, odtwarzacz z okładką oraz cztery najważniejsze przypisane skróty. W nawigacji reprezentuje go bieżący czas HH:mm, zgodnie z korektą użytkownika.
_Avoid_: Status Bar Clock, Header timer, Corner uptime

**Deck Profile**:
Nazwany zestaw skrótów użytkownika, z którego pochodzą przypisane pozycje CYD Display. Aktywny profil określa zestaw przeznaczony do zapisania i synchronizacji.
_Avoid_: Theme profile, Display Appearance

**Desktop Appearance**:
Konfiguracja wizualna dotycząca wyłącznie okna aplikacji **Windows Agent** na komputerze (motyw jasny/ciemny/systemowy, styl tła okna, kolor akcentu UI).
_Avoid_: Global Theme, Shared Appearance, Client Theme

**Display Appearance**:
Niezależna konfiguracja wizualna synchronizowana do **CYD Display**, określająca motyw wyświetlacza, schemat kolorystyczny **Deck Tiles**, **Shortcuts Layout**, jasność podświetlenia, układ stron oraz styl trybu **StandBy Mode**.
_Avoid_: Device Theme, Hardware Colors

**StandBy Mode**:
Wysokokontrastowy widok zegara i statusu na **CYD Display** wzorowany na iOS StandBy / watchOS, prezentowany w strefie bezpiecznej ekranu bez obcinania krawędzi.
_Avoid_: Screensaver, Idle text

**Configuration Draft**:
Robocza konfiguracja edytowana w **Windows Agent**, której użytkownik jeszcze nie zapisał.
_Avoid_: Live configuration, Pending sync

**Saved Configuration**:
Konfiguracja zatwierdzona przez użytkownika i zachowana przez **Windows Agent**, przeznaczona do synchronizacji z **CYD Display** również po ponownym połączeniu.
_Avoid_: Draft, Device state

**Local Brightness Override**:
Lokalna korekta jasności wybrana na **CYD Display**, obowiązująca do jawnego zastosowania ustawień z **Windows Agent**.
_Avoid_: Saved Display Appearance, Synced brightness
