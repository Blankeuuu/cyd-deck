#include <cassert>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#include "ui/cover_pixel_buffers.h"

namespace {
constexpr std::size_t kPixels = 128 * 128;
constexpr std::size_t kBytes = kPixels * sizeof(std::uint16_t);
int allocationCalls = 0;
int deallocationCalls = 0;
bool rejectAllocations = false;
int failOnCall = 0;

void* countedAllocate(std::size_t bytes) {
  ++allocationCalls;
  if (rejectAllocations || allocationCalls == failOnCall) return nullptr;
  assert(bytes == kBytes);
  return std::malloc(bytes);
}
void countedDeallocate(void* p) {
  ++deallocationCalls;
  std::free(p);
}

using Pool = streamdeck::ui::CoverPixelBuffers<kPixels>;

void testReservationFailuresRetry() {
  {
    allocationCalls = deallocationCalls = 0;
    failOnCall = 1;
    Pool pool(countedAllocate, countedDeallocate);
    assert(!pool.reserve());
    assert(!pool.reserved() && pool.begin("no-buffer").slot == 0xff);
    failOnCall = 0;
    assert(pool.reserve());
    assert(allocationCalls == 3 && deallocationCalls == 0);
    assert(pool.begin("retry").slot == 0);
  }
  assert(deallocationCalls == 2);
  {
    allocationCalls = deallocationCalls = 0;
    failOnCall = 2;
    Pool pool(countedAllocate, countedDeallocate);
    assert(!pool.reserve());
    assert(!pool.reserved() && pool.visible() == nullptr);
    assert(allocationCalls == 2 && deallocationCalls == 1);
    failOnCall = 0;
    assert(pool.reserve());
    assert(allocationCalls == 4 && deallocationCalls == 1);
  }
  assert(deallocationCalls == 3);
}

void testPersistentRuntime() {
  allocationCalls = deallocationCalls = 0;
  failOnCall = 0;
  Pool pool(countedAllocate, countedDeallocate);
  static_assert(sizeof(pool) <= 192, "pool object must remain small and pointer-only");
  assert(pool.visible() == nullptr);
  assert(pool.reserve());
  assert(pool.reserve());
  assert(allocationCalls == 2);

  auto first = pool.begin("cached-A");
  assert(first && pool.writable(first));
  pool.writable(first)[0] = 0x1234;
  assert(pool.publish(first, "cached-A", true));
  auto* originalVisible = pool.visible();
  assert(originalVisible && originalVisible[0] == 0x1234);

  rejectAllocations = true;
  for (int i = 0; i < 160; ++i) {
    char id[32];
    std::snprintf(id, sizeof(id), "cover-%d", i);
    auto t = pool.begin(id);
    assert(t && pool.writable(t));
    const auto* visibleBefore = pool.visible();
    const auto oldVisiblePixel = pool.visible()[0];
    assert(visibleBefore != pool.writable(t));
    pool.writable(t)[0] = static_cast<std::uint16_t>(i + 1);
    const bool shouldPublish = (i % 7) != 0;
    assert(pool.publish(t, id, shouldPublish) == shouldPublish);
    if (!shouldPublish) {
      assert(pool.visible() == visibleBefore);
      assert(pool.visible()[0] == oldVisiblePixel);
    }
  }
  assert(allocationCalls == 2);
  assert(deallocationCalls == 0);

  auto stale = pool.begin("old-id");
  assert(stale);
  auto* beforeStale = pool.visible();
  assert(pool.writable(stale) != beforeStale);
  pool.writable(stale)[0] = 0xbeef;
  assert(!pool.publish(stale, "new-id", true));
  assert(pool.visible() == beforeStale);
  assert(std::strcmp(pool.visibleId(), "old-id") != 0);
  assert(!pool.publish(stale, "new-id", true));

  auto abandoned = pool.begin("abandoned");
  assert(abandoned);
  pool.abandon(abandoned);
  assert(!pool.busy() && pool.visible() == beforeStale);
  auto afterAbandon = pool.begin("after-abandon");
  assert(afterAbandon);
  assert(pool.publish(afterAbandon, "after-abandon", false) == false);

  auto cachedReturn = pool.begin("cached-A");
  assert(cachedReturn);
  pool.writable(cachedReturn)[0] = 0x1234;
  assert(pool.publish(cachedReturn, "cached-A", true));
  assert(pool.visible()[0] == 0x1234);
  assert(std::strcmp(pool.visibleId(), "cached-A") == 0);
  assert(allocationCalls == 2 && deallocationCalls == 0);
  rejectAllocations = false;
}
}  // namespace

int main() {
  testReservationFailuresRetry();
  testPersistentRuntime();
  std::printf("cover_pixel_buffers_test: PASS (pool object=%zu B, persistent allocations=2, 160 updates, first/second failure retry)\n",
              sizeof(Pool));
}
