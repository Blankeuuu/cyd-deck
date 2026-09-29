# LVGL app shell (Phase 3)

The cyd28_ili9341_app and cyd28_st7789_app PlatformIO environments are separate from the hardware diagnostic environments. They compile app_main.cpp plus UI/touch/display modules; diagnostic environments retain main.cpp and diagnostics/. Default environments remain the diagnostic pair.

LVGL is pinned to 9.3.0 and LovyanGFX to 1.2.7. The display has one 320x20 RGB565 partial-render buffer (12,800 bytes), with no full-screen frame buffer or PSRAM request. Dirty rectangles flush through LovyanGFX.

The shell presents media, six shortcut intents, and offline PC status. Horizontal swipes navigate; media controls, volume slider, shortcut tiles, and recalibration enqueue bounded intents. Firmware prints those intents to Serial only. No PC agent exists in this phase, so playback, shortcuts, volume, and connection are not represented as executed or confirmed.

Touch uses the existing XPT2046 software-SPI driver and the existing touch NVS affine keys (xx, xy, xo, yx, yy, yo, valid). Without valid coefficients, a five-target calibration starts on boot. Tap and release each target. The first three points fit a candidate; points four and five validate it. Only an accepted candidate is persisted. PC page has a recalibration request.

## Build and smoke tests

From the project root, run these sequentially:

    powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_ili9341_app
    powershell -ExecutionPolicy Bypass -File scripts/build-firmware.ps1 run -e cyd28_st7789_app
    powershell -ExecutionPolicy Bypass -File scripts/test-ui-logic.ps1

On hardware, first boot should show calibration targets, then the media page. Tap Volume and the 3x2 shortcuts and verify Serial logs queued intents without claiming PC action. Swipe left/right and verify each gesture changes page once; vertical motion and taps must not navigate. PC page remains explicitly offline. These observations require physical hardware and are not inferred from a successful compile.

Do not upload until the controller is selected for the connected panel and the operator explicitly chooses the variant.