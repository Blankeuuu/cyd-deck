#include "ui/app.h"

#include <Arduino.h>
#include <DNSServer.h>
#include <Preferences.h>
#include <SD.h>
#include <SPI.h>
#include <WiFi.h>
#include <cstdlib>
#include <cstring>
#include <new>
#include <esp_system.h>
#include <lgfx/v1/LGFX_Sprite.hpp>
#include <lgfx/v1/lgfx_filesystem_support.hpp>
#include <lvgl.h>

#include "affine_calibration.h"
#include "board_pins.h"
#include "display/display_hal.h"
#include "network/agent_client.h"
#include "network/cover_art.h"
#include "network/ota_update.h"
#include "storage/sd_storage.h"
#include "touch/xpt2046.h"
#include "ui/app_state.h"
#include "ui/gesture_recognizer.h"
#include "ui/lvgl_port.h"
#include "ui/onboarding.h"
#include "ui/ui_manager.h"

#if defined(WOKWI_SIMULATOR)
#include "../../../tests/fixtures/wokwi_cover_rgb_jpeg.inc"
#endif

namespace {
using namespace streamdeck;

enum class BootMode : uint8_t { Calibration, Choice, Provisioning, App };

struct SetupSettings {
  bool done = false;
  onboarding::ConnectionChoice connectionChoice = onboarding::ConnectionChoice::Unset;
  String deviceName;
  String apSsid;
  String setupPassword;
  String wifiSsid;
  String wifiPassword;
  String agentIp;
  uint8_t brightness = onboarding::kDefaultBrightness;
};

struct PendingSetup {
  bool active = false;
  bool fromAppMode = false;
  String ssid;
  String password;
  String deviceName;
  String agentIp;
  uint8_t brightness = onboarding::kDefaultBrightness;
  uint32_t startedAt = 0;
  String previousSsid;
  String previousPassword;
  String previousDeviceName;
  String previousAgentIp;
  uint8_t previousBrightness = onboarding::kDefaultBrightness;
};

DisplayHal display;
Xpt2046 touch;
Preferences prefs;
AppState state;
IntentQueue intents;
GestureRecognizer gesture;
AgentClient agentClient;
OtaUpdateService otaUpdate;
SPIClass sdBus(VSPI);
storage::SdStorage sdStorage;
touchcal::Coefficients calibration;
SetupSettings setupSettings;
PendingSetup pendingSetup;
DNSServer dnsServer;
WiFiServer setupServer(80);

BootMode bootMode = BootMode::Calibration;
bool down = false;
bool calibrating = false;
bool calibrationArmed = false;
[[maybe_unused]] bool cancelled = false;
bool setupPortalActive = false;
bool setupUiBuilt = false;
bool setupQrNeedsDraw = false;
bool appUiStarted = false;
bool agentClientStarted = false;
bool otaConfigured = false;
bool stationConnectStarted = false;
bool sdMounted = false;
String setupStatusMessage;
constexpr uint16_t kCoverImageSize = 128;
#if defined(WOKWI_SIMULATOR)
constexpr bool kUsbProtocolEnabled = false;
#else
constexpr bool kUsbProtocolEnabled = true;
#endif
uint16_t* coverPixels = nullptr;
lv_image_dsc_t coverImageDsc{};
uint32_t lastUserInputAt = 0;
bool displayOff = false;
bool displayDimmed = false;
bool consumeWakeTouch = false;
RawTouch lastRaw{};
int16_t lastX = 0;
int16_t lastY = 0;
touchcal::Point rawPoints[5]{};
uint8_t calibrationStep = 0;
lv_indev_t* pointer = nullptr;
lv_obj_t* setupStatusLabel = nullptr;

bool renderCachedCover(const char* coverId, AppState& appState);
#if defined(WOKWI_SIMULATOR)
void renderWokwiCoverDiagnostic(AppState& appState);
#endif
void startWebConfigServer(bool provisioningMode);
void servicePendingSetupResult();
void sendHttp(WiFiClient& client, int status, const char* statusText,
              const char* contentType, const String& body);
void startChoiceScreen();
void buildChoiceUi();
void chooseUsbMode();
void chooseWifiMode();
void startAppUi();
void startSetupPortal();
void stopSetupPortal();

void appLogLine(const char* message) {
  if (kUsbProtocolEnabled && bootMode == BootMode::App) return;
  Serial.println(message);
}

template <typename... Args>
void appLogf(const char* format, Args... args) {
  if (kUsbProtocolEnabled && bootMode == BootMode::App) return;
  Serial.printf(format, args...);
}



constexpr int kTargetX[5] = {12, 307, 12, 307, 160};
constexpr int kTargetY[5] = {12, 12, 227, 227, 120};
constexpr uint32_t kSetupBg = 0x0B0D12;
constexpr uint32_t kSetupText = 0xF7F8FA;
constexpr uint32_t kSetupMuted = 0xB2BAC6;
constexpr uint32_t kSetupAccent = 0x7EAFE3;
constexpr uint32_t kSetupPanel = 0x171B22;
constexpr const char* kSetupApSsid = "CYD-Deck-Setup";

constexpr byte kDnsPort = 53;
constexpr uint32_t kWifiConnectTimeoutMs = 20000;
constexpr std::size_t kSetupHttpMaxRequestBytes = 1536;
constexpr std::size_t kSetupHttpMaxBodyBytes = 1024;

void disableScrolling(lv_obj_t* object) {
  lv_obj_clear_flag(object, LV_OBJ_FLAG_SCROLLABLE);
  lv_obj_set_scrollbar_mode(object, LV_SCROLLBAR_MODE_OFF);
}

void setBounds(lv_obj_t* object, int x, int y, int w, int h) {
  lv_obj_set_pos(object, x, y);
  lv_obj_set_size(object, w, h);
  lv_obj_set_style_pad_all(object, 0, 0);
  disableScrolling(object);
}

lv_obj_t* makeLabel(lv_obj_t* parent, const char* text, int x, int y, int w,
                    int h, uint32_t color, const lv_font_t* font) {
  lv_obj_t* label = lv_label_create(parent);
  lv_label_set_text(label, text);
  lv_label_set_long_mode(label, LV_LABEL_LONG_DOT);
  setBounds(label, x, y, w, h);
  lv_obj_set_style_text_color(label, lv_color_hex(color), 0);
  lv_obj_set_style_text_font(label, font, 0);
  return label;
}

void applyBacklight(uint8_t percent) {
  const uint8_t brightness = percent == 0 ? 0 : onboarding::clampBrightness(percent);
  analogWrite(board::backlight, (255 * brightness) / 100);
}

void applyConfiguredBacklight() {
  if (displayOff) {
    applyBacklight(0);
  } else if (displayDimmed) {
    applyBacklight(state.appearance.dimBrightness);
  } else {
    applyBacklight(state.ui.localBrightness ? state.ui.localBrightness : state.appearance.brightness);
  }
}

String htmlEscape(const String& value) {
  String out;
  out.reserve(value.length() + 8);
  for (size_t i = 0; i < value.length(); ++i) {
    const char c = value[i];
    if (c == '&') out += F("&amp;");
    else if (c == '<') out += F("&lt;");
    else if (c == '>') out += F("&gt;");
    else if (c == '"') out += F("&quot;");
    else out += c;
  }
  return out;
}

String makeDefaultDeviceName() {
  const uint32_t chip = static_cast<uint32_t>(ESP.getEfuseMac() & 0xFFFFFF);
  char buffer[24];
  std::snprintf(buffer, sizeof(buffer), "StreamDeck-%06lX",
      static_cast<unsigned long>(chip));
  return String(buffer);
}

String makeDeviceId() {
  const uint32_t chip = static_cast<uint32_t>(ESP.getEfuseMac() & 0xFFFFFF);
  char buffer[16];
  std::snprintf(buffer, sizeof(buffer), "CYD-%06lX",
      static_cast<unsigned long>(chip));
  return String(buffer);
}

String makeSetupPassword() {
  char buffer[17];
  std::snprintf(buffer, sizeof(buffer), "%08lX%08lX",
      static_cast<unsigned long>(esp_random()),
      static_cast<unsigned long>(esp_random()));
  return String(buffer);
}

void loadCalibration() {
  prefs.begin("touch", true);
  calibration.xFromRawX = prefs.getFloat("xx", 0);
  calibration.xFromRawY = prefs.getFloat("xy", 0);
  calibration.xOffset = prefs.getFloat("xo", 0);
  calibration.yFromRawX = prefs.getFloat("yx", 0);
  calibration.yFromRawY = prefs.getFloat("yy", 0);
  calibration.yOffset = prefs.getFloat("yo", 0);
  calibration.valid = prefs.getBool("valid", false) &&
      prefs.getUChar("rotation", 0) == board::landscapeRotation &&
      prefs.getUChar("mapver", 0) == touchcal::kCalibrationMapVersion;
  prefs.end();
}

void persistCalibration() {
  prefs.begin("touch", false);
  prefs.putFloat("xx", calibration.xFromRawX);
  prefs.putFloat("xy", calibration.xFromRawY);
  prefs.putFloat("xo", calibration.xOffset);
  prefs.putFloat("yx", calibration.yFromRawX);
  prefs.putFloat("yy", calibration.yFromRawY);
  prefs.putFloat("yo", calibration.yOffset);
  prefs.putBool("valid", true);
  prefs.putUChar("rotation", board::landscapeRotation);
  prefs.putUChar("mapver", touchcal::kCalibrationMapVersion);
  prefs.end();
}

void loadSetupSettings() {
  prefs.begin("setup", false);
  setupSettings.done = prefs.getBool("done", false);
  const uint8_t rawConn = prefs.getUChar("conn", 0);
  setupSettings.deviceName = prefs.getString("name", "");
  setupSettings.apSsid = prefs.getString("apssid", "");
  setupSettings.setupPassword = prefs.getString("secret", "");
  setupSettings.wifiSsid = prefs.getString("ssid", "");
  setupSettings.wifiPassword = prefs.getString("pass", "");
  setupSettings.agentIp = prefs.getString("agentip", "");
  setupSettings.brightness = onboarding::clampBrightness(
      prefs.getUChar("bright", onboarding::kDefaultBrightness));

  setupSettings.connectionChoice = onboarding::resolveInitialConnectionChoice(
      setupSettings.done, rawConn, setupSettings.wifiSsid.length() > 0);
  if (rawConn == 0 && setupSettings.done) {
    prefs.putUChar("conn", static_cast<uint8_t>(setupSettings.connectionChoice));
  }

  if (!onboarding::validDeviceName(setupSettings.deviceName.c_str())) {
    setupSettings.deviceName = makeDefaultDeviceName();
    prefs.putString("name", setupSettings.deviceName);
  }
  if (setupSettings.apSsid != kSetupApSsid) {
    setupSettings.apSsid = kSetupApSsid;
    prefs.putString("apssid", setupSettings.apSsid);
  }
  const uint8_t setupSecretVersion = prefs.getUChar("secver", 0);
  if (setupSecretVersion != 1 || setupSettings.setupPassword.length() < 8) {
    setupSettings.setupPassword = makeSetupPassword();
    prefs.putString("secret", setupSettings.setupPassword);
    prefs.putUChar("secver", 1);
  }
  prefs.putUChar("bright", setupSettings.brightness);
  prefs.end();
}

void loadShortcutSettings() {
  prefs.begin("shortcuts", true);
  const auto version = prefs.getUChar("version", 0);
  const size_t storedBytes = prefs.getBytesLength("slots");
  const size_t legacyBytes = offsetof(ShortcutState, colors);
  const size_t version2Bytes = offsetof(ShortcutState, iconPixels);
  if ((version == 3 && storedBytes == sizeof(state.shortcuts)) || (version == 2 && storedBytes == version2Bytes) || (version == 1 && storedBytes == legacyBytes)) {
    prefs.getBytes("slots", &state.shortcuts, storedBytes);
    for (int i = 0; i < 6; ++i) {
      state.shortcuts.ids[i][sizeof(state.shortcuts.ids[i]) - 1] = '\0';
      state.shortcuts.names[i][sizeof(state.shortcuts.names[i]) - 1] = '\0';
      state.shortcuts.icons[i][sizeof(state.shortcuts.icons[i]) - 1] = '\0';
      state.shortcuts.colors[i][sizeof(state.shortcuts.colors[i]) - 1] = '\0';
      state.shortcuts.displayModes[i][sizeof(state.shortcuts.displayModes[i]) - 1] = '\0';
    }
  }
  prefs.end();
}

void persistShortcutSettings() {
  auto* previous = new (std::nothrow) ShortcutState;
  prefs.begin("shortcuts", false);
  const bool unchanged = previous && prefs.getUChar("version", 0) == 3 && prefs.getBytesLength("slots") == sizeof(*previous)
      && prefs.getBytes("slots", previous, sizeof(*previous)) == sizeof(*previous)
      && std::memcmp(previous, &state.shortcuts, offsetof(ShortcutState, iconRevision)) == 0;
  delete previous;
  if (!unchanged) {
    prefs.putBytes("slots", &state.shortcuts, sizeof(state.shortcuts));
    prefs.putUChar("version", 3);
  }
  prefs.end();
}

void loadAppearanceSettings() {
  prefs.begin("appearance", true);
  const String brightnessRevision = prefs.getString("brightrev", "");
  applyBrightnessRevision(state.ui, brightnessRevision.c_str(), false);
  state.appearance.tileColorScheme = prefs.getUChar("tiles", 0);
  state.appearance.standbyStyle = prefs.getUChar("clock", 0);
  state.appearance.shortcutsLayout = prefs.getUChar("layout", 0);
  const PreferenceType dimSecondsType = prefs.getType("dimsec");
  const PreferenceType offSecondsType = prefs.getType("offsec");
  state.appearance.theme = prefs.getUChar("theme", state.appearance.theme);
  state.appearance.buttonStyle = prefs.getUChar("button", state.appearance.buttonStyle);
  state.appearance.accent = prefs.getUInt("accent", state.appearance.accent);
  state.appearance.background = prefs.getUInt("bg", state.appearance.background);
  state.appearance.brightness = prefs.getUChar("bright", setupSettings.brightness);
  state.appearance.dimAfterSeconds = dimSecondsType == PT_U16
      ? prefs.getUShort("dimsec", state.appearance.dimAfterSeconds)
      : prefs.getUInt("dimsec", state.appearance.dimAfterSeconds);
  state.appearance.dimBrightness = prefs.getUChar("dimbright", state.appearance.dimBrightness);
  state.appearance.screenOffAfterSeconds = offSecondsType == PT_U16
      ? prefs.getUShort("offsec", state.appearance.screenOffAfterSeconds)
      : prefs.getUInt("offsec", state.appearance.screenOffAfterSeconds);
  state.appearance.wakeOnTouch = prefs.getBool("wake", state.appearance.wakeOnTouch);
  const PreferenceType orderType = prefs.getType("order");
  uint64_t order = orderType == PT_U64
      ? prefs.getULong64("order", 0x0302010400ULL)
      : prefs.getUInt("order", 0x03020100UL);
  uint8_t visible = prefs.getUChar("visible", 0x1F);
  for (uint8_t i = 0; i < kPageCount; ++i) {
    state.appearance.pageOrder[i] = static_cast<Page>((order >> (i * 8)) & 0xFF);
    state.appearance.pageVisible[i] = (visible & (1 << i)) != 0;
  }
  prefs.end();
  normalizeAppearance(state.appearance);
  if (dimSecondsType == PT_U16 || offSecondsType == PT_U16) {
    prefs.begin("appearance", false);
    if (dimSecondsType == PT_U16) {
      prefs.putUInt("dimsec", state.appearance.dimAfterSeconds);
    }
    if (offSecondsType == PT_U16) {
      prefs.putUInt("offsec", state.appearance.screenOffAfterSeconds);
    }
    prefs.end();
  }
}

void persistAppearanceSettings() {
  normalizeAppearance(state.appearance);
  prefs.begin("appearance", false);
  prefs.putString("brightrev", state.ui.brightnessRevision);
  prefs.putUChar("tiles", state.appearance.tileColorScheme);
  prefs.putUChar("clock", state.appearance.standbyStyle);
  prefs.putUChar("layout", state.appearance.shortcutsLayout);
  prefs.putUChar("theme", state.appearance.theme);
  prefs.putUChar("button", state.appearance.buttonStyle);
  prefs.putUInt("accent", state.appearance.accent);
  prefs.putUInt("bg", state.appearance.background);
  prefs.putUChar("bright", state.appearance.brightness);
  prefs.putUInt("dimsec", state.appearance.dimAfterSeconds);
  prefs.putUChar("dimbright", state.appearance.dimBrightness);
  prefs.putUInt("offsec", state.appearance.screenOffAfterSeconds);
  prefs.putBool("wake", state.appearance.wakeOnTouch);
  uint64_t order = 0;
  uint8_t visible = 0;
  for (uint8_t i = 0; i < kPageCount; ++i) {
    order |= static_cast<uint64_t>(pageIndex(state.appearance.pageOrder[i])) << (i * 8);
    if (state.appearance.pageVisible[i]) visible |= 1 << i;
  }
  prefs.putULong64("order", order);
  prefs.putUChar("visible", visible);
  prefs.end();
}

void persistSuccessfulSetup() {
  prefs.begin("setup", false);
  prefs.putString("name", pendingSetup.deviceName);
  prefs.putUChar("bright", pendingSetup.brightness);
  prefs.putString("agentip", pendingSetup.agentIp);
  prefs.putString("ssid", pendingSetup.ssid);
  prefs.putString("pass", pendingSetup.password);
  prefs.putBool("done", true);
  prefs.putUChar("conn", static_cast<uint8_t>(onboarding::ConnectionChoice::Wifi));
  prefs.end();

  setupSettings.done = true;
  setupSettings.connectionChoice = onboarding::ConnectionChoice::Wifi;
  setupSettings.deviceName = pendingSetup.deviceName;
  setupSettings.brightness = pendingSetup.brightness;
  setupSettings.agentIp = pendingSetup.agentIp;
  setupSettings.wifiSsid = pendingSetup.ssid;
  setupSettings.wifiPassword = pendingSetup.password;
}

void chooseUsbMode() {
  pendingSetup.active = false;
  setupStatusLabel = nullptr;
  setupQrNeedsDraw = false;
  stopSetupPortal();
  WiFi.disconnect(true, false);
  WiFi.mode(WIFI_OFF);
  stationConnectStarted = false;

  prefs.begin("setup", false);
  prefs.putBool("done", true);
  prefs.putUChar("conn", static_cast<uint8_t>(onboarding::ConnectionChoice::Usb));
  prefs.end();

  setupSettings.done = true;
  setupSettings.connectionChoice = onboarding::ConnectionChoice::Usb;
  appLogLine("[SETUP] USB mode selected; Wi-Fi disabled");
  startAppUi();
}

void chooseWifiMode() {
  setupSettings.connectionChoice = onboarding::ConnectionChoice::Wifi;
  startSetupPortal();
}

void restorePreviousNetwork() {
  setupSettings.wifiSsid = pendingSetup.previousSsid;
  setupSettings.wifiPassword = pendingSetup.previousPassword;
  setupSettings.deviceName = pendingSetup.previousDeviceName;
  setupSettings.agentIp = pendingSetup.previousAgentIp;
  setupSettings.brightness = pendingSetup.previousBrightness;
  applyBacklight(setupSettings.brightness);
  if (setupSettings.wifiSsid.length() > 0) {
    WiFi.persistent(false);
    WiFi.mode(WIFI_STA);
    WiFi.begin(setupSettings.wifiSsid.c_str(), setupSettings.wifiPassword.c_str());
    stationConnectStarted = true;
  }
}

void setSetupStatus(const char* text) {
  setupStatusMessage = text ? text : "";
  if (setupStatusLabel) lv_label_set_text(setupStatusLabel, text);
}

String setupQrPayload() {
  String qrPayload = "WIFI:T:WPA;S:";
  qrPayload += setupSettings.apSsid;
  qrPayload += ";P:";
  qrPayload += setupSettings.setupPassword;
  qrPayload += ";;";
  return qrPayload;
}

void drawSetupQr() {
  if (bootMode != BootMode::Provisioning || !setupPortalActive) return;
  const String payload = setupQrPayload();
  display.qrcode(payload.c_str(), 175, 16, 120, 6, true);
  setupQrNeedsDraw = false;
}


void startAppUi() {
  bootMode = BootMode::App;
  calibrating = false;
  setupUiBuilt = false;
  setupStatusLabel = nullptr;
  setupQrNeedsDraw = false;
  if (!setupPortalActive && onboarding::shouldRunWebConfigServer(
          setupSettings.connectionChoice, setupSettings.wifiSsid.length() > 0)) {
    startWebConfigServer(false);
  }
  if (!appUiStarted) {
    setupStatusLabel = nullptr;
    setupQrNeedsDraw = false;
    lv_obj_clean(lv_scr_act());
    uiBegin(state, intents);
    appUiStarted = true;
  }
  if (!agentClientStarted) {
    AgentClientConfig config;
    config.deviceId = makeDeviceId();
    config.deviceName = setupSettings.deviceName;
    config.manualHost = setupSettings.agentIp;
    config.usbProtocolEnabled = kUsbProtocolEnabled;
    config.renderCover = renderCachedCover;
    agentClient.begin(config, state, intents, sdStorage);
    agentClientStarted = true;
  }
}

void markUserInput() {
  lastUserInputAt = millis();
  if (displayOff || displayDimmed) {
    displayOff = false;
    displayDimmed = false;
    state.ui.standby = false;
    uiRefresh(state);
    applyConfiguredBacklight();
  }
}

bool renderCachedCover(const char* coverId, AppState& appState) {
  if (!coverId || !sdMounted || !appUiStarted) return false;
  File jpeg = sdStorage.openCoverRead(coverId);
  if (!jpeg) return false;
  if (!coverPixels) {
    coverPixels = static_cast<uint16_t*>(std::calloc(kCoverImageSize * kCoverImageSize, sizeof(uint16_t)));
    if (!coverPixels) {
      jpeg.close();
      return false;
    }
  }

  lgfx::LGFX_FILESYSTEM_Support<lgfx::LGFX_Sprite> sprite;
  sprite.setColorDepth(16);
  sprite.setBuffer(coverPixels, kCoverImageSize, kCoverImageSize, 16);
  sprite.fillScreen(0x0000);
  const bool decoded = sprite.drawJpg(&jpeg, 0, 0, kCoverImageSize, kCoverImageSize, 0, 0, 1.0f);
  jpeg.close();
  if (!decoded) return false;
  convertDisplayOrderRgb565ToLvgl(coverPixels, kCoverImageSize * kCoverImageSize);
  coverImageDsc.header.magic = LV_IMAGE_HEADER_MAGIC;
  coverImageDsc.header.cf = LV_COLOR_FORMAT_RGB565;
  coverImageDsc.header.flags = 0;
  coverImageDsc.header.w = kCoverImageSize;
  coverImageDsc.header.h = kCoverImageSize;
  coverImageDsc.header.stride = kCoverImageSize * sizeof(uint16_t);
  coverImageDsc.data_size = kCoverImageSize * kCoverImageSize * sizeof(uint16_t);
  coverImageDsc.data = reinterpret_cast<const uint8_t*>(coverPixels);
  uiSetCoverImage(&coverImageDsc);
  appState.media.cover.rendered = true;
  return true;
}

#if defined(WOKWI_SIMULATOR)
void renderWokwiCoverDiagnostic(AppState& appState) {
  if (!appUiStarted) return;
  if (!coverPixels) {
    coverPixels = static_cast<uint16_t*>(std::calloc(kCoverImageSize * kCoverImageSize, sizeof(uint16_t)));
    if (!coverPixels) {
      appLogLine("[WOKWI] COVER_RGB_DIAG_ALLOC_FAILED");
      return;
    }
  }

  lgfx::LGFX_Sprite sprite;
  sprite.setColorDepth(16);
  sprite.setBuffer(coverPixels, kCoverImageSize, kCoverImageSize, 16);
  sprite.fillScreen(0x0000);
  const bool decoded = sprite.drawJpg(kWokwiCoverRgbJpeg, kWokwiCoverRgbJpegLength,
      0, 0, kCoverImageSize, kCoverImageSize, 0, 0, 1.0f);
  if (!decoded) {
    appLogLine("[WOKWI] COVER_RGB_DIAG_DECODE_FAILED");
    return;
  }

  convertDisplayOrderRgb565ToLvgl(coverPixels, kCoverImageSize * kCoverImageSize);
  coverImageDsc.header.magic = LV_IMAGE_HEADER_MAGIC;
  coverImageDsc.header.cf = LV_COLOR_FORMAT_RGB565;
  coverImageDsc.header.flags = 0;
  coverImageDsc.header.w = kCoverImageSize;
  coverImageDsc.header.h = kCoverImageSize;
  coverImageDsc.header.stride = kCoverImageSize * sizeof(uint16_t);
  coverImageDsc.data_size = kCoverImageSize * kCoverImageSize * sizeof(uint16_t);
  coverImageDsc.data = reinterpret_cast<const uint8_t*>(coverPixels);
  uiSetCoverImage(&coverImageDsc);
  appState.media.cover.status = CoverStatus::Available;
  appState.media.cover.rendered = true;
  uiRefresh(appState);
  appLogLine("[WOKWI] COVER_RGB_DIAG_READY");
}
#endif

void beginSdStatus() {
  sdMounted = sdStorage.begin();
  state.storage.mounted = sdMounted;
  state.storage.ready = sdMounted;
  std::strncpy(state.storage.status, sdMounted ? "mounted" : "unavailable",
      sizeof(state.storage.status) - 1);
  state.storage.status[sizeof(state.storage.status) - 1] = '\0';
}

void stopSetupPortal();

void clearNamespace(const char* name) {
  prefs.begin(name, false);
  prefs.clear();
  prefs.end();
}

void restartDevice() {
  appLogLine("[APP] restarting device");
  delay(150);
  ESP.restart();
}

void factoryResetDevice() {
  appLogLine("[APP] factory reset requested");
  stopSetupPortal();
  if (agentClientStarted) {
    agentClientStarted = false;
  }
  WiFi.disconnect(true, true);
  clearNamespace("setup");
  clearNamespace("touch");
  clearNamespace("pairing");
  clearNamespace("appearance");
  clearNamespace("shortcuts");
  delay(250);
  ESP.restart();
}

void serviceOtaUpdate() {
  if (bootMode != BootMode::App || otaConfigured || !setupSettings.done ||
      !onboarding::shouldRunOta(setupSettings.connectionChoice, WiFi.status() == WL_CONNECTED)) {
    if (otaUpdate.started()) otaUpdate.loop();
    return;
  }

  OtaUpdateConfig config;
  config.hostname = setupSettings.deviceName;
  config.password = setupSettings.setupPassword;
  otaUpdate.begin(config, state);
  otaConfigured = otaUpdate.started();
}

void stopSetupPortal() {
  if (!setupPortalActive) return;
  setupServer.stop();
  dnsServer.stop();
  WiFi.softAPdisconnect(true);
  setupPortalActive = false;
}

void startWebConfigServer(bool provisioningMode) {
  if (provisioningMode) {
    const IPAddress apIp(192, 168, 4, 1);
    const IPAddress netmask(255, 255, 255, 0);
    WiFi.softAPConfig(apIp, apIp, netmask);
    WiFi.softAP(setupSettings.apSsid.c_str(), setupSettings.setupPassword.c_str());
    dnsServer.start(kDnsPort, "*", apIp);
  }
  setupServer.begin();
  setupPortalActive = true;
}

void startStationIfConfigured() {
  if (stationConnectStarted || !onboarding::shouldStartStation(
          setupSettings.connectionChoice, setupSettings.wifiSsid.length() > 0)) {
    return;
  }
  WiFi.persistent(false);
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);
  WiFi.begin(setupSettings.wifiSsid.c_str(), setupSettings.wifiPassword.c_str());
  stationConnectStarted = true;
  appLogf("[SETUP] connecting STA for device %s\n",
      setupSettings.deviceName.c_str());
}

