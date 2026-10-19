// Render the production widget tree and fonts through real LVGL, without ESP32 hardware.
#include <cstdio>
#include <cstring>
#include <cstdint>
#include "../firmware/src/ui/ui_manager.cpp"

namespace {
uint16_t frame[320 * 240]{};
uint16_t draw[320 * 20]{};

void previewFlush(lv_display_t* display, const lv_area_t* area, uint8_t* pixels) {
  const auto* source = reinterpret_cast<const uint16_t*>(pixels);
  for (int y = area->y1; y <= area->y2; ++y)
    for (int x = area->x1; x <= area->x2; ++x) frame[y * 320 + x] = *source++;
  lv_display_flush_ready(display);
}

bool capture(const char* directory, const char* theme, const char* screen) {
  lv_refr_now(nullptr);
  char path[512];
  std::snprintf(path, sizeof(path), "%s/%s-%s.bmp", directory, theme, screen);
  FILE* output = std::fopen(path, "wb");
  if (!output) return false;
  uint8_t header[54] = {'B', 'M'};
  auto field = [&](int offset, uint32_t value) {
    for (int i = 0; i < 4; ++i) header[offset + i] = static_cast<uint8_t>(value >> (8 * i));
  };
  field(2, 54 + 320 * 240 * 3);
  field(10, 54); field(14, 40); field(18, 320); field(22, 240);
  header[26] = 1; header[28] = 24;
  bool ok = std::fwrite(header, 1, sizeof(header), output) == sizeof(header);
  uint8_t row[320 * 3];
  for (int y = 239; y >= 0 && ok; --y) {
    for (int x = 0; x < 320; ++x) {
      const uint16_t pixel = frame[y * 320 + x];
      row[x * 3] = static_cast<uint8_t>((pixel & 31) * 255 / 31);
      row[x * 3 + 1] = static_cast<uint8_t>(((pixel >> 5) & 63) * 255 / 63);
      row[x * 3 + 2] = static_cast<uint8_t>((pixel >> 11) * 255 / 31);
    }
    ok = std::fwrite(row, 1, sizeof(row), output) == sizeof(row);
  }
  return std::fclose(output) == 0 && ok;
}
}

int main(int argc, char** argv) {
  if (argc != 2) return 2;
  lv_init();
  lv_display_t* display = lv_display_create(320, 240);
  lv_display_set_color_format(display, LV_COLOR_FORMAT_RGB565);
  lv_display_set_buffers(display, draw, nullptr, sizeof(draw), LV_DISPLAY_RENDER_MODE_PARTIAL);
  lv_display_set_flush_cb(display, previewFlush);
  streamdeck::AppState state;
  streamdeck::IntentQueue intents;
  state.connection.agentConnected = state.connection.online = state.connection.wifiConnected = true;
  std::strcpy(state.connection.agentHost, "usb");
  std::strcpy(state.connection.ssid, "Home Wi-Fi");
  std::strcpy(state.connection.deviceIp, "192.168.1.84");
  state.storage.mounted = state.storage.ready = true;
  state.storage.freeMb = 31784;
  state.device.uptimeSeconds = 7324;
  state.ui.calibrationValid = true;
  state.appearance.brightness = 60;
  state.ota.enabled = true;
  std::strcpy(state.ota.status, "OTA ready");
  streamdeck::uiBegin(state, intents);
  for (int light = 0; light <= 1; ++light) {
    state.appearance.theme = light;
    state.appearance.background = light ? 0xF3F5F7 : 0x101216;
    state.appearance.accent = light ? 0x276EF1 : 0x7EAFE3;
    const char* theme = light ? "light" : "dark";
    settingsClicked(nullptr);
    if (!capture(argv[1], theme, "display")) return 1;
    lv_obj_send_event(settingsTabs[1], LV_EVENT_CLICKED, nullptr);
    if (!capture(argv[1], theme, "touch")) return 1;
    lv_obj_send_event(settingsTabs[2], LV_EVENT_CLICKED, nullptr);
    if (!capture(argv[1], theme, "device")) return 1;
    settingsDetailsClicked(nullptr);
    if (!capture(argv[1], theme, "status")) return 1;
    settingsDetailsClicked(nullptr);
    showRestartDialog(nullptr);
    if (!capture(argv[1], theme, "restart")) return 1;
    showFactoryResetDialog(nullptr);
    if (!capture(argv[1], theme, "reset")) return 1;
    hideFactoryResetDialog(nullptr);
    state.system.cpuAvailable = state.system.ramAvailable = state.system.ramCapacityAvailable = true;
    state.system.pcStatsReceived = true;
    state.system.pcStatsAtMs = lv_tick_get();
    state.system.cpuPercent = 34;
    state.system.ramPercent = 62;
    state.system.ramUsedMb = 10240;
    state.system.ramTotalMb = 16384;
    streamdeck::uiSetPage(streamdeck::Page::Pc);
    if (!capture(argv[1], theme, "pc-live")) return 1;
    state.system.pcStatsReceived = false;
    streamdeck::uiRefresh(state);
    if (!capture(argv[1], theme, "pc-waiting")) return 1;
    state.connection.agentConnected = false;
    streamdeck::uiRefresh(state);
    if (!capture(argv[1], theme, "pc-offline")) return 1;
    state.connection.agentConnected = true;
    std::strcpy(state.media.title, "Accent check");
    std::strcpy(state.media.artist, "Windows media");
    state.media.positionMs = 75000;
    state.media.durationMs = 210000;
    streamdeck::uiSetPage(streamdeck::Page::Media);
    if (!capture(argv[1], theme, "media")) return 1;
    state.appearance.accent = light ? 0x28724F : 0xD85D93;
    streamdeck::uiRefresh(state);
    if (!capture(argv[1], theme, "media-accent")) return 1;
  }
  lv_deinit();
  return 0;
}
