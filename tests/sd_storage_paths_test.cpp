#include <cassert>
#include <cstring>

#include "storage/sd_paths.h"
#include "storage/paged_index.h"

using namespace streamdeck::storage;

struct TrackedEntry {
  static int live;
  int value = 0;
  TrackedEntry() { ++live; }
  ~TrackedEntry() { --live; }
};
int TrackedEntry::live = 0;

void testPagedIndex() {
  PagedIndex<TrackedEntry, 512> index;
  assert(TrackedEntry::live == 0); // No SD/cache must cost no entry heap.
  assert(!index.ensure(512));
  for (std::size_t i = 0; i < 512; ++i) {
    assert(index.ensure(i));
    index[i].value = static_cast<int>(i + 1);
    assert(TrackedEntry::live == static_cast<int>(((i / 16) + 1) * 16));
  }
  const auto& readOnly = index;
  for (std::size_t i = 0; i < 512; ++i) assert(readOnly[i].value == static_cast<int>(i + 1));
  index.truncate(17);
  assert(TrackedEntry::live == 32);
  assert(index[16].value == 17);
  index.truncate(16);
  assert(TrackedEntry::live == 16);
  assert(index.ensure(16));
  assert(index[16].value == 0);
  assert(index[15].value == 16);
  index.truncate(0);
  assert(TrackedEntry::live == 0);
  assert(index.ensure(0));
  assert(index[0].value == 0);
}

int main() {
  testPagedIndex();
  assert(TrackedEntry::live == 0);
  assert(isSafeAssetId("e23a937fb4"));
  assert(isSafeAssetId("cover_ID-123"));
  assert(!isSafeAssetId(""));
  assert(!isSafeAssetId(nullptr));
  assert(!isSafeAssetId("../secret"));
  assert(!isSafeAssetId("album.cover"));
  assert(!isSafeAssetId("cover/id"));

  char tooLong[kMaxCoverIdLength + 2]{};
  memset(tooLong, 'a', sizeof(tooLong) - 1);
  assert(!isSafeAssetId(tooLong));

  char path[kMaxCoverPathLength]{};
  assert(makeCoverPath("e23a937fb4", path, sizeof(path)));
  assert(strcmp(path, "/covers/e23a937fb4.jpg") == 0);
  assert(makeCoverTempPath("e23a937fb4", path, sizeof(path)));
  assert(strcmp(path, "/cache/e23a937fb4.jpg.tmp") == 0);
  assert(!makeCoverPath("../secret", path, sizeof(path)));
  assert(!makeCoverPath("e23a937fb4", path, 8));
}
