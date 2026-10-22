---
title: Protokół CYD-DECK — niezawodność poleceń i artwork
tags: [cyd-deck, protocol, usb, websocket, reliability]
aliases: [CYD-DECK protocol reliability]
updated: 2026-10-21
status: active
---

# Protokół 1 — aktualizacja niezawodności

Implementacja referencyjna: `agent/src/Networking/StreamDeck.Protocol/`,
`firmware/include/network/agent_protocol.h`, `firmware/src/network/agent_client.cpp`.
Pełna wcześniejsza specyfikacja znajduje się w kanonicznym Obsidian wiki:
`wiki/projects/stream-deck/docs/protocol.md`. Ten plik przywraca brakujący w repo
odnośnik i opisuje zmiany gałęzi `fix/firmware-e2e-reliability`; nie oznacza wdrożenia.

## Transport i kolejność

USB używa JSON UTF-8, jedna ramka na linię CRLF, 115200 baud. Limit Agenta to
16 KiB na ramkę; payload artwork jest dzielony na maksymalnie 512 bajtów przed
Base64. Blokada zapisu obejmuje całą ramkę. Nigdy nie przeplata się jej bajtów.
Transfer okładki ma osobne zadanie, a odbiornik nadal obsługuje polecenia.
W kolejce firmware łączone są sąsiednie wartości SetVolume i zbędne RefreshUi;
nie łączy się Previous, Next, PlayPause ani uruchomień Shortcut.

## Polecenia i ACK

`media.command`, `audio.setVolume`, `audio.toggleMute`, `shortcut.execute` zachowują
istniejące payloady i `requestId`. ACK ma pola na poziomie envelope:

```json
{"protocol":1,"type":"ack","requestId":"volume-7","success":true}
```

Firmware śledzi do 16 oczekujących komend. Po 5 s bez ACK pokazuje timeout.
Timeout oznacza wynik niepewny, a nie dowód niewykonania. Nie ponawia automatycznie
poleceń z efektami ubocznymi. Błędy rozróżniają m.in. `unknownShortcut`,
`targetNotFound`, `invalidConfiguration`, `actionUnavailable`, `accessDenied`,
`executionFailed`, `busy` i dotychczasowy `commandFailed`.

Jedna sesja wykonuje jeden Shortcut naraz; kolejny podczas wykonania dostaje
`busy`. Długi skrypt nie blokuje odbioru poleceń multimedialnych. Dla skryptu
sukces oznacza zakończenie procesu kodem 0; dla aplikacji/URL — przyjęcie
uruchomienia przez Windows, nie potwierdzenie gotowości jej okna.

## Uzgadnianie audio

Nowy Agent dodaje opcjonalną monotoniczną `data.revision` do `audio.update`.
Odpowiedź z obserwowanym stanem po `audio.setVolume` przenosi także `requestId`:

```json
{"protocol":1,"type":"audio.update","requestId":"volume-7","data":{"volumePercent":80,"muted":false,"revision":7}}
```

Firmware oddziela `requestedVolume` od `reportedVolume`. Podczas oczekiwania
pokazuje żądanie użytkownika, ignoruje starsze rewizje i uzgadnia wartość po
odpowiedzi dotyczącej bieżącego żądania. Negatywny ACK/timeout przywraca ostatni
obserwowany stan. Po reconnect resetuje filtr rewizji, ponieważ Agent mógł się
uruchomić ponownie. Starszy Agent bez revision jest obsługiwany przez dopasowanie
wartości; ochrona przed kolejnością starych odpowiedzi jest wtedy słabsza.

## Cykl artwork

`coverId` jest SHA-256 bajtów przetworzonego JPEG, nie identyfikatorem utworu.
Firmware najpierw próbuje odczytać i zdekodować cache. Cache miss lub błąd dekodowania
powoduje `cover.request` po USB albo pobranie HTTP po Wi-Fi. Jawny request może
ponownie przesłać ten sam coverId, nawet jeśli Agent wcześniej wysłał complete.
Jednoczesne requesty podczas tego samego transferu są łączone.

