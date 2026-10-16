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
  assert(lv_test_indev.long_press_time == 3000);

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
  assert(findButtonByText(lv_test_root(), "Skróty") != nullptr);
  assert(findButtonByText(lv_test_root(), "PC") != nullptr);
  assert(findButtonByText(lv_test_root(), "Info") != nullptr);
  lv_test_send_event(findButtonByText(lv_test_root(), "Info"), LV_EVENT_CLICKED);
  assert(state.ui.page == streamdeck::Page::Info);
  lv_test_send_event(findButtonByText(lv_test_root(), "Media"), LV_EVENT_CLICKED);
  assert(state.ui.page == streamdeck::Page::Media);
  std::vector<lv_obj_t*> initialButtons;
  collectButtons(lv_test_root(), initialButtons);
  for (lv_obj_t* button : initialButtons) {
    assert(button->w >= 48 || button->h >= 40);
    assert(lv_obj_get_child(button, 0)->text_align == LV_TEXT_ALIGN_CENTER);
  }
  assert(findLabel(lv_test_root(), "Ready to play") != nullptr);
  state.connection.online = false;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "Waiting for PC") != nullptr);
  assert(findLabel(lv_test_root(), "Offline - actions queue") != nullptr);
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
  assert(findLabel(lv_test_root(), "Factory Reset?")->text_color == 0x101216);
  assert(lv_obj_get_child(findButtonByText(lv_test_root(), LV_SYMBOL_PAUSE), 0)->text_color == 0x101216);
  state.appearance.theme = 0;
  streamdeck::normalizeAppearance(state.appearance);
  streamdeck::uiRefresh(state);
  assert(lv_test_root()->bg_color == 0x101216);
  assert(coverPanel->bg_color == 0x1D1D1F);
  assert(findLabel(lv_test_root(), "Now playing")->text_color == 0xF7F8FA);
  streamdeck::uiSetPage(streamdeck::Page::Info);
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "FW 1.1.0") != nullptr);
  assert(findLabel(lv_test_root(), "Wi-Fi  LabWiFi  -53 dBm") != nullptr);
  assert(findLabel(lv_test_root(), "IP      192.168.50.205") != nullptr);
  assert(findLabel(lv_test_root(), "PC      Connected") != nullptr);
  assert(findLabel(lv_test_root(), "WIFI") != nullptr);
  assert(findLabel(lv_test_root(), "Agent   192.168.50.190") != nullptr);
  state.appearance.pageOrder[0] = streamdeck::Page::Media;
  state.appearance.pageOrder[1] = streamdeck::Page::Info;
  state.appearance.pageOrder[2] = streamdeck::Page::Shortcuts;
  state.appearance.pageOrder[3] = streamdeck::Page::Pc;
  state.appearance.pageVisible[streamdeck::pageIndex(streamdeck::Page::Shortcuts)] = false;
  streamdeck::uiSetPage(streamdeck::Page::Media);
  streamdeck::uiNavigate(1);
  assert(state.ui.page == streamdeck::Page::Info);
  assert(dots[0]->w == 6);
  assert(dots[1]->w == 18);
  assert(dots[2]->bg_color == 0x2B3038);
  streamdeck::uiSetPage(streamdeck::Page::Shortcuts);
  assert(state.ui.page == streamdeck::Page::Info);

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
  assert(findButtonByText(lv_test_root(), "[play] Zażółć") != nullptr);
  assert(findButtonByText(lv_test_root(), "[next]") != nullptr);
  assert(findButtonByText(lv_test_root(), "Icon hidden label") == nullptr);
  streamdeck::resetShortcuts(state.shortcuts);
  std::strcpy(state.shortcuts.names[0], "Keep A");
  std::strcpy(state.shortcuts.ids[0], "shortcut-a");
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), "Keep A") != nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") == nullptr);
  assert(findButtonByText(lv_test_root(), "No shortcut") != nullptr);
  // With 1 active shortcut in auto mode (0), button 0 scales to hero-1 (296x182)
  assert(shortcutButtons[0]->w == 296 && shortcutButtons[0]->h == 182);
  assert((shortcutButtons[1]->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  // Explicit split-2 layout (2) scales buttons 0..1 to 144x182
  state.appearance.shortcutsLayout = 2;
  streamdeck::uiRefresh(state);
  assert(shortcutButtons[0]->w == 144 && shortcutButtons[0]->h == 182);
  assert(shortcutButtons[1]->w == 144 && shortcutButtons[1]->h == 182);
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

  // Verify persistent Windows corner clock and 2x2 watchOS PC complications
  state.system.hostLocalMinutes = 14 * 60 + 37;
  state.system.hostTimeSyncedAtSec = state.device.uptimeSeconds;
  state.system.cpuPercent = 34;
  state.system.ramPercent = 62;
  state.system.gpuPercent = 18;
  state.system.tempCelsius = 54;
  streamdeck::uiSetPage(streamdeck::Page::Pc);
  assert(findLabel(lv_test_root(), "14:37") != nullptr);
  assert(findLabel(lv_test_root(), "CPU") != nullptr);
  assert(findLabel(lv_test_root(), "RAM") != nullptr);
  assert(findLabel(lv_test_root(), "GPU") != nullptr);
  assert(findLabel(lv_test_root(), "TMP") != nullptr);
  assert(findLabel(lv_test_root(), "34%") != nullptr);
  assert(findLabel(lv_test_root(), "62%") != nullptr);
  assert(findLabel(lv_test_root(), "18%") != nullptr);
  assert(findLabel(lv_test_root(), "54 C") != nullptr);
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
  lv_obj_t* restart = findButtonByText(lv_test_root(), "Restart");
  lv_obj_t* factoryReset = findButtonByText(lv_test_root(), "Factory Reset");
  lv_obj_t* setupKey = findButtonByText(lv_test_root(), "Setup key");
  assert(restart != nullptr);
  assert(factoryReset != nullptr);
  assert(setupKey == nullptr);

  lv_test_send_event(restart, LV_EVENT_CLICKED);
  streamdeck::Intent intent;
  assert(intents.pop(intent));
  assert(intent.type == streamdeck::IntentType::RestartDevice);

  lv_test_send_event(factoryReset, LV_EVENT_CLICKED);
  assert((resetDialog->flags & LV_OBJ_FLAG_HIDDEN) != 0);

  lv_test_send_event(factoryReset, LV_EVENT_LONG_PRESSED);
  assert((resetDialog->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  lv_obj_t* confirm = findButtonByText(lv_test_root(), "CONFIRM");
  assert(confirm != nullptr);
  lv_test_send_event(confirm, LV_EVENT_CLICKED);
  assert(intents.pop(intent));
  assert(intent.type == streamdeck::IntentType::FactoryReset);

  std::size_t objectCounts[streamdeck::kPageCount]{};
  for (int cycle = 0; cycle < 40; ++cycle) {
    state.appearance.theme = cycle % 2;
    streamdeck::normalizeAppearance(state.appearance);
    for (uint8_t p = 0; p < streamdeck::kPageCount; ++p) {
      streamdeck::uiSetPage(static_cast<streamdeck::Page>(p));
      assertInsideParent(lv_test_root());
      assertNoScrollbars(lv_test_root());
      for (uint8_t other = 0; other < streamdeck::kPageCount; ++other) assert((pages[other] != nullptr) == (other == p));
      assert(lv_test_root()->children.front() == pages[p]);
      if (cycle == 0) objectCounts[p] = lv_test_allocations.size();
      else assert(lv_test_allocations.size() == objectCounts[p]);
      if (p == streamdeck::pageIndex(streamdeck::Page::Media)) {
        assert(coverImage->image_src == &testCover);
        assert(mediaTitle->text_color == (state.appearance.theme ? 0x101216U : 0xF7F8FAU));
      }
    }
  }
  lv_image_dsc_t nextCover{};
  streamdeck::uiSetCoverImage(&nextCover); // While Media widgets are absent.
  streamdeck::uiSetPage(streamdeck::Page::Media);
  assert(coverImage->image_src == &nextCover);
  lv_test_reset();
}
