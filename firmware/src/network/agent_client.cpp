#include "network/agent_client.h"

#include <ESPmDNS.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <WiFi.h>
#include <esp_heap_caps.h>
#include <algorithm>
#include <cstring>

#include "network/agent_protocol.h"
#include "network/cover_art.h"
#include "storage/sd_paths.h"

#if __has_include(<WebSocketsClient.h>)
#include <WebSocketsClient.h>
#define STREAMDECK_HAS_WEBSOCKETS 1
#else
#define STREAMDECK_HAS_WEBSOCKETS 0
#endif

namespace streamdeck {
namespace {

constexpr uint32_t kInitialBackoffMs = 2000;
constexpr uint32_t kMaxBackoffMs = 30000;
constexpr uint32_t kHeartbeatMs = 15000;
constexpr uint32_t kDeviceInfoMs = 5000;
constexpr uint32_t kMdnsRetryMs = 10000;
constexpr uint32_t kCoverHttpTimeoutMs = 2500;
void copyText(char* target, size_t targetSize, const char* value, const char* fallback = "") {
  if (targetSize == 0) return;
  const char* source = value && value[0] ? value : fallback;
  std::strncpy(target, source, targetSize - 1);
  target[targetSize - 1] = '\0';
}

String escapeJson(const String& value) {
  String out;
  out.reserve(value.length() + 8);
  for (size_t i = 0; i < value.length(); ++i) {
    const char c = value[i];
    if (c == '"' || c == '\\') {
      out += '\\';
      out += c;
    } else if (c >= 0x20) {
      out += c;
    }
  }
  return out;
}

String makeRequestId(const char* prefix) {
  static uint32_t sequence = 0;
  char buffer[32];
  std::snprintf(buffer, sizeof(buffer), "%s-%lu", prefix,
      static_cast<unsigned long>(++sequence));
  return String(buffer);
}

String quotedField(const char* json, size_t length, const char* field) {
  String needle = "\"";
  needle += field;
  needle += "\"";
  const char* begin = json;
  const char* end = json + length;
  const char* found = std::search(begin, end, needle.c_str(), needle.c_str() + needle.length());
  if (found == end) return "";
  const char* cursor = found + needle.length();
  while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  if (cursor >= end || *cursor != ':') return "";
  ++cursor;
  while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  if (cursor >= end || *cursor != '"') return "";
  ++cursor;

  String out;
  while (cursor < end && *cursor != '"') {
    if (*cursor == '\\' && cursor + 1 < end) {
      ++cursor;
    }
    out += *cursor;
    ++cursor;
  }
  return out;
}

long integerField(const char* json, size_t length, const char* field, long fallback) {
  String needle = "\"";
  needle += field;
  needle += "\"";
  const char* begin = json;
  const char* end = json + length;
  const char* found = std::search(begin, end, needle.c_str(), needle.c_str() + needle.length());
  if (found == end) return fallback;
  const char* cursor = found + needle.length();
  while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  if (cursor >= end || *cursor != ':') return fallback;
  ++cursor;
  while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  bool negative = false;
  if (cursor < end && *cursor == '-') {
    negative = true;
    ++cursor;
  }
  long value = 0;
  bool any = false;
  while (cursor < end && *cursor >= '0' && *cursor <= '9') {
    any = true;
    value = value * 10 + (*cursor - '0');
    ++cursor;
  }
  if (!any) return fallback;
  return negative ? -value : value;
}

bool hasField(const char* json, size_t length, const char* field) {
  String needle = "\"";
  needle += field;
  needle += "\"";
  return std::search(json, json + length, needle.c_str(), needle.c_str() + needle.length()) != json + length;
}

bool boolField(const char* json, size_t length, const char* field, bool fallback) {
  String needle = "\"";
  needle += field;
  needle += "\"";
  const char* begin = json;
  const char* end = json + length;
  const char* found = std::search(begin, end, needle.c_str(), needle.c_str() + needle.length());
  if (found == end) return fallback;
  const char* cursor = found + needle.length();
  while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  if (cursor >= end || *cursor != ':') return fallback;
  ++cursor;
  while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  if (end - cursor >= 4 && std::strncmp(cursor, "true", 4) == 0) return true;
  if (end - cursor >= 5 && std::strncmp(cursor, "false", 5) == 0) return false;
  return fallback;
}

uint32_t colorField(const char* json, size_t length, const char* field, uint32_t fallback) {
  const String value = quotedField(json, length, field);
  if (value.length() != 7 || value[0] != '#') return fallback;
  char* end = nullptr;
  const uint32_t color = std::strtoul(value.c_str() + 1, &end, 16);
  return end && *end == '\0' ? color : fallback;
}

uint8_t enumField(const char* json, size_t length, const char* field,
                  const char* first, const char* second, const char* third,
                  uint8_t fallback) {
  const String value = quotedField(json, length, field);
  if (value == first) return 0;
  if (value == second) return 1;
  if (third && value == third) return 2;
  return fallback;
}

Page pageFromName(const String& value, Page fallback) {
  if (value == "media") return Page::Media;
  if (value == "shortcuts") return Page::Shortcuts;
  if (value == "pc") return Page::Pc;
  if (value == "info") return Page::Info;
  return fallback;
}

void copyBoundedText(char* target, size_t targetSize, const String& value) {
  if (!target || targetSize == 0) return;
  const size_t copyLength = std::min(targetSize - 1, static_cast<size_t>(value.length()));
  std::memcpy(target, value.c_str(), copyLength);
  target[copyLength] = '\0';
}

void parsePageOrder(const char* json, size_t length, AppearanceState& appearance) {
  String needle = "\"pageOrder\"";
  const char* begin = json;
  const char* end = json + length;
  const char* found = std::search(begin, end, needle.c_str(), needle.c_str() + needle.length());
  if (found == end) return;

  bool seen[kPageCount] = {};
  Page parsed[kPageCount]{};
  uint8_t count = 0;
  const char* cursor = found + needle.length();
  while (cursor < end && *cursor != '[') ++cursor;
  if (cursor == end) return;
  ++cursor;
  while (cursor < end && *cursor != ']' && count < kPageCount) {
    while (cursor < end && *cursor != '"' && *cursor != ']') ++cursor;
    if (cursor == end || *cursor == ']') break;
    ++cursor;
    String item;
    while (cursor < end && *cursor != '"') item += *cursor++;
    if (cursor < end) ++cursor;
    const Page page = pageFromName(item, Page::Info);
    const uint8_t index = pageIndex(page);
    if (index < kPageCount && !seen[index]) {
      parsed[count++] = page;
      seen[index] = true;
    }
  }
  if (count > 0) {
    for (uint8_t i = 0; i < kPageCount; ++i) {
      appearance.pageOrder[i] = i < count ? parsed[i] : static_cast<Page>(255);
    }
  }
}

void parsePageVisibility(const char* json, size_t length, AppearanceState& appearance) {
  String needle = "\"pageVisibility\"";
  const char* begin = json;
  const char* end = json + length;
  const char* found = std::search(begin, end, needle.c_str(), needle.c_str() + needle.length());
  if (found == end) return;

  for (uint8_t i = 0; i < kPageCount; ++i) appearance.pageVisible[i] = false;
  const char* cursor = found + needle.length();
  while (cursor < end && *cursor != '[') ++cursor;
  if (cursor == end) return;
  ++cursor;
  while (cursor < end && *cursor != ']') {
    while (cursor < end && *cursor != '"' && *cursor != ']') ++cursor;
    if (cursor == end || *cursor == ']') break;
    ++cursor;
    String item;
    while (cursor < end && *cursor != '"') item += *cursor++;
    if (cursor < end) ++cursor;
    appearance.pageVisible[pageIndex(pageFromName(item, Page::Info))] = true;
  }
  appearance.pageVisible[pageIndex(Page::Info)] = true;
}

}  // namespace

#if STREAMDECK_HAS_WEBSOCKETS

class AgentClient::Impl {
 public:
  void begin(const AgentClientConfig& config, AppState& state, IntentQueue* intents, storage::SdStorage* storage = nullptr) {
    config_ = config;
    state_ = &state;
    intents_ = intents;
    storage_ = storage;
    loadToken();
  }

