#include "sd_diagnostic.h"

namespace {
constexpr const char* testPath = "/.pcdock-hwdiag.tmp";
}

void SdDiagnostic::begin() {
  storage_.begin();
  syncStatus();
}

void SdDiagnostic::runSafeTest() {
  status_.testPassed = false;
  syncStatus();
  if (!status_.mounted) {
    status_.message = "SD unavailable";
    return;
  }
  if (!status_.storageReady || storage_.fs() == nullptr) {
    status_.message = "SD layout unavailable";
    return;
  }

  FS& fs = *storage_.fs();
  if (fs.exists(testPath)) {
    status_.message = "test skipped: reserved temp path exists";
    Serial.println("[SD] preserving pre-existing test path");
    return;
  }

  constexpr char payload[] = "PC Dock SD diagnostic v1";
  const uint32_t started = micros();
  File out = fs.open(testPath, FILE_WRITE);
  if (!out) {
    status_.message = "write open failed";
    return;
  }
  const size_t written = out.write(reinterpret_cast<const uint8_t*>(payload), sizeof(payload) - 1);
  out.flush();
  out.close();

  File in = fs.open(testPath, FILE_READ);
  char data[sizeof(payload)] = {};
  const size_t read = in ? in.readBytes(data, sizeof(payload) - 1) : 0;
  if (in) in.close();
  const bool contentMatches = read == sizeof(payload) - 1 &&
      memcmp(data, payload, sizeof(payload) - 1) == 0;
  const bool removed = fs.remove(testPath);
  const uint32_t elapsedUs = micros() - started;

  status_.testPassed = written == sizeof(payload) - 1 &&
      contentMatches && removed && !fs.exists(testPath);
  status_.message = status_.testPassed ? "write/read/delete OK" :
      (contentMatches ? "delete failed" : "read/verify failed");
  syncStatus();
  status_.testPassed = written == sizeof(payload) - 1 &&
      contentMatches && removed && !fs.exists(testPath);
  status_.message = status_.testPassed ? "write/read/delete OK" :
      (contentMatches ? "delete failed" : "read/verify failed");
  Serial.printf("[SD] elapsed=%lu us payload=%u bytes rate=%lu B/s\n",
      static_cast<unsigned long>(elapsedUs),
      static_cast<unsigned>(sizeof(payload) - 1),
      static_cast<unsigned long>(elapsedUs ? (sizeof(payload) - 1) * 1000000UL / elapsedUs : 0));
}

void SdDiagnostic::syncStatus() {
  const auto& storageStatus = storage_.status();
  status_.mounted = storageStatus.mounted;
  status_.storageReady = storageStatus.ready;
  status_.totalBytes = storageStatus.totalBytes;
  status_.usedBytes = storageStatus.usedBytes;
  status_.coverCacheBytes = storageStatus.coverCacheBytes;
  status_.coverIndexEntries = storageStatus.coverIndexEntries;
  status_.message = storageStatus.message;
}