String buildSetupPage(const char* message, bool lanMode = false) {
  String html;
  html.reserve(5000);
  html += F("<!doctype html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'>");
  html += F("<title>CYD Stream Deck ");
  html += lanMode ? F("Settings") : F("Setup");
  html += F("</title><style>body{font-family:system-ui;margin:24px;background:#101216;color:#f3f5f7}label{display:block;margin-top:14px}.card{max-width:460px;margin:auto}.hint{color:#a3abb7}input{width:100%;box-sizing:border-box;padding:10px;margin-top:5px;border-radius:8px;border:1px solid #555;background:#1c2026;color:#fff}button{margin-top:18px;width:100%;padding:12px;border:0;border-radius:8px;background:#7eafe3;color:#101216;font-weight:700}</style></head><body><main class='card'>");
  html += F("<h1>CYD Stream Deck</h1>");
  if (message && message[0]) {
    html += F("<p class='hint'>");
    html += htmlEscape(message);
    html += F("</p>");
  } else if (setupStatusMessage.length() > 0) {
    html += F("<p class='hint'>");
    html += htmlEscape(setupStatusMessage);
    html += F("</p>");
  }
  html += F("<form method='post' action='/save'>");
  if (lanMode) {
    html += F("<label>Setup password<input name='key' type='password' required></label>");
  }
  html += F("<label>Wi-Fi SSID<input name='ssid' maxlength='32' value='");
  html += htmlEscape(setupSettings.wifiSsid);
  html += F("' required></label>");
  html += F("<label>Wi-Fi password<input name='pass' type='password' maxlength='63'></label>");
  html += F("<label>Device name<input name='name' maxlength='31' value='");
  html += htmlEscape(setupSettings.deviceName);
  html += F("' required></label>");
  html += F("<label>Backlight brightness 10-100<input name='bright' type='number' min='10' max='100' value='");
  html += String(setupSettings.brightness);
  html += F("'></label>");
  html += F("<label>Windows agent IP fallback<input name='agentip' placeholder='optional, e.g. 192.168.1.50' value='");
  html += htmlEscape(setupSettings.agentIp);
  html += F("'></label>");
  html += F("<button type='submit'>Save and connect</button></form>");
  if (lanMode) {
    html += F("<p class='hint'>Changes are verified and saved. Setup password is required for LAN security.</p>");
  } else {
    html += F("<p class='hint'>Wi-Fi settings are saved only after this device connects successfully.</p>");
  }
  html += F("</main></body></html>");
  return html;
}

