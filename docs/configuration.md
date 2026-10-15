# Konfiguracja CYD-DECK

## Źródło prawdy i synchronizacja

- Windows Agent przechowuje katalog skrótów oraz globalną konfigurację wyglądu: theme, kolory, styl przycisków, kolejność i widoczność stron, jasność, dim/screen-off i wake-on-touch. Pusta tabela skrótów oznacza brak konfiguracji po stronie Agenta; firmware nie importuje swoich slotów do komputera. Urządzenie wyświetla maksymalnie pierwszych 6 wpisów katalogu Agenta.
- CYD przechowuje kopię ustawień wyglądu w NVS, żeby zachować działanie lokalne/offline. Po autoryzowanym połączeniu Agent wysyła `device.settings` i `shortcuts.update`; live zmiany Appearance są wysyłane tym samym protokołem.
- Wi-Fi, pairing/bearer token i kalibracja dotyku pozostają lokalnymi danymi urządzenia. Nie są częścią eksportu konfiguracji Agenta.
- CYD wysyła skrót do wykonania wyłącznie przez zdefiniowane `shortcutId`; nie przyjmuje z sieci dowolnych poleceń shell.

Szczegóły payloadów opisuje [[protocol]].

## Import i eksport

W Settings Window użyj Shortcuts → Export albo Import. Są to operacje na pełnej konfiguracji Agenta, nie import/eksport skrótów z urządzenia. Eksport ma postać JSON z `formatVersion: 1` i sekcją `configuration`. Zawiera konfigurację Agenta, w tym skróty, wygląd, parametry serwera/monitorowania oraz ustawienia cover art. Import sprawdza wersję formatu i waliduje konfigurację przed zapisem; uszkodzony, nieobsługiwany lub niepoprawny plik jest odrzucany.

Eksport nie zawiera pairing tokenów, haseł Wi-Fi ani kluczy prywatnych. Traktuj plik jako konfigurację użytkownika: skróty mogą zawierać ścieżki aplikacji lub skryptów z danego komputera.

## Pairing i zapomnienie urządzenia

Pairing wymaga lokalnego zatwierdzenia przez użytkownika w dashboardzie Agenta. Pairing tokenów nie wolno logować ani eksportować. „Forget device” wymaga jawnego potwierdzenia, usuwa rekord/token po stronie Agenta i wymusza ponowne zestawienie połączenia; jeśli token jest także zapisany na CYD, ponowne pairing wymaga zatwierdzenia. Ostatnia znana tożsamość CYD pozostaje dostępna w UI po rozłączeniu, aby można było usunąć pairing także offline.

## Firmware update

Firmware używa partycji OTA `app0`/`app1`; każdy slot ma limit rozmiaru 0x1F0000 bajtów. Zakładka Updates pokazuje zainstalowane wersje i pozwala ręcznie wybrać obraz `.bin`; automatyczny katalog/manifest wydań nie jest skonfigurowany. Uploader sprawdza nagłówek obrazu ESP i limit slotu przed uploadem, pokazuje postęp, a po transferze czeka na ponowne połączenie i świeżą telemetrię wersji. Sukces jest potwierdzany tylko po zmianie raportowanej wersji.

Firmware ArduinoOTA używa hasła setup zapisanego lokalnie w NVS; nie jest to pairing token. Agent prosi o hasło przez maskowane pole przy każdej aktualizacji i nie zapisuje go w konfiguracji ani logu. Hasło jako zarządzany string nie może być zagwarantowane jako wyzerowane z pamięci procesu.

Do fizycznego wdrożenia należy użyć zgodnego obrazu dla właściwej konfiguracji płytki. Build lub test walidatora nie potwierdza aktualizacji na hardware. Przepływ UI/uploader wymaga jeszcze testu na fizycznym CYD; jeśli urządzenie nie zgłosi nowej wersji po transferze, aplikacja wyświetla timeout zamiast deklarować sukces.

## Recovery

- `Restart` wykonuje zwykły restart ESP32 i zachowuje konfigurację.
- `Factory Reset` usuwa dane użytkownika urządzenia, w tym Wi-Fi, pairing i appearance. Na ekranie CYD wymaga 3-sekundowego przytrzymania oraz osobnego potwierdzenia; z Windows wymaga potwierdzenia w UI.
- Po Factory Reset urządzenie wraca do provisioning. Backup konfiguracji Agenta nie zawiera danych potrzebnych do automatycznego odtworzenia Wi-Fi/pairing.
