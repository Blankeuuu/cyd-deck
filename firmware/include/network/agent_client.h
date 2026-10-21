#pragma once

#include <Arduino.h>
#include <cstdint>

#include "ui/app_state.h"
#include "storage/sd_storage.h"

namespace streamdeck {

struct AgentClientConfig {
  String deviceId;
  String deviceName;
  String manualHost;
  uint16_t port = 8765;
  bool usbProtocolEnabled = true;
  CoverRenderResult (*renderCover)(const char* coverId, AppState& state) = nullptr;
  bool (*coverDecodeBusy)() = nullptr;
};

class AgentClient {
 public:
  void begin(const AgentClientConfig& config, AppState& state);
  void begin(const AgentClientConfig& config, AppState& state, IntentQueue& intents);
  void begin(const AgentClientConfig& config, AppState& state, IntentQueue& intents, storage::SdStorage& storage);
  void loop();
  void sendIntent(const Intent& intent);

 private:
  class Impl;
  Impl* impl_ = nullptr;
};

}  // namespace streamdeck