String urlDecode(const String& value) {
  String out;
  out.reserve(value.length());
  for (size_t i = 0; i < value.length(); ++i) {
    const char c = value[i];
    if (c == '+') {
      out += ' ';
    } else if (c == '%' && i + 2 < value.length()) {
      const auto hex = [](char h) -> int {
        if (h >= '0' && h <= '9') return h - '0';
        if (h >= 'A' && h <= 'F') return h - 'A' + 10;
        if (h >= 'a' && h <= 'f') return h - 'a' + 10;
        return -1;
      };
      const int high = hex(value[i + 1]);
      const int low = hex(value[i + 2]);
      if (high >= 0 && low >= 0) {
        out += static_cast<char>((high << 4) | low);
        i += 2;
      }
    } else {
      out += c;
    }
  }
  return out;
}

String formValue(const String& body, const char* name) {
  const String prefix = String(name) + "=";
  int cursor = 0;
  while (cursor < static_cast<int>(body.length())) {
    int end = body.indexOf('&', cursor);
    if (end < 0) end = body.length();
    const String pair = body.substring(cursor, end);
    if (pair.startsWith(prefix)) return urlDecode(pair.substring(prefix.length()));
    cursor = end + 1;
  }
  return "";
}

void sendHttp(WiFiClient& client, int status, const char* statusText,
              const char* contentType, const String& body) {
  client.print(F("HTTP/1.1 "));
  client.print(status);
  client.print(' ');
  client.println(statusText);
  client.print(F("Content-Type: "));
  client.println(contentType);
  client.print(F("Content-Length: "));
  client.println(body.length());
  client.println(F("Cache-Control: no-store"));
  client.println(F("Connection: close"));
  client.println();
  client.print(body);
}