  void loop() {
    if (!state_) return;

    if (WiFi.status() != WL_CONNECTED) {
      if (connected_) socket_.disconnect();
      connected_ = false;
      authorized_ = false;
      state_->connection.agentConnected = false;
      state_->connection.online = false;
      nextAttemptAt_ = millis() + kInitialBackoffMs;
      return;
    }

    socket_.loop();
    maybeConnect();
    maybeFetchCover();
    maybeHeartbeat();
    maybeSendDeviceInfo();
  }

  void sendIntent(const Intent& intent) {
    if (!connected_ || !authorized_) return;

    if (protocol::isMediaIntent(intent.type)) {
      sendMediaCommand(protocol::mediaCommandFor(intent.type));
      return;
    }

    if (intent.type == IntentType::SetVolume) {
      sendSetVolume(intent.value);
      return;
    }

    if (intent.type == IntentType::RunShortcut && state_) {
      const int index = intent.value;
      if (index >= 0 && index < 6 && state_->shortcuts.ids[index][0] != '\0') {
        sendShortcut(state_->shortcuts.ids[index]);
      }
    }
  }

 private:
  void loadToken() {
    Preferences prefs;
    prefs.begin("pairing", true);
    token_ = prefs.getString("token", "");
    prefs.end();
  }

  void persistToken(const char* token) {
    if (!token || token[0] == '\0') return;
    Preferences prefs;
    prefs.begin("pairing", false);
    prefs.putString("token", token);
    prefs.end();
    token_ = token;
    Serial.println("[NET] pairing token stored in NVS");
  }

