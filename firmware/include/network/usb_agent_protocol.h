#pragma once

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace streamdeck::usb {

constexpr uint32_t kUsbHeartbeatIntervalMs = 5000;
constexpr uint32_t kUsbPeerTimeoutMs = 30000;

constexpr std::size_t kMaxFrameBytes = 16U * 1024U;
constexpr std::size_t kMaxCoverChunkRawBytes = 768U;
constexpr char kHelloBearerTokenFieldPrefix[] = ",\"bearerToken\":\"";

inline bool shouldSendUnauthedHello(bool usbEnabled, bool usbAuthorized,
                                    uint32_t nowMs, uint32_t lastHelloMs,
                                    uint32_t intervalMs) {
  if (!usbEnabled || usbAuthorized || intervalMs == 0) return false;
  return lastHelloMs == 0 || nowMs - lastHelloMs >= intervalMs;
}

// An empty-role hello is a discovery probe; an Agent hello is already a reply.
// Replying to that reply creates an endless hello/hydration/artwork loop.
inline bool shouldReplyToHello(const char* role) {
  return !role || role[0] == '\0';
}

enum class IncomingUsbDecision : uint8_t { Ignore, Dispatch };

inline IncomingUsbDecision decideIncomingUsbFrame(bool wifiAuthorized,
                                                  bool frameIsHandshakeOrRecovery) {
  // Every USB frame still passes decideUsbAuth before dispatch.
  // Wi-Fi availability must not silently discard authenticated USB updates.
  (void)wifiAuthorized;
  (void)frameIsHandshakeOrRecovery;
  return IncomingUsbDecision::Dispatch;
}

enum class UsbAuthDecision : uint8_t { Reject, AllowHello, AuthorizeExistingPairing };

inline UsbAuthDecision decideUsbAuth(const char* frameType,
                                     const char* presentedToken,
                                     const char* persistedToken,
                                     bool alreadyAuthorized) {
  if (!frameType) return UsbAuthDecision::Reject;
  if (std::strcmp(frameType, "hello") == 0) return UsbAuthDecision::AllowHello;
  if (alreadyAuthorized) return UsbAuthDecision::AuthorizeExistingPairing;
  if (std::strcmp(frameType, "pairing.token") != 0) return UsbAuthDecision::Reject;
  if (!presentedToken || presentedToken[0] == '\0') return UsbAuthDecision::Reject;
  if (!persistedToken || persistedToken[0] == '\0') {
    return UsbAuthDecision::AuthorizeExistingPairing;
  }
  return std::strcmp(presentedToken, persistedToken) == 0
      ? UsbAuthDecision::AuthorizeExistingPairing
      : UsbAuthDecision::Reject;
}

inline bool accountBoundedBytes(std::size_t& received, std::size_t contentLength,
                                std::size_t readBytes) {
  if (readBytes == 0 || received > contentLength || readBytes > contentLength - received) return false;
  received += readBytes;
  return true;
}

enum class FrameReadResult : uint8_t { None, Complete, Overflow };

class LineFrameReader {
 public:
  explicit LineFrameReader(char* buffer, std::size_t capacity)
      : buffer_(buffer), capacity_(capacity) { reset(); }

  FrameReadResult push(char c) {
    if (draining_) {
      if (c == '\n') {
        draining_ = false;
        reset();
        return FrameReadResult::Overflow;
      }
      return FrameReadResult::None;
    }
    if (c == '\n') {
      if (length_ > 0 && buffer_[length_ - 1] == '\r') --length_;
      buffer_[length_] = '\0';
      return FrameReadResult::Complete;
    }
    if (length_ + 1 >= capacity_ || length_ + 1 > kMaxFrameBytes) {
      draining_ = true;
      length_ = 0;
      if (capacity_ > 0) buffer_[0] = '\0';
      return FrameReadResult::None;
    }
    buffer_[length_++] = c;
    buffer_[length_] = '\0';
    return FrameReadResult::None;
  }

  void reset() {
    length_ = 0;
    draining_ = false;
    if (buffer_ && capacity_ > 0) buffer_[0] = '\0';
  }

  const char* data() const { return buffer_; }
  std::size_t length() const { return length_; }
  bool draining() const { return draining_; }

 private:
  char* buffer_ = nullptr;
  std::size_t capacity_ = 0;
  std::size_t length_ = 0;
  bool draining_ = false;
};

inline int base64Value(char c) {
  if (c >= 'A' && c <= 'Z') return c - 'A';
  if (c >= 'a' && c <= 'z') return c - 'a' + 26;
  if (c >= '0' && c <= '9') return c - '0' + 52;
  if (c == '+') return 62;
  if (c == '/') return 63;
  return -1;
}

inline bool decodeBase64(const char* input, std::size_t length, uint8_t* out,
                         std::size_t outCapacity, std::size_t& outLength) {
  outLength = 0;
  if (!input || (!out && outCapacity > 0) || length % 4 != 0) return false;
  for (std::size_t i = 0; i < length; i += 4) {
    int values[4]{};
    int pad = 0;
    for (int j = 0; j < 4; ++j) {
      const char c = input[i + j];
      if (c == '=') {
        values[j] = 0;
        ++pad;
      } else {
        if (pad > 0) return false;
        values[j] = base64Value(c);
        if (values[j] < 0) return false;
      }
    }
    if (pad > 2) return false;
    const uint32_t triple = (static_cast<uint32_t>(values[0]) << 18) |
        (static_cast<uint32_t>(values[1]) << 12) |
        (static_cast<uint32_t>(values[2]) << 6) | static_cast<uint32_t>(values[3]);
    const int bytes = 3 - pad;
    if (outLength + static_cast<std::size_t>(bytes) > outCapacity) return false;
    if (bytes > 0) out[outLength++] = static_cast<uint8_t>((triple >> 16) & 0xFF);
    if (bytes > 1) out[outLength++] = static_cast<uint8_t>((triple >> 8) & 0xFF);
    if (bytes > 2) out[outLength++] = static_cast<uint8_t>(triple & 0xFF);
    if (pad > 0 && i + 4 != length) return false;
  }
  return outLength <= kMaxCoverChunkRawBytes;
}

}  // namespace streamdeck::usb
