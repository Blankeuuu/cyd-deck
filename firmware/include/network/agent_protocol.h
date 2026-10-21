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
  const char* read = begin;
  while (true) {
    const char* found = std::search(read, end, needle, needle + needleLength);
    if (found == end) return false;
    read = found + needleLength;
    while (read < end && (*read == ' ' || *read == '\t' || *read == '\r' || *read == '\n')) ++read;
    if (read >= end || *read != ':') continue;
    ++read;
    while (read < end && (*read == ' ' || *read == '\t' || *read == '\r' || *read == '\n')) ++read;
    // The envelope also has a data object. Only the string payload is a match.
    if (read < end && *read == '"') { ++read; break; }
  }
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

inline std::size_t utf8SafeLength(const char* value, std::size_t maxBytes) {
  std::size_t safe = 0;
  for (std::size_t i = 0; value && value[i] && i < maxBytes;) {
    const unsigned char c = static_cast<unsigned char>(value[i]);
    std::size_t length = 1;
    if ((c & 0x80U) == 0) length = 1;
    else if ((c & 0xE0U) == 0xC0U) length = 2;
    else if ((c & 0xF0U) == 0xE0U) length = 3;
    else if ((c & 0xF8U) == 0xF0U) length = 4;
    else break;
    if (i + length > maxBytes) break;
    bool continuation = true;
    for (std::size_t j = 1; j < length; ++j) {
      const unsigned char next = static_cast<unsigned char>(value[i + j]);
      if ((next & 0xC0U) != 0x80U) continuation = false;
    }
    if (!continuation) break;
    safe = i + length;
    i += length;
  }
  return safe;
}

inline void copyShortcutText(char* target, std::size_t targetSize, const char* value, const char* fallback = "") {
  if (targetSize == 0) return;
  const char* source = value && value[0] ? value : fallback;
  const std::size_t length = utf8SafeLength(source, targetSize - 1);
  std::memcpy(target, source, length);
  target[length] = '\0';
}

inline bool validShortcutDisplayMode(const char* value) {
  return value && (std::strcmp(value, "label") == 0 || std::strcmp(value, "icon") == 0 ||
      std::strcmp(value, "both") == 0);
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

// Fixed-size icon payload; strict length avoids accepting truncated images.
inline bool decodeShortcutIcon(const char* data, uint8_t* pixels) {
  if (std::strlen(data) != 2304) return false;
  auto value = [](char c) -> int {
    if (c >= 'A' && c <= 'Z') return c - 'A';
    if (c >= 'a' && c <= 'z') return c - 'a' + 26;
    if (c >= '0' && c <= '9') return c - '0' + 52;
    if (c == '+' || c == '-') return 62;
    if (c == '/' || c == '_') return 63;
    return -1;
  };
  for (size_t i = 0, out = 0; i < 2304; i += 4, out += 3) {
    int a = value(data[i]), b = value(data[i + 1]), c = value(data[i + 2]), d = value(data[i + 3]);
    if (a < 0 || b < 0 || c < 0 || d < 0) return false;
    const uint32_t triple = (a << 18) | (b << 12) | (c << 6) | d;
    pixels[out] = triple >> 16; pixels[out + 1] = triple >> 8; pixels[out + 2] = triple;
  }
  return true;
}

inline bool isEmptyShortcutSlot(const char* id, const char* icon) {
  return (icon && std::strcmp(icon, "empty") == 0) ||
      (id && std::strncmp(id, "__empty_slot_", 13) == 0);
}

inline void applyShortcutsUpdateJson(ShortcutState& shortcuts, const char* payload, std::size_t length) {
  resetShortcuts(shortcuts);
  if (!payload || length == 0) return;
  uint8_t index = 0;
  const char* cursor = payload;
  const char* end = payload + length;
  constexpr char kIdNeedle[] = "\"id\"";
  while (cursor < end && index < 6) {
    const char* idAt = std::search(cursor, end, kIdNeedle, kIdNeedle + sizeof(kIdNeedle) - 1);
    if (idAt == end) break;
    const char* objStart = idAt;
    while (objStart > cursor && *objStart != '{') --objStart;
    const char* objEnd = std::find(idAt, end, '}');
    const std::size_t objLen = static_cast<std::size_t>((objEnd == end ? end : objEnd) - objStart);
    char id[kMaxShortcutIdLength + 1]{};
    char label[20]{};
    char icon[16]{};
    char displayMode[6]{};
    char color[8]{};
    char iconData[2306]{};
    readQuotedField(objStart, objLen, "id", id, sizeof(id));
    readQuotedField(objStart, objLen, "label", label, sizeof(label));
    readQuotedField(objStart, objLen, "icon", icon, sizeof(icon));
    readQuotedField(objStart, objLen, "displayMode", displayMode, sizeof(displayMode));
    readQuotedField(objStart, objLen, "color", color, sizeof(color));
    readQuotedField(objStart, objLen, "iconData", iconData, sizeof(iconData));
    if (id[0] != '\0') {
      if (isEmptyShortcutSlot(id, icon)) {
        // Keep the Deck Tile position, but do not persist an executable ID.
        ++index;
        cursor = (objEnd == end) ? end : objEnd + 1;
        continue;
      }
      copyShortcutText(shortcuts.names[index], sizeof(shortcuts.names[index]), label, "Shortcut");
      copyShortcutText(shortcuts.ids[index], sizeof(shortcuts.ids[index]), id, "");
      copyShortcutText(shortcuts.icons[index], sizeof(shortcuts.icons[index]), icon, "");
      copyShortcutText(shortcuts.displayModes[index], sizeof(shortcuts.displayModes[index]),
          validShortcutDisplayMode(displayMode) ? displayMode : "label", "label");
      if (std::strlen(color) == 7 && color[0] == '#' && std::all_of(color + 1, color + 7, [](char c) { return std::isxdigit(static_cast<unsigned char>(c)) != 0; }))
        copyShortcutText(shortcuts.colors[index], sizeof(shortcuts.colors[index]), color, "");
      shortcuts.hasIconPixels[index] = decodeShortcutIcon(iconData, shortcuts.iconPixels[index]);
      ++index;
    }
    cursor = (objEnd == end) ? end : objEnd + 1;
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
    if (shortcuts.ids[i][0] == '\0' || isEmptyShortcutSlot(shortcuts.ids[i], shortcuts.icons[i])) continue;
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