  void maybeConnect() {
    if (connected_) return;
    const uint32_t now = millis();
    if (now < nextAttemptAt_) return;

    String host = discoverHost();
    if (host.length() == 0) {
      scheduleRetry();
      return;
    }

    String path = protocol::kWebSocketPath;
    if (token_.length() > 0) {
      path += "?token=";
      path += token_;
    }

    socket_.disconnect();
    socket_.begin(host.c_str(), config_.port, path.c_str());
    socket_.setReconnectInterval(0);
    socket_.onEvent([this](WStype_t type, uint8_t* payload, size_t length) {
      handleEvent(type, payload, length);
    });
    host_ = host;
    nextAttemptAt_ = now + backoffMs_;
    Serial.printf("[NET] connecting to agent %s:%u%s\n",
        host.c_str(), config_.port, protocol::kWebSocketPath);
  }

  String discoverHost() {
    const uint32_t now = millis();
    if (now >= nextMdnsAt_) {
      nextMdnsAt_ = now + kMdnsRetryMs;
      if (!mdnsStarted_) {
        mdnsStarted_ = MDNS.begin(config_.deviceId.c_str());
      }
      if (mdnsStarted_) {
        const int count = MDNS.queryService(protocol::kMdnsService, protocol::kMdnsProto);
        if (count > 0) {
          config_.port = MDNS.port(0);
          return MDNS.IP(0).toString();
        }
      }
    }

    return config_.manualHost;
  }

  void scheduleRetry() {
    nextAttemptAt_ = millis() + backoffMs_;
    backoffMs_ = std::min<uint32_t>(backoffMs_ * 2, kMaxBackoffMs);
    if (state_) state_->connection.online = false;
    if (state_) state_->connection.agentConnected = false;
  }

  void handleEvent(WStype_t type, uint8_t* payload, size_t length) {
    switch (type) {
      case WStype_CONNECTED:
        connected_ = true;
        authorized_ = false;
        backoffMs_ = kInitialBackoffMs;
        lastHeartbeatAt_ = 0;
        lastDeviceInfoAt_ = 0;
        sendHello();
        break;
      case WStype_DISCONNECTED:
        connected_ = false;
        authorized_ = false;
        if (state_) {
          state_->connection.agentConnected = false;
          state_->connection.online = false;
        }
        scheduleRetry();
        break;
      case WStype_TEXT:
        handleText(reinterpret_cast<const char*>(payload), length);
        break;
      default:
        break;
    }
  }