void sendRedirect(WiFiClient& client) {
  client.println(F("HTTP/1.1 302 Found"));
  client.println(F("Location: http://192.168.4.1/"));
  client.println(F("Content-Length: 0"));
  client.println(F("Connection: close"));
  client.println();
}

void handleSetupSave(WiFiClient& client, const String& body, bool isLanMode) {
  const String key = formValue(body, "key");
  const String ssid = formValue(body, "ssid");
  const String pass = formValue(body, "pass");
  const String name = formValue(body, "name");
  const String agentIp = formValue(body, "agentip");
  const int brightness = formValue(body, "bright").toInt();

  const onboarding::SetupPortalMode portalMode = isLanMode
      ? onboarding::SetupPortalMode::LanSettings
      : onboarding::SetupPortalMode::FirstSetup;
  if (!onboarding::validateSetupAuth(portalMode, key.c_str(), setupSettings.setupPassword.c_str())) {
    sendHttp(client, 401, "Unauthorized", "text/html",
        buildSetupPage("Authentication required: wrong setup password.", isLanMode));
    return;
  }
  if (!onboarding::validSsid(ssid.c_str()) ||
      !onboarding::validWifiPassword(pass.c_str()) ||
      !onboarding::validDeviceName(name.c_str()) ||
      !onboarding::validIpv4OrEmpty(agentIp.c_str())) {
    sendHttp(client, 400, "Bad Request", "text/html",
        buildSetupPage("Check SSID, password length, device name, and agent IP.", isLanMode));
    return;
  }

  pendingSetup.active = true;
  pendingSetup.fromAppMode = (bootMode == BootMode::App);
  pendingSetup.ssid = ssid;
  pendingSetup.password = pass;
  pendingSetup.deviceName = name;
  pendingSetup.agentIp = agentIp;
  pendingSetup.brightness = onboarding::clampBrightness(brightness);
  pendingSetup.startedAt = millis();
  pendingSetup.previousSsid = setupSettings.wifiSsid;
  pendingSetup.previousPassword = setupSettings.wifiPassword;
  pendingSetup.previousDeviceName = setupSettings.deviceName;
  pendingSetup.previousAgentIp = setupSettings.agentIp;
  pendingSetup.previousBrightness = setupSettings.brightness;
  applyBacklight(pendingSetup.brightness);

  WiFi.persistent(false);
  WiFi.mode(pendingSetup.fromAppMode ? WIFI_STA : WIFI_AP_STA);
  WiFi.begin(pendingSetup.ssid.c_str(), pendingSetup.password.c_str());
  setSetupStatus("Testing Wi-Fi credentials...");
  sendHttp(client, 202, "Accepted", "text/html",
      buildSetupPage("Testing Wi-Fi credentials. Refresh this page for the result.", isLanMode));
  appLogLine("[SETUP] Wi-Fi credentials received; testing connection");
}

