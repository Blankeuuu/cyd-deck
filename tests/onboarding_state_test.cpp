#include <cassert>

#include "ui/app_state.h"
#include "ui/onboarding.h"

using streamdeck::onboarding::BootState;
using streamdeck::onboarding::ConnectionChoice;
using streamdeck::onboarding::SetupPortalMode;

int main() {
  // Calibration & Provisioning state flags:
  assert(streamdeck::onboarding::needsCalibration({false, false}));
  assert(streamdeck::onboarding::needsCalibration({false, true}));
  assert(!streamdeck::onboarding::needsProvisioning({false, false}));
  assert(streamdeck::onboarding::needsProvisioning({true, false}));
  assert(!streamdeck::onboarding::needsProvisioning({true, true}));
  assert(streamdeck::onboarding::canStartApp({true, true}));
  assert(!streamdeck::onboarding::canStartApp({true, false}));

  // Connection Choice and First-boot onboarding state tests:
  // Initial state after calibration: choice is Unset, needs choice before portal:
  const BootState calibratedUnset{true, false, ConnectionChoice::Unset};
  assert(!streamdeck::onboarding::needsCalibration(calibratedUnset));
  assert(streamdeck::onboarding::needsProvisioning(calibratedUnset));
  assert(streamdeck::onboarding::needsConnectionChoice(calibratedUnset));
  assert(!streamdeck::onboarding::canStartApp(calibratedUnset));

  // User chooses USB: USB works without Wi-Fi, app starts immediately:
  const BootState usbSelected{true, true, ConnectionChoice::Usb};
  assert(!streamdeck::onboarding::needsCalibration(usbSelected));
  assert(!streamdeck::onboarding::needsProvisioning(usbSelected));
  assert(!streamdeck::onboarding::needsConnectionChoice(usbSelected));
  assert(streamdeck::onboarding::canStartApp(usbSelected));
  assert(streamdeck::onboarding::isUsbChoice(usbSelected));
  assert(!streamdeck::onboarding::isWifiChoice(usbSelected));

  // User chooses Wi-Fi: portal is required before app can start:
  const BootState wifiSelected{true, false, ConnectionChoice::Wifi};
  assert(!streamdeck::onboarding::needsCalibration(wifiSelected));
  assert(streamdeck::onboarding::needsProvisioning(wifiSelected));
  assert(!streamdeck::onboarding::needsConnectionChoice(wifiSelected));
  assert(!streamdeck::onboarding::canStartApp(wifiSelected));
  assert(!streamdeck::onboarding::isUsbChoice(wifiSelected));
  assert(streamdeck::onboarding::isWifiChoice(wifiSelected));

  // Wi-Fi setup completed successfully:
  const BootState wifiComplete{true, true, ConnectionChoice::Wifi};
  assert(!streamdeck::onboarding::needsCalibration(wifiComplete));
  assert(!streamdeck::onboarding::needsProvisioning(wifiComplete));
  assert(!streamdeck::onboarding::needsConnectionChoice(wifiComplete));
  assert(streamdeck::onboarding::canStartApp(wifiComplete));

  // 1. resolveInitialConnectionChoice (NVS migration and stored choice):
  // Explicit USB choice stored in NVS:
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(true, 1, true) == ConnectionChoice::Usb);
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(true, 1, false) == ConnectionChoice::Usb);
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(false, 1, false) == ConnectionChoice::Usb);
  // Explicit Wi-Fi choice stored in NVS:
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(true, 2, true) == ConnectionChoice::Wifi);
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(true, 2, false) == ConnectionChoice::Wifi);
  // Legacy NVS (rawConn == 0):
  // - done == true with preserved SSID -> migrates to Wifi:
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(true, 0, true) == ConnectionChoice::Wifi);
  // - done == true without SSID -> defaults to Usb:
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(true, 0, false) == ConnectionChoice::Usb);
  // - done == false -> Unset (shows choice screen):
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(false, 0, false) == ConnectionChoice::Unset);
  assert(streamdeck::onboarding::resolveInitialConnectionChoice(false, 0, true) == ConnectionChoice::Unset);

  // 2. USB Regression tests: When USB is chosen, network services MUST NOT start,
  // even if an SSID was preserved from previous Wi-Fi configurations:
  assert(!streamdeck::onboarding::shouldStartStation(ConnectionChoice::Usb, true));
  assert(!streamdeck::onboarding::shouldStartStation(ConnectionChoice::Usb, false));
  assert(!streamdeck::onboarding::shouldRunWebConfigServer(ConnectionChoice::Usb, true));
  assert(!streamdeck::onboarding::shouldRunWebConfigServer(ConnectionChoice::Usb, false));
  assert(!streamdeck::onboarding::shouldRunOta(ConnectionChoice::Usb, true));
  assert(!streamdeck::onboarding::shouldRunOta(ConnectionChoice::Usb, false));

  // Wi-Fi choice correctly starts services when configured/connected:
  assert(streamdeck::onboarding::shouldStartStation(ConnectionChoice::Wifi, true));
  assert(!streamdeck::onboarding::shouldStartStation(ConnectionChoice::Wifi, false));
  assert(streamdeck::onboarding::shouldRunWebConfigServer(ConnectionChoice::Wifi, true));
  assert(!streamdeck::onboarding::shouldRunWebConfigServer(ConnectionChoice::Wifi, false));
  assert(streamdeck::onboarding::shouldRunOta(ConnectionChoice::Wifi, true));
  assert(!streamdeck::onboarding::shouldRunOta(ConnectionChoice::Wifi, false));

  // 3. Portal Auth Matrix: SoftAP vs LAN:
  // First setup only applies when bootMode == Provisioning AND client interface is SoftAP:
  assert(streamdeck::onboarding::resolveSetupPortalMode(true, true) == SetupPortalMode::FirstSetup);
  assert(!streamdeck::onboarding::shouldRequireSetupAuth(true, true));
  assert(streamdeck::onboarding::validateSetupAuth(SetupPortalMode::FirstSetup, nullptr, "device_secret"));
  assert(streamdeck::onboarding::validateSetupAuth(SetupPortalMode::FirstSetup, "", "device_secret"));
  assert(streamdeck::onboarding::validateSetupAuth(SetupPortalMode::FirstSetup, "any_key", "device_secret"));

  // In Provisioning mode, if a request arrives on the LAN interface (WIFI_AP_STA),
  // password is STRICTLY required to prevent unauthorized takeover from the home network:
  assert(streamdeck::onboarding::resolveSetupPortalMode(true, false) == SetupPortalMode::LanSettings);
  assert(streamdeck::onboarding::shouldRequireSetupAuth(true, false));
  assert(!streamdeck::onboarding::validateSetupAuth(SetupPortalMode::LanSettings, nullptr, "device_secret"));
  assert(!streamdeck::onboarding::validateSetupAuth(SetupPortalMode::LanSettings, "", "device_secret"));
  assert(!streamdeck::onboarding::validateSetupAuth(SetupPortalMode::LanSettings, "wrong_secret", "device_secret"));
  assert(streamdeck::onboarding::validateSetupAuth(SetupPortalMode::LanSettings, "device_secret", "device_secret"));

  // In App mode, requests on both LAN and SoftAP require password:
  assert(streamdeck::onboarding::resolveSetupPortalMode(false, false) == SetupPortalMode::LanSettings);
  assert(streamdeck::onboarding::shouldRequireSetupAuth(false, false));
  assert(streamdeck::onboarding::resolveSetupPortalMode(false, true) == SetupPortalMode::LanSettings);
  assert(streamdeck::onboarding::shouldRequireSetupAuth(false, true));

  // 5. AP password display formatting:
  // Must render 16 hex characters across 2 lines of 8 chars so LVGL does not truncate with dots:
  char formattedPass[24]{};
  assert(streamdeck::onboarding::formatDisplayPassword("FFFFFFFFFFFFFFFF", formattedPass, sizeof(formattedPass)));
  assert(std::strcmp(formattedPass, "FFFFFFFF\nFFFFFFFF") == 0);
  const char* newlinePos = std::strchr(formattedPass, '\n');
  assert(newlinePos != nullptr);
  assert(newlinePos - formattedPass == 8);
  assert(std::strlen(newlinePos + 1) == 8);
  // Connection decision helper:
  using streamdeck::PendingSetupDecision;
  using streamdeck::decidePendingSetupResult;
  assert(decidePendingSetupResult(true, true, 1000, 20000) == PendingSetupDecision::Success);
  assert(decidePendingSetupResult(true, false, 1000, 20000) == PendingSetupDecision::Wait);
  assert(decidePendingSetupResult(false, false, 1000, 20000) == PendingSetupDecision::Wait);
  assert(decidePendingSetupResult(false, false, 20001, 20000) == PendingSetupDecision::Failure);

  // 7. Validation helpers:
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

  return 0;
}