Pusty lub niepoprawny `coverId` zachowuje poprzednią okładkę, jeśli tytuł,
artysta i album po normalizacji do buforów firmware nie zmieniły się. Zmiana
któregokolwiek z tych pól przy pustym ID czyści okładkę. Reguła obejmuje długie
nazwy obcięte do pojemności bufora; format JSON i `protocol: 1` pozostają bez zmian.

USB zachowuje `cover.chunk` z `coverId`, `offset`, `total`, `data` oraz końcowe
`cover.complete`. Odrzucane są stare identyfikatory, złe offsety, nieprawidłowy
Base64, niezgodne rozmiary, niepełny transfer, zły SHA-256 i nieprawidłowe znaczniki
JPEG. Bez complete nie ma commitu. Plik tymczasowy jest zamykany przed rename;
poprzedni plik jest zachowywany jako `.bak` do zakończenia zastąpienia. Nie jest to
gwarancja odporności FAT/SD na utratę zasilania.

Po niepowodzeniu są maksymalnie trzy dodatkowe próby z backoffem 1/2/4 s, liczonym
od zaobserwowania awarii. Timeout bez postępu wynosi 15 s. Nowy coverId dostaje
nowy budżet; cykliczne metadane nie zerują budżetu tego samego ID.

Opcjonalne `device.info.data.cover.coverId` identyfikuje wynik. Firmware dodaje je
wyłącznie po włączeniu diagnostyki przez Agenta, ponieważ starszy Agent ma ścisłą
listę pól device.info. Domyślny raport zachowuje stary schemat. `cover.rendered`
jest teraz ustawiane po zakończeniu ramki LVGL wysłanej do sterownika TFT przy
widocznej stronie Home/Media, a nie po samym przesłaniu pliku. Nadal nie potwierdza
wizualnej poprawności fizycznego panelu. Dekodowanie i przypisanie obrazu są
osobnymi etapami. Samo `USB transfer_completed` nigdy nie dowodzi wyświetlenia.

HTTP pracuje w osobnym zadaniu FreeRTOS z kolejką czterech bloków po 512 B.
Zapis konsumuje jeden blok na wywołanie wątku aplikacji. Odczyt cache i dekodowanie
JPEG pracują w osobnym, pojedynczym zadaniu. SHA-256 strumienia jest sprawdzany
przed commitem; render nie hashuje ponownie całego pliku. Sprawdza jego rozmiar,
nagłówek JPEG i wynik dekodowania, więc nie wykrywa każdej zmiany bajtów
pozostawiającej plik poprawnym JPEG.
Worker nie wywołuje LVGL ani nie modyfikuje widocznego bufora. Wątek UI odbiera
gotowy wynik, odrzuca nieaktualny coverId i podmienia descriptor. Szczytowo potrzebne
są dwa bufory RGB565 po 32 KiB oraz stos workera; błąd alokacji ma ograniczone retry.
Maszyna stanów nie rozpoczyna transferu/zapisu podczas trwającego dekodowania.
Walidacja znaczników JPEG przy commicie jest strumieniowa, bez ponownego odczytu
SD na wątku UI. Pojedyncze append/flush/rename/index write nadal są synchroniczne;
ich rzeczywisty koszt wymaga pomiaru na karcie użytkownika.

## System stats

`cpuPercent`, `memoryPercent`, `memoryUsedBytes`, `memoryTotalBytes`, `gpuPercent`,
`tempCelsius` pochodzą z tego samego SystemMetrics co Windows System Monitor.
GPU i TEMP nie mają syntetycznych fallbacków: brak czujnika jest null lub brakiem
pola. Firmware rozróżnia brak pakietu, świeży pakiet z niedostępnymi metrykami,
przeterminowanie po 5 s i rozłączenie. Świeżość używa lokalnego `millis()` od
odbioru; nie porównuje zegara Windows z uptime ESP32. RAM parsowany jest 64-bitowo.