void serviceSetupHttp() {
  WiFiClient client = setupServer.available();
  if (!client) return;

  String request;
  request.reserve(kSetupHttpMaxRequestBytes);
  const uint32_t start = millis();
  HttpRequestReadDecision readDecision = HttpRequestReadDecision::Incomplete;
  while (client.connected() && millis() - start < 250 &&
      request.length() < kSetupHttpMaxRequestBytes) {
    while (client.available() && request.length() < kSetupHttpMaxRequestBytes) {
      request += static_cast<char>(client.read());
    }
    readDecision = decideHttpRequestRead(request.c_str(), request.length(),
        kSetupHttpMaxBodyBytes);
    if (readDecision != HttpRequestReadDecision::Incomplete) break;
    delay(1);
  }

  if (readDecision == HttpRequestReadDecision::Incomplete) {
    readDecision = decideHttpRequestRead(request.c_str(), request.length(),
        kSetupHttpMaxBodyBytes);
  }
  if (readDecision == HttpRequestReadDecision::Invalid ||
      readDecision == HttpRequestReadDecision::Incomplete) {
    sendHttp(client, 400, "Bad Request", "text/plain", "Bad Request");
    client.stop();
    return;
  }

  const int firstLineEnd = request.indexOf("\r\n");
  const String firstLine = firstLineEnd >= 0 ? request.substring(0, firstLineEnd) : "";
  const int bodyAt = request.indexOf("\r\n\r\n");
  const String body = bodyAt >= 0 ? request.substring(bodyAt + 4) : "";

  const bool isSoftAp = (client.localIP() == WiFi.softAPIP());
  const bool isLanMode = onboarding::shouldRequireSetupAuth(
      bootMode == BootMode::Provisioning, isSoftAp);
  if (firstLine.startsWith("GET / ") || firstLine.startsWith("GET /?")) {
    sendHttp(client, 200, "OK", "text/html", buildSetupPage("", isLanMode));
  } else if (firstLine.startsWith("POST /save ")) {
    handleSetupSave(client, body, isLanMode);
  } else {
    sendRedirect(client);
  }
  client.stop();
}

