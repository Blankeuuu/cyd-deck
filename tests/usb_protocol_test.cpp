#include <cassert>
#include <cstring>
#include <string>

#include "network/usb_agent_protocol.h"

namespace {

std::string escapeJsonForUsbHelloTest(const std::string& value) {
  std::string out;
  out.reserve(value.size() + 8);
  for (char c : value) {
    if (c == '"' || c == '\\') {
      out.push_back('\\');
      out.push_back(c);
    } else if (c >= 0x20) {
      out.push_back(c);
    }
  }
  return out;
}

std::string makeExpectedUsbHelloPayload(const std::string& deviceId,
                                        const std::string& deviceName,
                                        const std::string& bearerToken) {
  std::string message = "{\"protocol\":1,\"type\":\"hello\",\"data\":{\"deviceId\":\"";
  message += escapeJsonForUsbHelloTest(deviceId);
  message += "\",\"name\":\"";
  message += escapeJsonForUsbHelloTest(deviceName);
  message += "\",\"role\":\"esp32\",\"capabilities\":[\"media\",\"audio\",\"shortcuts\",\"system\",\"device-control\"]";
  if (!bearerToken.empty()) {
    message += streamdeck::usb::kHelloBearerTokenFieldPrefix;
    message += escapeJsonForUsbHelloTest(bearerToken);
    message += "\"";
  }
  message += "}}";
  return message;
}

}  // namespace

int main() {
  using namespace streamdeck::usb;

  char frame[64]{};
  LineFrameReader reader(frame, sizeof(frame));
  assert(reader.push('{') == FrameReadResult::None);
  assert(reader.push('}') == FrameReadResult::None);
  assert(reader.push('\r') == FrameReadResult::None);
  assert(reader.push('\n') == FrameReadResult::Complete);
  assert(std::strcmp(reader.data(), "{}") == 0);
  assert(reader.length() == 2);
  reader.reset();

  for (int i = 0; i < 80; ++i) {
    assert(reader.push('x') == FrameReadResult::None);
  }
  assert(reader.draining());
  assert(reader.push('y') == FrameReadResult::None);
  assert(reader.push('\n') == FrameReadResult::Overflow);
  assert(!reader.draining());

  uint8_t decoded[768]{};
  std::size_t decodedLength = 0;
  assert(decodeBase64("SGVsbG8=", 8, decoded, sizeof(decoded), decodedLength));
  assert(decodedLength == 5);
  assert(std::memcmp(decoded, "Hello", 5) == 0);
  assert(decodeBase64("AAEC", 4, decoded, sizeof(decoded), decodedLength));
  assert(decodedLength == 3);
  assert(decoded[0] == 0 && decoded[1] == 1 && decoded[2] == 2);
  assert(!decodeBase64("A", 1, decoded, sizeof(decoded), decodedLength));
  assert(!decodeBase64("!!!!", 4, decoded, sizeof(decoded), decodedLength));
  assert(!decodeBase64("AAAA", 4, decoded, 2, decodedLength));

  assert(shouldSendUnauthedHello(true, false, 1, 0, 1000));
  assert(!shouldSendUnauthedHello(true, true, 2000, 0, 1000));
  assert(!shouldSendUnauthedHello(false, false, 2000, 0, 1000));
  assert(!shouldSendUnauthedHello(true, false, 1500, 1000, 1000));
  assert(shouldSendUnauthedHello(true, false, 2000, 1000, 1000));

  const auto unpairedHello = makeExpectedUsbHelloPayload("cyd-1", "CYD", "");
  assert(unpairedHello.find("\"type\":\"hello\"") != std::string::npos);
  assert(unpairedHello.find("\"bearerToken\"") == std::string::npos);
  assert(unpairedHello.find("\"deviceId\":\"cyd-1\"") != std::string::npos);

  const auto pairedHello = makeExpectedUsbHelloPayload("cyd\\1", "CYD \"Desk\"", "tok\\en\"1");
  assert(pairedHello.find("\"deviceId\":\"cyd\\\\1\"") != std::string::npos);
  assert(pairedHello.find("\"name\":\"CYD \\\"Desk\\\"\"") != std::string::npos);
  assert(pairedHello.find("\"bearerToken\":\"tok\\\\en\\\"1\"") != std::string::npos);
  assert(pairedHello.find("tok\\en\"1") == std::string::npos);

  assert(decideIncomingUsbFrame(false, false) == IncomingUsbDecision::Dispatch);
  assert(decideIncomingUsbFrame(false, true) == IncomingUsbDecision::Dispatch);
  assert(decideIncomingUsbFrame(true, true) == IncomingUsbDecision::Dispatch);
  assert(decideIncomingUsbFrame(true, false) == IncomingUsbDecision::Dispatch);

  assert(decideUsbAuth("hello", nullptr, nullptr, false) == UsbAuthDecision::AllowHello);
  assert(decideUsbAuth("connection.state", nullptr, "secret", false) == UsbAuthDecision::Reject);
  assert(decideUsbAuth("pairing.token", "wrong", "secret", false) == UsbAuthDecision::Reject);
  assert(decideUsbAuth("pairing.token", "secret", "secret", false) == UsbAuthDecision::AuthorizeExistingPairing);
  assert(decideUsbAuth("pairing.token", "secret", "", false) == UsbAuthDecision::AuthorizeExistingPairing);
  assert(decideUsbAuth("pairing.token", "", "", false) == UsbAuthDecision::Reject);
  assert(decideUsbAuth("pairing.token", nullptr, "", false) == UsbAuthDecision::Reject);
  assert(decideUsbAuth("pairing.token", "", "secret", false) == UsbAuthDecision::Reject);
  assert(decideUsbAuth("pairing.token", nullptr, "secret", false) == UsbAuthDecision::Reject);
  assert(decideUsbAuth("connection.state", nullptr, "secret", true) == UsbAuthDecision::AuthorizeExistingPairing);

  std::size_t received = 0;
  assert(accountBoundedBytes(received, 5, 2));
  assert(received == 2);
  assert(accountBoundedBytes(received, 5, 3));
  assert(received == 5);
  assert(!accountBoundedBytes(received, 5, 1));
  assert(received == 5);
  received = 0;
  assert(!accountBoundedBytes(received, 5, 0));
  assert(received == 0);

  assert(shouldReplyToHello(nullptr));
  assert(shouldReplyToHello(""));
  assert(!shouldReplyToHello("agent"));
  assert(!shouldReplyToHello("esp32"));

  static_assert(kUsbHeartbeatIntervalMs * 3 < kUsbPeerTimeoutMs);
  static_assert(kMaxFrameBytes == 16U * 1024U);
  static_assert(kMaxCoverChunkRawBytes == 768U);
}
