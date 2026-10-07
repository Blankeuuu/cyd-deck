#include <cassert>
#include <cstddef>

#include "storage/paged_index.h"

using streamdeck::storage::PagedIndex;

struct Entry {
  static int live;
  int value = 0;

  Entry() { ++live; }
  ~Entry() { --live; }
};

int Entry::live = 0;

void testCapacityBoundariesAndZeroInitialization() {
  PagedIndex<Entry, 10, 4> index;
  assert(Entry::live == 0);

  assert(index.ensure(0));
  assert(Entry::live == 4);
  assert(index[0].value == 0);
  assert(index[3].value == 0);

  index[0].value = 11;
  index[3].value = 33;
  assert(index.ensure(3));
  assert(Entry::live == 4);
  assert(index[0].value == 11);
  assert(index[3].value == 33);

  assert(index.ensure(4));
  assert(Entry::live == 8);
  assert(index[4].value == 0);
  index[4].value = 44;

  assert(index.ensure(9));
  assert(Entry::live == 12);
  assert(index[9].value == 0);
  index[9].value = 99;

  assert(!index.ensure(10));
  assert(!index.ensure(999));
  assert(Entry::live == 12);
  assert(index[9].value == 99);
}

void testTruncateAcrossPageBoundaryAndReuse() {
  PagedIndex<Entry, 10, 4> index;
  for (std::size_t i = 0; i < 10; ++i) {
    assert(index.ensure(i));
    index[i].value = static_cast<int>(i + 1);
  }
  assert(Entry::live == 12);

  index.truncate(5);
  assert(Entry::live == 8);
  assert(index[0].value == 1);
  assert(index[4].value == 5);

  index.truncate(4);
  assert(Entry::live == 4);
  assert(index[3].value == 4);

  assert(index.ensure(4));
  assert(Entry::live == 8);
  assert(index[4].value == 0);
  index[4].value = 40;

  assert(index.ensure(9));
  assert(Entry::live == 12);
  assert(index[9].value == 0);
  index[9].value = 90;

  index.truncate(10);
  assert(Entry::live == 12);
  assert(index[4].value == 40);
  assert(index[9].value == 90);

  assert(!index.ensure(10));
  assert(Entry::live == 12);

  index.truncate(0);
  assert(Entry::live == 0);

  assert(index.ensure(0));
  assert(Entry::live == 4);
  assert(index[0].value == 0);
}

int main() {
  testCapacityBoundariesAndZeroInitialization();
  assert(Entry::live == 0);

  testTruncateAcrossPageBoundaryAndReuse();
  assert(Entry::live == 0);
}
