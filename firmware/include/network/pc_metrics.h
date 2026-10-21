#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>

#include "ui/app_state.h"

namespace streamdeck::protocol {

inline bool pcNumberDelimiter(char c) {
  return c == ',' || c == '}' || c == ' ' || c == '\t' || c == '\r' || c == '\n';
}

// Bounded numeric tokens from the flat, authenticated system.stats payload.
// Null/missing fields are unavailable, never a request to keep the previous sample.
inline bool pcNumberToken(const char* json, std::size_t length, const char* field,
                           char (&token)[48]) {
  if (!json || !field) return false;
  char key[40];
  const int keyLength = std::snprintf(key, sizeof(key), "\"%s\"", field);
  if (keyLength <= 0 || static_cast<std::size_t>(keyLength) >= sizeof(key)) return false;
  const char* end = json + length;
  const char* cursor = std::search(json, end, key, key + keyLength);
  if (cursor == end) return false;
  cursor += keyLength;
  auto whitespace = [&]() {
    while (cursor < end && (*cursor == ' ' || *cursor == '\t' || *cursor == '\r' || *cursor == '\n')) ++cursor;
  };
  whitespace();
  if (cursor == end || *cursor++ != ':') return false;
  whitespace();
  if (cursor == end || (*cursor != '-' && (*cursor < '0' || *cursor > '9'))) return false;
  std::size_t used = 0;
  while (cursor < end && !pcNumberDelimiter(*cursor)) {
    if ((*cursor < '0' || *cursor > '9') && *cursor != '.' && *cursor != '-' &&
        *cursor != '+' && *cursor != 'e' && *cursor != 'E') return false;
    if (used + 1 >= sizeof(token)) return false;
    token[used++] = *cursor++;
  }
  token[used] = '\0';
  return used > 0 && cursor < end;
}

inline bool pcPercent(const char* json, std::size_t length, const char* field, uint8_t& value) {
  char token[48];
  if (!pcNumberToken(json, length, field, token)) return false;
  char* end = nullptr;
  const double number = std::strtod(token, &end);
  if (!end || *end || !std::isfinite(number) || number < 0 || number > 100) return false;
  value = static_cast<uint8_t>(std::lround(number));
  return true;
}

inline bool pcBytes(const char* json, std::size_t length, const char* field, uint64_t& value) {
  char token[48];
  if (!pcNumberToken(json, length, field, token)) return false;
  uint64_t parsed = 0;
  for (const char* read = token; *read; ++read) {
    if (*read < '0' || *read > '9') return false;
    const unsigned digit = static_cast<unsigned>(*read - '0');
    if (parsed > (UINT64_MAX - digit) / 10U) return false;
    parsed = parsed * 10U + digit;
  }
  value = parsed;
  return true;
}

inline void applyPcMetricsJson(SystemState& state, const char* json, std::size_t length, uint32_t nowMs) {
  state.cpuAvailable = pcPercent(json, length, "cpuPercent", state.cpuPercent);
  state.ramAvailable = pcPercent(json, length, "memoryPercent", state.ramPercent);
  state.gpuAvailable = pcPercent(json, length, "gpuPercent", state.gpuPercent);
  char temperature[48];
  state.tempAvailable = false;
  if (pcNumberToken(json, length, "tempCelsius", temperature)) {
    char* end = nullptr;
    const double value = std::strtod(temperature, &end);
    if (end && !*end && std::isfinite(value) && value >= -40 && value <= 150) {
      state.tempCelsius = static_cast<int16_t>(std::lround(value));
      state.tempAvailable = true;
    }
  }
  uint64_t used = 0, total = 0;
  state.ramCapacityAvailable = pcBytes(json, length, "memoryUsedBytes", used) &&
      pcBytes(json, length, "memoryTotalBytes", total) && total > 0 && used <= total &&
      total / 1048576ULL <= UINT32_MAX;
  state.ramUsedMb = state.ramCapacityAvailable ? static_cast<uint32_t>(used / 1048576ULL) : 0;
  state.ramTotalMb = state.ramCapacityAvailable ? static_cast<uint32_t>(total / 1048576ULL) : 0;
  state.pcStatsReceived = true;
  state.pcStatsAtMs = nowMs;
}

}  // namespace streamdeck::protocol