  void handleText(const char* payload, size_t length) {
    const String type = quotedField(payload, length, "type");
    if (type.length() == 0) {
      Serial.println("[NET] protocol parse failed");
      return;
    }

    if (type == protocol::kHello) {
      return;
    }
    if (type == protocol::kConnectionState) {
      const String value = quotedField(payload, length, "state");
      authorized_ = value == "connected";
      if (state_) {
        state_->connection.agentConnected = authorized_;
        state_->connection.online = authorized_;
        copyText(state_->connection.agentHost, sizeof(state_->connection.agentHost), host_.c_str(), "");
      }
      if (authorized_) {
        sendDeviceInfo();
        sendShortcutsReport();
      }
      return;
    }
    if (type == protocol::kPairingToken) {
      const String token = quotedField(payload, length, "token");
      persistToken(token.c_str());
      authorized_ = true;
      if (state_) {
        state_->connection.agentConnected = true;
        state_->connection.online = true;
        copyText(state_->connection.agentHost, sizeof(state_->connection.agentHost), host_.c_str(), "");
      }
      sendDeviceInfo();
      sendShortcutsReport();
      return;
    }
    if (type == protocol::kDeviceRestart) {
      const String requestId = quotedField(payload, length, "requestId");
      sendAck(requestId.c_str(), true);
      if (intents_) intents_->push({IntentType::RestartDevice, 0});
      return;
    }
    if (type == protocol::kDeviceFactoryReset) {
      const String requestId = quotedField(payload, length, "requestId");
      sendAck(requestId.c_str(), true);
      if (intents_) intents_->push({IntentType::FactoryReset, 0});
      return;
    }
    if (type == protocol::kDeviceSettings) {
      const String requestId = quotedField(payload, length, "requestId");
      const long version = integerField(payload, length, "version", -1);
      if (version == 1 && state_) {
        applySettings(payload, length);
        sendAck(requestId.c_str(), true);
        if (intents_) intents_->push({IntentType::SettingsUpdated, 0});
      } else {
        sendAck(requestId.c_str(), false);
      }
      return;
    }
    if (type == protocol::kMediaUpdate) {
      applyMediaUpdate(payload, length);
      return;
    }
    if (type == protocol::kMediaTimeline) {
      if (state_) {
        state_->media.positionMs = integerField(payload, length, "positionMs", state_->media.positionMs);
        state_->media.durationMs = integerField(payload, length, "durationMs", state_->media.durationMs);
      }
      return;
    }
    if (type == protocol::kMediaPlayback) {
      if (state_) state_->media.playing = boolField(payload, length, "playing", state_->media.playing);
      return;
    }
    if (type == protocol::kAudioUpdate) {
      if (state_) {
        state_->audio.reportedVolume = static_cast<uint8_t>(
            constrain(integerField(payload, length, "volumePercent", state_->audio.reportedVolume), 0, 100));
        state_->audio.available = true;
      }
      return;
    }
    if (type == protocol::kSystemStats) {
      return;
    }
    if (type == protocol::kShortcutsUpdate) {
      applyShortcuts(payload, length);
      return;
    }
    if (type == protocol::kAck || type == protocol::kPong) {
      return;
    }
  }

  void applyMediaUpdate(const char* payload, size_t length) {
    if (!state_) return;
    const String title = quotedField(payload, length, "title");
    const String artist = quotedField(payload, length, "artist");
    const String album = quotedField(payload, length, "album");
    const String coverId = quotedField(payload, length, "coverId");
    copyText(state_->media.title, sizeof(state_->media.title), title.c_str(), "No track");
    copyText(state_->media.artist, sizeof(state_->media.artist), artist.c_str(), "Unknown artist");
    copyText(state_->media.album, sizeof(state_->media.album), album.c_str(), "");
    copyText(state_->media.coverId, sizeof(state_->media.coverId), coverId.c_str(), "");
    updateCoverFetch(state_->media.cover, state_->media.coverId);
    state_->media.playing = boolField(payload, length, "playing", false);
    state_->media.positionMs = integerField(payload, length, "positionMs", 0);
    state_->media.durationMs = integerField(payload, length, "durationMs", 0);
  }

