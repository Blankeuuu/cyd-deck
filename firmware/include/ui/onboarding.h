#pragma once

#include <cstdint>
#include <cstring>

namespace streamdeck {
namespace onboarding {

constexpr uint8_t kDefaultBrightness = 86;
constexpr uint8_t kMinBrightness = 10;
constexpr uint8_t kMaxBrightness = 100;

struct BootState {
  constexpr BootState(bool calibration, bool provisioning)
      : calibrationValid(calibration), provisioningComplete(provisioning) {}

  bool calibrationValid = false;
  bool provisioningComplete = false;
};

inline bool needsCalibration(const BootState& state) {
  return !state.calibrationValid;
}

inline bool needsProvisioning(const BootState& state) {
  return state.calibrationValid && !state.provisioningComplete;
}

inline bool canStartApp(const BootState& state) {
  return state.calibrationValid && state.provisioningComplete;
}

inline uint8_t clampBrightness(int value) {
  if (value < kMinBrightness) return kMinBrightness;
  if (value > kMaxBrightness) return kMaxBrightness;
  return static_cast<uint8_t>(value);
}

inline bool validSsid(const char* value) {
  if (!value) return false;
  const size_t len = std::strlen(value);
  return len >= 1 && len <= 32;
}

inline bool validWifiPassword(const char* value) {
  if (!value) return false;
  const size_t len = std::strlen(value);
  return len == 0 || (len >= 8 && len <= 63);
}

inline bool validDeviceName(const char* value) {
  if (!value) return false;
  const size_t len = std::strlen(value);
  if (len < 1 || len > 31) return false;
  for (size_t i = 0; i < len; ++i) {
    const char c = value[i];
    const bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
        (c >= '0' && c <= '9') || c == '-' || c == '_';
    if (!ok) return false;
  }
  return true;
}

inline bool validIpv4OrEmpty(const char* value) {
  if (!value || value[0] == '\0') return true;
  uint8_t parts = 0;
  const char* cursor = value;
  while (*cursor) {
    if (parts == 4) return false;
    int number = 0;
    int digits = 0;
    while (*cursor >= '0' && *cursor <= '9') {
      number = number * 10 + (*cursor - '0');
      ++digits;
      ++cursor;
      if (number > 255 || digits > 3) return false;
    }
    if (digits == 0) return false;
    ++parts;
    if (*cursor == '.') {
      ++cursor;
      if (*cursor == '\0') return false;
    } else if (*cursor != '\0') {
      return false;
    }
  }
  return parts == 4;
}

}  // namespace onboarding
}  // namespace streamdeck
