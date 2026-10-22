#include <cassert>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include "storage/sd_storage.h"
#include "SD.h"

class FakeFsState {
 public:
  std::map<std::string, std::vector<uint8_t>> files;
  std::vector<std::string> dirs;
};
FakeSD SD;
File::File(std::shared_ptr<FakeFsState> state, std::string path, bool write, bool dir)
    : state_(std::move(state)), path_(std::move(path)), write_(write), dir_(dir) {}

std::size_t File::write(uint8_t value) { return write(&value, 1); }

std::size_t File::write(const uint8_t* data, std::size_t length) {
  if (!state_ || !write_) return 0;
  auto& bytes = state_->files[path_];
  bytes.insert(bytes.end(), data, data + length);
  return length;
}

int File::available() {
  return state_ && pos_ < state_->files[path_].size()
      ? static_cast<int>(state_->files[path_].size() - pos_)
      : 0;
}

int File::read() { return available() ? state_->files[path_][pos_++] : -1; }
int File::peek() { return available() ? state_->files[path_][pos_] : -1; }

std::size_t File::size() const {
  return state_ && state_->files.count(path_) ? state_->files[path_].size() : 0;
}

std::size_t File::readBytesUntil(char end, char* buffer, std::size_t capacity) {
  std::size_t count = 0;
  while (count < capacity && available()) {
    const int value = read();
    if (value == end) break;
    buffer[count++] = static_cast<char>(value);
  }
  return count;
}

int File::printf(const char* format, ...) {
  char buffer[256];
  va_list args;
  va_start(args, format);
  const int count = vsnprintf(buffer, sizeof(buffer), format, args);
  va_end(args);
  return count > 0
      ? static_cast<int>(write(reinterpret_cast<uint8_t*>(buffer), static_cast<std::size_t>(count)))
      : count;
}

FS::FS() : state_(std::make_shared<FakeFsState>()) {}

File FS::open(const char* path, const char* mode, const bool) {
  if (!path) return {};
  const bool writing = std::strcmp(mode, FILE_WRITE) == 0;
  if (writing) state_->files[path].clear();
  if (!writing && !state_->files.count(path)) return {};
  return File(state_, path, writing);
}

bool FS::exists(const char* path) { return state_->files.count(path) != 0; }

bool FS::remove(const char* path) {
  if (failRemove) return false;
  return state_->files.erase(path) != 0;
}

bool FS::rename(const char* from, const char* to) {
  auto entry = state_->files.find(from);
  if (entry == state_->files.end()) return false;
  state_->files[to] = std::move(entry->second);
  state_->files.erase(entry);
  return true;
}

bool FS::mkdir(const char* path) {
  state_->dirs.emplace_back(path);
  return true;
}

int main() {
  FS fs;
  streamdeck::storage::SdStorage storage;
  storage.setFileSystemForTest(&fs);
  const uint8_t jpg[] = {0xff, 0xd8, 0x00, 0xff, 0xd9};
  char id[24];

  // Fill the real SdStorage index through its public write path.
  for (int i = 0; i < 512; ++i) {
    std::snprintf(id, sizeof(id), "cover-%03d", i);
    assert(storage.writeCover(id, jpg, sizeof(jpg)));
  }

  // A full index still permits hits, and touching its oldest item protects it
  // from the single-entry LRU eviction required by the 513th write.
  assert(storage.status().coverIndexEntries == 512);
  assert(storage.status().coverCacheBytes == 512 * sizeof(jpg));
  assert(storage.hasCover("cover-100")); // Existing ID at capacity is still a hit.
  assert(storage.hasCover("cover-000")); // Touch the original LRU entry.
  assert(storage.writeCover("cover-512", jpg, sizeof(jpg)));
  assert(storage.status().coverIndexEntries == 512);
  assert(fs.exists("/covers/cover-000.jpg"));
  assert(!fs.exists("/covers/cover-001.jpg"));
  assert(fs.exists("/covers/cover-512.jpg"));
  assert(storage.status().coverCacheBytes == 512 * sizeof(jpg));

  // Failure to remove the selected LRU file must preserve it and reject write.
  FS failing;
  streamdeck::storage::SdStorage protectedStorage;
  protectedStorage.setFileSystemForTest(&failing);
  for (int i = 0; i < 512; ++i) {
    std::snprintf(id, sizeof(id), "old-%03d", i);
    assert(protectedStorage.writeCover(id, jpg, sizeof(jpg)));
  }
  failing.failRemove = true;
  assert(!protectedStorage.writeCover("new-cover", jpg, sizeof(jpg)));
  assert(failing.exists("/covers/old-000.jpg"));
  assert(protectedStorage.status().coverIndexEntries == 512);
  assert(protectedStorage.status().coverCacheBytes == 512 * sizeof(jpg));
}
