#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>
#include <cstdio>
namespace streamdeck {

constexpr std::size_t kCoverIdLength = 64;
constexpr std::size_t kCoverRequestPathMax = 96;
constexpr std::size_t kMaxCoverBytes = 512U * 1024U;

enum class CoverStatus : unsigned char { NoCover, Pending, Available, Failed };
enum class CoverTransition : unsigned char { Unchanged, Requested, Cleared, Available, Failed, Stale };
enum class CoverRenderResult : unsigned char { Pending, Ready, Failed };

struct CoverFetchState {
  char requestedId[kCoverIdLength + 1] = "";
  char availableId[kCoverIdLength + 1] = "";
  CoverStatus status = CoverStatus::NoCover;
  bool inFlight = false;
  bool rendered = false;
  bool displayed = false;
  bool decoding = false;
  bool decodeAfterTransfer = false;
  bool cacheChecked = false;
  uint32_t requestedAtMs = 0;
  uint8_t retries = 0;
  bool retryArmed = false;
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

// Late chunks/completions must not change a newer request or reopen a finished one.
inline bool acceptsCoverTransfer(const CoverFetchState& state, const char* coverId) {
  return state.status == CoverStatus::Pending && isValidCoverId(coverId) &&
      std::strcmp(state.requestedId, coverId) == 0;
}

// SdStorage::appendCoverTemp owns the byte counter. Validate the response bounds
// before writing and require exactly one increment after a successful append.
template <typename Append>
inline bool appendCountedCoverChunk(std::size_t& received, std::size_t total,
                                    std::size_t length, Append append) {
  if (total == 0 || total > kMaxCoverBytes || length == 0 ||
      received > total || length > total - received) return false;
  const std::size_t expected = received + length;
  return append() && received == expected;
}

inline uint16_t swapRgb565Bytes(uint16_t value) {
  return static_cast<uint16_t>((value >> 8) | (value << 8));
}

inline void convertDisplayOrderRgb565ToLvgl(uint16_t* pixels, std::size_t count) {
  if (!pixels) return;
  for (std::size_t i = 0; i < count; ++i) pixels[i] = swapRgb565Bytes(pixels[i]);
}

inline CoverTransition updateCoverFetch(CoverFetchState& state, const char* coverId, uint32_t nowMs = 0) {
  if (!isValidCoverId(coverId)) {
    const bool changed = state.status != CoverStatus::NoCover || state.requestedId[0] || state.availableId[0] || state.inFlight;
    state = {};
    (void)changed;
    return CoverTransition::Cleared;
  }

  if (std::strcmp(state.requestedId, coverId) == 0) {
    if (state.status != CoverStatus::Failed || state.retries >= 3) return CoverTransition::Unchanged;
    if (!state.retryArmed) {
      state.retryArmed = true;
      state.requestedAtMs = nowMs;
      return CoverTransition::Unchanged;
    }
    if (nowMs - state.requestedAtMs < (1000U << state.retries)) return CoverTransition::Unchanged;
    ++state.retries;
  } else {
    state.retries = 0;
  }

  std::strncpy(state.requestedId, coverId, sizeof(state.requestedId) - 1);
  state.requestedId[sizeof(state.requestedId) - 1] = '\0';
  state.availableId[0] = '\0';
  state.status = CoverStatus::Pending;
  state.requestedAtMs = nowMs;
  state.inFlight = false;
  state.rendered = false;
  state.displayed = false;
  state.retryArmed = false;
  state.decoding = false;
  state.decodeAfterTransfer = false;
  state.cacheChecked = false;
  return CoverTransition::Requested;
}

template <std::size_t N>
inline void copyMediaText(char (&target)[N], const char* value, const char* fallback = "") {
  const char* source = value && value[0] ? value : fallback;
  std::strncpy(target, source, N - 1);
  target[N - 1] = '\0';
}

template <std::size_t N>
inline bool mediaTextMatches(const char (&current)[N], const char* incoming,
                             const char* fallback = "") {
  char normalized[N]{};
  copyMediaText(normalized, incoming, fallback);
  return std::strcmp(current, normalized) == 0;
}

template <std::size_t TitleSize, std::size_t ArtistSize, std::size_t AlbumSize,
          std::size_t CoverIdSize>
inline CoverTransition updateMediaArtwork(CoverFetchState& cover,
                                          char (&title)[TitleSize],
                                          char (&artist)[ArtistSize],
                                          char (&album)[AlbumSize],
                                          char (&coverId)[CoverIdSize],
                                          const char* nextTitle,
                                          const char* nextArtist,
                                          const char* nextAlbum,
                                          const char* nextCoverId,
                                          uint32_t nowMs = 0) {
  const bool sameTrack = mediaTextMatches(title, nextTitle, "No track") &&
      mediaTextMatches(artist, nextArtist, "Unknown artist") &&
      mediaTextMatches(album, nextAlbum);
  if (sameTrack && !isValidCoverId(nextCoverId)) return CoverTransition::Unchanged;

  copyMediaText(title, nextTitle, "No track");
  copyMediaText(artist, nextArtist, "Unknown artist");
  copyMediaText(album, nextAlbum);
  copyMediaText(coverId, nextCoverId);
  return updateCoverFetch(cover, coverId, nowMs);
}

inline CoverTransition markCoverFetchComplete(CoverFetchState& state, const char* coverId, bool ok, bool rendered = true) {
  if (!isValidCoverId(coverId) || std::strcmp(state.requestedId, coverId) != 0) {
    return CoverTransition::Stale;
  }

  state.inFlight = false;
  state.retryArmed = false;
  state.displayed = false;
  state.decoding = false;
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

inline bool expirePendingCover(CoverFetchState& state, uint32_t nowMs, uint32_t timeoutMs = 15000) {
  if (state.status != CoverStatus::Pending || nowMs - state.requestedAtMs < timeoutMs) return false;
  markCoverFetchComplete(state, state.requestedId, false);
  return true;
}

}  // namespace streamdeck
