#include <cassert>
#include <cstring>
#include <string>

#include "network/agent_discovery.h"
#include "network/agent_protocol.h"
#include "network/cover_art.h"
#include "ui/app_state.h"

int main() {
  using namespace streamdeck;
  {
  char iconData[2305]; std::memset(iconData, '_', 2304); iconData[2304] = 0;
  uint8_t pixels[1728]{};
  assert(protocol::decodeShortcutIcon(iconData, pixels));
  for (auto pixel : pixels) assert(pixel == 255);
  iconData[1] = '!'; assert(!protocol::decodeShortcutIcon(iconData, pixels));
  iconData[1] = '_'; iconData[2303] = 0; assert(!protocol::decodeShortcutIcon(iconData, pixels));
  std::memset(iconData, 'A', 2304); iconData[2304] = 0;
  std::string iconFrame = "{\"shortcuts\":[";
  for (int i = 0; i < 6; ++i) {
    if (i) iconFrame += ",";
    iconFrame += "{\"id\":\"slot" + std::to_string(i) + "\",\"label\":\"Custom\",\"icon\":\"custom\",\"displayMode\":\"both\",\"iconData\":\"" + std::string(iconData) + "\"}";
  }
  iconFrame += "]}";
  assert(iconFrame.size() < 16384);
  ShortcutState imageShortcuts;
  protocol::applyShortcutsUpdateJson(imageShortcuts, iconFrame.c_str(), iconFrame.size());
  for (int i = 0; i < 6; ++i) { assert(imageShortcuts.hasIconPixels[i]); assert(imageShortcuts.iconPixels[i][1727] == 0); }
  protocol::applyShortcutsUpdateJson(imageShortcuts, "{}", 2);
  assert(!imageShortcuts.hasIconPixels[0]);

  }
  using namespace streamdeck;

  const char coverFrame[] = R"({"protocol":1,"type":"cover.chunk","data":{"coverId":"abc","offset":0,"total":5,"data":"SGVsbG8="}})";
  char coverData[32]{};
  assert(protocol::readQuotedField(coverFrame, sizeof(coverFrame) - 1, "data", coverData, sizeof(coverData)));
  assert(std::strcmp(coverData, "SGVsbG8=") == 0);

  CoverFetchState timeoutCover;
  const char* stableCover = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
  assert(updateCoverFetch(timeoutCover, stableCover, 100) == CoverTransition::Requested);
  assert(!expirePendingCover(timeoutCover, 15099));
  assert(expirePendingCover(timeoutCover, 15100));
  assert(timeoutCover.status == CoverStatus::Failed);
  assert(updateCoverFetch(timeoutCover, stableCover, 16000) == CoverTransition::Unchanged);
  assert(markCoverFetchComplete(timeoutCover, stableCover, true) == CoverTransition::Available);
  assert(updateCoverFetch(timeoutCover, stableCover, 17000) == CoverTransition::Unchanged);
  assert(timeoutCover.rendered);

  AppState state;

  static_assert(protocol::kVersion == 1);
  static_assert(protocol::kDefaultPort == 8765);
  assert(std::strcmp(protocol::kWebSocketPath, "/ws") == 0);
  assert(std::strcmp(protocol::kDeviceRestart, "device.restart") == 0);
  assert(std::strcmp(protocol::kDeviceFactoryReset, "device.factoryReset") == 0);
  assert(std::strcmp(protocol::kDeviceSettings, "device.settings") == 0);
  assert(std::strcmp(protocol::kShortcutsReport, "shortcuts.report") == 0);
  assert(std::strcmp(protocol::kMdnsService, "streamdeck") == 0);
  assert(std::strcmp(protocol::kMdnsProto, "tcp") == 0);

  const auto mdnsEndpoint = protocol::selectAgentEndpoint(
      "192.168.1.10", protocol::kDefaultPort, "192.168.1.50", 9001);
  assert(mdnsEndpoint.valid);
  assert(mdnsEndpoint.source == protocol::AgentEndpointSource::Mdns);
  assert(std::strcmp(mdnsEndpoint.host, "192.168.1.50") == 0);
  assert(mdnsEndpoint.port == 9001);

  const auto fallbackEndpoint = protocol::selectAgentEndpoint(
      "192.168.1.10", protocol::kDefaultPort, "", 0);
  assert(fallbackEndpoint.valid);
  assert(fallbackEndpoint.source == protocol::AgentEndpointSource::Manual);
  assert(std::strcmp(fallbackEndpoint.host, "192.168.1.10") == 0);
  assert(fallbackEndpoint.port == protocol::kDefaultPort);

  const auto zeroPortEndpoint = protocol::selectAgentEndpoint(
      "192.168.1.10", protocol::kDefaultPort, "192.168.1.50", 0);
  assert(zeroPortEndpoint.valid);
  assert(zeroPortEndpoint.source == protocol::AgentEndpointSource::Manual);
  assert(std::strcmp(zeroPortEndpoint.host, "192.168.1.10") == 0);
  assert(zeroPortEndpoint.port == protocol::kDefaultPort);

  assert(protocol::isMediaIntent(IntentType::Previous));
  assert(protocol::isMediaIntent(IntentType::TogglePlayback));
  assert(protocol::isMediaIntent(IntentType::Next));
  assert(!protocol::isMediaIntent(IntentType::SetVolume));
  assert(protocol::isDeviceControlIntent(IntentType::RestartDevice));
  assert(protocol::isDeviceControlIntent(IntentType::FactoryReset));
  assert(!protocol::shouldSendHeartbeat(false, false));
  assert(!protocol::shouldSendHeartbeat(true, false));
  assert(!protocol::shouldSendHeartbeat(false, true));
  assert(protocol::shouldSendHeartbeat(true, true));
  assert(std::strcmp(protocol::mediaCommandFor(IntentType::Previous), "previous") == 0);
  assert(std::strcmp(protocol::mediaCommandFor(IntentType::TogglePlayback), "playPause") == 0);
  assert(std::strcmp(protocol::mediaCommandFor(IntentType::Next), "next") == 0);
  const char* longShortcutId = "launcher.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
  assert(std::strlen(longShortcutId) == kMaxShortcutIdLength);
  std::strncpy(state.shortcuts.ids[0], longShortcutId, sizeof(state.shortcuts.ids[0]) - 1);
  assert(std::strcmp(state.shortcuts.ids[0], longShortcutId) == 0);
  char shortcutJson[192]{};
  assert(protocol::makeShortcutExecuteJson("shortcut-1", state.shortcuts.ids[0], shortcutJson, sizeof(shortcutJson)));
  assert(std::strstr(shortcutJson, longShortcutId) != nullptr);

  assert(isValidCoverId("0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789ABCDEF"));
  assert(!isValidCoverId("0123456789abcdef"));
  assert(!isValidCoverId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg"));
  char path[96]{};
  assert(makeCoverRequestPath("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", path, sizeof(path)));
  assert(std::strcmp(path, "/api/covers/0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.jpg") == 0);
  CoverFetchState cover;
  assert(updateCoverFetch(cover, "not-hex") == CoverTransition::Cleared);
  assert(cover.status == CoverStatus::NoCover);
  assert(updateCoverFetch(cover, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef") == CoverTransition::Requested);
  assert(cover.status == CoverStatus::Pending);
  assert(updateCoverFetch(cover, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef") == CoverTransition::Unchanged);
  assert(markCoverFetchComplete(cover, "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210", true) == CoverTransition::Stale);
  assert(cover.status == CoverStatus::Pending);
  assert(markCoverFetchComplete(cover, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true, false) == CoverTransition::Failed);
  assert(!cover.rendered);
  assert(updateCoverFetch(cover, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef") == CoverTransition::Unchanged);
  assert(markCoverFetchComplete(cover, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true, true) == CoverTransition::Available);
  assert(cover.status == CoverStatus::Available);
  assert(cover.rendered);
  assert(kMaxCoverBytes == 512U * 1024U);
  uint16_t pixels[] = {0x1234, 0xABCD, 0x00F8};
  convertDisplayOrderRgb565ToLvgl(pixels, 3);
  assert(pixels[0] == 0x3412);
  assert(pixels[1] == 0xCDAB);
  assert(pixels[2] == 0xF800);
  uint16_t rgbBands[] = {0x00F8, 0xE007, 0x1F00};
  convertDisplayOrderRgb565ToLvgl(rgbBands, 3);
  assert(rgbBands[0] == 0xF800);
  assert(rgbBands[1] == 0x07E0);
  assert(rgbBands[2] == 0x001F);
  assert(updateCoverFetch(cover, "") == CoverTransition::Cleared);
  assert(cover.status == CoverStatus::NoCover);

  AppState defaultState;
  assert(std::strcmp(defaultState.shortcuts.ids[0], "") == 0);
  assert(std::strcmp(defaultState.shortcuts.names[0], "No shortcut") == 0);
  ShortcutState colors;
  const char colored[] = R"({"shortcuts":[{"id":"color-tile","label":"Color","color":"#0A84FF"}]})";
  protocol::applyShortcutsUpdateJson(colors, colored, sizeof(colored) - 1);
  assert(std::strcmp(colors.colors[0], "#0A84FF") == 0);
  const char uncolored[] = R"({"shortcuts":[{"id":"color-tile","label":"Color"}]})";
  protocol::applyShortcutsUpdateJson(colors, uncolored, sizeof(uncolored) - 1);
  assert(colors.colors[0][0] == '\0');

  ShortcutState shortcutState;
  std::strcpy(shortcutState.names[0], "Stale shortcut");
  std::strcpy(shortcutState.ids[0], "stale-id");
  const char* shortenedUpdate = "{\"protocol\":1,\"type\":\"shortcuts.update\",\"data\":{\"shortcuts\":[{\"id\":\"new-id\",\"label\":\"New Label\"}]}}";
  protocol::applyShortcutsUpdateJson(shortcutState, shortenedUpdate, std::strlen(shortenedUpdate));
  assert(std::strcmp(shortcutState.ids[0], "new-id") == 0);
  assert(std::strcmp(shortcutState.names[0], "New Label") == 0);
  assert(std::strcmp(shortcutState.icons[0], "") == 0);
  assert(std::strcmp(shortcutState.displayModes[0], "label") == 0);
  assert(std::strcmp(shortcutState.ids[1], "") == 0);
  assert(std::strcmp(shortcutState.names[1], "No shortcut") == 0);
  const char* iconUpdate = "{\"protocol\":1,\"type\":\"shortcuts.update\",\"data\":{\"shortcuts\":[{\"id\":\"polish\",\"label\":\"Zażółć gęślą\",\"icon\":\"play\",\"displayMode\":\"both\"},{\"id\":\"icon-only\",\"label\":\"Ignored\",\"icon\":\"next\",\"displayMode\":\"icon\"},{\"id\":\"bad-mode\",\"label\":\"Fallback\",\"icon\":\"pause\",\"displayMode\":\"bad\"}]}}";
  protocol::applyShortcutsUpdateJson(shortcutState, iconUpdate, std::strlen(iconUpdate));
  assert(std::strcmp(shortcutState.ids[0], "polish") == 0);
  assert(std::strcmp(shortcutState.names[0], "Zażółć gęślą") == 0);
  assert(std::strcmp(shortcutState.icons[0], "play") == 0);
  assert(std::strcmp(shortcutState.displayModes[0], "both") == 0);
  assert(std::strcmp(shortcutState.icons[1], "next") == 0);
  assert(std::strcmp(shortcutState.displayModes[1], "icon") == 0);
  assert(std::strcmp(shortcutState.displayModes[2], "label") == 0);
  const char* emptyUpdate = "{\"protocol\":1,\"type\":\"shortcuts.update\",\"data\":{\"shortcuts\":[]}}";
  protocol::applyShortcutsUpdateJson(shortcutState, emptyUpdate, std::strlen(emptyUpdate));
  assert(std::strcmp(shortcutState.ids[0], "") == 0);
  assert(std::strcmp(shortcutState.names[0], "No shortcut") == 0);

  char reportJson[512]{};
  assert(protocol::makeShortcutsReportJson(shortcutState, reportJson, sizeof(reportJson)));
  assert(std::strcmp(reportJson, "{\"protocol\":1,\"type\":\"shortcuts.report\",\"data\":{\"shortcuts\":[]}}") == 0);

  // Test Mic toggle with spaces/uppercase, label before id, and 7 shortcuts (limit 6)
  ShortcutState micState;
  const char* micAndSevenShortcuts = "{\"protocol\":1,\"type\":\"shortcuts.update\",\"data\":{\"shortcuts\":["
      "{\"label\":\"Mic toggle\",\"id\":\"s-mic\",\"icon\":\"audio\",\"displayMode\":\"icon\"},"
      "{\"id\":\"s-2\",\"label\":\"Spotify Video\",\"icon\":\"play\",\"displayMode\":\"both\"},"
      "{\"id\":\"s-3\",\"label\":\"Discord\",\"icon\":\"audio\",\"displayMode\":\"label\"},"
      "{\"id\":\"s-4\",\"label\":\"Lock\",\"icon\":\"record\",\"displayMode\":\"label\"},"
      "{\"id\":\"s-5\",\"label\":\"Browser\",\"icon\":\"next\",\"displayMode\":\"icon\"},"
      "{\"id\":\"s-6\",\"label\":\"Terminal\",\"icon\":\"prev\",\"displayMode\":\"label\"},"
      "{\"id\":\"s-7-overflow\",\"label\":\"Overflow 7th\",\"icon\":\"audio\",\"displayMode\":\"both\"}"
      "]}}";
  protocol::applyShortcutsUpdateJson(micState, micAndSevenShortcuts, std::strlen(micAndSevenShortcuts));
  assert(std::strcmp(micState.ids[0], "s-mic") == 0);
  assert(std::strcmp(micState.names[0], "Mic toggle") == 0);
  assert(std::strcmp(micState.icons[0], "audio") == 0);
  assert(std::strcmp(micState.displayModes[0], "icon") == 0);
  assert(std::strcmp(micState.ids[1], "s-2") == 0);
  assert(std::strcmp(micState.names[1], "Spotify Video") == 0);
  assert(std::strcmp(micState.ids[5], "s-6") == 0);
  assert(std::strcmp(micState.names[5], "Terminal") == 0);
  std::strcpy(shortcutState.names[0], "Clip \"A\"");
  std::strcpy(shortcutState.ids[0], "clip-a");
  std::strcpy(shortcutState.names[1], "Ignored");
  shortcutState.ids[1][0] = '\0';
  std::strcpy(shortcutState.names[2], "Mute\\Mic");
  std::strcpy(shortcutState.ids[2], "mute-mic");
  assert(protocol::makeShortcutsReportJson(shortcutState, reportJson, sizeof(reportJson)));
  assert(std::strstr(reportJson, "\"type\":\"shortcuts.report\"") != nullptr);
  assert(std::strstr(reportJson, "\"id\":\"clip-a\",\"label\":\"Clip \\\"A\\\"\",\"actionType\":\"unknown\"") != nullptr);
  assert(std::strstr(reportJson, "Ignored") == nullptr);
  assert(std::strstr(reportJson, "\"id\":\"mute-mic\",\"label\":\"Mute\\\\Mic\",\"actionType\":\"unknown\"") != nullptr);
  assert(!protocol::makeShortcutsReportJson(shortcutState, reportJson, 24));

  ShortcutState worstCaseReport;
  for (std::size_t slot = 0; slot < protocol::kMaxShortcutsPerReport; ++slot) {
    for (std::size_t i = 0; i < kMaxShortcutIdLength; ++i) {
      worstCaseReport.ids[slot][i] = (i % 2 == 0) ? '\\' : '"';
    }
    worstCaseReport.ids[slot][kMaxShortcutIdLength] = '\0';
    for (std::size_t i = 0; i < sizeof(worstCaseReport.names[slot]) - 1; ++i) {
      worstCaseReport.names[slot][i] = '"';
    }
    worstCaseReport.names[slot][sizeof(worstCaseReport.names[slot]) - 1] = '\0';
  }
  char worstCaseJson[protocol::kShortcutsReportJsonCapacity]{};
  assert(protocol::makeShortcutsReportJson(worstCaseReport, worstCaseJson, sizeof(worstCaseJson)));
  assert(std::strlen(worstCaseJson) + 1 == protocol::kShortcutsReportJsonCapacity);
  assert(std::strstr(worstCaseJson, "\\\\\\\"") != nullptr);
  assert(!protocol::makeShortcutsReportJson(worstCaseReport, worstCaseJson, protocol::kShortcutsReportJsonCapacity - 1));

  AppearanceState light;
  light.theme = 1;
  normalizeAppearance(light);
  assert(light.background == 0xF3F5F7);
  assert(light.accent == 0x276EF1);
  light.theme = 0;
  normalizeAppearance(light);
  assert(light.background == 0x101216);
  assert(light.accent == 0x7EAFE3);
}
