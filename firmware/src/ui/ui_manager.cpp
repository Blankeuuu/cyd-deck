#include "ui/ui_manager.h"

#include <cstdio>
#include <cstring>
#include <lvgl.h>

namespace {
using namespace streamdeck;

AppState* statePtr = nullptr;
IntentQueue* queuePtr = nullptr;

lv_obj_t* root = nullptr;
constexpr int kPageCount = 4;
lv_obj_t* pages[kPageCount]{};
lv_obj_t* dots[kPageCount]{};
lv_obj_t* buttons[16]{};
lv_obj_t* labels[64]{};
uint8_t buttonCount = 0;
uint8_t labelCount = 0;
uint32_t appliedBackground = 0xFFFFFFFF;
uint32_t appliedAccent = 0xFFFFFFFF;
uint8_t appliedTheme = 0xFF;
uint8_t appliedButtonStyle = 0xFF;
uint32_t appliedDotColors[kPageCount] = {0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF};
lv_obj_t* coverPanel = nullptr;
lv_obj_t* overlay = nullptr;
lv_obj_t* resetDialog = nullptr;
lv_obj_t* mediaTitle = nullptr;
lv_obj_t* mediaArtist = nullptr;
lv_obj_t* mediaStatus = nullptr;
lv_obj_t* mediaTimeLeft = nullptr;
lv_obj_t* mediaTimeRight = nullptr;
lv_obj_t* coverBadge = nullptr;
lv_obj_t* coverStatus = nullptr;
lv_obj_t* coverImage = nullptr;
lv_obj_t* playButtonLabel = nullptr;
lv_obj_t* mediaProgress = nullptr;
lv_obj_t* volumeButtonLabel = nullptr;
lv_obj_t* overlayStatus = nullptr;
lv_obj_t* overlayVolumePercent = nullptr;
lv_obj_t* shortcutLabels[6]{};
lv_obj_t* pcConnection = nullptr;
lv_obj_t* pcAudio = nullptr;
lv_obj_t* heapFree = nullptr;
lv_obj_t* heapMin = nullptr;
lv_obj_t* heapLargest = nullptr;
lv_obj_t* otaStatus = nullptr;
lv_obj_t* infoFw = nullptr;
lv_obj_t* infoWifi = nullptr;
lv_obj_t* infoIp = nullptr;
lv_obj_t* infoPc = nullptr;
lv_obj_t* infoAgent = nullptr;
lv_obj_t* infoSd = nullptr;
lv_obj_t* infoUptime = nullptr;

constexpr uint32_t kBg = 0x101216;
constexpr uint32_t kPanel = 0x1C2026;
constexpr uint32_t kButton = 0x252B33;
constexpr uint32_t kText = 0xF3F5F7;
constexpr uint32_t kMuted = 0xA3ABB7;
constexpr uint32_t kAccent = 0x7EAFE3;
constexpr uint32_t kLightText = 0x101216;
constexpr uint32_t kLightPanel = 0xFFFFFF;
constexpr uint32_t kLightButton = 0xE3E7ED;
constexpr int kScreenWidth = 320;
constexpr int kScreenHeight = 240;
constexpr uint16_t kFactoryResetLongPressMs = 3000;

void configureLongPressTiming() {
  for (lv_indev_t* indev = lv_indev_get_next(nullptr); indev;
       indev = lv_indev_get_next(indev)) {
    lv_indev_set_long_press_time(indev, kFactoryResetLongPressMs);
  }
}

void disableScrolling(lv_obj_t* object) {
  lv_obj_clear_flag(object, LV_OBJ_FLAG_SCROLLABLE);
  lv_obj_set_scrollbar_mode(object, LV_SCROLLBAR_MODE_OFF);
}

void setStaticBounds(lv_obj_t* object, int x, int y, int w, int h) {
  lv_obj_set_pos(object, x, y);
  lv_obj_set_size(object, w, h);
  lv_obj_set_style_pad_all(object, 0, 0);
  disableScrolling(object);
}

lv_obj_t* makeLabel(lv_obj_t* parent, const char* text, int x, int y, int w,
                    int h, uint32_t color, uint32_t size) {
  lv_obj_t* label = lv_label_create(parent);
  if (labelCount < 64) labels[labelCount++] = label;
  lv_label_set_text(label, text);
  lv_label_set_long_mode(label, LV_LABEL_LONG_DOT);
  setStaticBounds(label, x, y, w, h);
  lv_obj_set_style_text_color(label, lv_color_hex(color), 0);
  lv_obj_set_style_text_font(label,
      size >= 20 ? &lv_font_montserrat_20 :
      (size >= 16 ? &lv_font_montserrat_16 : &lv_font_montserrat_14), 0);
  return label;
}

uint32_t textColor() {
  return statePtr && statePtr->appearance.theme == 1 ? kLightText : kText;
}

void setLabelText(lv_obj_t* label, const char* text) {
  if (!label) return;
  const char* safe = text ? text : "";
  const char* current = lv_label_get_text(label);
  if (current && std::strcmp(current, safe) == 0) return;
  lv_label_set_text(label, safe);
}

void applyDynamicTextColors() {
  for (uint8_t i = 0; i < labelCount; ++i) {
    const uint32_t color = labels[i] == coverBadge
        ? (statePtr ? statePtr->appearance.accent : kAccent)
        : textColor();
    lv_obj_set_style_text_color(labels[i], lv_color_hex(color), 0);
  }
  for (uint8_t i = 0; i < buttonCount; ++i) {
    lv_obj_t* label = lv_obj_get_child(buttons[i], 0);
    if (label) lv_obj_set_style_text_color(label, lv_color_hex(textColor()), 0);
  }
}

void styleButton(lv_obj_t* button) {
  const uint32_t color = statePtr ? statePtr->appearance.accent : kButton;
  const uint8_t style = statePtr ? statePtr->appearance.buttonStyle : 1;
  const uint32_t flatColor = statePtr && statePtr->appearance.theme == 1
      ? kLightButton : kButton;
  lv_obj_set_style_bg_color(button, lv_color_hex(style == 0 ? flatColor : color), 0);
  lv_obj_set_style_bg_opa(button, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(button, style == 0 ? 2 : (style == 2 ? 18 : 10), 0);
  lv_obj_set_style_border_width(button, 0, 0);
}

void applyAppearanceStyles(const AppState& state) {
  const bool themeChanged = appliedTheme != state.appearance.theme;
  const bool buttonsChanged = themeChanged || appliedAccent != state.appearance.accent
      || appliedButtonStyle != state.appearance.buttonStyle;
  if (appliedBackground != state.appearance.background) {
    lv_obj_set_style_bg_color(root, lv_color_hex(state.appearance.background), 0);
    appliedBackground = state.appearance.background;
  }
  if (themeChanged) {
    const uint32_t panel = state.appearance.theme == 1 ? kLightPanel : kPanel;
    lv_obj_set_style_bg_color(coverPanel, lv_color_hex(panel), 0);
    lv_obj_set_style_bg_color(overlay, lv_color_hex(panel), 0);
    lv_obj_set_style_bg_color(resetDialog, lv_color_hex(panel), 0);
  }
  if (buttonsChanged) {
    for (uint8_t i = 0; i < buttonCount; ++i) styleButton(buttons[i]);
    applyDynamicTextColors();
    appliedAccent = state.appearance.accent;
    appliedTheme = state.appearance.theme;
    appliedButtonStyle = state.appearance.buttonStyle;
  }
}

lv_obj_t* button(lv_obj_t* parent, const char* text, int x, int y, int w, int h,
                 lv_event_cb_t cb, void* data, lv_obj_t** labelOut = nullptr) {
  lv_obj_t* b = lv_btn_create(parent);
  setStaticBounds(b, x, y, w, h);
  styleButton(b);
  if (buttonCount < 16) buttons[buttonCount++] = b;
  if (cb) lv_obj_add_event_cb(b, cb, LV_EVENT_CLICKED, data);
  lv_obj_t* label = lv_label_create(b);
  lv_label_set_text(label, text);
  lv_label_set_long_mode(label, LV_LABEL_LONG_DOT);
  lv_obj_set_width(label, w - 12);
  disableScrolling(label);
  lv_obj_set_style_text_color(label, lv_color_hex(textColor()), 0);
  lv_obj_center(label);
  if (labelOut) *labelOut = label;
  return b;
}

void enqueue(lv_event_t* e) {
  queuePtr->push({static_cast<IntentType>(
      reinterpret_cast<intptr_t>(lv_event_get_user_data(e))), 0});
}

void showFactoryResetDialog(lv_event_t*) {
  lv_obj_clear_flag(resetDialog, LV_OBJ_FLAG_HIDDEN);
}

void hideFactoryResetDialog(lv_event_t*) {
  lv_obj_add_flag(resetDialog, LV_OBJ_FLAG_HIDDEN);
}

void confirmFactoryReset(lv_event_t*) {
  lv_obj_add_flag(resetDialog, LV_OBJ_FLAG_HIDDEN);
  queuePtr->push({IntentType::FactoryReset, 0});
}

void shortcut(lv_event_t* e) {
  queuePtr->push({IntentType::RunShortcut,
      static_cast<int16_t>(reinterpret_cast<intptr_t>(lv_event_get_user_data(e)))});
}

int16_t clampVolume(int value) {
  if (value < 0) return 0;
  if (value > 100) return 100;
  return static_cast<int16_t>(value);
}

void volumeStep(lv_event_t* e) {
  const int delta = static_cast<int>(reinterpret_cast<intptr_t>(lv_event_get_user_data(e)));
  queuePtr->push({IntentType::SetVolume,
      clampVolume(static_cast<int>(statePtr->audio.reportedVolume) + delta)});
}

void toggleOverlay(lv_event_t*) {
  statePtr->ui.volumeOpen = !statePtr->ui.volumeOpen;
  if (statePtr->ui.volumeOpen) {
    lv_obj_clear_flag(overlay, LV_OBJ_FLAG_HIDDEN);
  } else {
    lv_obj_add_flag(overlay, LV_OBJ_FLAG_HIDDEN);
  }
  uiRefresh(*statePtr);
}

void formatTime(char* out, size_t size, uint32_t ms) {
  const uint32_t seconds = ms / 1000;
  std::snprintf(out, size, "%lu:%02lu",
      static_cast<unsigned long>(seconds / 60),
      static_cast<unsigned long>(seconds % 60));
}

void buildMediaPage() {
  coverPanel = lv_obj_create(pages[0]);
  setStaticBounds(coverPanel, 12, 30, 140, 140);
  lv_obj_set_style_bg_color(coverPanel, lv_color_hex(kPanel), 0);
  lv_obj_set_style_bg_opa(coverPanel, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(coverPanel, 14, 0);
  lv_obj_set_style_border_width(coverPanel, 0, 0);
  coverBadge = makeLabel(coverPanel, "SD", 45, 42, 58, 30, kAccent, 20);
  coverStatus = makeLabel(coverPanel, "cover cache", 18, 77, 104, 20, kMuted, 14);
  coverImage = lv_image_create(coverPanel);
  setStaticBounds(coverImage, 6, 6, 128, 128);
  lv_obj_add_flag(coverImage, LV_OBJ_FLAG_HIDDEN);

  makeLabel(pages[0], "NOW PLAYING", 12, 7, 180, 18, kMuted, 14);
  mediaTitle = makeLabel(pages[0], "", 166, 34, 142, 28, kText, 20);
  mediaArtist = makeLabel(pages[0], "", 166, 65, 142, 21, kMuted, 14);
  mediaStatus = makeLabel(pages[0], "", 166, 88, 142, 20, kMuted, 14);
  mediaProgress = lv_bar_create(pages[0]);
  setStaticBounds(mediaProgress, 166, 116, 142, 5);
  lv_bar_set_range(mediaProgress, 0, 100);
  mediaTimeLeft = makeLabel(pages[0], "0:00", 166, 126, 60, 17, kMuted, 14);
  mediaTimeRight = makeLabel(pages[0], "0:00", 260, 126, 48, 17, kMuted, 14);

  button(pages[0], LV_SYMBOL_PREV, 164, 147, 43, 39, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::Previous)));
  button(pages[0], LV_SYMBOL_PLAY, 212, 147, 48, 39, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::TogglePlayback)),
      &playButtonLabel);
  button(pages[0], LV_SYMBOL_NEXT, 265, 147, 43, 39, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::Next)));
  button(pages[0], "Volume", 12, 181, 140, 39, toggleOverlay, nullptr,
      &volumeButtonLabel);
  makeLabel(pages[0], "swipe for pages", 166, 193, 145, 20, kMuted, 14);
}

