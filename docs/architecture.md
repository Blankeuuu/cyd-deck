# Architektura

Katalog projects/stream-deck jest korzeniem monorepo bez dodatkowego poziomu pc-dock. Firmware znajduje się w firmware/, przyszły Windows Agent w agent/, typy protokołu w shared/protocol/. Bieżący zakres to Hardware Diagnostic Mode. ILI9341 i ST7789 współdzielą diagnostic app; wariant wybiera wyłącznie panel HAL. TFT i SD używają dwóch hostów hardware SPI, a XPT2046 software SPI. Agent, LVGL, sieć i docelowe funkcje docka należą do późniejszych etapów po hardware diagnostics.