  void applyShortcuts(const char* payload, size_t length) {
    if (!state_) return;
    protocol::applyShortcutsUpdateJson(state_->shortcuts, payload, length);
    if (intents_) intents_->push({IntentType::RefreshUi, 0});
  }

  void applySettings(const char* payload, size_t length) {
    AppearanceState next = state_->appearance;
    next.theme = enumField(payload, length, "theme", "dark", "light", nullptr, next.theme);
    next.buttonStyle = enumField(payload, length, "buttonStyle", "flat", "rounded", "pill", next.buttonStyle);
    if (hasField(payload, length, "accent")) next.accent = colorField(payload, length, "accent", next.accent);
    if (hasField(payload, length, "background")) next.background = colorField(payload, length, "background", next.background);
    if (hasField(payload, length, "brightness")) next.brightness = clampPercent(integerField(payload, length, "brightness", next.brightness), 10, 100);
    if (hasField(payload, length, "dimAfterSec")) next.dimAfterSeconds = clampSeconds(integerField(payload, length, "dimAfterSec", next.dimAfterSeconds), 0, 86400);
    if (hasField(payload, length, "dimBrightness")) next.dimBrightness = clampPercent(integerField(payload, length, "dimBrightness", next.dimBrightness), 1, 100);
    if (hasField(payload, length, "screenOffAfterSec")) next.screenOffAfterSeconds = clampSeconds(integerField(payload, length, "screenOffAfterSec", next.screenOffAfterSeconds), 0, 86400);
    if (hasField(payload, length, "wakeOnTouch")) next.wakeOnTouch = boolField(payload, length, "wakeOnTouch", next.wakeOnTouch);
    parsePageOrder(payload, length, next);
    parsePageVisibility(payload, length, next);
    normalizeAppearance(next);
    state_->appearance = next;
  }

  void maybeHeartbeat() {
    if (!protocol::shouldSendHeartbeat(connected_, authorized_)) return;
    const uint32_t now = millis();
    if (lastHeartbeatAt_ != 0 && now - lastHeartbeatAt_ < kHeartbeatMs) return;
    lastHeartbeatAt_ = now;
    String message = "{\"protocol\":1,\"type\":\"ping\",\"data\":{\"sentAtUnixMs\":";
    message += now;
    message += "}}";
    socket_.sendTXT(message);
  }

  void maybeFetchCover() {
    if (!state_ || !storage_ || !authorized_ || WiFi.status() != WL_CONNECTED) return;
    CoverFetchState& cover = state_->media.cover;
    if (cover.status != CoverStatus::Pending || cover.inFlight || !isValidCoverId(cover.requestedId)) return;

    char path[kCoverRequestPathMax]{};
    if (!makeCoverRequestPath(cover.requestedId, path, sizeof(path))) {
      markCoverFetchComplete(cover, cover.requestedId, false);
      return;
    }

    char requestId[kCoverIdLength + 1]{};
    copyText(requestId, sizeof(requestId), cover.requestedId, "");
    cover.inFlight = true;

    if (storage_->hasCover(requestId)) {
      const bool rendered = config_.renderCover ? config_.renderCover(requestId, *state_) : false;
      markCoverFetchComplete(cover, requestId, rendered, rendered);
      return;
    }

    WiFiClient client;
    client.setTimeout(kCoverHttpTimeoutMs);
    bool ok = false;
    if (client.connect(host_.c_str(), config_.port, kCoverHttpTimeoutMs)) {
      client.print(F("GET "));
      client.print(path);
      client.print(F(" HTTP/1.1\r\nHost: "));
      client.print(host_);
      client.print(F("\r\nConnection: close\r\n\r\n"));

      const String statusLine = client.readStringUntil('\n');
      int statusCode = 0;
      if (statusLine.startsWith("HTTP/")) statusCode = statusLine.substring(9, 12).toInt();

      int contentLength = -1;
      while (client.connected()) {
        const String header = client.readStringUntil('\n');
        if (header == "\r" || header.length() == 0) break;
        if (header.startsWith("Content-Length:")) contentLength = header.substring(15).toInt();
      }

      if (statusCode == 200 && contentLength > 0 &&
          contentLength <= static_cast<int>(storage::kMaxCoverBytes)) {
        File out = storage_->openCoverTempWrite(requestId);
        std::size_t received = 0;
        uint8_t buffer[512];
        const uint32_t startedAt = millis();
        while (out && received < static_cast<std::size_t>(contentLength) &&
            millis() - startedAt < kCoverHttpTimeoutMs) {
          const int available = client.available();
          if (available <= 0) {
            delay(1);
            continue;
          }
          const int want = std::min<int>(available,
              std::min<int>(static_cast<int>(sizeof(buffer)), contentLength - static_cast<int>(received)));
          const int read = client.read(buffer, want);
          if (read <= 0) break;
          if (!storage_->appendCoverTemp(out, buffer, static_cast<std::size_t>(read), received)) break;
        }
        if (out) {
          out.flush();
          out.close();
        }
        ok = received == static_cast<std::size_t>(contentLength) && storage_->commitCoverTemp(requestId, received);
        if (!ok) storage_->discardCoverTemp(requestId);
      }
    }
    client.stop();
    const bool rendered = ok && config_.renderCover && config_.renderCover(requestId, *state_);
    markCoverFetchComplete(cover, requestId, rendered, rendered);
  }