Zegar (`hostLocalMinutes`, `hostDate`) jest częścią `system.stats`; podczas
hydration jego wysyłanie poprzedza przetwarzanie artwork. `stats_sent` oznacza
skuteczne przekazanie do aktywnego transportu, nie potwierdzenie odbioru CYD.
`stats_received` i `metrics_updated` są osobnymi zdarzeniami urządzenia.

## Błędy ramek i puste Deck Tiles

Puste pozycje profilu mogą przychodzić w `shortcuts.update` jako wewnętrzne
placeholdery `__empty_slot_…` z ikoną `empty`. Firmware zachowuje pozycję kafelka,
ale nie traktuje placeholdera jako wykonywalnego Shortcut ani nie odsyła go
w `shortcuts.report`. Dotyczy to również historycznych zapisów NVS.

W autoryzowanej sesji błąd pojedynczej ramki nie zmienia autoryzacji i nie
powoduje `connection.state=degraded`. Ramka jest odrzucana i logowana. Jeśli
parsowalny JSON ma bezpieczny requestId (1–64 widoczne znaki ASCII), Host może
zwrócić failed ACK z `invalidPayload`. Na ACK nie odpowiada kolejnym ACK,
także gdy przychodzący ACK ma nieprawidłowy kształt. Przed autoryzacją zachowano
dotychczasowe odrzucanie niezgodnych ramek przez `degraded`.

USB drenuje nadmierną ramkę do newline i odrzuca niepoprawne UTF-8, po czym
przetwarza kolejną ramkę. Odczyt 0 oznacza EOF i kończy sesję, a błąd portu
propaguje się do reconnect. Brak bajtów na otwartym porcie jest oczekiwaniem
wewnątrz adaptera; nie jest EOF. Obowiązująca serializacja całych ramek pozostaje.

## Diagnostyka opcjonalna

Przed uruchomieniem testowej instancji Agenta ustaw `CYD_DECK_DIAGNOSTICS=1`.
Podczas hydration wysyła ona autoryzowane `diagnostics.configure` z
`data.enabled=true`. Firmware domyślnie ma diagnostykę wyłączoną. Wyłączenie w
aktywnej sesji: ta sama ramka z `false`; zmienna środowiskowa steruje logowaniem
Agenta i konfiguracją kolejnej sesji. Nie uruchamiaj drugiej instancji na zajętym COM.

Firmware publikuje `diagnostic.event` z `stage`, `correlation`, `deviceMs`,
`transport`. coverId w zdarzeniach jest skracany do 8 znaków. Etapy obejmują request,
cache, transfer, zapis, dekodowanie, ramkę TFT, kolejkę intencji, wysłanie polecenia,
ACK i parsowanie stats. Agent loguje początek i koniec wykonania oraz czas trwania.
Do porównania zegarów używaj różnic na tej samej maszynie; timestamp odbioru logu
Windows nie jest czasem dotyku ESP32. Nie loguje się Base64 ani argumentów akcji.

## Kompatybilność

Envelope pozostaje `protocol:1`. Nowe pola są opcjonalne, a dotychczasowe nazwy i
typy pozostają zgodne. Starsze firmware ignoruje rozszerzoną diagnostykę i revision.
Nowe firmware zachowuje fallback dla Agenta bez revision. Nowe raportowanie
rendered jest celowo bardziej rygorystyczne. Dotychczasowe dane NVS i konfiguracji
użytkownika są zachowane. Lokalny Debug mode dodaje opcjonalny klucz bool
`appearance/debug` z domyślną wartością false; nie jest ustawieniem protokołu
ani zamiennikiem rozszerzonej diagnostyki Agenta. Pełna macierz sprzętowa
USB/Wi-Fi pozostaje do wykonania.
