#pragma once

#include <cstddef>
#include <cstring>
#include <cstdio>

namespace streamdeck {

constexpr std::size_t kCoverIdLength = 64;
constexpr std::size_t kCoverRequestPathMax = 96;
constexpr std::size_t kMaxCoverBytes = 512U * 1024U;

enum class CoverStatus : unsigned char { NoCover, Pending, Available, Failed };
enum class CoverTransition : unsigned char { Unchanged, Requested, Cleared, Available, Failed, Stale };

struct CoverFetchState {
  char requestedId[kCoverIdLength + 1] = "";
  char availableId[kCoverIdLength + 1] = "";
  CoverStatus status = CoverStatus::NoCover;
  bool inFlight = false;
  bool rendered = false;
};

inline bool isHex(char value) {
  return (value >= '0' && value <= '9') ||
      (value >= 'a' && value <= 'f') ||
      (value >= 'A' && value <= 'F');
}

inline bool isValidCoverId(const char* coverId) {
  if (!coverId) return false;
  for (std::size_t i = 0; i < kCoverIdLength; ++i) {
    if (!isHex(coverId[i])) return false;
  }
  return coverId[kCoverIdLength] == '\0';
}

inline bool makeCoverRequestPath(const char* coverId, char* out, std::size_t outSize) {
  if (!isValidCoverId(coverId) || !out || outSize < kCoverRequestPathMax) return false;
  const int written = std::snprintf(out, outSize, "/api/covers/%s.jpg", coverId);
  return written > 0 && static_cast<std::size_t>(written) < outSize;
}

inline CoverTransition updateCoverFetch(CoverFetchState& state, const char* coverId) {
  if (!isValidCoverId(coverId)) {
    const bool changed = state.status != CoverStatus::NoCover || state.requestedId[0] || state.availableId[0] || state.inFlight;
    state = {};
    (void)changed;
    return CoverTransition::Cleared;
  }

  if (std::strcmp(state.requestedId, coverId) == 0 && state.status != CoverStatus::Failed) {
    return CoverTransition::Unchanged;
  }

  std::strncpy(state.requestedId, coverId, sizeof(state.requestedId) - 1);
  state.requestedId[sizeof(state.requestedId) - 1] = '\0';
  state.availableId[0] = '\0';
  state.status = CoverStatus::Pending;
  state.inFlight = false;
  state.rendered = false;
  return CoverTransition::Requested;
}

inline CoverTransition markCoverFetchComplete(CoverFetchState& state, const char* coverId, bool ok, bool rendered = true) {
  if (!isValidCoverId(coverId) || std::strcmp(state.requestedId, coverId) != 0) {
    return CoverTransition::Stale;
  }

  state.inFlight = false;
  if (!ok) {
    state.availableId[0] = '\0';
    state.status = CoverStatus::Failed;
    state.rendered = false;
    return CoverTransition::Failed;
  }

  if (!rendered) {
    state.availableId[0] = '\0';
    state.status = CoverStatus::Failed;
    state.rendered = false;
    return CoverTransition::Failed;
  }

  std::strncpy(state.availableId, coverId, sizeof(state.availableId) - 1);
  state.availableId[sizeof(state.availableId) - 1] = '\0';
  state.status = CoverStatus::Available;
  state.rendered = true;
  return CoverTransition::Available;
}

}  // namespace streamdeck