void onSetupUseUsbClicked(lv_event_t*) {
  chooseUsbMode();
}

void buildSetupUi() {
  setupUiBuilt = true;
  lv_obj_t* screen = lv_scr_act();
  lv_obj_clean(screen);
  setBounds(screen, 0, 0, board::screenWidth, board::screenHeight);
  lv_obj_set_style_bg_color(screen, lv_color_hex(kSetupBg), 0);
  lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);

  makeLabel(screen, "WI-FI SETUP", 12, 6, 150, 18, kSetupMuted,
      &lv_font_montserrat_14);
  makeLabel(screen, setupSettings.apSsid.c_str(), 12, 24, 150, 20, kSetupText,
      &lv_font_montserrat_16);
  makeLabel(screen, "AP Password", 12, 44, 150, 16, kSetupMuted,
      &lv_font_montserrat_14);

  char dispPass[24]{};
  onboarding::formatDisplayPassword(setupSettings.setupPassword.c_str(), dispPass, sizeof(dispPass));
  lv_obj_t* lblPass = makeLabel(screen, dispPass, 12, 60, 150, 36,
      kSetupAccent, &lv_font_montserrat_14);
  lv_label_set_long_mode(lblPass, LV_LABEL_LONG_WRAP);

  makeLabel(screen, "Open 192.168.4.1", 12, 98, 150, 16, kSetupMuted,
      &lv_font_montserrat_14);
  setupStatusLabel = makeLabel(screen, "Scan QR or join AP", 12, 116, 150, 18,
      kSetupText, &lv_font_montserrat_14);

  lv_obj_t* panel = lv_obj_create(screen);
  setBounds(panel, 169, 10, 132, 132);
  lv_obj_set_style_bg_color(panel, lv_color_hex(kSetupPanel), 0);
  lv_obj_set_style_bg_opa(panel, LV_OPA_COVER, 0);
  lv_obj_set_style_border_width(panel, 0, 0);
  lv_obj_set_style_radius(panel, 8, 0);
  setupQrNeedsDraw = true;

  makeLabel(screen, "Enter target Wi-Fi SSID & password in portal.", 12, 148, 296,
      18, kSetupMuted, &lv_font_montserrat_14);

  lv_obj_t* btnUsbFallback = lv_btn_create(screen);
  setBounds(btnUsbFallback, 12, 172, 296, 44);
  lv_obj_set_style_bg_color(btnUsbFallback, lv_color_hex(kSetupPanel), 0);
  lv_obj_set_style_bg_opa(btnUsbFallback, LV_OPA_COVER, 0);
  lv_obj_set_style_border_color(btnUsbFallback, lv_color_hex(kSetupAccent), 0);
  lv_obj_set_style_border_width(btnUsbFallback, 1, 0);
  lv_obj_set_style_radius(btnUsbFallback, 8, 0);
  lv_obj_add_event_cb(btnUsbFallback, onSetupUseUsbClicked, LV_EVENT_CLICKED, nullptr);

  lv_obj_t* btnLbl = lv_label_create(btnUsbFallback);
  lv_label_set_text(btnLbl, "Use USB instead (No Wi-Fi)");
  lv_obj_set_style_text_color(btnLbl, lv_color_hex(kSetupText), 0);
  lv_obj_set_style_text_font(btnLbl, &lv_font_montserrat_14, 0);
  lv_obj_center(btnLbl);
}

void onUsbChoiceClicked(lv_event_t*) {
  chooseUsbMode();
}

void onWifiChoiceClicked(lv_event_t*) {
  chooseWifiMode();
}

void buildChoiceUi() {
  setupUiBuilt = true;
  lv_obj_t* screen = lv_scr_act();
  lv_obj_clean(screen);
  setBounds(screen, 0, 0, board::screenWidth, board::screenHeight);
  lv_obj_set_style_bg_color(screen, lv_color_hex(kSetupBg), 0);
  lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);

  makeLabel(screen, "FIRST SETUP", 16, 8, 288, 18, kSetupMuted,
      &lv_font_montserrat_14);
  makeLabel(screen, "Choose connection mode:", 16, 28, 288, 22, kSetupText,
      &lv_font_montserrat_16);

  lv_obj_t* btnUsb = lv_btn_create(screen);
  setBounds(btnUsb, 16, 56, 288, 64);
  lv_obj_set_style_bg_color(btnUsb, lv_color_hex(kSetupPanel), 0);
  lv_obj_set_style_bg_opa(btnUsb, LV_OPA_COVER, 0);
  lv_obj_set_style_border_color(btnUsb, lv_color_hex(kSetupAccent), 0);
  lv_obj_set_style_border_width(btnUsb, 2, 0);
  lv_obj_set_style_radius(btnUsb, 10, 0);
  lv_obj_add_event_cb(btnUsb, onUsbChoiceClicked, LV_EVENT_CLICKED, nullptr);

  lv_obj_t* lblUsb = lv_label_create(btnUsb);
  lv_label_set_text(lblUsb, "USB Cable (Recommended)\nNo Wi-Fi. Approve in Windows Agent.");
  setBounds(lblUsb, 12, 12, 264, 40);
  lv_obj_set_style_text_color(lblUsb, lv_color_hex(kSetupText), 0);
  lv_obj_set_style_text_font(lblUsb, &lv_font_montserrat_14, 0);

  lv_obj_t* btnWifi = lv_btn_create(screen);
  setBounds(btnWifi, 16, 130, 288, 64);
  lv_obj_set_style_bg_color(btnWifi, lv_color_hex(kSetupPanel), 0);
  lv_obj_set_style_bg_opa(btnWifi, LV_OPA_COVER, 0);
  lv_obj_set_style_border_color(btnWifi, lv_color_hex(0x30363D), 0);
  lv_obj_set_style_border_width(btnWifi, 1, 0);
  lv_obj_set_style_radius(btnWifi, 10, 0);
  lv_obj_add_event_cb(btnWifi, onWifiChoiceClicked, LV_EVENT_CLICKED, nullptr);

  lv_obj_t* lblWifi = lv_label_create(btnWifi);
  lv_label_set_text(lblWifi, "Wi-Fi Network\nOpen SoftAP portal to configure network.");
  setBounds(lblWifi, 12, 12, 264, 40);
  lv_obj_set_style_text_color(lblWifi, lv_color_hex(kSetupText), 0);
  lv_obj_set_style_text_font(lblWifi, &lv_font_montserrat_14, 0);

  makeLabel(screen, "You can change this anytime on the device.", 16, 204, 288,
      18, kSetupMuted, &lv_font_montserrat_14);
}

void startChoiceScreen() {
  bootMode = BootMode::Choice;
  appUiStarted = false;
  setupUiBuilt = false;
  buildChoiceUi();
  appLogLine("[SETUP] connection mode choice screen displayed");
}

void startSetupPortal() {
  bootMode = BootMode::Provisioning;
  pendingSetup.active = false;
  WiFi.persistent(false);
  WiFi.mode(WIFI_AP_STA);
  startWebConfigServer(true);
  appUiStarted = false;
  buildSetupUi();
  appLogf("[SETUP] portal started ssid=%s ip=%s\n",
      setupSettings.apSsid.c_str(), WiFi.softAPIP().toString().c_str());
}

