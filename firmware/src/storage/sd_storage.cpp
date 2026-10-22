#include "storage/sd_storage.h"

#include <SD.h>
#include <cstring>
#include <cstdio>

#include "board_pins.h"

namespace streamdeck::storage {
namespace {
constexpr const char* kIndexPath = "/cache/covers.tsv";
constexpr const char* kIndexTempPath = "/cache/covers.tsv.tmp";
constexpr const char* kRequiredDirectories[] = {
  "/covers", "/icons", "/assets", "/cache", "/config", "/logs"
};

bool isJpeg(const uint8_t* data, std::size_t length) {
  return data != nullptr && length >= 4 &&
      data[0] == 0xFF && data[1] == 0xD8 &&
      data[length - 2] == 0xFF && data[length - 1] == 0xD9;
}

bool copyId(char* target, const char* source) {
  if (!isSafeAssetId(source)) {
    return false;
  }

  std::size_t i = 0;
  for (; source[i] != '\0' && i < kMaxCoverIdLength; ++i) {
    target[i] = source[i];
  }
  target[i] = '\0';
  return true;
}

const char* baseName(const char* path) {
  if (path == nullptr) {
    return "";
  }

  const char* last = path;
  for (const char* cursor = path; *cursor != '\0'; ++cursor) {
    if (*cursor == '/') {
      last = cursor + 1;
    }
  }
  return last;
}

bool coverIdFromFileName(const char* name, char* out) {
  const char* base = baseName(name);
  std::size_t length = 0;
  while (base[length] != '\0') {
    ++length;
  }

  if (length <= 4 || base[length - 4] != '.' ||
      base[length - 3] != 'j' || base[length - 2] != 'p' ||
      base[length - 1] != 'g') {
    return false;
  }
  if (length - 4 > kMaxCoverIdLength) {
    return false;
  }
  for (std::size_t i = 0; i < length - 4; ++i) {
    out[i] = base[i];
  }
  out[length - 4] = '\0';
  return isSafeAssetId(out);
}
}  // namespace

bool SdStorage::begin() {
  entries_.truncate(0);
  entryCount_ = 0;
  lastUseTick_ = 0;
  status_ = {};
  status_.message = "mounting";
  sdBus_.begin(board::sdSclk, board::sdMiso, board::sdMosi, board::sdCs);
  status_.mounted = SD.begin(board::sdCs, sdBus_, 10000000);
  if (!status_.mounted) {
    fs_ = nullptr;
    status_.message = "mount failed / card absent";
    return false;
  }

  fs_ = &SD;
  refreshCapacity();
  status_.ready = ensureLayout();
  if (!status_.ready) {
    status_.message = "mounted; layout failed";
    return false;
  }

  loadIndex();
  if (!status_.ready) return false;
  reconcileIndexFromDirectory();
  if (!status_.ready) return false;
  const bool indexReady = saveIndex();
  status_.message = indexReady ? "mounted; storage ready" : "mounted; cover index unavailable";
  return true;
}

bool SdStorage::hasCover(const char* coverId) {
  if (!status_.ready || !isSafeAssetId(coverId)) {
    return false;
  }

  char path[kMaxCoverPathLength]{};
  if (!makeCoverPath(coverId, path, sizeof(path))) {
    return false;
  }

  char backup[kMaxCoverPathLength]{};
  std::snprintf(backup, sizeof(backup), "%s.bak", path);
  if (!fs_->exists(path) && fs_->exists(backup)) fs_->rename(backup, path);
  File file = fs_->open(path, FILE_READ);
  if (!file) {
    return false;
  }

  const std::size_t size = file.size();
  file.close();
  if (size == 0 || size > kMaxCoverBytes) {
    fs_->remove(path);
    const int index = findCover(coverId);
    if (index >= 0) {
      eraseIndexEntry(static_cast<std::size_t>(index));
      saveIndex();
    }
    status_.message = "removed corrupt cover";
    return false;
  }

  if (!ensureCoverSlot(coverId)) return false;
  upsertCover(coverId, static_cast<uint32_t>(size));
  saveIndex();
  return true;
}

bool SdStorage::writeCover(const char* coverId, const uint8_t* jpeg, std::size_t length) {
  if (!status_.ready || !isSafeAssetId(coverId)) {
    status_.message = "invalid cover id or storage unavailable";
    return false;
  }
  if (length == 0 || length > kMaxCoverBytes || !isJpeg(jpeg, length)) {
    status_.message = "invalid cover payload";
    return false;
  }
  if (!pruneFor(length)) {
    status_.message = "cover cache prune failed";
    return false;
  }
  if (!ensureCoverSlot(coverId)) return false;

  char path[kMaxCoverPathLength]{};
  char tempPath[kMaxCoverPathLength]{};
  if (!makeCoverPath(coverId, path, sizeof(path)) ||
      !makeCoverTempPath(coverId, tempPath, sizeof(tempPath))) {
    status_.message = "cover path failed";
    return false;
  }

  if (fs_->exists(tempPath)) {
    fs_->remove(tempPath);
  }

  File out = fs_->open(tempPath, FILE_WRITE);
  if (!out) {
    status_.message = "cover temp open failed";
    return false;
  }

  const std::size_t written = out.write(jpeg, length);
  out.flush();
  out.close();
  if (written != length) {
    fs_->remove(tempPath);
    status_.message = "cover write failed";
    return false;
  }

  File verify = fs_->open(tempPath, FILE_READ);
  const bool sizeOk = verify && verify.size() == length;
  if (verify) {
    verify.close();
  }
  if (!sizeOk) {
    fs_->remove(tempPath);
    status_.message = "cover verify failed";
    return false;
  }

  if (fs_->exists(path) && !fs_->remove(path)) {
    fs_->remove(tempPath);
    status_.message = "cover replace failed";
    return false;
  }
  if (!fs_->rename(tempPath, path)) {
    fs_->remove(tempPath);
    status_.message = "cover commit failed";
    return false;
  }

  upsertCover(coverId, static_cast<uint32_t>(length));
  saveIndex();
  refreshCapacity();
  status_.message = "cover cached";
  return true;
}

File SdStorage::openCoverRead(const char* coverId) {
  if (!hasCover(coverId)) {
    return File();
  }

  char path[kMaxCoverPathLength]{};
  if (!makeCoverPath(coverId, path, sizeof(path))) {
    return File();
  }
  return fs_->open(path, FILE_READ);
}

File SdStorage::openCoverReadOnly(const char* coverId) {
  char path[kMaxCoverPathLength]{};
  if (!status_.ready || !isSafeAssetId(coverId) || !makeCoverPath(coverId, path, sizeof(path))) return File();
  return fs_->open(path, FILE_READ);
}

File SdStorage::openCoverTempWrite(const char* coverId) {
  if (!status_.ready || !isSafeAssetId(coverId)) {
    status_.message = "invalid cover id or storage unavailable";
    return File();
  }
  char tempPath[kMaxCoverPathLength]{};
  if (!makeCoverTempPath(coverId, tempPath, sizeof(tempPath))) {
    status_.message = "cover temp path failed";
    return File();
  }
  if (fs_->exists(tempPath)) fs_->remove(tempPath);
  mbedtls_sha256_init(&coverHash_);
  coverStream_ = {};
  mbedtls_sha256_starts_ret(&coverHash_, 0);
  std::snprintf(hashingCoverId_, sizeof(hashingCoverId_), "%s", coverId);
  return fs_->open(tempPath, FILE_WRITE);
}

bool SdStorage::appendCoverTemp(File& file, const uint8_t* data, std::size_t length, std::size_t& totalWritten) {
  if (!file || !data || length == 0 || totalWritten + length > kMaxCoverBytes) {
    status_.message = "cover stream bounds failed";
    return false;
  }
  const std::size_t written = file.write(data, length);
  if (written != length) {
    status_.message = "cover stream write failed";
    return false;
  }
  totalWritten += written;
  if (!coverStream_.append(data, written)) return false;
  mbedtls_sha256_update_ret(&coverHash_, data, written);
  return true;
}

bool SdStorage::commitCoverTemp(const char* coverId, std::size_t length) {
  if (!status_.ready || !isSafeAssetId(coverId) || length == 0 || length > kMaxCoverBytes) {
    discardCoverTemp(coverId);
    status_.message = "invalid cover payload";
    return false;
  }
  uint8_t hash[32]{};
  char hashId[65]{};
  mbedtls_sha256_finish_ret(&coverHash_, hash);
  for (unsigned i = 0; i < 32; ++i) std::snprintf(hashId + i * 2, 3, "%02x", hash[i]);
  if (std::strcmp(hashingCoverId_, coverId) != 0 || strcasecmp(hashId, coverId) != 0) {
    discardCoverTemp(coverId);
    status_.message = "cover checksum failed";
    return false;
  }
  if (!pruneFor(length)) {
    discardCoverTemp(coverId);
    status_.message = "cover cache prune failed";
    return false;
  }
  if (!ensureCoverSlot(coverId)) {
    discardCoverTemp(coverId);
    return false;
  }
  char path[kMaxCoverPathLength]{};
  char tempPath[kMaxCoverPathLength]{};
  if (!makeCoverPath(coverId, path, sizeof(path)) ||
      !makeCoverTempPath(coverId, tempPath, sizeof(tempPath))) {
    status_.message = "cover path failed";
    return false;
  }
  const bool jpeg = coverStream_.complete(length);
  if (!jpeg) {
    fs_->remove(tempPath);
    status_.message = "invalid cover payload";
    return false;
  }
  char backup[kMaxCoverPathLength]{};
  std::snprintf(backup, sizeof(backup), "%s.bak", path);
  if (fs_->exists(backup) && !fs_->remove(backup)) {
    fs_->remove(tempPath);
    status_.message = "cover backup unavailable";
    return false;
  }
  const bool replacing = fs_->exists(path);
  if (replacing && !fs_->rename(path, backup)) {
    fs_->remove(tempPath);
    status_.message = "cover replace failed";
    return false;
  }
  if (!fs_->rename(tempPath, path)) {
    if (replacing) fs_->rename(backup, path);
    fs_->remove(tempPath);
    status_.message = "cover commit failed";
    return false;
  }
  if (replacing) fs_->remove(backup);
  upsertCover(coverId, static_cast<uint32_t>(length));
  saveIndex();
  refreshCapacity();
  status_.message = "cover cached";
  return true;
}

void SdStorage::discardCoverTemp(const char* coverId) {
  if (!status_.ready || !isSafeAssetId(coverId)) return;
  char tempPath[kMaxCoverPathLength]{};
  if (makeCoverTempPath(coverId, tempPath, sizeof(tempPath)) && fs_->exists(tempPath)) {
    fs_->remove(tempPath);
  }
}

bool SdStorage::removeCover(const char* coverId) {
  if (!status_.ready || !isSafeAssetId(coverId)) {
    return false;
  }

  char path[kMaxCoverPathLength]{};
  if (!makeCoverPath(coverId, path, sizeof(path))) {
    return false;
  }

  const bool removed = !fs_->exists(path) || fs_->remove(path);
  const int index = findCover(coverId);
  if (index >= 0) {
    eraseIndexEntry(static_cast<std::size_t>(index));
    saveIndex();
  }
  refreshCapacity();
  return removed;
}

bool SdStorage::ensureLayout() {
  for (const char* path : kRequiredDirectories) {
    if (!ensureDirectory(path)) {
      return false;
    }
  }
  return true;
}

bool SdStorage::ensureDirectory(const char* path) {
  File existing = fs_->open(path, FILE_READ);
  if (existing) {
    const bool directory = existing.isDirectory();
    existing.close();
    return directory;
  }
  return fs_->mkdir(path);
}

bool SdStorage::loadIndex() {
  entries_.truncate(0);
  entryCount_ = 0;
  lastUseTick_ = 0;
  File file = fs_->open(kIndexPath, FILE_READ);
  if (!file) {
    return false;
  }

  char line[112]{};
  while (file.available() && entryCount_ < kMaxIndexEntries) {
    const std::size_t read = file.readBytesUntil('\n', line, sizeof(line) - 1);
    line[read] = '\0';
    char id[kMaxCoverIdLength + 1]{};
    unsigned long size = 0;
    unsigned long lastUse = 0;
    if (sscanf(line, "%64[ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-]\t%lu\t%lu",
        id, &size, &lastUse) == 3 &&
        isSafeAssetId(id) && size > 0 && size <= kMaxCoverBytes) {
      if (!entries_.ensure(entryCount_)) {
        status_.ready = false;
        status_.message = "cover index allocation failed";
        file.close();
        return false;
      }
      copyId(entries_[entryCount_].id, id);
      entries_[entryCount_].size = static_cast<uint32_t>(size);
      entries_[entryCount_].lastUse = static_cast<uint32_t>(lastUse);
      if (entries_[entryCount_].lastUse > lastUseTick_) {
        lastUseTick_ = entries_[entryCount_].lastUse;
      }
      ++entryCount_;
    }
  }
  file.close();
  status_.coverIndexEntries = static_cast<uint16_t>(entryCount_);
  return true;
}

bool SdStorage::saveIndex() {
  if (fs_->exists(kIndexTempPath)) {
    fs_->remove(kIndexTempPath);
  }
  File file = fs_->open(kIndexTempPath, FILE_WRITE);
  if (!file) {
    status_.message = "cover index write failed";
    return false;
  }

  uint64_t total = 0;
  for (std::size_t i = 0; i < entryCount_; ++i) {
    file.printf("%s\t%lu\t%lu\n", entries_[i].id,
        static_cast<unsigned long>(entries_[i].size),
        static_cast<unsigned long>(entries_[i].lastUse));
    total += entries_[i].size;
  }
  file.flush();
  file.close();

  if (fs_->exists(kIndexPath) && !fs_->remove(kIndexPath)) {
    fs_->remove(kIndexTempPath);
    status_.message = "cover index replace failed";
    return false;
  }
  if (!fs_->rename(kIndexTempPath, kIndexPath)) {
    fs_->remove(kIndexTempPath);
    status_.message = "cover index commit failed";
    return false;
  }
  status_.coverCacheBytes = total;
  status_.coverIndexEntries = static_cast<uint16_t>(entryCount_);
  return true;
}

void SdStorage::reconcileIndexFromDirectory() {
  File dir = fs_->open("/covers", FILE_READ);
  if (!dir || !dir.isDirectory()) {
    if (dir) {
      dir.close();
    }
    return;
  }

  File file = dir.openNextFile();
  while (file && entryCount_ < kMaxIndexEntries) {
    if (!file.isDirectory()) {
      char id[kMaxCoverIdLength + 1]{};
      const std::size_t size = file.size();
      if (coverIdFromFileName(file.name(), id)) {
        if (size == 0 || size > kMaxCoverBytes) {
          char path[kMaxCoverPathLength]{};
          if (makeCoverPath(id, path, sizeof(path))) {
            fs_->remove(path);
          }
        } else if (findCover(id) < 0) {
          if (!entries_.ensure(entryCount_)) {
            status_.ready = false;
            status_.message = "cover index allocation failed";
            file.close();
            dir.close();
            return;
          }
          copyId(entries_[entryCount_].id, id);
          entries_[entryCount_].size = static_cast<uint32_t>(size);
          entries_[entryCount_].lastUse = 0;
          ++entryCount_;
        }
      }
    }
    file = dir.openNextFile();
  }
  dir.close();

  uint64_t total = 0;
  for (std::size_t i = 0; i < entryCount_; ++i) {
    total += entries_[i].size;
  }
  status_.coverCacheBytes = total;
  status_.coverIndexEntries = static_cast<uint16_t>(entryCount_);
}

bool SdStorage::pruneFor(std::size_t incomingBytes) {
  if (incomingBytes > kMaxCoverCacheBytes) {
    return false;
  }

  uint64_t total = 0;
  for (std::size_t i = 0; i < entryCount_; ++i) {
    total += entries_[i].size;
  }

  while (total + incomingBytes > kMaxCoverCacheBytes && entryCount_ > 0) {
    std::size_t oldest = 0;
    for (std::size_t i = 1; i < entryCount_; ++i) {
      if (entries_[i].lastUse < entries_[oldest].lastUse) {
        oldest = i;
      }
    }

    char path[kMaxCoverPathLength]{};
    const uint32_t oldSize = entries_[oldest].size;
    if (!makeCoverPath(entries_[oldest].id, path, sizeof(path)) ||
        (fs_->exists(path) && !fs_->remove(path))) {
      return false;
    }
    eraseIndexEntry(oldest);
    total = total > oldSize ? total - oldSize : 0;
  }
  status_.coverCacheBytes = total;
  return true;
}

void SdStorage::refreshCapacity() {
  if (!status_.mounted) {
    status_.totalBytes = 0;
    status_.usedBytes = 0;
    return;
  }
  status_.totalBytes = SD.totalBytes();
  status_.usedBytes = SD.usedBytes();
}

bool SdStorage::ensureCoverSlot(const char* coverId) {
  if (!isSafeAssetId(coverId)) return false;
  if (findCover(coverId) >= 0) return true;
  if (entryCount_ >= kMaxIndexEntries) {
    std::size_t oldest = 0;
    for (std::size_t i = 1; i < entryCount_; ++i) {
      if (entries_[i].lastUse < entries_[oldest].lastUse) oldest = i;
    }
    char path[kMaxCoverPathLength]{};
    if (!makeCoverPath(entries_[oldest].id, path, sizeof(path)) ||
        (fs_->exists(path) && !fs_->remove(path))) {
      status_.message = "cover eviction failed";
      return false;
    }
    eraseIndexEntry(oldest);
    uint64_t total = 0;
    for (std::size_t i = 0; i < entryCount_; ++i) total += entries_[i].size;
    status_.coverCacheBytes = total;
    status_.coverIndexEntries = static_cast<uint16_t>(entryCount_);
  }
  if (!entries_.ensure(entryCount_)) {
    status_.message = "cover index allocation failed";
    return false;
  }
  return true;
}

void SdStorage::upsertCover(const char* coverId, uint32_t size) {
  const int existing = findCover(coverId);
  if (existing >= 0) {
    entries_[existing].size = size;
    entries_[existing].lastUse = nextUseTick();
    return;
  }
  if (entryCount_ >= kMaxIndexEntries || !entries_.ensure(entryCount_) ||
      !copyId(entries_[entryCount_].id, coverId)) {
    return;
  }
  entries_[entryCount_].size = size;
  entries_[entryCount_].lastUse = nextUseTick();
  ++entryCount_;
}

void SdStorage::eraseIndexEntry(std::size_t index) {
  if (index >= entryCount_) {
    return;
  }
  for (std::size_t i = index + 1; i < entryCount_; ++i) {
    entries_[i - 1] = entries_[i];
  }
  --entryCount_;
  entries_.truncate(entryCount_);
}

int SdStorage::findCover(const char* coverId) const {
  if (!isSafeAssetId(coverId)) {
    return -1;
  }
  for (std::size_t i = 0; i < entryCount_; ++i) {
    if (strcmp(entries_[i].id, coverId) == 0) {
      return static_cast<int>(i);
    }
  }
  return -1;
}

uint32_t SdStorage::nextUseTick() {
  const uint32_t now = millis() / 1000UL;
  if (now > lastUseTick_) {
    lastUseTick_ = now;
  } else {
    ++lastUseTick_;
  }
  return lastUseTick_;
}

}  // namespace streamdeck::storage
