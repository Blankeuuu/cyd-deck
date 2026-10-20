# CYD-DECK

<div align="center">

![Platform](https://img.shields.io/badge/Platform-ESP32--2432S028R-blue?style=flat-square)
[![Firmware](https://img.shields.io/badge/Firmware-v1.4.0-success?style=flat-square)](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.4.0)
[![Windows Agent](https://img.shields.io/badge/Windows%20Agent-.NET%2010%20x64-512bd4?style=flat-square)](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.4.0)
![UI](https://img.shields.io/badge/Graphics-LVGL%209.3-orange?style=flat-square)
[![Web Flasher](https://img.shields.io/badge/Web%20Flasher-Online-teal?style=flat-square)](https://blankeuuu.github.io/cyd-deck/web-installer/)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

**A touchscreen desktop dock powered by ESP32 ("Cheap Yellow Display") and a companion Windows agent.**

[Web Installer (Browser)](https://blankeuuu.github.io/cyd-deck/web-installer/) • [Windows Setup](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.4.0) • [Overview](#overview) • [Features](#features) • [Installation Guide](#firmware-installation-cyd-esp32) • [Documentation](#documentation) • [Roadmap](#roadmap--todo) • [License](#license)

</div>

---

## Overview

**CYD-DECK** turns the affordable and popular **ESP32-2432S028R** development board (2.8" 320x240 touchscreen) into an interactive desktop controller and monitoring station for Windows PCs. It integrates media playback controls, real-time PC hardware monitoring, application hotkeys, macro shortcuts, and system controls into a cohesive graphical interface built with LVGL 9.3 and inspired by Apple HIG and Windows 11 Fluent Design principles.

Communication between the PC and the dock utilizes a low-latency **USB-Serial** primary connection with an automatic fallback to **Wi-Fi / WebSocket**, ensuring instant touch response and continuous operation even when physical cables are disconnected.

---

## Features

### Device Interface (CYD ESP32)
- **Home Screen**: Persistent dock clock (`HH:mm`), dynamic date display, compact media player with album art backdrop, and 4 quick-access tiles (148x44 px).
- **Deck Screen**: Flexible shortcut grids supporting 1, 2, 4, or 6 tiles, symmetrically centered icon and label alignment, built-in library of 21 vector glyphs, and support for custom 24x24 px icons decoded dynamically from web URLs.
- **Media Player**: Track metadata (title, artist, album), seek timeline with local 100 ms time interpolation, playback and volume controls, and 128x128 album art caching on microSD storage.
- **PC Resource Monitor**: Real-time telemetry graphs for CPU utilization, RAM usage (in megabytes and percentage), and aggregated GPU engine metrics via Windows PDH.
- **On-Device Settings**: Touch calibration, backlight brightness control, display sleep timers, and connection diagnostics.

### Windows Agent (Companion App)
- **System Tray Controls**: Context menu media player controls (Play/Pause, Previous/Next track, volume adjustment, Mute) and Windows balloon notifications on device connect/disconnect.
- **Windows 11 DWM Dark Mode**: Immersive dark titlebars and native rounded corners across all modal dialogs.
- **Modern Color Picker**: Dedicated dark-theme color selector with live previews, hex code input, RGB sliders, and quick accent presets.
- **USB Fault Tolerance**: Multi-port COM probing that gracefully handles busy ports and safely recovers from unexpected USB disconnects without application crashes.
- **Atomic Persistence**: Reliable offline profile configuration and shortcut saves without risking state corruption.

---

## Firmware Installation (CYD ESP32)

CYD-DECK is designed for the **ESP32-2432S028R** board with the **ST7789** display driver (320x240 landscape).

Choose the method that best fits your setup:

### Option 1: Online Browser Web Installer (ESP Web Tools - Recommended)

Flash your board directly from your browser without installing Python, PlatformIO, or extra tools:

1. Open the [CYD-DECK Web Installer](https://blankeuuu.github.io/cyd-deck/web-installer/) in **Google Chrome**, **Microsoft Edge**, or **Brave** on desktop.
2. Connect your CYD board to your computer using a micro-USB cable (ensure your cable supports data transfer, not charge-only).
3. Close the Windows Agent or any serial terminal/monitor using the port.
4. Click **Install Firmware** and select your board's serial port (e.g. `CH340` or `CP2102`).
5. Confirm installation. The process takes ~30 seconds, after which the device automatically reboots into the CYD-DECK interface.

> *Offline / Local alternative*: You can also run the web installer locally without an internet connection by running `.\scripts\launch-web-flasher.ps1` from PowerShell inside the repository folder.

---

### Option 2: Direct Command Line Flashing via `esptool.py`

If you prefer command-line tools:

1. Download the prebuilt firmware binary from [GitHub Releases](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.4.0):
   - **First-time install on a new board**: Download `firmware-ST7789-1.4.0-full.bin` (flashed at offset `0x0`).
   - **Upgrading an existing CYD-DECK**: Download `firmware-ST7789-1.4.0.bin` (flashed at offset `0x10000`, preserves Wi-Fi credentials and pairing).
2. Connect your CYD board to your computer using a micro-USB data cable.
3. Install `esptool` via Python (if not already installed):
   ```bash
   pip install esptool
   ```
4. Flash the board:
   - **First-time installation (merged full image at offset 0x0)**:
     ```bash
     esptool.py --chip esp32 --port COM5 --baud 460800 write_flash -z 0x0 firmware-ST7789-1.4.0-full.bin
     ```
   - **Upgrade existing device (application image at offset 0x10000)**:
     ```bash
     esptool.py --chip esp32 --port COM5 --baud 460800 write_flash -z 0x10000 firmware-ST7789-1.4.0.bin
     ```
   *(Replace `COM5` with your actual serial port name, e.g., `/dev/ttyUSB0` on Linux).*

---

### First Boot & Onboarding

1. **Touch Calibration**: On first power-up, tap each of the 5 calibration targets displayed on the screen and release.
2. **Connection Selection**:
   - **USB Cable (Recommended)**: Tap to use the dock directly over USB-Serial cable without Wi-Fi. When connecting a new device for the first time, approve the pairing dialog once in the Windows Agent.
   - **Wi-Fi Network**: Tap to launch the SoftAP configuration portal (`CYD-Deck-Setup`). Connect with your phone or PC, open `http://192.168.4.1/`, and submit your Wi-Fi SSID and password (no setup password required during initial onboarding).
   - *Cancel / Switch to USB*: If you decide not to configure Wi-Fi, tap **Use USB instead (No Wi-Fi)** at the bottom of the portal screen to switch to USB mode; this immediately disables Wi-Fi radio and cancels any pending network attempts.
3. **Changing Connection Later**: You can switch between Wi-Fi and USB anytime directly from the device without performing a factory reset:
   - Navigate to **Settings (Gear) > Device > Connection & status > Wi-Fi setup** to open the portal.
   - Tap **Use USB instead (No Wi-Fi)** on the portal screen to switch back to USB mode, turning off the Wi-Fi radio while preserving your saved Wi-Fi credentials for later use.
   - When accessing the portal over your home LAN after onboarding, the device setup password is required for security.

---

## Windows Agent Installation

The Windows Agent runs as a background companion service with a tray icon and a desktop management window. It requires Windows 10 (version 1809+) or Windows 11 (x64).

### Option 1: Windows Setup Installer (Recommended)

1. Download **`CYD-DECK-Setup-1.4.0-win-x64.exe`** from [GitHub Releases](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.4.0).
2. Run the installer. It installs the application for the current user into `%LOCALAPPDATA%\Programs\CYD-DECK` without requiring administrator privileges.
3. The installer bundles the .NET 10 runtime, SkiaSharp graphics engine, and creates Start Menu and optional Desktop shortcuts.
4. **Clean Uninstallation**: To uninstall, open Windows *Settings > Apps > Installed apps* and click **Uninstall**. The uninstaller automatically terminates any running background tray instance, removes autostart entries, and deletes all program files cleanly without file-in-use errors.

### Option 2: Portable Package

1. Download **`CYD-DECK-win-x64.zip`** from [GitHub Releases](https://github.com/Blankeuuu/cyd-deck/releases/tag/v1.4.0).
2. Extract the archive into any directory.
3. Launch `CYD-DECK.exe`.

---

## Building from Source

### Prerequisites
- Windows 10/11 x64, PowerShell 5.1+
- .NET 10 SDK
- Python 3.10+ and PlatformIO Core

### Build Windows Agent
```powershell
dotnet build agent/src/WindowsAgent/StreamDeck.WindowsAgent/StreamDeck.WindowsAgent.csproj -c Release
```

### Build ESP32 Firmware
```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app
```

### Run Automated Tests
```powershell
# Native C++ firmware logic and layout unit tests
powershell -ExecutionPolicy Bypass -File scripts/test-ui-logic.ps1

# Windows Agent unit and integration smoke tests
dotnet run --project agent/src/WindowsAgent/StreamDeck.WindowsAgent.Tests/StreamDeck.WindowsAgent.Tests.csproj
```

---

## Documentation

- [System Architecture](docs/architecture.md)
- [Agent <-> CYD Protocol Specification](docs/protocol.md)
- [User Interface Architecture](docs/ui.md)
- [Hardware & Display Guide (ST7789 vs ILI9341)](docs/hardware.md)
- [Windows Authenticode Code Signing & SmartScreen Guide](docs/code-signing-smartscreen.md)
- [Media Synchronization & Artwork Reliability Guide](docs/media-reliability.md)
- [In-App Software & Firmware Updates Guide](docs/software-updates.md)
- [Troubleshooting Guide](docs/troubleshooting.md)
- [Release Notes](docs/releases/)

---

## Roadmap / TODO

- [ ] **Windows SmartScreen & Code Signing** — Eliminate the *"Windows protected your PC"* SmartScreen warning when launching the Windows installer and `CYD-DECK.exe` binary through Authenticode code signing and trusted distribution.
  - *Status: In Progress / Blocked externally.* Verification and signing script (`scripts/sign-windows-artifacts.ps1`) and strict validation tests (19/19 PASS) implemented; live signing blocked pending maintainer SignPath Foundation application and foundation review. See [Code Signing Guide](docs/code-signing-smartscreen.md).
- [x] **Seamless In-App Auto-Updates (Agent & Firmware)** — Detect updates from GitHub Releases at startup or on request, then download and install with user confirmation. Firmware installation uses Wi-Fi OTA; USB-only devices use the Web Installer.
  - *Status: Implemented & Regression-Tested.* 136/136 updater assertions and Windows Agent smoke tests PASS, including minimized startup, unsaved changes, stale staging and tampering checks. Live installation and physical OTA remain pending; not yet released. See [Software Updates Guide](docs/software-updates.md).
- [ ] **Custom 3D-Printable CYD-DECK Enclosure** — Dedicated 3D-printable desktop enclosure and angled stand designed specifically for the ESP32-2432S028R (CYD-DECK) board.
  - *Out of scope for this software roadmap task, per maintainer decision.*
- [x] **Album Artwork Reliability (Spotify & Brave)** — Fix recurring cover art extraction and display failures for media sessions originating from desktop Spotify and Chromium/Brave browsers.
  - *Status: Implemented & Regression-Tested.* 3-attempt linear backoff retry (100/200 ms), 0-byte stream protection, and thumbnail reference cache invalidation verified in automated test suite (15/15 Covers, 3/3 Runtime regressions PASS). Note: Controlled mock/fixture validation only; physical streaming validation on CYD hardware pending. See [Media Reliability Guide](docs/media-reliability.md).
- [x] **Media Timeline & Progress Bar Synchronization** — Eliminate timeline jitter and stutter loops (e.g., rubberbanding 01s → 02s → 01s → 02s → 06s on CYD display) and resolve missing timeline progress (stuck at 00:00 on YouTube and web video playback) by stabilizing Windows Agent GSMTC timeline polling and CYD local interpolation.
  - *Status: Implemented & Regression-Tested.* WinRT GSMTC LastUpdatedTime/PlaybackRate extrapolation, active refresh counter with finally recovery, and atomic poll commit revalidation to prevent race conditions verified (22/22 Media, review probe PASS; protocol: 1 unchanged, no firmware clamp). Note: Controlled fixture testing; physical hardware validation pending. See [Media Reliability Guide](docs/media-reliability.md).

---

## Release History

- **[Release 1.4.0](docs/releases/1.4.0.md)** (Current) - On-device USB/Wi-Fi onboarding transport choice, dedicated Devices & Pairing sidebar view, SettingsWindow Page View Cache (< 50 ms navigation), and unified `CYD-DECK.exe` product identity & branding.
- **[Release 1.3.0](docs/releases/1.3.0.md)** - Browser-based Web Serial installer, system tray media and volume controls, Windows 11 DWM immersive dark mode, modern color picker, multi-port USB fault tolerance, and PDH PC hardware metrics.
- **[Release 1.2.0](docs/releases/1.2.0.md)** - Centered tile button icons and labels, 21-icon visual picker gallery, URL icon decoding to RGB565A8, and Windows Inno Setup installer.
- **[Release 1.1.0](docs/releases/1.1.0.md)** - MicroSD cache for album art, 100 ms timeline interpolation, and dock navigation bar with persistent clock.

---

## License

This project is licensed under the [MIT License](LICENSE).
Third-party component notices and licenses are documented in `THIRD-PARTY-NOTICES.txt`.