void buildShortcutsPage() {
  makeLabel(pages[1], "SHORTCUTS", 12, 8, 220, 22, kText, 20);
  for (int i = 0; i < 6; ++i) {
    const int x = 12 + (i % 3) * 102;
    const int y = 43 + (i / 3) * 77;
    button(pages[1], "", x, y, 94, 64, shortcut,
        reinterpret_cast<void*>(static_cast<intptr_t>(i)), &shortcutLabels[i]);
  }
}

void buildPcPage() {
  makeLabel(pages[2], "PC STATUS", 12, 8, 220, 22, kText, 20);
  pcConnection = makeLabel(pages[2], "", 12, 43, 292, 22, kMuted, 16);
  pcAudio = makeLabel(pages[2], "", 12, 70, 292, 22, kMuted, 14);
  heapFree = makeLabel(pages[2], "", 12, 107, 292, 20, kMuted, 14);
  heapMin = makeLabel(pages[2], "", 12, 132, 292, 20, kMuted, 14);
  heapLargest = makeLabel(pages[2], "", 12, 155, 292, 20, kMuted, 14);
  otaStatus = makeLabel(pages[2], "", 12, 177, 292, 20, kMuted, 14);
  button(pages[2], "Recalibrate touch", 12, 202, 174, 28, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::Recalibrate)));
}

