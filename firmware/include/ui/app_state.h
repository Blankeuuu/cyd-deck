#pragma once
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <algorithm>
#include <cctype>
#include <cstdlib>
#include <initializer_list>

#include "network/cover_art.h"

namespace streamdeck {
constexpr std::size_t kMaxShortcutIdLength = 64;
constexpr uint8_t kPageCount = 4;
enum class Page : uint8_t { Media = 0, Shortcuts = 1, Pc = 2, Info = 3 };
enum class IntentType : uint8_t { Previous, TogglePlayback, Next, SetVolume, RunShortcut, Recalibrate, RestartDevice, FactoryReset, SettingsUpdated, RefreshUi, ShortcutsUpdated, ToggleMute };
struct Intent { IntentType type; int16_t value; constexpr Intent(IntentType t = IntentType::Previous, int16_t v = 0) : type(t), value(v) {} };
struct MediaState { char title[48] = "No track"; char artist[48] = "Waiting for PC"; char album[48] = ""; char coverId[kCoverIdLength + 1] = ""; CoverFetchState cover; uint32_t positionMs = 0; uint32_t durationMs = 0; bool playing = false; uint32_t timelineAtMs = 0; bool timelineAnchored = false; };
inline uint32_t mediaPositionAt(const MediaState& media, uint32_t nowMs) {
  uint64_t position = media.positionMs;
  if (media.playing && media.timelineAnchored) position += static_cast<uint32_t>(nowMs - media.timelineAtMs);
  if (media.durationMs && position > media.durationMs) position = media.durationMs;
  return static_cast<uint32_t>(position > UINT32_MAX ? UINT32_MAX : position);
}
struct DisplayIdleDecision { bool off = false; bool dimmed = false; bool resetIdleTimer = false; };
enum class PendingSetupDecision : uint8_t { Wait, Success, Failure };
struct AudioState { uint8_t reportedVolume = 0; bool available = false; bool muted = false; };
struct SystemState { uint32_t freeHeap = 0; uint32_t minHeap = 0; uint32_t largestBlock = 0; };
struct StorageState { bool mounted = false; bool ready = false; uint32_t totalMb = 0; uint32_t freeMb = 0; char status[32] = "not started"; };
struct DeviceInfoState { char firmwareVersion[16] = "1.1.0"; uint32_t uptimeSeconds = 0; };
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
  char icons[6][16] = {"","","","","",""};
  char displayModes[6][6] = {"label","label","label","label","label","label"};
};
inline void resetShortcuts(ShortcutState& shortcuts) {
  for (std::size_t i = 0; i < 6; ++i) {
    std::memcpy(shortcuts.names[i], "No shortcut", sizeof("No shortcut"));
    shortcuts.ids[i][0] = '\0';
    shortcuts.icons[i][0] = '\0';
    std::memcpy(shortcuts.displayModes[i], "label", sizeof("label"));
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
  Page pageOrder[kPageCount] = {Page::Media, Page::Shortcuts, Page::Pc, Page::Info};
  bool pageVisible[kPageCount] = {true, true, true, true};
};
struct UiState { Page page = Page::Media; bool volumeOpen = false; };
struct LoopPerformance {
  uint32_t loopMaxUs = 0;
  uint32_t renderMaxUs = 0;
  uint32_t networkMaxUs = 0;
  uint32_t slowLoops = 0;
  uint32_t wifiDiscoveryCount = 0;
};
struct AppState {
  LoopPerformance performance;
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

  bool seen[kPageCount] = {};
  Page normalized[kPageCount] = {Page::Media, Page::Shortcuts, Page::Pc, Page::Info};
  uint8_t count = 0;
  for (Page page : appearance.pageOrder) {
    const uint8_t index = pageIndex(page);
    if (index < kPageCount && !seen[index]) {
      normalized[count++] = page;
      seen[index] = true;
    }
  }
  for (Page page : {Page::Media, Page::Shortcuts, Page::Pc, Page::Info}) {
    const uint8_t index = pageIndex(page);
    if (!seen[index]) normalized[count++] = page;
  }
  for (uint8_t i = 0; i < kPageCount; ++i) appearance.pageOrder[i] = normalized[i];
  appearance.pageVisible[pageIndex(Page::Info)] = true;
}

inline DisplayIdleDecision computeDisplayIdleDecision(const AppState& state, uint32_t idleSeconds,
    bool currentOff, bool currentDimmed) {
  (void)currentOff;
  (void)currentDimmed;
  DisplayIdleDecision decision;
  // Authenticated Agent connection keeps the desk display awake on USB or Wi-Fi.
  // Restart the idle countdown when the PC disconnects; Wi-Fi alone is not enough.
  if (state.connection.agentConnected) {
    decision.resetIdleTimer = true;
    return decision;
  }
  decision.off = state.appearance.screenOffAfterSeconds > 0 &&
      idleSeconds >= state.appearance.screenOffAfterSeconds;
  decision.dimmed = !decision.off && state.appearance.dimAfterSeconds > 0 &&
      idleSeconds >= state.appearance.dimAfterSeconds;
  return decision;
}

inline PendingSetupDecision decidePendingSetupResult(bool connected, bool ssidMatches,
    uint32_t elapsedMs, uint32_t timeoutMs) {
  if (connected && ssidMatches) return PendingSetupDecision::Success;
  if (elapsedMs >= timeoutMs) return PendingSetupDecision::Failure;
  return PendingSetupDecision::Wait;
}

inline bool asciiEqualsIgnoreCase(char a, char b) {
  return std::tolower(static_cast<unsigned char>(a)) ==
      std::tolower(static_cast<unsigned char>(b));
}

inline bool startsWithIgnoreCase(const char* value, const char* prefix) {
  if (!value || !prefix) return false;
  while (*prefix) {
    if (!*value || !asciiEqualsIgnoreCase(*value, *prefix)) return false;
    ++value;
    ++prefix;
  }
  return true;
}

struct HttpContentLength {
  bool present = false;
  bool valid = true;
  std::size_t length = 0;
};

enum class HttpRequestReadDecision : uint8_t { Incomplete, Complete, Invalid };

inline HttpContentLength parseHttpContentLength(const char* headers,
    std::size_t maxContentLength) {
  HttpContentLength result;
  if (!headers) return result;
  const char* line = headers;
  while (*line) {
    const char* lineEnd = std::strstr(line, "\015\012");
    const std::size_t lineLength = lineEnd
        ? static_cast<std::size_t>(lineEnd - line)
        : std::strlen(line);
    if (lineLength == 0) return result;
    if (startsWithIgnoreCase(line, "Content-Length:")) {
      const char* value = line + std::strlen("Content-Length:");
      while (*value == ' ' || *value == '\t') ++value;
      if (value >= line + lineLength || *value < '0' || *value > '9') {
        result.valid = false;
        return result;
      }
      std::size_t length = 0;
      while (*value >= '0' && *value <= '9') {
        const std::size_t digit = static_cast<std::size_t>(*value - '0');
        if (digit > maxContentLength || length > (maxContentLength - digit) / 10) {
          result.valid = false;
          return result;
        }
        length = length * 10 + (*value - '0');
        ++value;
      }
      while (value < line + lineLength && (*value == ' ' || *value == '\t')) ++value;
      if (value < line + lineLength) {
        result.valid = false;
        return result;
      }
      if (result.present && result.length != length) {
        result.valid = false;
        return result;
      }
      result.present = true;
      result.length = length;
    }
    if (!lineEnd) break;
    line = lineEnd + 2;
  }
  return result;
}

inline int parseHttpContentLength(const char* headers) {
  const HttpContentLength parsed = parseHttpContentLength(headers,
      static_cast<std::size_t>(2147483647));
  return parsed.valid && parsed.present ? static_cast<int>(parsed.length) : 0;
}

inline const char* findHttpHeaderEnd(const char* request) {
  return request ? std::strstr(request, "\015\012\015\012") : nullptr;
}

inline HttpRequestReadDecision decideHttpRequestRead(const char* request,
    std::size_t requestLength, std::size_t maxContentLength) {
  const char* headerEnd = findHttpHeaderEnd(request);
  if (!headerEnd) return HttpRequestReadDecision::Incomplete;
  const std::size_t headerBytes = static_cast<std::size_t>(headerEnd - request) + 4;
  HttpContentLength contentLength = parseHttpContentLength(request, maxContentLength);
  if (!contentLength.valid) return HttpRequestReadDecision::Invalid;
  if (!contentLength.present && startsWithIgnoreCase(request, "POST "))
    return HttpRequestReadDecision::Invalid;
  if (!contentLength.present) return HttpRequestReadDecision::Complete;
  if (requestLength > headerBytes + contentLength.length)
    return HttpRequestReadDecision::Invalid;
  return requestLength >= headerBytes + contentLength.length
      ? HttpRequestReadDecision::Complete
      : HttpRequestReadDecision::Incomplete;
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