void calibrationScreen() {
  lv_obj_t* screen = lv_scr_act();
  lv_obj_clean(screen);
  setBounds(screen, 0, 0, board::screenWidth, board::screenHeight);
  lv_obj_set_style_bg_color(screen, lv_color_hex(kSetupBg), 0);
  lv_obj_set_style_bg_opa(screen, LV_OPA_COVER, 0);

  lv_obj_t* cross = lv_obj_create(screen);
  setBounds(cross, kTargetX[calibrationStep] - 10, kTargetY[calibrationStep] - 10,
      20, 20);
  lv_obj_set_style_bg_opa(cross, LV_OPA_TRANSP, 0);
  lv_obj_set_style_border_color(cross, lv_color_hex(kSetupAccent), 0);
  lv_obj_set_style_border_width(cross, 2, 0);
  lv_obj_set_style_radius(cross, LV_RADIUS_CIRCLE, 0);

  lv_obj_t* label = makeLabel(screen, "", 42, 110, 238, 24, kSetupText,
      &lv_font_montserrat_16);
  char text[48];
  std::snprintf(text, sizeof(text), "Touch target %u/5, then release",
      calibrationStep + 1);
  lv_label_set_text(label, text);
}

void beginCalibration() {
  bootMode = BootMode::Calibration;
  calibrating = true;
  calibrationStep = 0;
  calibrationArmed = false;
  appUiStarted = false;
  calibrationScreen();
  appLogLine("[APP] tap each calibration target and release");
}

void afterCalibrationComplete() {
  calibrating = false;
  const onboarding::BootState bootState{
      calibration.valid, setupSettings.done, setupSettings.connectionChoice};
  if (onboarding::needsConnectionChoice(bootState)) {
    startChoiceScreen();
  } else if (onboarding::needsProvisioning(bootState)) {
    startSetupPortal();
  } else {
    startStationIfConfigured();
    startAppUi();
  }
}

void saveTap(RawTouch& raw) {
  rawPoints[calibrationStep] = {
      static_cast<float>(raw.x),
      static_cast<float>(raw.y)
  };

  if (calibrationStep == 4) {
    touchcal::Point screen[5];
    for (int i = 0; i < 5; ++i) {
      screen[i] = touchcal::rotate180({
          static_cast<float>(kTargetX[i]),
          static_cast<float>(kTargetY[i])
      }, board::screenWidth, board::screenHeight);
    }
    touchcal::Coefficients candidate;
    if (touchcal::fitFive(rawPoints, screen, candidate, 30.0f)) {
      calibration = candidate;
      persistCalibration();
      afterCalibrationComplete();
    } else {
      calibrationStep = 0;
      calibrationScreen();
      appLogLine("[APP] touch calibration rejected; prior saved map remains active");
    }
  } else {
    ++calibrationStep;
    calibrationScreen();
  }
  calibrationArmed = false;
}

void readTouch(lv_indev_t*, lv_indev_data_t* data) {
  RawTouch sample{};
  const bool isDown = touch.read(sample);
  if (calibrating) {
    if (touch.pressed() && !down && sample.z > 80) {
      calibrationArmed = true;
      lastRaw = sample;
    }
    if (touch.pressed() && sample.z > 80) lastRaw = sample;
    if (!touch.pressed() && calibrationArmed) saveTap(lastRaw);
    down = touch.pressed();
    data->state = LV_INDEV_STATE_RELEASED;
    data->point.x = -1;
    data->point.y = -1;
    return;
  }

  if (!calibration.valid) {
    data->state = LV_INDEV_STATE_RELEASED;
    data->point.x = 0;
    data->point.y = 0;
    return;
  }

  if (bootMode == BootMode::App) {
    if (isDown && (displayOff || displayDimmed) && state.appearance.wakeOnTouch) {
      markUserInput();
      consumeWakeTouch = true;
      down = true;
      data->state = LV_INDEV_STATE_RELEASED;
      data->point.x = -1;
      data->point.y = -1;
      return;
    }

    if (consumeWakeTouch) {
      if (!isDown) {
        consumeWakeTouch = false;
        down = false;
      }
      data->state = LV_INDEV_STATE_RELEASED;
      data->point.x = -1;
      data->point.y = -1;
      return;
    }
  }

  if (isDown) {
    if (!down && bootMode == BootMode::App) markUserInput();
    touchcal::Point pt = touchcal::map(calibration, {
        static_cast<float>(sample.x),
        static_cast<float>(sample.y)
    });
    pt = touchcal::rotate180(pt, board::screenWidth, board::screenHeight);
    const int x = constrain(static_cast<int>(pt.x), 0, board::screenWidth - 1);
    const int y = constrain(static_cast<int>(pt.y), 0, board::screenHeight - 1);
    if (bootMode == BootMode::App) {
      uiReportTouch(x, y);
    }
    lastX = x;
    lastY = y;
    data->state = LV_INDEV_STATE_PRESSED;
    data->point.x = x;
    data->point.y = y;
    down = true;
  } else {
    down = false;
    data->state = LV_INDEV_STATE_RELEASED;
    data->point.x = lastX;
    data->point.y = lastY;
  }
}

void handleIntent(const Intent& intent) {
  switch (intent.type) {
    case IntentType::TestTouch:
      uiShowTouchTest();
      break;
    case IntentType::SetBrightness:
      state.ui.localBrightness = clampPercent(intent.value, 10, 100);
      applyConfiguredBacklight();
      break;
    case IntentType::Recalibrate:
      beginCalibration();
      break;
    case IntentType::RestartDevice:
      restartDevice();
      break;
    case IntentType::FactoryReset:
      factoryResetDevice();
      break;
    case IntentType::OpenSetupPortal:
      startSetupPortal();
      break;
    case IntentType::SettingsUpdated:
      persistAppearanceSettings();
      // Settings wake the display: reset idle time as well as the flags.
      lastUserInputAt = millis();
      displayOff = false;
      displayDimmed = false;
      state.ui.standby = false;
      applyConfiguredBacklight();
      uiRefresh(state);
      break;
    case IntentType::ShortcutsUpdated:
      persistShortcutSettings();
      uiRefresh(state);
      break;
    case IntentType::RefreshUi:
      uiRefresh(state);
      break;
    case IntentType::SetVolume:
      state.audio.reportedVolume = constrain(intent.value, 0, 100);
      uiRefresh(state);
      agentClient.sendIntent(intent);
      break;
    case IntentType::ToggleMute:
      state.audio.muted = !state.audio.muted;
      uiRefresh(state);
      agentClient.sendIntent(intent);
      break;
    case IntentType::Previous:
    case IntentType::TogglePlayback:
    case IntentType::Next:
    case IntentType::RunShortcut:
      agentClient.sendIntent(intent);
      break;
  }
}

void serviceDisplayIdle() {
  if (bootMode != BootMode::App || calibrating) return;
  state.ui.calibrationValid = calibration.valid;
  const uint32_t idleSeconds = (millis() - lastUserInputAt) / 1000UL;
  const DisplayIdleDecision decision = computeDisplayIdleDecision(state, idleSeconds, displayOff, displayDimmed);
  if (decision.resetIdleTimer) lastUserInputAt = millis();
  if (decision.off != displayOff || decision.dimmed != displayDimmed) {
    displayOff = decision.off;
    displayDimmed = decision.dimmed;
    state.ui.standby = displayDimmed || displayOff;
    uiRefresh(state);
    applyConfiguredBacklight();
  }
}