void buildInfoPage() {
  makeLabel(pages[3], "DEVICE", 12, 8, 175, 22, kText, 20);
  infoFw = makeLabel(pages[3], "", 208, 10, 100, 19, kMuted, 14);
  infoWifi = makeLabel(pages[3], "", 12, 42, 296, 19, kMuted, 14);
  infoIp = makeLabel(pages[3], "", 12, 64, 296, 19, kMuted, 14);
  infoPc = makeLabel(pages[3], "", 12, 86, 296, 19, kMuted, 14);
  infoAgent = makeLabel(pages[3], "", 12, 108, 296, 19, kMuted, 14);
  infoSd = makeLabel(pages[3], "", 12, 130, 296, 19, kMuted, 14);
  infoUptime = makeLabel(pages[3], "", 12, 152, 296, 19, kMuted, 14);
  button(pages[3], "Restart", 12, 190, 112, 34, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::RestartDevice)));
  lv_obj_t* reset = button(pages[3], "Factory Reset", 142, 190, 166, 34,
      nullptr, nullptr);
  lv_obj_add_event_cb(reset, showFactoryResetDialog, LV_EVENT_LONG_PRESSED, nullptr);
}

void buildFactoryResetDialog() {
  resetDialog = lv_obj_create(root);
  setStaticBounds(resetDialog, 26, 34, 268, 172);
  lv_obj_set_style_bg_color(resetDialog, lv_color_hex(kPanel), 0);
  lv_obj_set_style_bg_opa(resetDialog, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(resetDialog, 14, 0);
  lv_obj_set_style_border_width(resetDialog, 0, 0);
  makeLabel(resetDialog, "Factory Reset?", 16, 14, 236, 24, kText, 20);
  makeLabel(resetDialog, "This removes Wi-Fi, pairing,", 16, 50, 236, 18, kMuted, 14);
  makeLabel(resetDialog, "touch calibration and settings.", 16, 70, 236, 18, kMuted, 14);
  button(resetDialog, "CANCEL", 16, 116, 96, 34, hideFactoryResetDialog, nullptr);
  button(resetDialog, "CONFIRM", 132, 116, 120, 34, confirmFactoryReset, nullptr);
  lv_obj_add_flag(resetDialog, LV_OBJ_FLAG_HIDDEN);
}

void buildOverlay() {
  overlay = lv_obj_create(root);
  setStaticBounds(overlay, 45, 65, 230, 112);
  lv_obj_set_style_bg_color(overlay, lv_color_hex(kPanel), 0);
  lv_obj_set_style_bg_opa(overlay, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(overlay, 14, 0);
  lv_obj_set_style_border_width(overlay, 0, 0);
  makeLabel(overlay, "Volume", 15, 12, 190, 22, kText, 16);
  overlayStatus = makeLabel(overlay, "", 15, 38, 200, 18, kMuted, 14);
  button(overlay, "+", 15, 66, 48, 32, volumeStep,
      reinterpret_cast<void*>(static_cast<intptr_t>(5)));
  overlayVolumePercent = makeLabel(overlay, "", 78, 72, 74, 20, kText, 16);
  button(overlay, "-", 167, 66, 48, 32, volumeStep,
      reinterpret_cast<void*>(static_cast<intptr_t>(-5)));
  lv_obj_add_flag(overlay, LV_OBJ_FLAG_HIDDEN);
}

void build() {
  root = lv_scr_act();
  setStaticBounds(root, 0, 0, kScreenWidth, kScreenHeight);
  lv_obj_set_style_bg_color(root, lv_color_hex(statePtr ? statePtr->appearance.background : kBg), 0);
  lv_obj_set_style_bg_opa(root, LV_OPA_COVER, 0);
  for (int n = 0; n < kPageCount; ++n) {
    pages[n] = lv_obj_create(root);
    setStaticBounds(pages[n], 0, 0, kScreenWidth, kScreenHeight);
    lv_obj_set_style_bg_opa(pages[n], LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(pages[n], 0, 0);
    if (n) lv_obj_add_flag(pages[n], LV_OBJ_FLAG_HIDDEN);
  }
  buildMediaPage();
  buildShortcutsPage();
  buildPcPage();
  buildInfoPage();
  for (int i = 0; i < kPageCount; ++i) {
    dots[i] = lv_obj_create(root);
    setStaticBounds(dots[i], 140 + i * 14, 231, 6, 6);
    lv_obj_set_style_radius(dots[i], LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_border_width(dots[i], 0, 0);
  }
  buildOverlay();
  buildFactoryResetDialog();
}

}  // namespace

namespace streamdeck {

void uiBegin(AppState& state, IntentQueue& intents) {
  statePtr = &state;
  queuePtr = &intents;
  configureLongPressTiming();
  build();
  uiRefresh(state);
}

void uiRefresh(AppState& s) {
  char buffer[64];
  const bool mediaOnline = s.connection.online;
  const uint32_t progress = s.media.durationMs
      ? (s.media.positionMs * 100UL) / s.media.durationMs
      : 0;
  const bool hasTrack = s.media.title[0] &&
      std::strcmp(s.media.title, "No track") != 0 &&
      std::strcmp(s.media.title, "Waiting for PC") != 0;

  applyAppearanceStyles(s);

  setLabelText(mediaTitle, hasTrack ? s.media.title : (mediaOnline ? "Ready to play" : "Waiting for PC"));
  if (hasTrack && s.media.album[0]) {
    std::snprintf(buffer, sizeof(buffer), "%s / %s", s.media.artist, s.media.album);
    setLabelText(mediaArtist, buffer);
  } else {
    setLabelText(mediaArtist, hasTrack ? s.media.artist : "");
  }
  setLabelText(mediaStatus,
      mediaOnline ? (s.media.playing ? "Playing" : (hasTrack ? "Paused" : "No media session")) : "Media offline");
  const bool coverVisible = s.media.cover.status == CoverStatus::Available && s.media.cover.rendered;
  setLabelText(coverBadge, coverVisible ? "ART" : LV_SYMBOL_AUDIO);
  if (coverImage) {
    if (coverVisible) lv_obj_clear_flag(coverImage, LV_OBJ_FLAG_HIDDEN);
    else lv_obj_add_flag(coverImage, LV_OBJ_FLAG_HIDDEN);
  }
  const char* coverText = hasTrack ? "No artwork" : (mediaOnline ? "Ready to play" : "Waiting for PC");
  if (s.media.cover.status == CoverStatus::Pending) coverText = "cover loading";
  else if (s.media.cover.status == CoverStatus::Available && s.media.cover.rendered) coverText = "cover ready";
  else if (s.media.cover.status == CoverStatus::Available) coverText = "cover fallback";
  else if (s.media.cover.status == CoverStatus::Failed) coverText = "cover unavailable";
  setLabelText(coverStatus, coverText);
  setLabelText(playButtonLabel, s.media.playing ? LV_SYMBOL_PAUSE : LV_SYMBOL_PLAY);
  lv_bar_set_value(mediaProgress, progress > 100 ? 100 : progress, LV_ANIM_OFF);
  formatTime(buffer, sizeof(buffer), s.media.positionMs);
  setLabelText(mediaTimeLeft, buffer);
  formatTime(buffer, sizeof(buffer), s.media.durationMs);
  setLabelText(mediaTimeRight, buffer);

  std::snprintf(buffer, sizeof(buffer), "%s %u%%", LV_SYMBOL_VOLUME_MAX, s.audio.reportedVolume);
  setLabelText(volumeButtonLabel, buffer);
  std::snprintf(buffer, sizeof(buffer), "%s %u%%",
      s.audio.available ? "Windows volume" : "Queued volume", s.audio.reportedVolume);
  setLabelText(overlayStatus, buffer);
  std::snprintf(buffer, sizeof(buffer), "%u%%", s.audio.reportedVolume);
  setLabelText(overlayVolumePercent, buffer);
  if (s.ui.volumeOpen) lv_obj_clear_flag(overlay, LV_OBJ_FLAG_HIDDEN);
  else lv_obj_add_flag(overlay, LV_OBJ_FLAG_HIDDEN);

  for (int i = 0; i < 6; ++i) {
    setLabelText(shortcutLabels[i], s.shortcuts.names[i]);
  }

  setLabelText(pcConnection, s.connection.online ? "Agent online" : "Agent offline");
  std::snprintf(buffer, sizeof(buffer), "Audio %s  volume %u%%",
      s.audio.available ? "available" : "offline", s.audio.reportedVolume);
  setLabelText(pcAudio, buffer);
  std::snprintf(buffer, sizeof(buffer), "Free heap  %lu",
      static_cast<unsigned long>(s.system.freeHeap));
  setLabelText(heapFree, buffer);
  std::snprintf(buffer, sizeof(buffer), "Min heap   %lu",
      static_cast<unsigned long>(s.system.minHeap));
  setLabelText(heapMin, buffer);
  std::snprintf(buffer, sizeof(buffer), "Largest block %lu",
      static_cast<unsigned long>(s.system.largestBlock));
  setLabelText(heapLargest, buffer);
  if (s.ota.running) {
    std::snprintf(buffer, sizeof(buffer), "%s %u%%", s.ota.status, s.ota.progress);
  } else {
    std::snprintf(buffer, sizeof(buffer), "%s", s.ota.status);
  }
  setLabelText(otaStatus, buffer);

  std::snprintf(buffer, sizeof(buffer), "FW %s", s.device.firmwareVersion);
  setLabelText(infoFw, buffer);
  if (s.connection.wifiConnected) {
    std::snprintf(buffer, sizeof(buffer), "Wi-Fi  %s  %d dBm",
        s.connection.ssid[0] ? s.connection.ssid : "connected",
        static_cast<int>(s.connection.rssi));
  } else {
    std::snprintf(buffer, sizeof(buffer), "Wi-Fi  Disconnected");
  }
  setLabelText(infoWifi, buffer);
  std::snprintf(buffer, sizeof(buffer), "IP      %s",
      s.connection.deviceIp[0] ? s.connection.deviceIp : "Unavailable");
  setLabelText(infoIp, buffer);
  std::snprintf(buffer, sizeof(buffer), "PC      %s",
      s.connection.agentConnected ? "Connected" : "Disconnected");
  setLabelText(infoPc, buffer);
  std::snprintf(buffer, sizeof(buffer), "Agent   %s",
      s.connection.agentHost[0] ? s.connection.agentHost : "Unavailable");
  setLabelText(infoAgent, buffer);
  if (s.storage.mounted) {
    std::snprintf(buffer, sizeof(buffer), "SD      %s / %lu MB free",
        s.storage.ready ? "OK" : "Mounted",
        static_cast<unsigned long>(s.storage.freeMb));
  } else {
    std::snprintf(buffer, sizeof(buffer), "SD      Unavailable");
  }
  setLabelText(infoSd, buffer);
  const uint32_t hours = s.device.uptimeSeconds / 3600UL;
  const uint32_t minutes = (s.device.uptimeSeconds / 60UL) % 60UL;
  const uint32_t seconds = s.device.uptimeSeconds % 60UL;
  std::snprintf(buffer, sizeof(buffer), "Uptime  %02lu:%02lu:%02lu",
      static_cast<unsigned long>(hours),
      static_cast<unsigned long>(minutes),
      static_cast<unsigned long>(seconds));
  setLabelText(infoUptime, buffer);

  for (uint8_t i = 0; i < kPageCount; ++i) {
    const Page page = s.appearance.pageOrder[i];
    const bool current = s.ui.page == page;
    const bool visible = s.appearance.pageVisible[pageIndex(page)];
    const uint32_t color = current ? s.appearance.accent : (visible ? 0x555C66 : 0x2B3038);
    if (appliedDotColors[i] != color) {
      lv_obj_set_style_bg_color(dots[i], lv_color_hex(color), 0);
      appliedDotColors[i] = color;
    }
  }
}

void uiSetCoverImage(const lv_image_dsc_t* image) {
  if (!coverImage) return;
  lv_image_set_src(coverImage, image);
}

void uiSetPage(Page page) {
  if (!statePtr) return;
  if (pageIndex(page) >= kPageCount || !statePtr->appearance.pageVisible[pageIndex(page)]) {
    page = Page::Info;
  }
  statePtr->ui.page = page;
  for (int n = 0; n < kPageCount; ++n) lv_obj_add_flag(pages[n], LV_OBJ_FLAG_HIDDEN);
  lv_obj_clear_flag(pages[static_cast<int>(page)], LV_OBJ_FLAG_HIDDEN);
  uiRefresh(*statePtr);
}

void uiNavigate(int8_t delta) {
  if (!statePtr || delta == 0) return;
  normalizeAppearance(statePtr->appearance);
  uint8_t currentSlot = 0;
  for (uint8_t i = 0; i < kPageCount; ++i) {
    if (statePtr->appearance.pageOrder[i] == statePtr->ui.page) {
      currentSlot = i;
      break;
    }
  }

  int8_t direction = delta > 0 ? 1 : -1;
  for (uint8_t step = 0; step < kPageCount; ++step) {
    currentSlot = static_cast<uint8_t>((currentSlot + direction + kPageCount) % kPageCount);
    Page candidate = statePtr->appearance.pageOrder[currentSlot];
    if (statePtr->appearance.pageVisible[pageIndex(candidate)]) {
      uiSetPage(candidate);
      return;
    }
  }

  uiSetPage(Page::Info);
}

}  // namespace streamdeck
