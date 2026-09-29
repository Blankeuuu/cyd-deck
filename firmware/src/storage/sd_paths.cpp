#include "storage/sd_paths.h"

namespace streamdeck::storage {
namespace {
bool isSafeAssetChar(char ch) {
  return (ch >= 'a' && ch <= 'z') ||
      (ch >= 'A' && ch <= 'Z') ||
      (ch >= '0' && ch <= '9') ||
      ch == '_' || ch == '-';
}

bool copyPath(const char* prefix, const char* id, const char* suffix,
    char* out, std::size_t outSize) {
  if (!isSafeAssetId(id) || out == nullptr || outSize == 0) {
    return false;
  }

  std::size_t prefixLength = 0;
  while (prefix[prefixLength] != '\0') {
    ++prefixLength;
  }
  std::size_t idLength = 0;
  while (id[idLength] != '\0') {
    ++idLength;
  }
  std::size_t suffixLength = 0;
  while (suffix[suffixLength] != '\0') {
    ++suffixLength;
  }

  if (prefixLength + idLength + suffixLength + 1 > outSize) {
    return false;
  }

  std::size_t pos = 0;
  for (std::size_t i = 0; prefix[i] != '\0'; ++i) {
    out[pos++] = prefix[i];
  }
  for (std::size_t i = 0; id[i] != '\0'; ++i) {
    out[pos++] = id[i];
  }
  for (std::size_t i = 0; suffix[i] != '\0'; ++i) {
    out[pos++] = suffix[i];
  }
  out[pos] = '\0';
  return true;
}
}  // namespace

bool isSafeAssetId(const char* id) {
  if (id == nullptr || id[0] == '\0') {
    return false;
  }

  std::size_t length = 0;
  while (id[length] != '\0') {
    if (length >= kMaxCoverIdLength || !isSafeAssetChar(id[length])) {
      return false;
    }
    ++length;
  }
  return true;
}

bool makeCoverPath(const char* coverId, char* out, std::size_t outSize) {
  return copyPath("/covers/", coverId, ".jpg", out, outSize);
}

bool makeCoverTempPath(const char* coverId, char* out, std::size_t outSize) {
  return copyPath("/cache/", coverId, ".jpg.tmp", out, outSize);
}

}  // namespace streamdeck::storage
