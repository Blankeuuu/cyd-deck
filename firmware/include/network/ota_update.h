#pragma once

#include <Arduino.h>

#include "ui/app_state.h"

namespace streamdeck {

struct OtaUpdateConfig {
  String hostname;
  String password;
};

class OtaUpdateService {
 public:
  void begin(const OtaUpdateConfig& config, AppState& state);
  void loop();
  bool started() const { return started_; }

 private:
  AppState* state_ = nullptr;
  bool started_ = false;

  void setStatus(const char* text);
};

}  // namespace streamdeck
