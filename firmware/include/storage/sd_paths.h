#pragma once

#include <cstddef>
#include <cstdint>

namespace streamdeck::storage {

constexpr std::size_t kMaxCoverIdLength = 64;
constexpr std::size_t kMaxCoverPathLength = 96;
constexpr std::size_t kMaxCoverBytes = 512UL * 1024UL;
constexpr uint64_t kMaxCoverCacheBytes = 256ULL * 1024ULL * 1024ULL;

bool isSafeAssetId(const char* id);
bool makeCoverPath(const char* coverId, char* out, std::size_t outSize);
bool makeCoverTempPath(const char* coverId, char* out, std::size_t outSize);

}  // namespace streamdeck::storage