  void maybeSendDeviceInfo() {
    if (!connected_ || !authorized_) return;
    const uint32_t now = millis();
    if (lastDeviceInfoAt_ != 0 && now - lastDeviceInfoAt_ < kDeviceInfoMs) return;
    sendDeviceInfo();
  }

  void sendDeviceInfo() {
    if (!state_) return;
    lastDeviceInfoAt_ = millis();
    state_->device.uptimeSeconds = lastDeviceInfoAt_ / 1000;
    state_->connection.wifiConnected = WiFi.status() == WL_CONNECTED;
    state_->connection.rssi = state_->connection.wifiConnected ? WiFi.RSSI() : 0;
    copyText(state_->connection.ssid, sizeof(state_->connection.ssid), state_->connection.wifiConnected ? WiFi.SSID().c_str() : "", "");
    copyText(state_->connection.deviceIp, sizeof(state_->connection.deviceIp), state_->connection.wifiConnected ? WiFi.localIP().toString().c_str() : "", "");
    state_->system.freeHeap = ESP.getFreeHeap();
    state_->system.minHeap = ESP.getMinFreeHeap();
    state_->system.largestBlock = heap_caps_get_largest_free_block(MALLOC_CAP_8BIT);

    String message = "{\"protocol\":1,\"type\":\"device.info\",\"data\":{\"firmwareVersion\":\"";
    message += escapeJson(state_->device.firmwareVersion);
    message += "\",\"uptimeSeconds\":";
    message += state_->device.uptimeSeconds;
    message += ",\"wifi\":{\"connected\":";
    message += state_->connection.wifiConnected ? "true" : "false";
    message += ",\"ssid\":\"";
    message += escapeJson(state_->connection.ssid);
    message += "\",\"rssi\":";
    message += state_->connection.rssi;
    message += ",\"ip\":\"";
    message += escapeJson(state_->connection.deviceIp);
    message += "\"},\"agentHost\":\"";
    message += escapeJson(state_->connection.agentHost);
    message += "\",\"sd\":{\"mounted\":";
    message += state_->storage.mounted ? "true" : "false";
    message += ",\"ready\":";
    message += state_->storage.ready ? "true" : "false";
    message += ",\"capacityBytes\":";
    message += static_cast<unsigned long long>(state_->storage.totalMb) * 1024ULL * 1024ULL;
    message += ",\"freeBytes\":";
    message += static_cast<unsigned long long>(state_->storage.freeMb) * 1024ULL * 1024ULL;
    message += "},\"heap\":{\"freeBytes\":";
    message += state_->system.freeHeap;
    message += ",\"minFreeBytes\":";
    message += state_->system.minHeap;
    message += ",\"largestBlockBytes\":";
    message += state_->system.largestBlock;
    message += "}}}";
    socket_.sendTXT(message);
  }

