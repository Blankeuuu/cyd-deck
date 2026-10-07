#pragma once

#include <cstddef>
#include <memory>
#include <new>

namespace streamdeck::storage {

// Keep the cache index bounded without reserving RAM for an entire SD library
// on devices with no card or only a few covers. Small independent pages also
// avoid requiring a single large contiguous allocation alongside TLS.
template <typename T, std::size_t MaxEntries, std::size_t EntriesPerPage = 16>
class PagedIndex {
 public:
  static_assert(MaxEntries > 0 && EntriesPerPage > 0, "nonempty index required");

  bool ensure(std::size_t index) {
    if (index >= MaxEntries) return false;
    auto& page = pages_[index / EntriesPerPage];
    if (!page) page.reset(new (std::nothrow) T[EntriesPerPage]{});
    return static_cast<bool>(page);
  }

  T& operator[](std::size_t index) { return pages_[index / EntriesPerPage][index % EntriesPerPage]; }
  const T& operator[](std::size_t index) const { return pages_[index / EntriesPerPage][index % EntriesPerPage]; }

  void truncate(std::size_t size) {
    const std::size_t firstUnusedPage = (size + EntriesPerPage - 1) / EntriesPerPage;
    for (std::size_t i = firstUnusedPage; i < kPageCount; ++i) pages_[i].reset();
  }

 private:
  static constexpr std::size_t kPageCount = (MaxEntries + EntriesPerPage - 1) / EntriesPerPage;
  std::unique_ptr<T[]> pages_[kPageCount];
};

}  // namespace streamdeck::storage
