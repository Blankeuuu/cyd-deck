#include <cassert>
#include <cstring>

#include "storage/sd_paths.h"

using namespace streamdeck::storage;

int main() {
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
