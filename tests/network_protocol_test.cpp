#include <cassert>
#include <cstring>
#include <string>

#include "network/agent_discovery.h"
#include "network/agent_protocol.h"
#include "network/cover_art.h"
#include "network/pc_metrics.h"
#include "ui/app_state.h"
#include "storage/cover_stream.h"

int main() {
  using namespace streamdeck;
  {
    storage::CoverStreamValidation stream;
    const uint8_t first[] = {0xff};
    const uint8_t middle[] = {0xd8, 0x10, 0xff};
    const uint8_t last[] = {0xd9};
    assert(stream.append(first, sizeof(first)));
    assert(stream.append(middle, sizeof(middle)));
    assert(!stream.complete(4));
    assert(stream.append(last, sizeof(last)));
    assert(stream.complete(5));
    assert(!stream.complete(6));
    assert(!stream.append(nullptr, 1));
    assert(!stream.append(first, storage::kMaxCoverBytes));
    stream = {};
    assert(stream.append(middle, sizeof(middle)));
    assert(!stream.complete(sizeof(middle)));
  }
  {
    CoverFetchState recovery;
    const char* id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    updateCoverFetch(recovery, id, UINT32_MAX - 100);
    uint32_t now = UINT32_MAX - 99;
    for (unsigned retry = 0; retry < 3; ++retry) {
      markCoverFetchComplete(recovery, id, false);
      assert(updateCoverFetch(recovery, id, now) == CoverTransition::Unchanged);
      now += (1000U << retry);
      assert(updateCoverFetch(recovery, id, now - 1) == CoverTransition::Unchanged);
      assert(updateCoverFetch(recovery, id, now) == CoverTransition::Requested);
    }
    markCoverFetchComplete(recovery, id, false);
    assert(updateCoverFetch(recovery, id, now + 60000) == CoverTransition::Unchanged);
    assert(recovery.retries == 3);
  }
  {
    SystemState metrics;
    const char sample[] = R"({"protocol":1,"type":"system.stats","data":{"cpuPercent":33.6,"memoryPercent":62,"memoryUsedBytes":10737418240,"memoryTotalBytes":17179869184,"gpuPercent":91,"tempCelsius":77}})";
    protocol::applyPcMetricsJson(metrics, sample, sizeof(sample) - 1, UINT32_MAX - 99U);
    assert(metrics.cpuAvailable && metrics.cpuPercent == 34);
    assert(metrics.ramAvailable && metrics.ramPercent == 62);
    assert(metrics.ramCapacityAvailable && metrics.ramUsedMb == 10240 && metrics.ramTotalMb == 16384);
    assert(pcMetricsFresh(metrics, true, UINT32_MAX - 99U));
    assert(pcMetricsFresh(metrics, true, 4899));
    assert(!pcMetricsFresh(metrics, true, 4900));
    assert(!pcMetricsFresh(metrics, false, 0));
    const char unavailable[] = R"({"cpuPercent":null,"memoryPercent":null,"memoryUsedBytes":null,"memoryTotalBytes":null})";
    protocol::applyPcMetricsJson(metrics, unavailable, sizeof(unavailable) - 1, 5000);
    assert(!metrics.cpuAvailable && !metrics.ramAvailable && !metrics.ramCapacityAvailable);
    assert(metrics.pcStatsReceived && pcMetricsFresh(metrics, true, 5000)); // Fresh, but unavailable.
    const char zero[] = R"({"cpuPercent":0,"memoryPercent":0,"memoryUsedBytes":0,"memoryTotalBytes":17179869184})";
    protocol::applyPcMetricsJson(metrics, zero, sizeof(zero) - 1, 10000);
    assert(metrics.cpuAvailable && metrics.cpuPercent == 0 && metrics.ramAvailable && metrics.ramPercent == 0);
    assert(metrics.ramCapacityAvailable && metrics.ramUsedMb == 0 && pcMetricsFresh(metrics, true, 10000));
    const char invalid[] = R"({"cpuPercent":101,"memoryPercent":-1,"memoryUsedBytes":18446744073709551616,"memoryTotalBytes":17179869184})";
    protocol::applyPcMetricsJson(metrics, invalid, sizeof(invalid) - 1, 10001);
    assert(metrics.pcStatsReceived && !metrics.ramCapacityAvailable);
    const char partial[] = R"({"cpuPercent":12.4,"memoryUsedBytes":2000,"memoryTotalBytes":1000})";
    protocol::applyPcMetricsJson(metrics, partial, sizeof(partial) - 1, 10002);
    assert(metrics.cpuAvailable && metrics.cpuPercent == 12 && !metrics.ramAvailable && !metrics.ramCapacityAvailable);
    uint64_t bytes = 0;
    const char maxBytes[] = R"({"n":18446744073709551615})";
    assert(protocol::pcBytes(maxBytes, sizeof(maxBytes) - 1, "n", bytes) && bytes == UINT64_MAX);
    for (const char* number : {"\"42\"", "true", "0x10", "1.2", "-1"}) {
      std::string json = std::string("{\"n\":") + number + "}";
      assert(!protocol::pcBytes(json.c_str(), json.size(), "n", bytes));
    }
  }
  {
    protocol::RetryTimer timer;
    assert(timer.ready(0));
    assert(timer.ready(UINT32_MAX));
    for (uint32_t interval : {2000U, 10000U, 30000U}) {
      for (uint32_t start : {0U, 1000U, UINT32_MAX - 999U, UINT32_MAX - interval + 1U}) {
        timer.schedule(start, interval);
        assert(!timer.ready(start));
        assert(!timer.ready(start + interval - 1U));
        assert(timer.ready(start + interval));
        assert(timer.ready(start + interval + 1U));
        timer.reset();
        assert(timer.ready(start));
      }
    }
  }
  {
    // Model appendCoverTemp's reference counter while streaming an odd-sized JPEG.
    std::size_t received = 0;
    std::size_t fileBytes = 0;
    for (std::size_t chunk : {512U, 512U, 1U}) {
      assert(appendCountedCoverChunk(received, 1025, chunk, [&]() {
        fileBytes += chunk;
        received += chunk;
        return true;
      }));
      assert(received == fileBytes);
    }
    assert(received == 1025);
    bool called = false;
    auto unexpectedWrite = [&]() { called = true; return true; };
    assert(!appendCountedCoverChunk(received, 1025, 1, unexpectedWrite));
    assert(!appendCountedCoverChunk(received, 1025, 0, unexpectedWrite));
    assert(!appendCountedCoverChunk(received, 1000, 1, unexpectedWrite));
    assert(!appendCountedCoverChunk(received, kMaxCoverBytes + 1, 1, unexpectedWrite));
    assert(!called);
    received = 0;
    assert(!appendCountedCoverChunk(received, 1, 1, []() { return false; }));
    assert(received == 0);
  }
  {
    const char* first = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const char* second = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    CoverFetchState transfer;
    assert(!acceptsCoverTransfer(transfer, first));
    updateCoverFetch(transfer, first, 100);
    assert(acceptsCoverTransfer(transfer, first));
    updateCoverFetch(transfer, second, 200);
    assert(!acceptsCoverTransfer(transfer, first));
    assert(!acceptsCoverTransfer(transfer, nullptr));
    assert(!acceptsCoverTransfer(transfer, "invalid"));
    assert(acceptsCoverTransfer(transfer, second));
    assert(!expirePendingCover(transfer, 15199));
    assert(expirePendingCover(transfer, 15200));
    assert(!acceptsCoverTransfer(transfer, second));
    updateCoverFetch(transfer, first, 16000);
    markCoverFetchComplete(transfer, first, true);
    assert(!acceptsCoverTransfer(transfer, first));
    updateCoverFetch(transfer, "", 17000);
    assert(!acceptsCoverTransfer(transfer, first));
  }
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
  assert(updateCoverFetch(timeoutCover, stableCover, 15100) == CoverTransition::Unchanged);
  assert(updateCoverFetch(timeoutCover, stableCover, 16100) == CoverTransition::Requested);
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

  // Active profiles retain blank Deck Tile positions, but those placeholders
  // must never become executable Shortcut IDs or appear in shortcuts.report.
  ShortcutState sparseProfile;
  const char* sparseUpdate = "{\"protocol\":1,\"type\":\"shortcuts.update\",\"data\":{\"shortcuts\":["
      "{\"id\":\"__empty_slot_0\",\"label\":\"No shortcut\",\"icon\":\"empty\"},"
      "{\"id\":\"valid-action\",\"label\":\"Action\",\"icon\":\"app\"},"
      "{\"id\":\"__empty_slot_2\",\"label\":\"No shortcut\",\"icon\":\"empty\"}]}}";
  protocol::applyShortcutsUpdateJson(sparseProfile, sparseUpdate, std::strlen(sparseUpdate));
  assert(sparseProfile.ids[0][0] == '\0');
  assert(std::strcmp(sparseProfile.ids[1], "valid-action") == 0);
  assert(sparseProfile.ids[2][0] == '\0');
  assert(protocol::makeShortcutsReportJson(sparseProfile, reportJson, sizeof(reportJson)));
  assert(std::strstr(reportJson, "__empty_slot_") == nullptr);
  assert(std::strstr(reportJson, "valid-action") != nullptr);
  // Older NVS contents may still contain placeholders until the next sync.
  std::strcpy(sparseProfile.ids[0], "__empty_slot_0");
  std::strcpy(sparseProfile.icons[0], "empty");
  assert(protocol::makeShortcutsReportJson(sparseProfile, reportJson, sizeof(reportJson)));
  assert(std::strstr(reportJson, "__empty_slot_") == nullptr);

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
