#include "network/ota_update.h"

#include <ArduinoOTA.h>
#include <Update.h>
#include <WiFi.h>
#include <cstring>

namespace streamdeck {

namespace {

constexpr uint16_t kOtaPort = 3232;

}  // namespace

void OtaUpdateService::setStatus(const char* text) {
  if (!state_) return;
  std::strncpy(state_->ota.status, text, sizeof(state_->ota.status) - 1);
  state_->ota.status[sizeof(state_->ota.status) - 1] = '\0';
}

void OtaUpdateService::begin(const OtaUpdateConfig& config, AppState& state) {
  if (started_ || WiFi.status() != WL_CONNECTED || config.password.length() < 8) return;

  state_ = &state;
  state_->ota.enabled = true;
  state_->ota.running = false;
  state_->ota.progress = 0;
  setStatus("OTA ready");

  ArduinoOTA.setPort(kOtaPort);
  ArduinoOTA.setHostname(config.hostname.c_str());
  ArduinoOTA.setPassword(config.password.c_str());
  ArduinoOTA.setMdnsEnabled(false);
  ArduinoOTA.setRebootOnSuccess(true);

  ArduinoOTA.onStart([this]() {
    if (!state_) return;
    state_->ota.running = true;
    state_->ota.progress = 0;
    setStatus("OTA receiving");
  });
  ArduinoOTA.onProgress([this](unsigned int progress, unsigned int total) {
    if (!state_ || total == 0) return;
    state_->ota.progress = static_cast<uint8_t>((progress * 100U) / total);
  });
  ArduinoOTA.onEnd([this]() {
    if (!state_) return;
    state_->ota.running = false;
    state_->ota.progress = 100;
    setStatus("OTA complete");
  });
  ArduinoOTA.onError([this](ota_error_t error) {
    if (!state_) return;
    state_->ota.running = false;
    state_->ota.progress = 0;
    switch (error) {
      case OTA_AUTH_ERROR:
        setStatus("OTA auth failed");
        break;
      case OTA_BEGIN_ERROR:
        setStatus("OTA begin failed");
        break;
      case OTA_CONNECT_ERROR:
        setStatus("OTA connect failed");
        break;
      case OTA_RECEIVE_ERROR:
        setStatus("OTA receive failed");
        break;
      case OTA_END_ERROR:
        setStatus("OTA end failed");
        break;
      default:
        setStatus("OTA failed");
        break;
    }
  });

  ArduinoOTA.begin();
  started_ = true;
}

void OtaUpdateService::loop() {
  if (!started_) return;
  ArduinoOTA.handle();
}

}  // namespace streamdeck
