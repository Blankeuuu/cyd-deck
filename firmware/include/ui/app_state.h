#pragma once
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <initializer_list>

#include "network/cover_art.h"

namespace streamdeck {
constexpr std::size_t kMaxShortcutIdLength = 64;
enum class Page : uint8_t { Media, Shortcuts, Pc, Info };
enum class IntentType : uint8_t { Previous, TogglePlayback, Next, SetVolume, RunShortcut, Recalibrate, RestartDevice, FactoryReset, SettingsUpdated, RefreshUi };
struct Intent { IntentType type; int16_t value; constexpr Intent(IntentType t = IntentType::Previous, int16_t v = 0) : type(t), value(v) {} };
struct MediaState { char title[48] = "No track"; char artist[48] = "Waiting for PC"; char album[48] = ""; char coverId[kCoverIdLength + 1] = ""; CoverFetchState cover; uint32_t positionMs = 0; uint32_t durationMs = 0; bool playing = false; };
struct AudioState { uint8_t reportedVolume = 0; bool available = false; };
struct SystemState { uint32_t freeHeap = 0; uint32_t minHeap = 0; uint32_t largestBlock = 0; };
struct StorageState { bool mounted = false; bool ready = false; uint32_t totalMb = 0; uint32_t freeMb = 0; char status[32] = "not started"; };
struct DeviceInfoState { char firmwareVersion[16] = "1.0.0"; uint32_t uptimeSeconds = 0; };
struct ConnectionState {
  bool online = false;
  bool wifiConnected = false;
  bool agentConnected = false;
  int16_t rssi = 0;
  char ssid[33] = "";
  char deviceIp[16] = "";
  char agentHost[32] = "";
};
struct OtaState { bool enabled = false; bool running = false; uint8_t progress = 0; char status[32] = "OTA unavailable"; };
struct ShortcutState {
  char names[6][20] = {"No shortcut","No shortcut","No shortcut","No shortcut","No shortcut","No shortcut"};
  char ids[6][kMaxShortcutIdLength + 1] = {"","","","","",""};
};
inline void resetShortcuts(ShortcutState& shortcuts) {
  for (std::size_t i = 0; i < 6; ++i) {
    std::memcpy(shortcuts.names[i], "No shortcut", sizeof("No shortcut"));
    shortcuts.ids[i][0] = '\0';
  }
}
struct AppearanceState {
  uint32_t accent = 0x7EAFE3;
  uint32_t background = 0x101216;
  uint8_t theme = 0;
  uint8_t buttonStyle = 1;
  uint8_t brightness = 70;
  uint32_t dimAfterSeconds = 300;
  uint8_t dimBrightness = 20;
  uint32_t screenOffAfterSeconds = 1800;
  bool wakeOnTouch = true;
  Page pageOrder[4] = {Page::Media, Page::Shortcuts, Page::Pc, Page::Info};
  bool pageVisible[4] = {true, true, true, true};
};
struct UiState { Page page = Page::Media; bool volumeOpen = false; };
struct AppState {
  MediaState media;
  AudioState audio;
  SystemState system;
  StorageState storage;
  DeviceInfoState device;
  ConnectionState connection;
  OtaState ota;
  ShortcutState shortcuts;
  AppearanceState appearance;
  UiState ui;
};

inline uint8_t pageIndex(Page page) {
  return static_cast<uint8_t>(page);
}

inline uint8_t clampPercent(int value, uint8_t minValue = 0, uint8_t maxValue = 100) {
  if (value < minValue) return minValue;
  if (value > maxValue) return maxValue;
  return static_cast<uint8_t>(value);
}

inline uint32_t clampSeconds(int value, uint32_t minValue, uint32_t maxValue) {
  if (value < 0) return minValue;
  const uint32_t unsignedValue = static_cast<uint32_t>(value);
  if (unsignedValue < minValue) return minValue;
  if (unsignedValue > maxValue) return maxValue;
  return unsignedValue;
}

inline void normalizeAppearance(AppearanceState& appearance) {
  appearance.theme = appearance.theme > 1 ? 0 : appearance.theme;
  if (appearance.theme == 1 && appearance.background == 0x101216) appearance.background = 0xF3F5F7;
  if (appearance.theme == 1 && appearance.accent == 0x7EAFE3) appearance.accent = 0x276EF1;
  if (appearance.theme == 0 && appearance.background == 0xF3F5F7) appearance.background = 0x101216;
  if (appearance.theme == 0 && appearance.accent == 0x276EF1) appearance.accent = 0x7EAFE3;
  appearance.buttonStyle = appearance.buttonStyle > 2 ? 1 : appearance.buttonStyle;
  appearance.brightness = clampPercent(appearance.brightness, 10, 100);
  appearance.dimBrightness = clampPercent(appearance.dimBrightness, 1, appearance.brightness);
  appearance.dimAfterSeconds = clampSeconds(appearance.dimAfterSeconds, 0, 86400);
  appearance.screenOffAfterSeconds = clampSeconds(appearance.screenOffAfterSeconds, 0, 86400);

  bool seen[4] = {};
  Page normalized[4] = {Page::Media, Page::Shortcuts, Page::Pc, Page::Info};
  uint8_t count = 0;
  for (Page page : appearance.pageOrder) {
    const uint8_t index = pageIndex(page);
    if (index < 4 && !seen[index]) {
      normalized[count++] = page;
      seen[index] = true;
    }
  }
  for (Page page : {Page::Media, Page::Shortcuts, Page::Pc, Page::Info}) {
    const uint8_t index = pageIndex(page);
    if (!seen[index]) normalized[count++] = page;
  }
  for (uint8_t i = 0; i < 4; ++i) appearance.pageOrder[i] = normalized[i];
  appearance.pageVisible[pageIndex(Page::Info)] = true;
}
class IntentQueue {
 public:
  bool push(Intent intent) { if (size_ == Capacity) return false; items_[tail_] = intent; tail_=(tail_+1)%Capacity; ++size_; return true; }
  bool pop(Intent& intent) { if (!size_) return false; intent=items_[head_]; head_=(head_+1)%Capacity; --size_; return true; }
  static constexpr uint8_t Capacity=16;
 private:
  Intent items_[Capacity]{};
  uint8_t head_=0,tail_=0,size_=0;
};
}
