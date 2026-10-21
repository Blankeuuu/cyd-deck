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
enum class Page : uint8_t { Media = 0, Shortcuts = 1, Pc = 2, Info = 3, Home = 4 };
enum class IntentType : uint8_t { Previous, TogglePlayback, Next, SetVolume, RunShortcut, Recalibrate, RestartDevice, FactoryReset, SettingsUpdated, RefreshUi, ShortcutsUpdated, ToggleMute, TestTouch, SetBrightness, OpenSetupPortal, ToggleDebugMode };
struct Intent { IntentType type; int16_t value; uint32_t queuedAtMs; constexpr Intent(IntentType t = IntentType::Previous, int16_t v = 0, uint32_t at = 0) : type(t), value(v), queuedAtMs(at) {} };
struct MediaState { char title[48] = "No track"; char artist[48] = "Waiting for PC"; char album[48] = ""; char coverId[kCoverIdLength + 1] = ""; CoverFetchState cover; uint32_t positionMs = 0; uint32_t durationMs = 0; bool playing = false; uint32_t timelineAtMs = 0; bool timelineAnchored = false; };
inline uint32_t mediaPositionAt(const MediaState& media, uint32_t nowMs) {
  uint64_t position = media.positionMs;
  if (media.playing && media.timelineAnchored) position += static_cast<uint32_t>(nowMs - media.timelineAtMs);
  if (media.durationMs && position > media.durationMs) position = media.durationMs;
  return static_cast<uint32_t>(position > UINT32_MAX ? UINT32_MAX : position);
}
struct DisplayIdleDecision { bool off = false; bool dimmed = false; bool resetIdleTimer = false; };
enum class PendingSetupDecision : uint8_t { Wait, Success, Failure };
struct AudioState {
  uint8_t reportedVolume = 0;
  uint8_t requestedVolume = 0;
  bool available = false;
  bool muted = false;
  bool pending = false;
  bool failed = false;
  uint32_t requestedAtMs = 0;
  uint32_t revision = 0;
  char requestId[40] = "";
};
inline uint8_t displayedVolume(const AudioState& audio) {
  return audio.pending ? audio.requestedVolume : audio.reportedVolume;
}
inline void resetAudioSession(AudioState& audio) {
  audio.revision = 0;
  audio.pending = false;
  audio.failed = false;
  audio.requestId[0] = '\0';
}
inline void requestVolume(AudioState& audio, uint8_t value, uint32_t now) {
  audio.requestedVolume = value;
  audio.requestedAtMs = now;
  audio.pending = true;
  audio.failed = false;
  audio.requestId[0] = '\0';
}
inline bool receiveVolume(AudioState& audio, uint8_t value, uint32_t revision, const char* requestId) {
  if (revision && audio.revision && static_cast<int32_t>(revision - audio.revision) < 0) return false;
  if (revision) audio.revision = revision;
  audio.reportedVolume = value;
  audio.available = true;
  if (audio.pending && ((requestId && audio.requestId[0] && std::strcmp(requestId, audio.requestId) == 0) ||
      (!revision && value == audio.requestedVolume))) audio.pending = false;
  return true;
}
struct SystemState {
  uint32_t freeHeap = 0;
  uint32_t minHeap = 0;
  uint32_t largestBlock = 0;
  uint8_t cpuPercent = 0;
  uint8_t ramPercent = 0;
  uint8_t gpuPercent = 0;
  int16_t tempCelsius = 0;
  bool gpuAvailable = false;
  bool tempAvailable = false;
  bool cpuAvailable = false;
  bool ramAvailable = false;
  bool ramCapacityAvailable = false;
  bool pcStatsReceived = false;
  uint32_t pcStatsAtMs = 0;
  uint32_t ramUsedMb = 0;
  uint32_t ramTotalMb = 0;
  char hostDate[40] = "";
  int16_t hostLocalMinutes = -1;
  uint32_t hostTimeSyncedAtSec = 0;
};
constexpr uint32_t kPcStatsFreshMs = 5000;
inline bool pcMetricsFresh(const SystemState& state, bool connected, uint32_t nowMs) {
  return connected && state.pcStatsReceived && nowMs - state.pcStatsAtMs < kPcStatsFreshMs;
}
struct StorageState { bool mounted = false; bool ready = false; uint32_t totalMb = 0; uint32_t freeMb = 0; char status[32] = "not started"; };
struct DeviceInfoState { char firmwareVersion[16] = "1.4.0"; uint32_t uptimeSeconds = 0; };
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
  char colors[6][8] = {};
  uint8_t iconPixels[6][1728] = {}; // 24x24 RGB565 plane, then 8-bit alpha plane.
  bool hasIconPixels[6] = {};
  uint32_t iconRevision = 0;
};
inline void resetShortcuts(ShortcutState& shortcuts) {
  ++shortcuts.iconRevision;
  for (std::size_t i = 0; i < 6; ++i) {
    std::memcpy(shortcuts.names[i], "No shortcut", sizeof("No shortcut"));
    shortcuts.ids[i][0] = '\0';
    shortcuts.icons[i][0] = '\0';
    shortcuts.colors[i][0] = '\0';
    shortcuts.hasIconPixels[i] = false;
    std::memset(shortcuts.iconPixels[i], 0, sizeof(shortcuts.iconPixels[i]));
    std::memcpy(shortcuts.displayModes[i], "label", sizeof("label"));
  }
}
struct AppearanceState {
  uint32_t accent = 0x7EAFE3;
  uint32_t background = 0x101216;
  uint8_t theme = 0;
  uint8_t buttonStyle = 1;
  uint8_t tileColorScheme = 0;
  uint8_t standbyStyle = 0;
  uint8_t shortcutsLayout = 0;
  uint8_t brightness = 70;
  uint32_t dimAfterSeconds = 300;
  uint8_t dimBrightness = 20;
  uint32_t screenOffAfterSeconds = 1800;
  bool wakeOnTouch = true;
  Page pageOrder[kPageCount] = {Page::Media, Page::Shortcuts, Page::Pc, Page::Info};
  bool pageVisible[kPageCount] = {true, true, true, true};
};
struct UiState { Page page = Page::Home; bool volumeOpen = false; uint8_t localBrightness = 0; char brightnessRevision[33] = ""; bool calibrationValid = false; bool standby = false; bool debugMode = false; };
inline void applyBrightnessRevision(UiState& ui, const char* revision, bool force) {
  const bool valid = revision && std::strlen(revision) == 32 &&
      std::all_of(revision, revision + 32, [](char c) { return std::isxdigit(static_cast<unsigned char>(c)) != 0; });
  if (force || (valid && std::strcmp(ui.brightnessRevision, revision) != 0)) ui.localBrightness = 0;
  if (valid) std::memcpy(ui.brightnessRevision, revision, 33);
}
struct LoopPerformance {
  uint32_t loopMaxUs = 0;
  uint32_t renderMaxUs = 0;
  uint32_t networkMaxUs = 0;
  uint32_t slowLoops = 0;
  uint32_t wifiDiscoveryCount = 0;
};
struct AppState {
  char commandFeedback[48] = "";
  uint32_t feedbackAtMs = 0;
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

inline uint32_t sanitizeCydBackground(uint32_t bg, uint8_t theme) {
  if (theme == 1) return bg;
  if (bg == 0x101216 || bg == 0x0B0D12 || bg == 0x000000) return bg;
  const uint32_t r = (bg >> 16) & 0xFF;
  const uint32_t g = (bg >> 8) & 0xFF;
  const uint32_t b = bg & 0xFF;
  const uint32_t luma = (r * 77 + g * 150 + b * 29) >> 8;
  if (luma <= 42) return bg;
  const uint32_t dr = std::min<uint32_t>(36, 10 + (r * 18) / 100);
  const uint32_t dg = std::min<uint32_t>(38, 12 + (g * 18) / 100);
  const uint32_t db = std::min<uint32_t>(46, 16 + (b * 20) / 100);
  return (dr << 16) | (dg << 8) | db;
}

inline void normalizeAppearance(AppearanceState& appearance) {
  appearance.theme = appearance.theme > 1 ? 0 : appearance.theme;
  if (appearance.theme == 1 && appearance.background == 0x101216) appearance.background = 0xF3F5F7;
  if (appearance.theme == 1 && appearance.accent == 0x7EAFE3) appearance.accent = 0x276EF1;
  if (appearance.theme == 0 && appearance.background == 0xF3F5F7) appearance.background = 0x101216;
  if (appearance.theme == 0 && appearance.accent == 0x276EF1) appearance.accent = 0x7EAFE3;
  appearance.buttonStyle = appearance.buttonStyle > 2 ? 1 : appearance.buttonStyle;
  appearance.tileColorScheme = appearance.tileColorScheme > 2 ? 0 : appearance.tileColorScheme;
  appearance.standbyStyle = appearance.standbyStyle > 2 ? 0 : appearance.standbyStyle;
  appearance.shortcutsLayout = appearance.shortcutsLayout > 4 ? 0 : appearance.shortcutsLayout;
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
  for (uint8_t i = 0; i < kPageCount; ++i) appearance.pageVisible[i] = true;
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

namespace nvs_keys {
constexpr const char* kAppearanceNamespace = "appearance";
constexpr const char* kDebugModeKey = "debug";
constexpr bool kDebugModeDefault = false;
}

inline bool toggleDebugMode(UiState& ui) {
  ui.debugMode = !ui.debugMode;
  return ui.debugMode;
}

inline bool shouldShowCommandToast(const AppState& state, uint32_t nowMs, uint32_t timeoutMs = 2000) {
  return state.ui.debugMode && state.commandFeedback[0] != '\0' && (nowMs - state.feedbackAtMs < timeoutMs);
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
  bool push(Intent intent) {
    // Coalesce only adjacent volume values; never reorder media/shortcut actions.
    if (size_ && intent.type == IntentType::SetVolume && items_[(tail_ + Capacity - 1) % Capacity].type == intent.type) {
      items_[(tail_ + Capacity - 1) % Capacity] = intent; return true;
    }
    if (intent.type == IntentType::RefreshUi) {
      for (uint8_t i = 0; i < size_; ++i) if (items_[(head_ + i) % Capacity].type == intent.type) return true;
    }
    if (size_ == Capacity) {
      // Redundant repaint requests must never crowd out a physical control.
      for (uint8_t i = 0; i < size_; ++i) if (items_[(head_ + i) % Capacity].type == IntentType::RefreshUi) {
        for (uint8_t j = i; j + 1 < size_; ++j) items_[(head_ + j) % Capacity] = items_[(head_ + j + 1) % Capacity];
        tail_ = (tail_ + Capacity - 1) % Capacity; --size_; break;
      }
    }
    if (size_ == Capacity) return false;
    items_[tail_] = intent; tail_=(tail_+1)%Capacity; ++size_; return true;
  }
  bool pop(Intent& intent) { if (!size_) return false; intent=items_[head_]; head_=(head_+1)%Capacity; --size_; return true; }
  static constexpr uint8_t Capacity=16;
 private:
  Intent items_[Capacity]{};
  uint8_t head_=0,tail_=0,size_=0;
};
}