  void sendShortcutsReport() {
    if (!state_) return;
    char message[protocol::kShortcutsReportJsonCapacity]{};
    if (protocol::makeShortcutsReportJson(state_->shortcuts, message, sizeof(message))) {
      socket_.sendTXT(message);
    }
  }

  void sendHello() {
    String message = "{\"protocol\":1,\"type\":\"hello\",\"data\":{\"deviceId\":\"";
    message += escapeJson(config_.deviceId);
    message += "\",\"name\":\"";
    message += escapeJson(config_.deviceName);
    message += "\",\"role\":\"esp32\",\"capabilities\":[\"media\",\"audio\",\"shortcuts\",\"system\",\"device-control\"]}}";
    socket_.sendTXT(message);
  }

  void sendAck(const char* requestId, bool success) {
    if (!requestId || requestId[0] == '\0') return;
    String message = "{\"protocol\":1,\"type\":\"ack\",\"requestId\":\"";
    message += escapeJson(requestId);
    message += "\",\"success\":";
    message += success ? "true" : "false";
    if (!success) {
      message += ",\"error\":{\"code\":\"invalidSettings\",\"message\":\"Device settings payload is invalid.\"}";
    }
    message += "}";
    socket_.sendTXT(message);
  }

  void sendMediaCommand(const char* command) {
    String requestId = makeRequestId("media");
    String message = "{\"protocol\":1,\"type\":\"media.command\",\"requestId\":\"";
    message += requestId;
    message += "\",\"data\":{\"command\":\"";
    message += command;
    message += "\"}}";
    socket_.sendTXT(message);
  }

  void sendSetVolume(int value) {
    String requestId = makeRequestId("volume");
    String message = "{\"protocol\":1,\"type\":\"audio.setVolume\",\"requestId\":\"";
    message += requestId;
    message += "\",\"data\":{\"volumePercent\":";
    message += constrain(value, 0, 100);
    message += "}}";
    socket_.sendTXT(message);
  }

  void sendShortcut(const char* shortcutId) {
    String requestId = makeRequestId("shortcut");
    char message[192]{};
    if (protocol::makeShortcutExecuteJson(requestId.c_str(), shortcutId, message, sizeof(message))) {
      socket_.sendTXT(message);
    }
  }

  AgentClientConfig config_{};
  AppState* state_ = nullptr;
  IntentQueue* intents_ = nullptr;
  storage::SdStorage* storage_ = nullptr;
  WebSocketsClient socket_;
  String token_;
  String host_;
  bool connected_ = false;
  bool authorized_ = false;
  bool mdnsStarted_ = false;
  uint32_t nextAttemptAt_ = 0;
  uint32_t nextMdnsAt_ = 0;
  uint32_t backoffMs_ = kInitialBackoffMs;
  uint32_t lastHeartbeatAt_ = 0;
  uint32_t lastDeviceInfoAt_ = 0;
};

#else

class AgentClient::Impl {
 public:
  void begin(const AgentClientConfig&, AppState& state, IntentQueue*, storage::SdStorage* = nullptr) {
    state.connection.agentConnected = false;
    state.connection.online = false;
  }

  void loop() {}

  void sendIntent(const Intent&) {}
};

#endif

void AgentClient::begin(const AgentClientConfig& config, AppState& state) {
  static Impl impl;
  impl_ = &impl;
  impl_->begin(config, state, nullptr);
}

void AgentClient::begin(const AgentClientConfig& config, AppState& state, IntentQueue& intents) {
  static Impl impl;
  impl_ = &impl;
  impl_->begin(config, state, &intents);
}

void AgentClient::begin(const AgentClientConfig& config, AppState& state, IntentQueue& intents, storage::SdStorage& storage) {
  static Impl impl;
  impl_ = &impl;
  impl_->begin(config, state, &intents, &storage);
}

void AgentClient::loop() {
  if (impl_) impl_->loop();
}

void AgentClient::sendIntent(const Intent& intent) {
  if (impl_) impl_->sendIntent(intent);
}

}  // namespace streamdeck
