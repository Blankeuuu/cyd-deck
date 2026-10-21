#pragma once
#include <cstdint>
#include <cstdio>
#include <cstring>

namespace streamdeck {
struct PendingCommand {
  char id[40] = "";
  uint32_t sentAtMs = 0;
  bool volume = false;
};
class PendingCommands {
 public:
  bool add(const char* id, uint32_t now, bool volume) {
    for (auto& entry : entries_) if (!entry.id[0]) {
      std::snprintf(entry.id, sizeof(entry.id), "%s", id);
      entry.sentAtMs = now; entry.volume = volume; return true;
    }
    return false;
  }
  bool take(const char* id, PendingCommand& out) {
    for (auto& entry : entries_) if (entry.id[0] && std::strcmp(entry.id, id) == 0) {
      out = entry; entry = {}; return true;
    }
    return false;
  }
  bool expire(uint32_t now, PendingCommand& out, uint32_t timeout = 5000) {
    for (auto& entry : entries_) if (entry.id[0] && now - entry.sentAtMs >= timeout) {
      out = entry; entry = {}; return true;
    }
    return false;
  }
 private:
  PendingCommand entries_[16]{};
};
} // namespace streamdeck
