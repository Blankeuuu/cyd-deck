#pragma once
#include <Arduino.h>
#include "storage/sd_storage.h"

struct SdStatus {
  bool mounted = false;
  bool storageReady = false;
  bool testPassed = false;
  uint64_t totalBytes = 0;
  uint64_t usedBytes = 0;
  uint64_t coverCacheBytes = 0;
  uint16_t coverIndexEntries = 0;
  String message = "not tested";
};

class SdDiagnostic {
 public:
  void begin();
  const SdStatus& status() const { return status_; }
  void runSafeTest();

 private:
  void syncStatus();

  streamdeck::storage::SdStorage storage_;
  SdStatus status_;
};