void serviceSetupPortal() {
  if (!setupPortalActive) return;
  dnsServer.processNextRequest();
  serviceSetupHttp();

  servicePendingSetupResult();
}

void servicePendingSetupResult() {
  if (!pendingSetup.active) return;

  const bool connected = WiFi.status() == WL_CONNECTED;
  const bool ssidMatches = connected && WiFi.SSID() == pendingSetup.ssid;
  const PendingSetupDecision decision = decidePendingSetupResult(
      connected, ssidMatches, millis() - pendingSetup.startedAt,
      kWifiConnectTimeoutMs);

  if (decision == PendingSetupDecision::Success) {
    const bool fromAppMode = pendingSetup.fromAppMode;
    persistSuccessfulSetup();
    pendingSetup.active = false;
    if (fromAppMode) {
      setSetupStatus("Wi-Fi connected. Settings saved. Restart to apply device name or agent IP changes.");
      stationConnectStarted = true;
      return;
    }
    setSetupStatus("Wi-Fi connected. Starting app...");
    appLogf("[SETUP] Wi-Fi validated; setup complete ip=%s\n",
        WiFi.localIP().toString().c_str());
    stopSetupPortal();
    startStationIfConfigured();
    startAppUi();
    return;
  }
  if (decision == PendingSetupDecision::Failure) {
    const bool fromAppMode = pendingSetup.fromAppMode;
    pendingSetup.active = false;
    if (fromAppMode) {
      WiFi.disconnect(false, false);
      restorePreviousNetwork();
      setSetupStatus("Wi-Fi failed. Previous network restored.");
      appLogLine("[SETUP] Wi-Fi validation failed; previous network restored");
      return;
    }
    WiFi.disconnect(false, false);
    WiFi.mode(WIFI_AP_STA);
    setSetupStatus("Wi-Fi failed. Portal still open.");
    appLogLine("[SETUP] Wi-Fi validation failed; portal remains open");
  }
}

void refreshSystemState() {
  static uint32_t last = 0;
  if (millis() - last <= 1000) return;
  last = millis();
  state.system.freeHeap = ESP.getFreeHeap();
  state.system.minHeap = ESP.getMinFreeHeap();
  state.system.largestBlock = ESP.getMaxAllocHeap();
  state.device.uptimeSeconds = millis() / 1000UL;
  state.connection.wifiConnected = WiFi.status() == WL_CONNECTED;
  state.connection.online = state.connection.agentConnected;
  if (state.connection.wifiConnected) {
    std::strncpy(state.connection.ssid, WiFi.SSID().c_str(), sizeof(state.connection.ssid) - 1);
    state.connection.ssid[sizeof(state.connection.ssid) - 1] = '\0';
    std::strncpy(state.connection.deviceIp, WiFi.localIP().toString().c_str(), sizeof(state.connection.deviceIp) - 1);
    state.connection.deviceIp[sizeof(state.connection.deviceIp) - 1] = '\0';
    state.connection.rssi = static_cast<int16_t>(WiFi.RSSI());
  } else {
    state.connection.ssid[0] = '\0';
    state.connection.deviceIp[0] = '\0';
    state.connection.rssi = 0;
  }

  state.storage.mounted = sdMounted;
  state.storage.ready = sdMounted;
  const storage::SdStorageStatus& storageStatus = sdStorage.status();
  const uint64_t totalBytes = storageStatus.totalBytes;
  const uint64_t usedBytes = storageStatus.usedBytes;
  state.storage.totalMb = static_cast<uint32_t>(totalBytes / 1048576ULL);
  const uint64_t freeBytes = totalBytes > usedBytes ? totalBytes - usedBytes : 0;
  state.storage.freeMb = static_cast<uint32_t>(freeBytes / 1048576ULL);
  std::strncpy(state.storage.status, storageStatus.message.c_str(), sizeof(state.storage.status) - 1);
  state.storage.status[sizeof(state.storage.status) - 1] = '\0';
  if (bootMode == BootMode::App && !calibrating) uiRefresh(state);
}

}  // namespace

namespace streamdeck {


void appBegin() {
  Serial.setRxBufferSize(8192);
  Serial.setTxBufferSize(2048);
  Serial.begin(115200);
  Serial.setDebugOutput(false);
  pinMode(board::backlight, OUTPUT);
  analogWriteFrequency(5000);
  analogWriteResolution(8);

  loadSetupSettings();
  loadAppearanceSettings();
  loadShortcutSettings();
  applyConfiguredBacklight();

  display.init();
  display.setRotation(board::landscapeRotation);
  display.setColorDepth(16);
  touch.begin();
  beginSdStatus();
  loadCalibration();
  state.ui.calibrationValid = calibration.valid;
  lvglPortBegin(display);

  pointer = lv_indev_create();
  lv_indev_set_type(pointer, LV_INDEV_TYPE_POINTER);
  lv_indev_set_read_cb(pointer, readTouch);

  state.system.freeHeap = ESP.getFreeHeap();
  state.system.minHeap = ESP.getMinFreeHeap();
  state.system.largestBlock = ESP.getMaxAllocHeap();
  lastUserInputAt = millis();
  refreshSystemState();

#if defined(WOKWI_SIMULATOR)
  calibration.valid = true;
  setupSettings.done = true;
  setupSettings.connectionChoice = onboarding::ConnectionChoice::Usb;
  appLogLine("[WOKWI] booting app preview; touch and onboarding are not simulated");
#endif
  const onboarding::BootState bootState{
      calibration.valid, setupSettings.done, setupSettings.connectionChoice};
  if (onboarding::needsCalibration(bootState)) {
    beginCalibration();
  } else if (onboarding::needsConnectionChoice(bootState)) {
    startChoiceScreen();
  } else if (onboarding::needsProvisioning(bootState)) {
    startSetupPortal();
  } else {
    startStationIfConfigured();
    startAppUi();
#if defined(WOKWI_SIMULATOR)
    renderWokwiCoverDiagnostic(state);
    appLogLine("[WOKWI] APP_UI_READY");
#endif
  }
}

void appLoop() {
  const uint32_t loopStarted = micros();
  lv_timer_handler();
  const uint32_t renderUs = micros() - loopStarted;
  const uint32_t networkStarted = micros();
  if (setupQrNeedsDraw) drawSetupQr();
  if (bootMode == BootMode::Provisioning) serviceSetupPortal();
  if (bootMode == BootMode::App) {
    serviceSetupHttp();
    servicePendingSetupResult();
  }
  if (bootMode == BootMode::App) agentClient.loop();
  const uint32_t networkUs = micros() - networkStarted;
  serviceOtaUpdate();
  serviceDisplayIdle();

  Intent intent;
  while (intents.pop(intent)) handleIntent(intent);

  refreshSystemState();
  static uint32_t lastTimelinePaintAt = 0;
  if (bootMode == BootMode::App && !calibrating && state.ui.page == Page::Media &&
      state.media.playing && state.connection.agentConnected && millis() - lastTimelinePaintAt >= 100) {
    lastTimelinePaintAt = millis();
    uiRefresh(state);
  }
  if (state.connection.agentConnected) {
    auto& perf = state.performance;
    const uint32_t loopUs = micros() - loopStarted;
    perf.loopMaxUs = std::max(perf.loopMaxUs, loopUs);
    perf.renderMaxUs = std::max(perf.renderMaxUs, renderUs);
    perf.networkMaxUs = std::max(perf.networkMaxUs, networkUs);
    if (loopUs > 50000) ++perf.slowLoops;
  }
}

}  // namespace streamdeck
