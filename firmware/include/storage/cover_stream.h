#pragma once
#include <cstddef>
#include <cstdint>
#include "storage/sd_paths.h"

namespace streamdeck::storage {
struct CoverStreamValidation {
  std::size_t received = 0;
  uint8_t first[2]{};
  uint8_t tail[2]{};
  bool append(const uint8_t* data, std::size_t length) {
    if (!data || !length || received > kMaxCoverBytes || length > kMaxCoverBytes - received) return false;
    for (std::size_t i = 0; i < length; ++i) {
      if (received < 2) first[received] = data[i];
      tail[0] = tail[1]; tail[1] = data[i]; ++received;
    }
    return true;
  }
  bool complete(std::size_t length) const {
    return received == length && length >= 4 && first[0] == 0xff && first[1] == 0xd8 && tail[0] == 0xff && tail[1] == 0xd9;
  }
};
}
