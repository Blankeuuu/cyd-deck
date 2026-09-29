#pragma once

#include <cstddef>
#include <cstdint>
#include <algorithm>
#include <cstdio>
#include <cstring>

#include "ui/app_state.h"

namespace streamdeck::protocol {

constexpr int kVersion = 1;
constexpr uint16_t kDefaultPort = 8765;
constexpr const char* kWebSocketPath = "/ws";
constexpr const char* kMdnsService = "streamdeck";
constexpr const char* kMdnsProto = "tcp";

constexpr const char* kHello = "hello";
constexpr const char* kMediaCommand = "media.command";
constexpr const char* kAudioSetVolume = "audio.setVolume";
constexpr const char* kShortcutExecute = "shortcut.execute";
constexpr const char* kDeviceRestart = "device.restart";
constexpr const char* kDeviceFactoryReset = "device.factoryReset";
constexpr const char* kDeviceSettings = "device.settings";
constexpr const char* kPing = "ping";
constexpr const char* kMediaUpdate = "media.update";
constexpr const char* kMediaTimeline = "media.timeline";
constexpr const char* kMediaPlayback = "media.playback";
constexpr const char* kAudioUpdate = "audio.update";
constexpr const char* kSystemStats = "system.stats";
constexpr const char* kShortcutsUpdate = "shortcuts.update";
constexpr const char* kShortcutsReport = "shortcuts.report";
constexpr const char* kConnectionState = "connection.state";
constexpr const char* kPairingToken = "pairing.token";
constexpr const char* kAck = "ack";
constexpr const char* kPong = "pong";

constexpr std::size_t kMaxShortcutsPerReport = 6;
constexpr std::size_t kShortcutReportMaxLabelLength = 19;
constexpr const char* kShortcutReportActionType = "unknown";
constexpr std::size_t kShortcutsReportPrefixLength = sizeof("{\"protocol\":1,\"type\":\"shortcuts.report\",\"data\":{\"shortcuts\":[") - 1;
constexpr std::size_t kShortcutsReportSuffixLength = sizeof("]}}") - 1;
constexpr std::size_t kShortcutReportEntryMaxLength =
    (sizeof("{\"id\":") - 1) + 2 + (kMaxShortcutIdLength * 2) +
    (sizeof(",\"label\":") - 1) + 2 + (kShortcutReportMaxLabelLength * 2) +
    (sizeof(",\"actionType\":\"") - 1) + (sizeof("unknown") - 1) +
    (sizeof("\"}") - 1);
constexpr std::size_t kShortcutsReportJsonCapacity =
    kShortcutsReportPrefixLength +
    (kMaxShortcutsPerReport * kShortcutReportEntryMaxLength) +
    (kMaxShortcutsPerReport - 1) +
    kShortcutsReportSuffixLength +
    1;
static_assert(sizeof(((ShortcutState*)nullptr)->names[0]) - 1 == kShortcutReportMaxLabelLength,
    "Shortcut report sizing must match ShortcutState label capacity");

inline bool appendText(char*& cursor, std::size_t& remaining, const char* text) {
  if (!text) return true;
  while (*text) {
    if (remaining <= 1) return false;
    *cursor++ = *text++;
    --remaining;
  }
  *cursor = '\0';
  return true;
}

inline bool appendJsonString(char*& cursor, std::size_t& remaining, const char* value) {
  if (!appendText(cursor, remaining, "\"")) return false;
  for (const char* source = value ? value : ""; *source; ++source) {
    const char c = *source;
    if (c == '"' || c == '\\') {
      if (!appendText(cursor, remaining, "\\")) return false;
      if (remaining <= 1) return false;
      *cursor++ = c;
      --remaining;
      *cursor = '\0';
    } else if (static_cast<unsigned char>(c) >= 0x20) {
      if (remaining <= 1) return false;
      *cursor++ = c;
      --remaining;
      *cursor = '\0';
    }
  }
  return appendText(cursor, remaining, "\"");
}

inline bool readQuotedField(const char* json, std::size_t length, const char* field,
                            char* out, std::size_t outSize) {
  if (!json || !field || !out || outSize == 0) return false;
  out[0] = '\0';
  char needle[32]{};
  const int needleLength = std::snprintf(needle, sizeof(needle), "\"%s\"", field);
  if (needleLength <= 0 || static_cast<std::size_t>(needleLength) >= sizeof(needle)) return false;
  const char* begin = json;
  const char* end = json + length;
  const char* found = std::search(begin, end, needle, needle + needleLength);
  if (found == end) return false;
  const char* read = found + needleLength;
  while (read < end && (*read == ' ' || *read == '\t' || *read == '\r' || *read == '\n')) ++read;
  if (read >= end || *read != ':') return false;
  ++read;
  while (read < end && (*read == ' ' || *read == '\t' || *read == '\r' || *read == '\n')) ++read;
  if (read >= end || *read != '"') return false;
  ++read;
  std::size_t written = 0;
  while (read < end && *read != '"') {
    char value = *read;
    if (value == '\\' && read + 1 < end) value = *++read;
    if (written + 1 < outSize) out[written++] = value;
    ++read;
  }
  out[written] = '\0';
  return true;
}

inline void copyShortcutText(char* target, std::size_t targetSize, const char* value, const char* fallback = "") {
  if (targetSize == 0) return;
  const char* source = value && value[0] ? value : fallback;
  std::strncpy(target, source, targetSize - 1);
  target[targetSize - 1] = '\0';
}

inline const char* mediaCommandFor(IntentType type) {
  switch (type) {
    case IntentType::Previous:
      return "previous";
    case IntentType::TogglePlayback:
      return "playPause";
    case IntentType::Next:
      return "next";
    default:
      return "";
  }
}

inline bool isMediaIntent(IntentType type) {
  return type == IntentType::Previous ||
      type == IntentType::TogglePlayback ||
      type == IntentType::Next;
}

inline bool isDeviceControlIntent(IntentType type) {
  return type == IntentType::RestartDevice || type == IntentType::FactoryReset;
}

inline bool shouldSendHeartbeat(bool connected, bool authorized) {
  return connected && authorized;
}

inline bool isValidShortcutIdLength(const char* shortcutId) {
  return shortcutId && std::strlen(shortcutId) <= kMaxShortcutIdLength;
}

inline bool makeShortcutExecuteJson(const char* requestId, const char* shortcutId,
                                    char* out, std::size_t outSize) {
  if (!requestId || !shortcutId || !out || !isValidShortcutIdLength(shortcutId)) return false;
  const int written = std::snprintf(out, outSize,
      "{\"protocol\":1,\"type\":\"shortcut.execute\",\"requestId\":\"%s\",\"data\":{\"shortcutId\":\"%s\"}}",
      requestId, shortcutId);
  return written > 0 && static_cast<std::size_t>(written) < outSize;
}

inline void applyShortcutsUpdateJson(ShortcutState& shortcuts, const char* payload, std::size_t length) {
  resetShortcuts(shortcuts);
  if (!payload) return;
  uint8_t index = 0;
  const char* cursor = payload;
  const char* end = payload + length;
  constexpr char kIdNeedle[] = "\"id\"";
  while (cursor < end && index < 6) {
    const char* idAt = std::search(cursor, end, kIdNeedle, kIdNeedle + sizeof(kIdNeedle) - 1);
    if (idAt == end) break;
    const std::size_t remaining = static_cast<std::size_t>(end - idAt);
    char id[kMaxShortcutIdLength + 1]{};
    char label[20]{};
    readQuotedField(idAt, remaining, "id", id, sizeof(id));
    readQuotedField(idAt, remaining, "label", label, sizeof(label));
    if (id[0] != '\0') {
      copyShortcutText(shortcuts.names[index], sizeof(shortcuts.names[index]), label, shortcuts.names[index]);
      copyShortcutText(shortcuts.ids[index], sizeof(shortcuts.ids[index]), id, shortcuts.ids[index]);
    }
    ++index;
    cursor = idAt + 4;
  }
}

inline bool makeShortcutsReportJson(const ShortcutState& shortcuts, char* out, std::size_t outSize) {
  if (!out || outSize == 0) return false;
  out[0] = '\0';
  char* cursor = out;
  std::size_t remaining = outSize;
  if (!appendText(cursor, remaining, "{\"protocol\":1,\"type\":\"shortcuts.report\",\"data\":{\"shortcuts\":[")) return false;
  bool first = true;
  for (std::size_t i = 0; i < kMaxShortcutsPerReport; ++i) {
    if (shortcuts.ids[i][0] == '\0') continue;
    if (!first && !appendText(cursor, remaining, ",")) return false;
    first = false;
    if (!appendText(cursor, remaining, "{\"id\":")) return false;
    if (!appendJsonString(cursor, remaining, shortcuts.ids[i])) return false;
    if (!appendText(cursor, remaining, ",\"label\":")) return false;
    if (!appendJsonString(cursor, remaining, shortcuts.names[i])) return false;
    if (!appendText(cursor, remaining, ",\"actionType\":\"unknown\"}")) return false;
  }
  return appendText(cursor, remaining, "]}}");
}

}  // namespace streamdeck::protocol
