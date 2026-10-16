---
title: Jawny zapis konfiguracji i synchronizacja po ponownym połączeniu
tags: [cyd-deck, adr, state-sync, configuration]
aliases: [Explicit Save and Offline Sync, zapis offline CYD]
updated: 2026-10-16
status: active
---

# 0002. Jawny zapis konfiguracji i synchronizacja po ponownym połączeniu

Uzgodniono z użytkownikiem rozdzielenie roboczej edycji od zapisanej konfiguracji: jeden przycisk `Save & Sync` przy połączeniu lub `Save` offline zapisuje konfigurację lokalnie, a ostatnia zapisana konfiguracja jest automatycznie synchronizowana po ponownym połączeniu. Niezapisana edycja nie trafia na CYD Display; zastępuje to dotychczasowy automatyczny zapis wyglądu, aby edycja ustawień i skrótów miała ten sam przewidywalny cykl.

Sukces zapisu lokalnego i wynik synchronizacji są odrębnymi stanami: utrata połączenia nie unieważnia zapisu i nie może być prezentowana jako potwierdzona synchronizacja urządzenia.

Przełączanie zakładek zachowuje roboczą edycję; zamknięcie okna z niezapisanymi zmianami wymaga wyboru `Save / Discard / Cancel`. Lokalna korekta jasności na CYD Display pozostaje skuteczna przez chwilowe rozłączenie: odtworzenie połączenia nie oznacza jawnego zastosowania jasności z Windows Agent, natomiast jawny zapis ustawień stosuje również zapisaną jasność.

Dla poprawnego zachowania również po zapisie offline, Display Appearance przechowuje `brightnessRevision`. Firmware zachowuje ostatnią zastosowaną rewizję w NVS: taka sama rewizja podczas hydration nie usuwa Local Brightness Override, nowa rewizja oznacza jawnie zapisane ustawienia i stosuje ich jasność. `applyBrightness: true` umożliwia ponowne jawne zastosowanie tej samej wartości przy aktywnym połączeniu.
