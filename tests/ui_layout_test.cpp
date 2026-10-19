#include <cassert>
#include <cstring>
#include <vector>

#include "lvgl.h"
#include "ui/app_state.h"
#include "ui/ui_manager.h"

#include "../firmware/src/ui/ui_manager.cpp"

namespace {

void assertNoScrollbars(const lv_obj_t* object) {
  assert((object->flags & LV_OBJ_FLAG_SCROLLABLE) == 0);
  assert(object->scrollbar_mode == LV_SCROLLBAR_MODE_OFF);
  for (const lv_obj_t* child : object->children) assertNoScrollbars(child);
}

void assertInsideParent(const lv_obj_t* object) {
  for (const lv_obj_t* child : object->children) {
    assert(child->w >= 0);
    assert(child->h >= 0);
    assert(child->x >= 0);
    assert(child->y >= 0);
    assert(child->x + child->w <= object->w);
    assert(child->y + child->h <= object->h);
    assertInsideParent(child);
  }
}

lv_obj_t* findLabel(lv_obj_t* object, const char* text) {
  if (object->type == lv_obj_t::Type::Label && object->text == text) return object;
  for (lv_obj_t* child : object->children) {
    if (lv_obj_t* found = findLabel(child, text)) return found;
  }
  return nullptr;
}

lv_obj_t* findButtonByText(lv_obj_t* object, const char* text) {
  if (object->type == lv_obj_t::Type::Button) {
    for (lv_obj_t* child : object->children) {
      if (child->type == lv_obj_t::Type::Label && child->text == text) return object;
    }
  }
  for (lv_obj_t* child : object->children) {
    if (lv_obj_t* found = findButtonByText(child, text)) return found;
  }
  return nullptr;
}

void collectButtons(lv_obj_t* object, std::vector<lv_obj_t*>& out) {
  if (object->type == lv_obj_t::Type::Button) out.push_back(object);
  for (lv_obj_t* child : object->children) collectButtons(child, out);
}

void assertVolumeIntent(streamdeck::IntentQueue& intents, int16_t value) {
  streamdeck::Intent intent;
  assert(intents.pop(intent));
  assert(intent.type == streamdeck::IntentType::SetVolume);
  assert(intent.value == value);
  assert(!intents.pop(intent));
}

}  // namespace

