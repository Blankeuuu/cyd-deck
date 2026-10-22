#pragma once

#include <cstddef>
#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace streamdeck::ui {

// Small owner for two long-lived RGB565 buffers. Allocation is an explicit,
// retryable initialization step; lifecycle operations never allocate.
template <std::size_t PixelCount>
class CoverPixelBuffers {
 public:
  static constexpr std::size_t kPixelCount = PixelCount;
  static constexpr std::size_t kBufferCount = 2;
  using Allocate = void* (*)(std::size_t);
  using Deallocate = void (*)(void*);

  struct Ticket {
    std::uint32_t generation = 0;
    std::uint8_t slot = 0xff;
    char id[65]{};
    explicit operator bool() const { return slot < kBufferCount; }
  };

  explicit CoverPixelBuffers(Allocate allocate = std::malloc,
                             Deallocate deallocate = std::free)
      : allocate_(allocate), deallocate_(deallocate) {}
  ~CoverPixelBuffers() { release(); }
  CoverPixelBuffers(const CoverPixelBuffers&) = delete;
  CoverPixelBuffers& operator=(const CoverPixelBuffers&) = delete;

  bool reserve() {
    if (pixels_[0] && pixels_[1]) return true;
    if (!allocate_ || !deallocate_) return false;
    void* first = allocate_(kPixelCount * sizeof(std::uint16_t));
    if (!first) return false;
    void* second = allocate_(kPixelCount * sizeof(std::uint16_t));
    if (!second) {
      deallocate_(first);
      return false;
    }
    pixels_[0] = static_cast<std::uint16_t*>(first);
    pixels_[1] = static_cast<std::uint16_t*>(second);
    return true;
  }

  void release() {
    if (deallocate_) {
      if (pixels_[0]) deallocate_(pixels_[0]);
      if (pixels_[1]) deallocate_(pixels_[1]);
    }
    pixels_[0] = pixels_[1] = nullptr;
    activeTicket_ = {};
    visibleSlot_ = 0xff;
    visibleId_[0] = '\0';
  }

  bool reserved() const { return pixels_[0] && pixels_[1]; }

  Ticket begin(const char* id) {
    Ticket ticket;
    if (!reserved() || !id || !*id || activeTicket_) return ticket;
    ticket.slot = visibleSlot_ == 0 ? 1 : 0;
    ++generation_;
    if (generation_ == 0) ++generation_;
    ticket.generation = generation_;
    std::strncpy(ticket.id, id, sizeof(ticket.id) - 1);
    activeTicket_ = ticket;
    return ticket;
  }

  std::uint16_t* writable(const Ticket& ticket) {
    return matches(ticket) ? pixels_[ticket.slot] : nullptr;
  }

  bool publish(const Ticket& ticket, const char* currentId, bool decoded) {
    if (!matches(ticket)) return false;
    const bool current = currentId && std::strcmp(ticket.id, currentId) == 0;
    activeTicket_ = {};
    if (!decoded || !current) return false;
    visibleSlot_ = ticket.slot;
    std::strncpy(visibleId_, ticket.id, sizeof(visibleId_) - 1);
    visibleId_[sizeof(visibleId_) - 1] = '\0';
    return true;
  }

  void abandon(const Ticket& ticket) {
    if (matches(ticket)) activeTicket_ = {};
  }
  bool isActive(const Ticket& ticket) const { return matches(ticket); }
  std::uint16_t* visible() { return visibleSlot_ < kBufferCount ? pixels_[visibleSlot_] : nullptr; }
  const std::uint16_t* visible() const { return visibleSlot_ < kBufferCount ? pixels_[visibleSlot_] : nullptr; }
  const char* visibleId() const { return visibleId_; }
  bool busy() const { return static_cast<bool>(activeTicket_); }

 private:
  bool matches(const Ticket& t) const {
    return t && activeTicket_ && t.generation == activeTicket_.generation &&
           t.slot == activeTicket_.slot && std::strcmp(t.id, activeTicket_.id) == 0;
  }

  std::uint16_t* pixels_[kBufferCount]{};
  Allocate allocate_;
  Deallocate deallocate_;
  Ticket activeTicket_{};
  std::uint32_t generation_ = 0;
  std::uint8_t visibleSlot_ = 0xff;
  char visibleId_[65]{};
};

}  // namespace streamdeck::ui
