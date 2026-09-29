#include <cassert>

#include "ui/onboarding.h"

using streamdeck::onboarding::BootState;

int main() {
  assert(streamdeck::onboarding::needsCalibration({false, false}));
  assert(streamdeck::onboarding::needsCalibration({false, true}));
  assert(!streamdeck::onboarding::needsProvisioning({false, false}));
  assert(streamdeck::onboarding::needsProvisioning({true, false}));
  assert(!streamdeck::onboarding::needsProvisioning({true, true}));
  assert(streamdeck::onboarding::canStartApp({true, true}));
  assert(!streamdeck::onboarding::canStartApp({true, false}));

  assert(streamdeck::onboarding::clampBrightness(0) == 10);
  assert(streamdeck::onboarding::clampBrightness(56) == 56);
  assert(streamdeck::onboarding::clampBrightness(101) == 100);

  assert(streamdeck::onboarding::validSsid("Home"));
  assert(!streamdeck::onboarding::validSsid(""));
  assert(!streamdeck::onboarding::validSsid("123456789012345678901234567890123"));

  assert(streamdeck::onboarding::validWifiPassword(""));
  assert(streamdeck::onboarding::validWifiPassword("12345678"));
  assert(!streamdeck::onboarding::validWifiPassword("short"));
  assert(!streamdeck::onboarding::validWifiPassword(
      "1234567890123456789012345678901234567890123456789012345678901234"));

  assert(streamdeck::onboarding::validDeviceName("StreamDeck-01"));
  assert(streamdeck::onboarding::validDeviceName("desk_unit_2"));
  assert(!streamdeck::onboarding::validDeviceName(""));
  assert(!streamdeck::onboarding::validDeviceName("bad name"));
  assert(!streamdeck::onboarding::validDeviceName("bad/name"));

  assert(streamdeck::onboarding::validIpv4OrEmpty(""));
  assert(streamdeck::onboarding::validIpv4OrEmpty("192.168.1.50"));
  assert(!streamdeck::onboarding::validIpv4OrEmpty("192.168.1"));
  assert(!streamdeck::onboarding::validIpv4OrEmpty("192.168.1.999"));
  assert(!streamdeck::onboarding::validIpv4OrEmpty("host.local"));
}