int main() {
  lv_test_reset();
  streamdeck::AppState state;
  state.connection.online = true;
  streamdeck::IntentQueue intents;
  streamdeck::uiBegin(state, intents);
  assert(state.ui.page == streamdeck::Page::Home);
  assert(homeClock != nullptr);
  assert(homePlayer->x == 8 && homeClockCard->x == 164);
  assert(homePlayer->clip_corner);
  assert(homeCoverImage->image_align == LV_IMAGE_ALIGN_COVER);
  assert(homeShortcutButtons[0]->w == 148 && homeShortcutButtons[0]->h == 44);
  assert(homeShortcutButtons[3]->y + homeShortcutButtons[3]->h < navBar->y);
  assert(navClockLabel == navLabels[0]);
  assert(navLabels[0]->text == "--:--");
  assert(navLabels[1]->text == "Deck");
  for (int i = 1; i < 4; ++i) assert(navButtons[i]->x - navButtons[i - 1]->x - navButtons[i - 1]->w == 6);
  assert(settingsButton->x - navButtons[3]->x - navButtons[3]->w == 6);
  assert(navBar->y == 202);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_SETTINGS) != nullptr);
  assertInsideParent(lv_test_root());
  streamdeck::uiSetPage(streamdeck::Page::Media);
  assert(lv_test_indev.long_press_time == 3000);
  assert(mediaProgress->indicator_color == state.appearance.accent);
  const uint32_t originalAccent = state.appearance.accent;
  for (int theme = 0; theme < 2; ++theme) {
    state.appearance.theme = theme;
    state.appearance.accent = theme ? 0xB43B76 : 0x6CB89C;
    streamdeck::uiRefresh(state);
    assert(mediaProgress->indicator_color == state.appearance.accent);
    assert(mediaProgress->indicator_color == mediaVolumeSlider->indicator_color);
    assert(playButtonLabel->text_color == contrastingText(state.appearance.accent));
    assert(mediaProgress->bg_color == (theme ? kLightMutedButton : kButton));
    streamdeck::uiSetPage(streamdeck::Page::Home);
    streamdeck::uiSetPage(streamdeck::Page::Media);
    assert(mediaProgress->indicator_color == state.appearance.accent);
  }
  state.appearance.theme = 0;
  state.appearance.accent = originalAccent;
  streamdeck::uiRefresh(state);

  const lv_obj_t* screen = lv_test_root();
  assert(screen->x == 0);
  assert(screen->y == 0);
  assert(screen->w == 320);
  assert(screen->h == 240);
  assertNoScrollbars(screen);
  assertInsideParent(screen);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_PREV) != nullptr);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_PLAY) != nullptr);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_NEXT) != nullptr);
  assert(findLabel(lv_test_root(), LV_SYMBOL_AUDIO) != nullptr);
  assert(findLabel(lv_test_root(), "Now playing") != nullptr);
  assert(findLabel(lv_test_root(), "PC ONLINE") != nullptr);
  assert(findLabel(lv_test_root(), "PC online") != nullptr);
  assert(findLabel(lv_test_root(), "Offline actions queue") == nullptr);
  assert(findButtonByText(lv_test_root(), "Media") != nullptr);
  assert(findButtonByText(lv_test_root(), "Deck") != nullptr);
  assert(findButtonByText(lv_test_root(), "PC") != nullptr);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_SETTINGS) != nullptr);
  lv_test_send_event(findButtonByText(lv_test_root(), LV_SYMBOL_SETTINGS), LV_EVENT_CLICKED);
  assert(state.ui.page == streamdeck::Page::Info);
  lv_test_send_event(findButtonByText(lv_test_root(), "Media"), LV_EVENT_CLICKED);
  assert(state.ui.page == streamdeck::Page::Media);
  std::vector<lv_obj_t*> initialButtons;
  collectButtons(lv_test_root(), initialButtons);
  for (lv_obj_t* button : initialButtons) {
    assert(button->w >= 28 && button->h >= 28);
    assert(lv_obj_get_child(button, 0)->text_align == LV_TEXT_ALIGN_CENTER);
  }
  assert(findLabel(lv_test_root(), "Ready to play") != nullptr);
  state.connection.online = false;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "Waiting for PC") != nullptr);
  assert(findLabel(lv_test_root(), "Offline") != nullptr);
  assert(findLabel(lv_test_root(), "Offline actions queue") != nullptr);
  state.connection.online = true;

  state.audio.reportedVolume = 56;
  state.connection.wifiConnected = true;
  state.connection.agentConnected = true;
  std::strcpy(state.connection.ssid, "LabWiFi");
  std::strcpy(state.connection.deviceIp, "192.168.50.205");
  std::strcpy(state.connection.agentHost, "192.168.50.190");
  state.connection.rssi = -53;
  state.storage.mounted = true;
  state.storage.ready = true;
  state.storage.freeMb = 58200;
  state.device.uptimeSeconds = 1337;
  std::strcpy(state.media.title, "Song A");
  std::strcpy(state.media.artist, "Artist B");
  std::strcpy(state.media.album, "Album C");
  state.media.positionMs = 27000;
  state.media.durationMs = 120000;
  state.media.playing = true;
  std::strcpy(state.media.coverId, "0123456789abcdef");
  state.media.cover.status = CoverStatus::Available;
  state.media.cover.rendered = true;
  lv_image_dsc_t testCover{};
  testCover.header.w = 126;
  testCover.header.h = 126;
  streamdeck::uiSetCoverImage(&testCover);
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "Song A") != nullptr);
  assert(mediaProgress->value == 2250);
  assert(findLabel(lv_test_root(), "Artist B") != nullptr);
  assert(findLabel(lv_test_root(), "ART") != nullptr);
  assert(findLabel(lv_test_root(), "Artwork ready") != nullptr);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_PAUSE) != nullptr);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_VOLUME_MAX) != nullptr);
  assert(findLabel(lv_test_root(), "56%") != nullptr);
  assert(volumeButtonLabel->font == &lv_font_montserrat_16);
  mediaVolumeSlider->state = LV_STATE_PRESSED;
  lv_slider_set_value(mediaVolumeSlider, 81, LV_ANIM_OFF);
  streamdeck::uiRefresh(state);
  assert(lv_slider_get_value(mediaVolumeSlider) == 81);
  assert(findLabel(lv_test_root(), "81%") != nullptr);
  mediaVolumeSlider->state = 0;
  state.audio.muted = true;
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_MUTE) != nullptr);
  assert(lv_obj_get_child(findButtonByText(lv_test_root(), LV_SYMBOL_MUTE), 0)->text_color == 0xFFFFFF);
  assert(findLabel(lv_test_root(), "56%") != nullptr);
  state.audio.muted = false;
  streamdeck::uiRefresh(state);
  assert(coverImage->image_src == &testCover);
  assert((coverImage->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert((coverStatus->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  assert((coverBadge->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  assert(lv_obj_get_child(mediaChip, 0)->text_color == 0xFFFFFF);
  assert(mediaChip->bg_color == 0x14583E);
  state.media.cover.rendered = false;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), LV_SYMBOL_AUDIO) != nullptr);
  assert(findLabel(lv_test_root(), "No artwork") != nullptr);
  assert((coverImage->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  state.media.cover.status = CoverStatus::Failed;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "No artwork") != nullptr);
  state.media.cover.status = CoverStatus::Available;
  state.media.cover.rendered = true;
  streamdeck::uiRefresh(state);
  const int titleSets = mediaTitle->text_set_count;
  const int backgroundWrites = lv_test_bg_writes;
  const int positionWrites = lv_test_position_writes;
  streamdeck::uiRefresh(state);
  assert(mediaTitle->text_set_count == titleSets);
  assert(lv_test_bg_writes == backgroundWrites);
  assert(lv_test_position_writes == positionWrites);
  assert(findLabel(lv_test_root(), "56%") != nullptr);
  state.appearance.theme = 1;
  streamdeck::normalizeAppearance(state.appearance);
  streamdeck::uiRefresh(state);
  assert(lv_test_root()->bg_color == 0xF3F5F7);
  assert(mediaTitle->text_color == 0x101216);
  assert(coverPanel->bg_color == 0xFFFFFF);
  assert(coverPanel->radius == 12);
  assert(coverPanel->clip_corner);
  for (lv_obj_t* object : lv_test_allocations) {
    if (object->type != lv_obj_t::Type::Slider) continue;
    assert(object->h == 6);
    assert(object->ext_click_area >= 16);
    assert(object->x - object->ext_click_area >= 60);
    assert(object->x + object->w + object->ext_click_area <= 266);
  }
  assert(coverPanel->border_width == 0);
  assert(findLabel(lv_test_root(), "Now playing")->text_color == 0x101216);
  assert(findLabel(lv_test_root(), "Factory reset?")->text_color == 0x101216);
  assert(playButtonLabel->text_color == contrastingText(state.appearance.accent));
  state.appearance.theme = 0;
  streamdeck::normalizeAppearance(state.appearance);
  streamdeck::uiRefresh(state);
  assert(lv_test_root()->bg_color == 0x101216);
  assert(coverPanel->bg_color == 0x1D1D1F);
  assert(findLabel(lv_test_root(), "Now playing")->text_color == 0xF7F8FA);
  streamdeck::uiSetPage(streamdeck::Page::Info);
  streamdeck::uiRefresh(state);
  assert(findLabel(pages[3], "Settings") != nullptr);
  assert(brightnessSlider != nullptr);
  assert(findButtonByText(pages[3], "Factory reset") == nullptr);
  assert(findButtonByText(pages[3], "Restart") == nullptr);
  lv_test_send_event(findButtonByText(pages[3], "Device"), LV_EVENT_CLICKED);
  lv_test_send_event(findButtonByText(pages[3], "Connection & status"), LV_EVENT_CLICKED);
  assert(findLabel(pages[3], "Device status") != nullptr);
  assert(infoFw->text == "1.3.0");
  assert(infoWifi->text == "LabWiFi");
  assert(infoIp->text == "192.168.50.205");
  assert(infoPc->text == "Wi-Fi connected");
  assert(infoUptime != nullptr && otaStatus != nullptr);
  lv_test_send_event(findButtonByText(pages[3], "Back"), LV_EVENT_CLICKED);
  lv_test_send_event(findButtonByText(pages[3], "Display"), LV_EVENT_CLICKED);
  state.appearance.pageOrder[0] = streamdeck::Page::Media;
  state.appearance.pageOrder[1] = streamdeck::Page::Info;
  state.appearance.pageOrder[2] = streamdeck::Page::Shortcuts;
  state.appearance.pageOrder[3] = streamdeck::Page::Pc;
  state.appearance.pageVisible[streamdeck::pageIndex(streamdeck::Page::Shortcuts)] = false;
  streamdeck::uiSetPage(streamdeck::Page::Media);
  streamdeck::uiNavigate(1);
  assert(state.ui.page == streamdeck::Page::Info);
  for (auto* dot : dots) assert((dot->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  streamdeck::uiSetPage(streamdeck::Page::Shortcuts);
  assert(state.ui.page == streamdeck::Page::Shortcuts);

  state.appearance.pageVisible[streamdeck::pageIndex(streamdeck::Page::Shortcuts)] = true;
  streamdeck::uiSetPage(streamdeck::Page::Shortcuts);
  streamdeck::resetShortcuts(state.shortcuts);
  std::strcpy(state.shortcuts.names[0], "Keep A");
  std::strcpy(state.shortcuts.ids[0], "shortcut-a");
  std::strcpy(state.shortcuts.names[1], "Delete B");
  std::strcpy(state.shortcuts.ids[1], "shortcut-b");
  std::strcpy(state.shortcuts.names[2], "Zażółć");
  std::strcpy(state.shortcuts.ids[2], "shortcut-polish");
  std::strcpy(state.shortcuts.icons[2], "play");
  std::strcpy(state.shortcuts.displayModes[2], "both");
  std::strcpy(state.shortcuts.names[3], "Icon hidden label");
  std::strcpy(state.shortcuts.ids[3], "shortcut-icon");
  std::strcpy(state.shortcuts.icons[3], "next");
  std::strcpy(state.shortcuts.displayModes[3], "icon");
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), "Keep A") != nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") != nullptr);
  assert(findButtonByText(lv_test_root(), "Zażółć") != nullptr);
  assert(shortcutIcons[2]->text == LV_SYMBOL_PLAY);
  assert(shortcutIcons[2]->font == &cyd_icons_20);
  assert(findButtonByText(lv_test_root(), "[next]") != nullptr);
  assert((shortcutLabels[3]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  streamdeck::resetShortcuts(state.shortcuts);
  std::strcpy(state.shortcuts.names[0], "Keep A");
  std::strcpy(state.shortcuts.ids[0], "shortcut-a");
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), "Keep A") != nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") == nullptr);
  assert(findButtonByText(lv_test_root(), "No shortcut") != nullptr);
  // With 1 active shortcut in auto mode (0), button 0 scales to hero-1 (296x182)
  assert(shortcutButtons[0]->w == 296 && shortcutButtons[0]->h == 180);
  assert((shortcutButtons[1]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  // Explicit split-2 layout (2) scales buttons 0..1 to 144x182
  state.appearance.shortcutsLayout = 2;
  streamdeck::uiRefresh(state);
  assert(shortcutButtons[0]->w == 144 && shortcutButtons[0]->h == 180);
  assert(shortcutButtons[1]->w == 144 && shortcutButtons[1]->h == 180);
  assert((shortcutButtons[1]->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert((shortcutButtons[2]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  // Explicit quad-4 layout (3) scales buttons 0..3 to 144x86
  state.appearance.shortcutsLayout = 3;
  streamdeck::uiRefresh(state);
  assert(shortcutButtons[0]->w == 144 && shortcutButtons[0]->h == 86);
  assert(shortcutButtons[3]->w == 144 && shortcutButtons[3]->h == 86);
  assert((shortcutButtons[3]->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert((shortcutButtons[4]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  state.appearance.shortcutsLayout = 0;
  streamdeck::resetShortcuts(state.shortcuts);
  streamdeck::uiRefresh(state);
  assert(shortcutButtons[0]->w == 94 && shortcutButtons[0]->h == 86);
  assert(findButtonByText(lv_test_root(), "Keep A") == nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") == nullptr);
  assert(findButtonByText(lv_test_root(), "No shortcut") != nullptr);

  // PC shows only fresh measurements from Windows, never CYD heap or guessed sensors.
  state.system.hostLocalMinutes = 14 * 60 + 37;
  state.system.hostTimeSyncedAtSec = state.device.uptimeSeconds;
  state.system.cpuPercent = 34;
  state.system.ramPercent = 62;
  state.system.cpuAvailable = state.system.ramAvailable = state.system.ramCapacityAvailable = true;
  state.system.pcStatsReceived = true;
  state.system.pcStatsAtMs = 0;
  state.system.ramUsedMb = 10240;
  state.system.ramTotalMb = 16384;
  state.connection.agentConnected = true;
  streamdeck::uiSetPage(streamdeck::Page::Pc);
  assert(navLabels[0]->text == "14:37");
  assert(findLabel(pages[2], "Windows PC") != nullptr);
  assert(findLabel(pages[2], "CPU usage") != nullptr);
  assert(findLabel(pages[2], "Memory (RAM)") != nullptr);
  assert(findLabel(pages[2], "Source: Windows Agent") != nullptr);
  assert(findLabel(pages[2], "GPU") == nullptr);
  assert(findLabel(pages[2], "TEMP") == nullptr);
  assert(otaStatus == nullptr); // Device OTA diagnostics belong to Settings, not PC.
  assert(findLabel(lv_test_root(), "34%") != nullptr);
  assert(findLabel(lv_test_root(), "62%") != nullptr);
  assert(pcMetricDetails[1]->text == "10.0 / 16.0 GB used");
  for (auto* bar : pcMetricBars) assert(bar->indicator_color == state.appearance.accent);
  lv_test_tick = 4999;
  streamdeck::uiRefresh(state);
  assert(pcConnection->text == "Live");
  lv_test_tick = 5000;
  streamdeck::uiRefresh(state);
  assert(pcConnection->text == "Stale" && pcMetricValues[0]->text == "--" && pcMetricBars[0]->value == 0);
  state.connection.agentConnected = false;
  streamdeck::uiRefresh(state);
  assert(pcConnection->text == "Offline" && pcMetricValues[1]->text == "--");
  lv_test_tick = 0;
  state.connection.agentConnected = true;
  state.system.pcStatsReceived = false;
  streamdeck::uiRefresh(state);
  assert(pcConnection->text == "Waiting" && pcMetricValues[0]->text == "--");
  state.system.pcStatsReceived = true;
  state.system.cpuPercent = 0;
  state.system.ramAvailable = false;
  streamdeck::uiRefresh(state);
  assert(pcMetricValues[0]->text == "0%" && pcMetricValues[1]->text == "--");
  assert(streamdeck::sanitizeCydBackground(0xB8F224, 0) != 0xB8F224);

  lv_obj_t* plus = findButtonByText(lv_test_root(), "+");
  lv_obj_t* minus = findButtonByText(lv_test_root(), "-");
  assert(plus != nullptr);
  assert(minus != nullptr);

  lv_test_send_event(plus, LV_EVENT_CLICKED);
  assertVolumeIntent(intents, 61);

  lv_test_send_event(minus, LV_EVENT_CLICKED);
  assertVolumeIntent(intents, 51);

  state.audio.reportedVolume = 98;
  streamdeck::uiRefresh(state);
  lv_test_send_event(plus, LV_EVENT_CLICKED);
  assertVolumeIntent(intents, 100);

  state.audio.reportedVolume = 2;
  streamdeck::uiRefresh(state);
  lv_test_send_event(minus, LV_EVENT_CLICKED);
  assertVolumeIntent(intents, 0);

  streamdeck::uiSetPage(streamdeck::Page::Info);
  assert(brightnessSlider->h + 2 * brightnessSlider->ext_click_area >= 44);
  assert(brightnessSlider->x - brightnessSlider->ext_click_area >= 0);
  assert(brightnessSlider->x + brightnessSlider->w + brightnessSlider->ext_click_area <= 320);
  lv_slider_set_value(brightnessSlider, 73, LV_ANIM_OFF);
  brightnessSlider->state |= LV_STATE_PRESSED;
  lv_test_send_event(brightnessSlider, LV_EVENT_VALUE_CHANGED);
  streamdeck::Intent intent;
  assert(intents.pop(intent) && intent.type == streamdeck::IntentType::SetBrightness && intent.value == 73);
  assert(brightnessPercent->text == "73%");
  state.appearance.brightness = 20;
  streamdeck::uiRefresh(state);
  assert(lv_slider_get_value(brightnessSlider) == 73);
  assert(brightnessPercent->text == "73%");
  brightnessSlider->state &= ~LV_STATE_PRESSED;
  state.ui.localBrightness = 73;
  lv_test_send_event(findButtonByText(pages[3], "25%"), LV_EVENT_CLICKED);
  assert(intents.pop(intent) && intent.type == streamdeck::IntentType::SetBrightness && intent.value == 25);
  assert(brightnessPercent->text == "25%" && lv_slider_get_value(brightnessSlider) == 25);
  state.ui.localBrightness = 25; // app.cpp applies the queued intent.
  streamdeck::uiRefresh(state);
  assert(brightnessPresets[0]->bg_color == state.appearance.accent);
  const int settingsBackgroundWrites = lv_test_bg_writes;
  streamdeck::uiRefresh(state);
  assert(lv_test_bg_writes == settingsBackgroundWrites);
  state.ui.localBrightness = 0; // A new Windows brightness revision clears the override.
  state.appearance.brightness = 60;
  streamdeck::uiRefresh(state);
  assert(brightnessPercent->text == "60%");
  assert(lv_slider_get_value(brightnessSlider) == 60);

  lv_test_send_event(findButtonByText(pages[3], "Touch"), LV_EVENT_CLICKED);
  assert(brightnessSlider == nullptr);
  lv_test_send_event(findButtonByText(pages[3], "Test touch"), LV_EVENT_CLICKED);
  assert(intents.pop(intent) && intent.type == streamdeck::IntentType::TestTouch);
  lv_test_send_event(findButtonByText(pages[3], "Calibrate"), LV_EVENT_CLICKED);
  assert(intents.pop(intent) && intent.type == streamdeck::IntentType::Recalibrate);
  state.ui.calibrationValid = true;
  streamdeck::uiRefresh(state);
  assert(touchCalibrationStatus->text == "Calibration ready");

  lv_test_send_event(findButtonByText(pages[3], "Device"), LV_EVENT_CLICKED);
  lv_obj_t* restart = findButtonByText(lv_test_root(), "Restart");
  lv_obj_t* factoryReset = findButtonByText(lv_test_root(), "Factory reset");
  lv_obj_t* setupKey = findButtonByText(lv_test_root(), "Setup key");
  assert(restart != nullptr);
  assert(factoryReset != nullptr);
  assert(setupKey == nullptr);

  lv_test_send_event(restart, LV_EVENT_CLICKED);
  assert(!intents.pop(intent));
  assert(!lv_obj_has_flag(resetDialog, LV_OBJ_FLAG_HIDDEN));
  assert(resetDialogTitle->text == "Restart device?");
  lv_test_send_event(findButtonByText(resetDialog, "Cancel"), LV_EVENT_CLICKED);
  assert(lv_obj_has_flag(resetDialog, LV_OBJ_FLAG_HIDDEN));
  assert(!intents.pop(intent));
  lv_test_send_event(restart, LV_EVENT_CLICKED);
  lv_test_send_event(findButtonByText(resetDialog, "Restart"), LV_EVENT_CLICKED);
  assert(intents.pop(intent));
  assert(intent.type == streamdeck::IntentType::RestartDevice);
  assert(lv_obj_has_flag(resetDialog, LV_OBJ_FLAG_HIDDEN));

  lv_test_send_event(factoryReset, LV_EVENT_CLICKED);
  assert((resetDialog->flags & LV_OBJ_FLAG_HIDDEN) != 0);

  lv_test_send_event(factoryReset, LV_EVENT_LONG_PRESSED);
  assert((resetDialog->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert(resetDialog->w == 320 && resetDialog->h == 240);
  assert(lv_obj_has_flag(resetDialog, LV_OBJ_FLAG_CLICKABLE));
  assert(resetDialogTitle->text == "Factory reset?");
  lv_obj_t* confirm = findButtonByText(resetDialog, "Reset device");
  assert(confirm != nullptr);
  lv_test_send_event(confirm, LV_EVENT_CLICKED);
  assert(intents.pop(intent));
  assert(intent.type == streamdeck::IntentType::FactoryReset);
  assert(!intents.pop(intent));

  std::size_t sectionCounts[3]{};
  for (int cycle = 0; cycle < 20; ++cycle) {
    state.appearance.theme = cycle % 2;
    for (int section = 0; section < 3; ++section) {
      lv_test_send_event(settingsTabs[section], LV_EVENT_CLICKED);
      assertInsideParent(lv_test_root());
      assertNoScrollbars(lv_test_root());
      for (uint8_t i = 0; i < settingsControlCount; ++i) {
        assert(settingsControls[i]->h >= 44 && settingsControls[i]->w >= 64);
        assert(settingsControls[i]->y + settingsControls[i]->h <= navBar->y);
      }
      if (cycle == 0) sectionCounts[section] = lv_test_allocations.size();
      else assert(lv_test_allocations.size() == sectionCounts[section]);
    }
    lv_test_send_event(findButtonByText(pages[3], "Connection & status"), LV_EVENT_CLICKED);
    assertInsideParent(lv_test_root());
    state.connection.wifiConnected = false;
    state.connection.agentConnected = false;
    state.storage.mounted = false;
    streamdeck::uiRefresh(state);
    assert(infoWifi->text == "Disconnected" && infoPc->text == "Offline" && infoSd->text == "Unavailable");
    lv_test_send_event(findButtonByText(pages[3], "Back"), LV_EVENT_CLICKED);
  }
  lv_test_send_event(settingsTabs[0], LV_EVENT_CLICKED);

  std::size_t objectCounts[streamdeck::kPageCount + 1]{};
  for (int cycle = 0; cycle < 40; ++cycle) {
    state.appearance.theme = cycle % 2;
    streamdeck::normalizeAppearance(state.appearance);
    for (uint8_t p = 0; p <= streamdeck::kPageCount; ++p) {
      streamdeck::uiSetPage(static_cast<streamdeck::Page>(p));
      assertInsideParent(lv_test_root());
      assertNoScrollbars(lv_test_root());
      for (uint8_t other = 0; other <= streamdeck::kPageCount; ++other) assert((pages[other] != nullptr) == (other == p));
      assert(lv_test_root()->children.front() == pages[p]);
      if (cycle == 0) objectCounts[p] = lv_test_allocations.size();
      else assert(lv_test_allocations.size() == objectCounts[p]);
      if (p == streamdeck::pageIndex(streamdeck::Page::Media)) {
        assert(coverImage->image_src == &testCover);
        assert(mediaTitle->text_color == (state.appearance.theme ? 0x101216U : 0xF7F8FAU));
      }
    }
  }
  streamdeck::uiSetPage(streamdeck::Page::Home);
  assert(homeCoverImage->image_src == &testCover);
  assert((homeCoverImage->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  std::strcpy(state.system.hostDate, "Friday, 16 October");
  streamdeck::uiRefresh(state);
  assert(homeDate->text == "Fri, Oct 16");
  assert(homeDate->long_mode == LV_LABEL_LONG_MODE_CLIP);
  state.system.hostLocalMinutes = 18 * 60 + 54;
  state.system.hostTimeSyncedAtSec = state.device.uptimeSeconds;
  streamdeck::uiRefresh(state);
  assert(navLabels[0]->text == "18:54" && homeClock->text == "18:54");
  assert(std::strcmp(shortcutSymbolFor("browser"), "\xEF\x82\xAC") == 0);
  assert(std::strcmp(shortcutSymbolFor("terminal"), "\xEF\x84\xA0") == 0);
  assert(std::strcmp(shortcutSymbolFor("star"), "\xEF\x80\x85") == 0);
  assert(std::strcmp(shortcutSymbolFor("lock"), "\xEF\x80\xA3") == 0);
  std::strcpy(state.shortcuts.icons[0], "hotkey");
  std::strcpy(state.shortcuts.displayModes[0], "icon");
  streamdeck::uiRefresh(state);
  assert(homeShortcutIcons[0]->text == LV_SYMBOL_KEYBOARD);
  assert((homeShortcutIcons[0]->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert((homeShortcutLabels[0]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  assert(homeShortcutIcons[0]->font == &cyd_icons_20);
  std::strcpy(state.shortcuts.displayModes[0], "both");
  streamdeck::uiRefresh(state);
  assert((homeShortcutLabels[0]->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert(homeShortcutIcons[0]->x > 12); // Dynamically centered horizontally with text
  assert(homeShortcutIcons[0]->y == 10 && homeShortcutLabels[0]->y == 12); // Vertically aligned on common center axis
  const int beforeTilePositions = lv_test_position_writes;
  streamdeck::uiRefresh(state);
  assert(lv_test_position_writes == beforeTilePositions); // Repeated hydration must not repaint/reposition unchanged tiles.
  lv_test_send_event(navButtons[0], LV_EVENT_CLICKED);
  assert(state.ui.page == streamdeck::Page::Home);
  std::strcpy(state.shortcuts.displayModes[0], "label");
  streamdeck::uiRefresh(state);
  assert((homeShortcutIcons[0]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  assert((homeShortcutLabels[0]->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert(homeShortcutLabels[0]->text_align == LV_TEXT_ALIGN_CENTER);
  state.shortcuts.hasIconPixels[0] = true;
  ++state.shortcuts.iconRevision;
  std::strcpy(state.shortcuts.displayModes[0], "icon");
  streamdeck::uiRefresh(state);
  assert(homeShortcutIcons[0]->x == (homeShortcutButtons[0]->w - 24) / 2);
  assert(homeShortcutIcons[0]->y == (homeShortcutButtons[0]->h - 24) / 2);
  auto* customImage = lv_obj_get_child(homeShortcutIcons[0], 0);
  assert((customImage->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert((homeShortcutIcons[0]->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  assert(shortcutImageDescriptors[0].header.cf == LV_COLOR_FORMAT_RGB565A8);
  assert(shortcutImageDescriptors[0].data == state.shortcuts.iconPixels[0]);
  streamdeck::uiSetPage(streamdeck::Page::Shortcuts);
  assert(lv_image_get_src(lv_obj_get_child(shortcutIcons[0], 0)) == &shortcutImageDescriptors[0]);
  state.shortcuts.hasIconPixels[0] = false;
  streamdeck::uiRefresh(state);
  assert((lv_obj_get_child(shortcutIcons[0], 0)->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  streamdeck::uiSetPage(streamdeck::Page::Home);
  lv_image_dsc_t liveCover{};
  streamdeck::uiSetCoverImage(&liveCover);
  assert(homeCoverImage->image_src == &liveCover);
  streamdeck::uiSetCoverImage(nullptr);
  assert((homeCoverImage->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  lv_image_dsc_t nextCover{};
  streamdeck::uiSetCoverImage(&nextCover); // While Media widgets are absent.
  streamdeck::uiSetPage(streamdeck::Page::Media);
  assert(coverImage->image_src == &nextCover);
  lv_test_reset();
}
