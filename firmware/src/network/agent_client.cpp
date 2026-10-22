#include "network/agent_client.h"

#include <ESPmDNS.h>
#include <HTTPClient.h>
#include <Preferences.h>
#include <WiFi.h>
#include <esp_heap_caps.h>
#include <algorithm>
#include <cstring>
#include <atomic>
#include <freertos/FreeRTOS.h>
#include <freertos/queue.h>
#include <freertos/task.h>

#include "network/agent_discovery.h"
#include "network/agent_protocol.h"
#include "network/cover_art.h"
#include "network/pc_metrics.h"
#include "network/pending_commands.h"
#include "network/usb_agent_protocol.h"
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
constexpr uint32_t kUsbHelloMs = 1000;
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
  char value[usb::kMaxCoverChunkRawBytes * 4 / 3 + 1]{};
  if (!protocol::readQuotedField(json, length, field, value, sizeof(value))) return "";
  return String(value);
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
    sendUsbHelloIfDue(true);
  }

  void loop() {
    if (!state_) return;
    const bool metricsFresh = pcMetricsFresh(state_->system, state_->connection.agentConnected, millis());
    if (metricsWereFresh_ && !metricsFresh) trace("stale_detected", "system");
    metricsWereFresh_ = metricsFresh;

    PendingCommand expired;
    while (pendingCommands_.expire(millis(), expired)) finishCommand(expired, false, "ACK timeout");
    if (state_->audio.pending && millis() - state_->audio.requestedAtMs >= 5000) {
      state_->audio.pending = false;
      state_->audio.failed = true;
      copyText(state_->commandFeedback, sizeof(state_->commandFeedback), "Volume state timeout");
      state_->feedbackAtMs = millis();
    }

    serviceUsb();
    if (state_->media.cover.displayed && std::strcmp(lastDisplayedCover_, state_->media.cover.availableId) != 0) {
      copyText(lastDisplayedCover_, sizeof(lastDisplayedCover_), state_->media.cover.availableId);
      trace("rendered", lastDisplayedCover_);
      sendDeviceInfo();
    }
    serviceHttpCover();
    maybeUsbHeartbeat();
    maybeSendUsbDeviceInfo();
    if (expirePendingCover(state_->media.cover, millis())) {
      resetUsbCoverTransfer();
      if (intents_) intents_->push({IntentType::RefreshUi, 0});
    }
    if (canSend()) {
      auto& cover = state_->media.cover;
      if (cover.status == CoverStatus::Failed && !cover.retryArmed && cover.retries < 3)
        trace("retry_scheduled", cover.requestedId);
      if (cover.status == CoverStatus::Failed &&
          updateCoverFetch(cover, state_->media.coverId, millis()) == CoverTransition::Requested) {
        resetUsbCoverTransfer();
      }
      if (cover.status == CoverStatus::Pending && cover.decoding) serviceCoverDecode();
      if (cover.status == CoverStatus::Pending && !cover.inFlight && !cover.decoding) startCoverRequest();
    }

    // USB owns control while authenticated. Never run blocking DNS/TCP discovery
    // on the touch/UI thread when there is already a working cable transport.
    const bool usbHandshakePending = lastUsbRxAt_ != 0 && millis() - lastUsbRxAt_ < 6000;
    if (usbAuthorized_ || usbHandshakePending) {
      if (!wifiSuspended_) {
        wifiSuspended_ = true;
        socket_.disconnect();
        connected_ = false;
        authorized_ = false;
        publishConnectionState();
      }
      return;
    }
    if (wifiSuspended_) {
      wifiSuspended_ = false;
      connectRetry_.reset();
      mdnsRetry_.reset();
      backoffMs_ = kInitialBackoffMs;
    }

    if (WiFi.status() != WL_CONNECTED) {
      if (connected_) socket_.disconnect();
      connected_ = false;
      authorized_ = false;
      publishConnectionState();
      maybeUsbHeartbeat();
      maybeSendUsbDeviceInfo();
      connectRetry_.schedule(millis(), kInitialBackoffMs);
      return;
    }

    socket_.loop();
    maybeConnect();
    maybeFetchCover();
    maybeHeartbeat();
    maybeSendDeviceInfo();
  }

  void sendIntent(const Intent& intent) {
    lastIntentAtMs_ = intent.queuedAtMs;
    if (!canSend()) {
      copyText(state_->commandFeedback, sizeof(state_->commandFeedback), "Disconnected");
      state_->feedbackAtMs = millis();
      state_->audio.pending = false;
      return;
    }

    if (protocol::isMediaIntent(intent.type)) {
      sendMediaCommand(protocol::mediaCommandFor(intent.type));
      return;
    }

    if (intent.type == IntentType::SetVolume) {
      sendSetVolume(intent.value);
      return;
    }

    if (intent.type == IntentType::ToggleMute) {
      sendToggleMute();
      return;
    }

    if (intent.type == IntentType::RunShortcut && state_) {
      const int index = intent.value;
      if (index >= 0 && index < 6 && state_->shortcuts.ids[index][0] != '\0' &&
          !protocol::isEmptyShortcutSlot(state_->shortcuts.ids[index], state_->shortcuts.icons[index])) {
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
    logLine("[NET] pairing token stored in NVS");
  }

  void clearToken() {
    Preferences prefs;
    prefs.begin("pairing", false);
    prefs.remove("token");
    prefs.end();
    token_ = "";
    authorized_ = false;
    usbAuthorized_ = false;
  }

  void publishConnectionState() {
    if (!state_) return;
    const bool wifiOnline = connected_ && authorized_;
    const bool agentOnline = wifiOnline || usbAuthorized_;
    if (agentOnline && !state_->connection.agentConnected) state_->audio.revision = 0;
    state_->connection.agentConnected = agentOnline;
    if (!agentOnline) state_->system.pcStatsReceived = false;
    state_->connection.online = agentOnline;
    copyText(state_->connection.agentHost, sizeof(state_->connection.agentHost),
        usbAuthorized_ ? "usb" : host_.c_str(), "");
  }

  void maybeConnect() {
    if (connected_) return;
    const uint32_t now = millis();
    if (!connectRetry_.ready(now)) return;

    const AgentEndpoint endpoint = discoverEndpoint();
    if (endpoint.host.length() == 0 || endpoint.port == 0) {
      scheduleRetry();
      return;
    }

    String path = protocol::kWebSocketPath;
    if (token_.length() > 0) {
      path += "?token=";
      path += token_;
    }

    socket_.disconnect();
    socket_.begin(endpoint.host.c_str(), endpoint.port, path.c_str());
    socket_.setReconnectInterval(0);
    socket_.onEvent([this](WStype_t type, uint8_t* payload, size_t length) {
      handleEvent(type, payload, length);
    });
    host_ = endpoint.host;
    connectRetry_.schedule(now, backoffMs_);
    logf("[NET] connecting to agent %s:%u%s\n",
        endpoint.host.c_str(), endpoint.port, protocol::kWebSocketPath);
  }

  struct AgentEndpoint {
    String host;
    uint16_t port = 0;

    AgentEndpoint() = default;
    AgentEndpoint(const String& selectedHost, uint16_t selectedPort)
        : host(selectedHost), port(selectedPort) {}
  };

  AgentEndpoint discoverEndpoint() {
    const uint32_t now = millis();
    String mdnsHost;
    uint16_t mdnsPort = 0;
    if (mdnsRetry_.ready(now)) {
      mdnsRetry_.schedule(now, kMdnsRetryMs);
      if (!mdnsStarted_) {
        mdnsStarted_ = MDNS.begin(config_.deviceId.c_str());
      }
      if (mdnsStarted_) {
        if (state_) ++state_->performance.wifiDiscoveryCount;
        const int count = MDNS.queryService(protocol::kMdnsService, protocol::kMdnsProto);
        if (count > 0) {
          mdnsHost = MDNS.IP(0).toString();
          mdnsPort = MDNS.port(0);
        }
      }
    }

    const auto selected = protocol::selectAgentEndpoint(
        config_.manualHost.c_str(), config_.port, mdnsHost.c_str(), mdnsPort);
    return AgentEndpoint(String(selected.host), selected.port);
  }

  void scheduleRetry() {
    connectRetry_.schedule(millis(), backoffMs_);
    backoffMs_ = std::min<uint32_t>(backoffMs_ * 2, kMaxBackoffMs);
    if (state_) state_->connection.online = false;
    if (state_) state_->connection.agentConnected = false;
    publishConnectionState();
  }

  void handleEvent(WStype_t type, uint8_t* payload, size_t length) {
    switch (type) {
      case WStype_CONNECTED:
        connected_ = true;
        authorized_ = false;
        backoffMs_ = kInitialBackoffMs;
        lastHeartbeatAt_ = 0;
        lastDeviceInfoAt_ = 0;
        sendHello(false);
        break;
      case WStype_DISCONNECTED:
        connected_ = false;
        authorized_ = false;
        publishConnectionState();
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
      logLine("[NET] protocol parse failed");
      return;
    }

    if (type == protocol::kHello) {
      if (state_ && quotedField(payload, length, "role") == "agent") resetAudioSession(state_->audio);
      return;
    }
    if (type == protocol::kConnectionState) {
      const String value = quotedField(payload, length, "state");
      authorized_ = value == "connected";
      if (state_) {
        copyText(state_->connection.agentHost, sizeof(state_->connection.agentHost), host_.c_str(), "");
      }
      publishConnectionState();
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
        copyText(state_->connection.agentHost, sizeof(state_->connection.agentHost), host_.c_str(), "");
      }
      publishConnectionState();
      sendDeviceInfo();
      sendShortcutsReport();
      return;
    }
    if (!authorized_) {
      return;
    }
    if (type == "cover.chunk") {
      handleCoverChunk(payload, length);
      return;
    }
    if (type == "cover.complete") {
      handleCoverComplete(payload, length);
      return;
    }
    if (type == "device.status.request") {
      sendDeviceInfo();
      return;
    }
    if (type == "diagnostics.configure") {
      diagnostics_ = boolField(payload, length, "enabled", false);
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
      clearToken();
      publishConnectionState();
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
        state_->media.timelineAtMs = millis();
        state_->media.timelineAnchored = true;
      }
      return;
    }
    if (type == protocol::kMediaPlayback) {
      if (state_) {
        state_->media.positionMs = mediaPositionAt(state_->media, millis());
        state_->media.timelineAtMs = millis();
        state_->media.timelineAnchored = true;
        state_->media.playing = boolField(payload, length, "playing", state_->media.playing);
      }
      return;
    }
    if (type == protocol::kAudioUpdate) {
      if (state_) {
        const auto volume = static_cast<uint8_t>(constrain(integerField(payload, length, "volumePercent", state_->audio.reportedVolume), 0, 100));
        const String requestId = quotedField(payload, length, "requestId");
        const bool wasPending = state_->audio.pending;
        trace("audio_update_received", requestId.length() ? requestId.c_str() : "audio");
        if (receiveVolume(state_->audio, volume, integerField(payload, length, "revision", 0), requestId.c_str()))
          state_->audio.muted = boolField(payload, length, "muted", false);
        if (wasPending && !state_->audio.pending) trace("volume_reconciled", requestId.length() ? requestId.c_str() : "audio");
        if (intents_) intents_->push({IntentType::RefreshUi, 0});
      }
      return;
    }
    if (type == protocol::kSystemStats) {
      if (state_) {
        trace("stats_received", "system");
        protocol::applyPcMetricsJson(state_->system, payload, length, millis());
        trace("stats_parsed", "system");
        trace("metrics_updated", "system");
        if (hasField(payload, length, "hostDate")) copyText(state_->system.hostDate, sizeof(state_->system.hostDate), quotedField(payload, length, "hostDate").c_str(), "");
        const long hostMinutes = integerField(payload, length, "hostLocalMinutes", -1);
        if (hostMinutes >= 0 && hostMinutes < 1440) {
          state_->system.hostLocalMinutes = static_cast<int16_t>(hostMinutes);
          state_->system.hostTimeSyncedAtSec = millis() / 1000;
        }
        if (intents_) intents_->push({IntentType::RefreshUi, 0});
      }
      return;
    }
    if (type == protocol::kShortcutsUpdate) {
      applyShortcuts(payload, length);
      return;
    }
    if (type == protocol::kAck) {
      PendingCommand command;
      const String id = quotedField(payload, length, "requestId");
      const String code = quotedField(payload, length, "code");
      const char* detail = code == "unknownShortcut" ? "Unknown Shortcut" :
          code == "invalidConfiguration" ? "Invalid configuration" :
          code == "targetNotFound" ? "Target not found" :
          code == "actionUnavailable" ? "Action unavailable" :
          code == "accessDenied" ? "Access denied" :
          code == "busy" ? "Another Shortcut is running" : "Command failed";
      if (pendingCommands_.take(id.c_str(), command))
        finishCommand(command, boolField(payload, length, "success", false), detail);
      return;
    }
    if (type == protocol::kPong) {
      return;
    }
  }

  void applyMediaUpdate(const char* payload, size_t length) {
    if (!state_) return;
    const String title = quotedField(payload, length, "title");
    const String artist = quotedField(payload, length, "artist");
    const String album = quotedField(payload, length, "album");
    const String coverId = quotedField(payload, length, "coverId");
    const CoverTransition transition = updateMediaArtwork(
        state_->media.cover, state_->media.title, state_->media.artist,
        state_->media.album, state_->media.coverId, title.c_str(), artist.c_str(),
        album.c_str(), coverId.c_str(), millis());
    if (transition == CoverTransition::Requested || transition == CoverTransition::Cleared) {
      cancelHttpCover();
      resetUsbCoverTransfer();
    }
    if (transition == CoverTransition::Requested) startCoverRequest();
    if (intents_) intents_->push({IntentType::RefreshUi, 0});
    state_->media.playing = boolField(payload, length, "playing", false);
    state_->media.positionMs = integerField(payload, length, "positionMs", 0);
    state_->media.durationMs = integerField(payload, length, "durationMs", 0);
    state_->media.timelineAtMs = millis();
    state_->media.timelineAnchored = true;
  }

  void startCoverRequest() {
      auto& cover = state_->media.cover;
      if (!cover.cacheChecked) {
        trace("requested", state_->media.coverId);
        cover.cacheChecked = true;
        cover.decoding = true;
        cover.decodeAfterTransfer = false;
        serviceCoverDecode();
        return;
      }
      if (usbAuthorized_) {
        cover.inFlight = true;
        cover.requestedAtMs = millis();
        String request = "{\"protocol\":1,\"type\":\"cover.request\",\"data\":{\"coverId\":\"";
        request += state_->media.coverId;
        request += "\"}}";
        sendText(request, false);
      }
  }

  void serviceCoverDecode() {
    auto& cover = state_->media.cover;
    const auto result = config_.renderCover ? config_.renderCover(cover.requestedId, *state_) : CoverRenderResult::Failed;
    if (result == CoverRenderResult::Pending) return;
    cover.decoding = false;
    if (result == CoverRenderResult::Ready) {
      trace(cover.decodeAfterTransfer ? "decode_success" : "cache_hit", cover.requestedId);
      markCoverFetchComplete(cover, cover.requestedId, true, true);
    } else if (cover.decodeAfterTransfer) {
      trace("decode_failed", cover.requestedId);
      markCoverFetchComplete(cover, cover.requestedId, false);
    } else {
      trace("cache_miss", cover.requestedId);
      cover.inFlight = false;
      startCoverRequest();
    }
    if (intents_) intents_->push({IntentType::RefreshUi, 0});
  }

  void applyShortcuts(const char* payload, size_t length) {
    if (!state_) return;
    protocol::applyShortcutsUpdateJson(state_->shortcuts, payload, length);
    if (intents_) intents_->push({IntentType::ShortcutsUpdated, 0});
  }

  void applySettings(const char* payload, size_t length) {
    const String revision = quotedField(payload, length, "brightnessRevision");
    applyBrightnessRevision(state_->ui, revision.c_str(), boolField(payload, length, "applyBrightness", false));
    AppearanceState next = state_->appearance;
    next.theme = enumField(payload, length, "theme", "dark", "light", nullptr, next.theme);
    next.buttonStyle = enumField(payload, length, "buttonStyle", "flat", "rounded", "pill", next.buttonStyle);
    next.tileColorScheme = enumField(payload, length, "tileColorScheme", "control-center", "accent-tint", "oled-contrast", next.tileColorScheme);
    next.standbyStyle = enumField(payload, length, "standbyStyle", "apple-standby", "watchos-modular", "minimal-oled", next.standbyStyle);
    const String layoutValue = quotedField(payload, length, "shortcutsLayout");
    if (layoutValue == "auto") next.shortcutsLayout = 0;
    else if (layoutValue == "hero-1") next.shortcutsLayout = 1;
    else if (layoutValue == "split-2") next.shortcutsLayout = 2;
    else if (layoutValue == "quad-4") next.shortcutsLayout = 3;
    else if (layoutValue == "grid-6") next.shortcutsLayout = 4;
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
    sendText(message, false);
  }

  void maybeUsbHeartbeat() {
    if (!usbAuthorized_) return;
    const uint32_t now = millis();
    if (lastHeartbeatAt_ != 0 && now - lastHeartbeatAt_ < usb::kUsbHeartbeatIntervalMs) return;
    lastHeartbeatAt_ = now;
    String message = "{\"protocol\":1,\"type\":\"ping\",\"data\":{\"sentAtUnixMs\":";
    message += now;
    message += "}}";
    sendText(message, false);
  }

  void maybeFetchCover() {
    if (usbAuthorized_ || !state_ || !storage_ || !authorized_ || WiFi.status() != WL_CONNECTED) return;
    CoverFetchState& cover = state_->media.cover;
    if (httpRunning_ || cover.status != CoverStatus::Pending || cover.inFlight || cover.decoding || !cover.cacheChecked || !isValidCoverId(cover.requestedId)) return;

    char path[kCoverRequestPathMax]{};
    if (!makeCoverRequestPath(cover.requestedId, path, sizeof(path))) {
      markCoverFetchComplete(cover, cover.requestedId, false);
      return;
    }

    char requestId[kCoverIdLength + 1]{};
    copyText(requestId, sizeof(requestId), cover.requestedId, "");
    cover.inFlight = true;

    if (!httpQueue_) httpQueue_ = xQueueCreate(4, sizeof(HttpCoverChunk));
    if (!httpQueue_) { markCoverFetchComplete(cover, requestId, false); return; }
    xQueueReset(httpQueue_);
    copyText(httpCoverId_, sizeof(httpCoverId_), requestId);
    copyText(httpHost_, sizeof(httpHost_), host_.c_str());
    httpPort_ = config_.port;
    httpCancel_ = false;
    httpRunning_ = true;
    if (xTaskCreate([](void* context) {
          auto* self = static_cast<Impl*>(context);
          self->downloadHttpCover();
          self->httpRunning_ = false;
          vTaskDelete(nullptr);
        }, "cover-http", 4096, this, 1, nullptr) != pdPASS) {
      httpRunning_ = false;
      markCoverFetchComplete(cover, requestId, false);
    }
  }

  struct HttpCoverChunk {
    uint8_t kind = 0; // 0 = bytes, 1 = complete, 2 = failed
    uint16_t length = 0;
    uint32_t total = 0;
    uint8_t bytes[512]{};
  };

  bool queueHttpChunk(const HttpCoverChunk& chunk) {
    while (!httpCancel_) {
      if (xQueueSend(httpQueue_, &chunk, pdMS_TO_TICKS(20)) == pdTRUE) return true;
    }
    return false;
  }

  // Network waits run on a worker. Only the UI thread touches SD and LVGL.
  void downloadHttpCover() {
    WiFiClient client;
    client.setTimeout(kCoverHttpTimeoutMs);
    bool ok = false;
    if (!httpCancel_ && client.connect(httpHost_, httpPort_, kCoverHttpTimeoutMs)) {
      char path[kCoverRequestPathMax]{};
      makeCoverRequestPath(httpCoverId_, path, sizeof(path));
      client.print(F("GET "));
      client.print(path);
      client.print(F(" HTTP/1.1\r\nHost: "));
      client.print(httpHost_);
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
        std::size_t received = 0;
        const uint32_t startedAt = millis();
        while (!httpCancel_ && received < static_cast<std::size_t>(contentLength) &&
            millis() - startedAt < kCoverHttpTimeoutMs) {
          const int available = client.available();
          if (available <= 0) {
            vTaskDelay(pdMS_TO_TICKS(1));
            continue;
          }
          HttpCoverChunk chunk;
          chunk.total = contentLength;
          const int want = std::min<int>(available, std::min<int>(sizeof(chunk.bytes), contentLength - received));
          const int read = client.read(chunk.bytes, want);
          if (read <= 0) break;
          chunk.length = read;
          if (!queueHttpChunk(chunk)) break;
          received += read;
        }
        ok = received == static_cast<std::size_t>(contentLength);
      }
    }
    client.stop();
    HttpCoverChunk result;
    result.kind = ok ? 1 : 2;
    queueHttpChunk(result);
  }

  void serviceHttpCover() {
    if (!httpQueue_) return;
    if (!acceptsCoverTransfer(state_->media.cover, httpCoverId_) || usbAuthorized_) httpCancel_ = true;
    if (httpCancel_ && (httpFile_ || httpWritten_)) {
      if (httpFile_) httpFile_.close();
      storage_->discardCoverTemp(httpCoverId_);
      httpWritten_ = 0;
    }
    HttpCoverChunk chunk;
    if (xQueueReceive(httpQueue_, &chunk, 0) != pdTRUE) return;
    if (httpCancel_) {
      return;
    }
    bool ok = true;
    if (chunk.kind == 0) {
      if (!httpFile_) {
        trace("transfer_started", httpCoverId_);
        httpFile_ = storage_->openCoverTempWrite(httpCoverId_); httpWritten_ = 0;
      }
      httpTotal_ = chunk.total;
      ok = appendCountedCoverChunk(httpWritten_, httpTotal_, chunk.length, [&]() {
        return storage_->appendCoverTemp(httpFile_, chunk.bytes, chunk.length, httpWritten_);
      });
      if (ok) state_->media.cover.requestedAtMs = millis();
    } else {
      trace(chunk.kind == 1 ? "transfer_completed" : "transfer_failed", httpCoverId_);
      if (httpFile_) { httpFile_.flush(); httpFile_.close(); }
      ok = chunk.kind == 1 && httpWritten_ == httpTotal_ && storage_->commitCoverTemp(httpCoverId_, httpWritten_);
      if (ok) trace("saved", httpCoverId_);
      if (ok) {
        state_->media.cover.inFlight = false;
        state_->media.cover.decoding = true;
        state_->media.cover.decodeAfterTransfer = true;
        serviceCoverDecode();
      } else markCoverFetchComplete(state_->media.cover, httpCoverId_, false);
      httpWritten_ = 0;
    }
    if (!ok) {
      httpCancel_ = true;
      if (httpFile_) httpFile_.close();
      storage_->discardCoverTemp(httpCoverId_);
      markCoverFetchComplete(state_->media.cover, httpCoverId_, false);
      httpWritten_ = 0;
    }
  }

  void cancelHttpCover() {
    httpCancel_ = true;
    if (httpFile_) httpFile_.close();
    if (storage_ && httpCoverId_[0] && !(config_.coverDecodeBusy && config_.coverDecodeBusy()))
      storage_->discardCoverTemp(httpCoverId_);
    httpWritten_ = 0;
  }

  void maybeSendDeviceInfo() {
    if (!connected_ || !authorized_) return;
    const uint32_t now = millis();
    if (lastDeviceInfoAt_ != 0 && now - lastDeviceInfoAt_ < kDeviceInfoMs) return;
    sendDeviceInfo();
  }

  void maybeSendUsbDeviceInfo() {
    if (!usbAuthorized_) return;
    const uint32_t now = millis();
    if (lastDeviceInfoAt_ != 0 && now - lastDeviceInfoAt_ < kDeviceInfoMs) return;
    sendDeviceInfo();
  }

  String deviceInfoPayload() {
    if (!state_) return "{}";
    lastDeviceInfoAt_ = millis();
    state_->device.uptimeSeconds = lastDeviceInfoAt_ / 1000;
    state_->connection.wifiConnected = WiFi.status() == WL_CONNECTED;
    state_->connection.rssi = state_->connection.wifiConnected ? WiFi.RSSI() : 0;
    copyText(state_->connection.ssid, sizeof(state_->connection.ssid), state_->connection.wifiConnected ? WiFi.SSID().c_str() : "", "");
    copyText(state_->connection.deviceIp, sizeof(state_->connection.deviceIp), state_->connection.wifiConnected ? WiFi.localIP().toString().c_str() : "", "");
    state_->system.freeHeap = ESP.getFreeHeap();
    state_->system.minHeap = ESP.getMinFreeHeap();
    state_->system.largestBlock = heap_caps_get_largest_free_block(MALLOC_CAP_8BIT);

    String message = "{\"firmwareVersion\":\"";
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
    message += "},\"performance\":{\"loopMaxUs\":";
    message += state_->performance.loopMaxUs;
    message += ",\"renderMaxUs\":";
    message += state_->performance.renderMaxUs;
    message += ",\"networkMaxUs\":";
    message += state_->performance.networkMaxUs;
    message += ",\"slowLoops\":";
    message += state_->performance.slowLoops;
    message += ",\"wifiDiscoveryCount\":";
    message += state_->performance.wifiDiscoveryCount;
    message += "},\"audio\":{\"volumePercent\":";
    message += state_->audio.reportedVolume;
    message += ",\"muted\":";
    message += state_->audio.muted ? "true" : "false";
    message += "},\"cover\":{\"status\":";
    message += static_cast<unsigned>(state_->media.cover.status);
    message += ",\"rendered\":";
    message += state_->media.cover.displayed ? "true" : "false";
    if (diagnostics_) {
      message += ",\"coverId\":\"";
      message += state_->media.cover.requestedId;
      message += "\"";
    }
    message += "}}";
    return message;
  }

  void sendDeviceInfo() {
    sendText(String("{\"protocol\":1,\"type\":\"device.info\",\"data\":") + deviceInfoPayload() + "}", false);
  }

  void sendShortcutsReport() {
    if (!state_) return;
    char message[protocol::kShortcutsReportJsonCapacity]{};
    if (protocol::makeShortcutsReportJson(state_->shortcuts, message, sizeof(message))) {
      sendText(message, false);
    }
  }

  void sendHello(bool allowUnauthedUsb) {
    String message = "{\"protocol\":1,\"type\":\"hello\",\"data\":{\"deviceId\":\"";
    message += escapeJson(config_.deviceId);
    message += "\",\"name\":\"";
    message += escapeJson(config_.deviceName);
    message += "\",\"role\":\"esp32\",\"capabilities\":[\"media\",\"audio\",\"shortcuts\",\"system\",\"device-control\",\"cover-cache-request\"]";
    message += ",\"deviceInfo\":";
    message += deviceInfoPayload();
    message += "}}";
    sendText(message, allowUnauthedUsb);
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
    sendText(message, false);
  }

  void sendMediaCommand(const char* command) {
    String requestId = makeRequestId("media");
    if (!trackCommand(requestId, false)) return;
    String message = "{\"protocol\":1,\"type\":\"media.command\",\"requestId\":\"";
    message += requestId;
    message += "\",\"data\":{\"command\":\"";
    message += command;
    message += "\"}}";
    sendText(message, false);
  }

  void sendSetVolume(int value) {
    String requestId = makeRequestId("volume");
    if (!trackCommand(requestId, true)) return;
    copyText(state_->audio.requestId, sizeof(state_->audio.requestId), requestId.c_str());
    trace("volume_requested", requestId.c_str());
    String message = "{\"protocol\":1,\"type\":\"audio.setVolume\",\"requestId\":\"";
    message += requestId;
    message += "\",\"data\":{\"volumePercent\":";
    message += constrain(value, 0, 100);
    message += "}}";
    sendText(message, false);
  }

  void sendToggleMute() {
    String requestId = makeRequestId("mute");
    if (!trackCommand(requestId, false)) return;
    String message = "{\"protocol\":1,\"type\":\"audio.toggleMute\",\"requestId\":\"";
    message += requestId;
    message += "\",\"data\":{}}";
    sendText(message, false);
  }

  void sendShortcut(const char* shortcutId) {
    String requestId = makeRequestId("shortcut");
    if (!trackCommand(requestId, false)) return;
    char message[192]{};
    if (protocol::makeShortcutExecuteJson(requestId.c_str(), shortcutId, message, sizeof(message))) {
      sendText(message, false);
    }
  }

  void resetUsbCoverTransfer() {
    if (usbCoverFile_) usbCoverFile_.close();
    if (storage_ && usbCoverId_[0] && !(config_.coverDecodeBusy && config_.coverDecodeBusy()))
      storage_->discardCoverTemp(usbCoverId_);
    usbCoverId_[0] = '\0';
    usbCoverTotal_ = 0;
    usbCoverWritten_ = 0;
  }

  bool trackCommand(const String& id, bool volume) {
    if (pendingCommands_.add(id.c_str(), millis(), volume)) {
      if (lastIntentAtMs_) trace(volume ? "slider_interaction" : (id.startsWith("shortcut") ? "tile_pressed" : "touch_registered"), id.c_str(), lastIntentAtMs_);
      if (id.startsWith("shortcut")) trace("shortcut_requested", id.c_str());
      if (lastIntentAtMs_) trace("intent_queued", id.c_str(), lastIntentAtMs_);
      trace("command_sent", id.c_str());
      return true;
    }
    copyText(state_->commandFeedback, sizeof(state_->commandFeedback), "Busy - try again");
    state_->feedbackAtMs = millis();
    if (volume) { state_->audio.pending = false; state_->audio.failed = true; }
    return false;
  }

  void finishCommand(const PendingCommand& command, bool success, const char* failure) {
    if (command.volume && std::strcmp(state_->audio.requestId, command.id) != 0) return;
    trace(success ? "ack_received" : "command_failed", command.id);
    copyText(state_->commandFeedback, sizeof(state_->commandFeedback), success ? "Done" : failure);
    state_->feedbackAtMs = millis();
    if (command.volume && std::strcmp(state_->audio.requestId, command.id) == 0 && !success) {
      state_->audio.pending = false;
      state_->audio.failed = true;
    }
    if (intents_) intents_->push({IntentType::RefreshUi, 0});
  }

  void handleCoverChunk(const char* payload, size_t length) {
    if (!storage_ || !state_) return;
    const String coverId = quotedField(payload, length, "coverId");
    if (!acceptsCoverTransfer(state_->media.cover, coverId.c_str()) || state_->media.cover.decoding) return;
    const long offset = integerField(payload, length, "offset", -1);
    const long total = integerField(payload, length, "total", -1);
    const String data = quotedField(payload, length, "data");
    uint8_t decoded[usb::kMaxCoverChunkRawBytes]{};
    std::size_t decodedLength = 0;
    const bool valid = isValidCoverId(coverId.c_str()) && offset >= 0 && total > 0 &&
        static_cast<std::size_t>(total) <= storage::kMaxCoverBytes && data.length() > 0 &&
        usb::decodeBase64(data.c_str(), data.length(), decoded, sizeof(decoded), decodedLength) &&
        decodedLength > 0 && static_cast<std::size_t>(offset) + decodedLength <= static_cast<std::size_t>(total);
    if (!valid) {
      trace("transfer_failed", state_->media.cover.requestedId);
      if (state_) markCoverFetchComplete(state_->media.cover, state_->media.cover.requestedId, false);
      resetUsbCoverTransfer();
      return;
    }
    if (usbCoverId_[0] == '\0') {
      if (offset != 0) return;
      copyText(usbCoverId_, sizeof(usbCoverId_), coverId.c_str(), "");
      usbCoverTotal_ = static_cast<std::size_t>(total);
      usbCoverWritten_ = 0;
      usbCoverFile_ = storage_->openCoverTempWrite(usbCoverId_);
      trace("transfer_started", usbCoverId_);
    }
    if (std::strcmp(usbCoverId_, coverId.c_str()) != 0 || usbCoverTotal_ != static_cast<std::size_t>(total) ||
        usbCoverWritten_ != static_cast<std::size_t>(offset) || !usbCoverFile_) {
      trace("transfer_failed", state_->media.cover.requestedId);
      if (state_) markCoverFetchComplete(state_->media.cover, state_->media.cover.requestedId, false);
      resetUsbCoverTransfer();
      return;
    }
    if (!storage_->appendCoverTemp(usbCoverFile_, decoded, decodedLength, usbCoverWritten_)) {
      trace("transfer_failed", state_->media.cover.requestedId);
      if (state_) markCoverFetchComplete(state_->media.cover, state_->media.cover.requestedId, false);
      resetUsbCoverTransfer();
      return;
    }
    if (state_) state_->media.cover.requestedAtMs = millis();
  }

  void handleCoverComplete(const char* payload, size_t length) {
    if (!storage_ || !state_) return;
    const String coverId = quotedField(payload, length, "coverId");
    if (!acceptsCoverTransfer(state_->media.cover, coverId.c_str()) || state_->media.cover.decoding) return;
    if (!isValidCoverId(coverId.c_str()) || std::strcmp(usbCoverId_, coverId.c_str()) != 0 ||
        usbCoverWritten_ != usbCoverTotal_ || !usbCoverFile_) {
      if (state_) markCoverFetchComplete(state_->media.cover, state_->media.cover.requestedId, false);
      resetUsbCoverTransfer();
      return;
    }
    usbCoverFile_.flush();
    usbCoverFile_.close();
    trace("transfer_completed", usbCoverId_);
    const bool committed = storage_->commitCoverTemp(usbCoverId_, usbCoverWritten_);
    trace(committed ? "saved" : "transfer_failed", usbCoverId_);
    const bool isCurrent = state_ && std::strcmp(state_->media.cover.requestedId, usbCoverId_) == 0;
    if (committed && isCurrent) {
      state_->media.cover.inFlight = false;
      state_->media.cover.decoding = true;
      state_->media.cover.decodeAfterTransfer = true;
      serviceCoverDecode();
    } else if (isCurrent) {
      markCoverFetchComplete(state_->media.cover, usbCoverId_, false);
    }
    if (intents_) intents_->push({IntentType::RefreshUi, 0});
    sendDeviceInfo();
    usbCoverId_[0] = '\0';
    usbCoverTotal_ = 0;
    usbCoverWritten_ = 0;
  }

  bool canSend() const {
    return (connected_ && authorized_) || (config_.usbProtocolEnabled && usbAuthorized_);
  }

  void sendText(String message, bool allowUnauthedUsb) {
    if (!inUsbDispatch_ && !usbAuthorized_ && connected_ && authorized_) {
      socket_.sendTXT(message);
      return;
    }
    if (config_.usbProtocolEnabled && (usbAuthorized_ || allowUnauthedUsb)) {
      Serial.print(message);
      Serial.write(13);
      Serial.write(10);
    }
  }

  void sendUsbText(const String& message) {
    if (!config_.usbProtocolEnabled) return;
    Serial.print(message);
    Serial.write(13);
    Serial.write(10);
  }

  void sendUsbHelloIfDue(bool force = false) {
    if (!config_.usbProtocolEnabled) return;
    const uint32_t now = millis();
    if (!force && !usb::shouldSendUnauthedHello(true, usbAuthorized_, now, lastUsbHelloAt_, kUsbHelloMs)) return;
    lastUsbHelloAt_ = now;
    String message = "{\"protocol\":1,\"type\":\"hello\",\"data\":{\"deviceId\":\"";
    message += escapeJson(config_.deviceId);
    message += "\",\"name\":\"";
    message += escapeJson(config_.deviceName);
    message += "\",\"role\":\"esp32\",\"capabilities\":[\"media\",\"audio\",\"shortcuts\",\"system\",\"device-control\",\"cover-cache-request\"]";
    if (token_.length() > 0) {
      message += usb::kHelloBearerTokenFieldPrefix;
      message += escapeJson(token_);
      message += "\"";
    }
    message += ",\"deviceInfo\":";
    message += deviceInfoPayload();
    message += "}}";
    sendUsbText(message);
  }

  bool isUsbHandshakeOrRecoveryFrame(const char* payload, size_t length) const {
    const String type = quotedField(payload, length, "type");
    return type == protocol::kHello || type == protocol::kConnectionState || type == protocol::kPairingToken;
  }

  usb::UsbAuthDecision decideUsbFrameAuth(const char* payload, size_t length) const {
    const String type = quotedField(payload, length, "type");
    const String presentedToken = quotedField(payload, length, "token");
    return usb::decideUsbAuth(type.c_str(), presentedToken.c_str(), token_.c_str(), usbAuthorized_);
  }

  void serviceUsb() {
    if (!config_.usbProtocolEnabled) return;
    const uint32_t now = millis();
    if (usbAuthorized_ && lastUsbRxAt_ != 0 && now - lastUsbRxAt_ > usb::kUsbPeerTimeoutMs) {
      usbAuthorized_ = false;
      resetUsbCoverTransfer();
      publishConnectionState();
    }
    sendUsbHelloIfDue();
    const uint32_t readStarted = micros();
    unsigned bytesRead = 0;
    while (Serial.available() > 0 && bytesRead < 2048 && micros() - readStarted < 2000) {
      ++bytesRead;
      const usb::FrameReadResult result = usbReader_.push(static_cast<char>(Serial.read()));
      if (result == usb::FrameReadResult::Complete) {
        if (usb::decideIncomingUsbFrame(connected_ && authorized_,
                isUsbHandshakeOrRecoveryFrame(usbReader_.data(), usbReader_.length())) == usb::IncomingUsbDecision::Dispatch) {
          const usb::UsbAuthDecision authDecision = decideUsbFrameAuth(usbReader_.data(), usbReader_.length());
          if (authDecision == usb::UsbAuthDecision::Reject) {
            usbReader_.reset();
            continue;
          }
          if (authDecision == usb::UsbAuthDecision::AllowHello) {
            lastUsbRxAt_ = millis();
            const String role = quotedField(usbReader_.data(), usbReader_.length(), "role");
            if (usb::shouldReplyToHello(role.c_str())) sendUsbHelloIfDue(true);
            handleText(usbReader_.data(), usbReader_.length());
            usbReader_.reset();
            continue;
          }
          lastUsbRxAt_ = millis();
          const bool wifiAuthorized = authorized_;
          const String wifiHost = host_;
          authorized_ = usbAuthorized_;
          if (!usbAuthorized_ && httpRunning_) {
            cancelHttpCover();
            if (state_->media.cover.status == CoverStatus::Pending)
              markCoverFetchComplete(state_->media.cover, state_->media.cover.requestedId, false);
          }
          usbAuthorized_ = true;
          inUsbDispatch_ = true;
          host_ = "usb";
          handleText(usbReader_.data(), usbReader_.length());
          inUsbDispatch_ = false;
          usbAuthorized_ = authorized_;
          authorized_ = wifiAuthorized;
          host_ = wifiHost;
        }
        usbReader_.reset();
      }
    }
  }

  void logLine(const char* message) {
    if (config_.usbProtocolEnabled) return;
    Serial.println(message);
  }

  void trace(const char* stage, const char* correlation, uint32_t eventAtMs = UINT32_MAX) {
    if (!diagnostics_ || !canSend()) return;
    String id = (correlation && correlation[0]) ? correlation : "none";
    if (isValidCoverId(id.c_str())) id = id.substring(0, 8);
    String message = "{\"protocol\":1,\"type\":\"diagnostic.event\",\"data\":{\"stage\":\"";
    message += stage;
    message += "\",\"correlation\":\"";
    message += escapeJson(id.c_str());
    message += "\",\"deviceMs\":";
    message += eventAtMs == UINT32_MAX ? millis() : eventAtMs;
    message += " ,\"transport\":\"";
    message += usbAuthorized_ ? "USB" : "WiFi";
    message += "\"}}";
    sendText(message, false);
  }

  void logf(const char* format, const char* arg, uint16_t port, const char* path) {
    if (config_.usbProtocolEnabled) return;
    Serial.printf(format, arg, port, path);
  }

  AgentClientConfig config_{};
  bool diagnostics_ = false;
  bool metricsWereFresh_ = false;
  uint32_t lastIntentAtMs_ = 0;
  char lastDisplayedCover_[kCoverIdLength + 1]{};
  QueueHandle_t httpQueue_ = nullptr;
  std::atomic<bool> httpRunning_{false};
  std::atomic<bool> httpCancel_{false};
  char httpCoverId_[kCoverIdLength + 1]{};
  char httpHost_[64]{};
  uint16_t httpPort_ = 0;
  File httpFile_;
  std::size_t httpWritten_ = 0, httpTotal_ = 0;
  PendingCommands pendingCommands_;
  AppState* state_ = nullptr;
  IntentQueue* intents_ = nullptr;
  storage::SdStorage* storage_ = nullptr;
  WebSocketsClient socket_;
  String token_;
  String host_;
  bool connected_ = false;
  bool authorized_ = false;
  bool mdnsStarted_ = false;
  bool wifiSuspended_ = false;
  protocol::RetryTimer connectRetry_;
  protocol::RetryTimer mdnsRetry_;
  uint32_t backoffMs_ = kInitialBackoffMs;
  uint32_t lastHeartbeatAt_ = 0;
  uint32_t lastDeviceInfoAt_ = 0;
  uint32_t lastUsbHelloAt_ = 0;
  uint32_t lastUsbRxAt_ = 0;
  char usbFrame_[usb::kMaxFrameBytes + 1]{};
  usb::LineFrameReader usbReader_{usbFrame_, sizeof(usbFrame_)};
  bool usbAuthorized_ = false;
  bool inUsbDispatch_ = false;
  File usbCoverFile_;
  char usbCoverId_[kCoverIdLength + 1]{};
  std::size_t usbCoverTotal_ = 0;
  std::size_t usbCoverWritten_ = 0;
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
