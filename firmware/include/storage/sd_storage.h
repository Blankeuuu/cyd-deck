#pragma once

#include <Arduino.h>
#include <FS.h>
#include <SPI.h>
#include <mbedtls/sha256.h>

#include "storage/sd_paths.h"
#include "storage/paged_index.h"
#include "storage/cover_stream.h"

namespace streamdeck::storage {

struct SdStorageStatus {
  bool mounted = false;
  bool ready = false;
  uint64_t totalBytes = 0;
  uint64_t usedBytes = 0;
  uint64_t coverCacheBytes = 0;
  uint16_t coverIndexEntries = 0;
  String message = "not started";
};

class SdStorage {
 public:
  bool begin();
  const SdStorageStatus& status() const { return status_; }
  FS* fs() { return fs_; }

  bool hasCover(const char* coverId);
  bool writeCover(const char* coverId, const uint8_t* jpeg, std::size_t length);
  File openCoverTempWrite(const char* coverId);
  bool appendCoverTemp(File& file, const uint8_t* data, std::size_t length, std::size_t& totalWritten);
  bool commitCoverTemp(const char* coverId, std::size_t length);
  void discardCoverTemp(const char* coverId);
  File openCoverRead(const char* coverId);
  File openCoverReadOnly(const char* coverId);
  bool removeCover(const char* coverId);

 private:
  struct CoverIndexEntry {
    char id[kMaxCoverIdLength + 1] = {};
    uint32_t size = 0;
    uint32_t lastUse = 0;
  };

  static constexpr std::size_t kMaxIndexEntries = 512;

  bool ensureLayout();
  bool ensureDirectory(const char* path);
  bool loadIndex();
  bool saveIndex();
  void reconcileIndexFromDirectory();
  bool pruneFor(std::size_t incomingBytes);
  void refreshCapacity();
  void upsertCover(const char* coverId, uint32_t size);
  bool ensureCoverSlot(const char* coverId);
  void eraseIndexEntry(std::size_t index);
  int findCover(const char* coverId) const;
  uint32_t nextUseTick();

  SPIClass sdBus_{VSPI};
  FS* fs_ = nullptr;
  SdStorageStatus status_{};
  PagedIndex<CoverIndexEntry, kMaxIndexEntries> entries_;
  std::size_t entryCount_ = 0;
  uint32_t lastUseTick_ = 0;
  mbedtls_sha256_context coverHash_{};
  char hashingCoverId_[kMaxCoverIdLength + 1]{};
  CoverStreamValidation coverStream_;
};

}  // namespace streamdeck::storage
