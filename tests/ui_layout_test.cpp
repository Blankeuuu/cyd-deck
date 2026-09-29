#include <cassert>
#include <cstring>

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
  assert(findLabel(lv_test_root(), "Ready to play") != nullptr);
  state.connection.online = false;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "Waiting for PC") != nullptr);
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
  state.media.playing = true;
  std::strcpy(state.media.coverId, "0123456789abcdef");
  state.media.cover.status = CoverStatus::Available;
  state.media.cover.rendered = true;
  lv_image_dsc_t testCover{};
  streamdeck::uiSetCoverImage(&testCover);
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "Song A") != nullptr);
  assert(findLabel(lv_test_root(), "Artist B / Album C") != nullptr);
  assert(findLabel(lv_test_root(), "ART") != nullptr);
  assert(findLabel(lv_test_root(), "cover ready") != nullptr);
  assert(findButtonByText(lv_test_root(), LV_SYMBOL_PAUSE) != nullptr);
  assert(findButtonByText(lv_test_root(), "[volume] 56%") != nullptr);
  assert(coverImage->image_src == &testCover);
  assert((coverImage->flags & LV_OBJ_FLAG_HIDDEN) == 0);
  state.media.cover.rendered = false;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), LV_SYMBOL_AUDIO) != nullptr);
  assert(findLabel(lv_test_root(), "cover fallback") != nullptr);
  assert((coverImage->flags & LV_OBJ_FLAG_HIDDEN) != 0);
  state.media.cover.status = CoverStatus::Failed;
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "cover unavailable") != nullptr);
  state.media.cover.status = CoverStatus::Available;
  state.media.cover.rendered = true;
  streamdeck::uiRefresh(state);
  const int titleSets = mediaTitle->text_set_count;
  streamdeck::uiRefresh(state);
  assert(mediaTitle->text_set_count == titleSets);
  assert(findLabel(lv_test_root(), "56%") != nullptr);
  state.appearance.theme = 1;
  streamdeck::normalizeAppearance(state.appearance);
  streamdeck::uiRefresh(state);
  assert(lv_test_root()->bg_color == 0xF3F5F7);
  assert(mediaTitle->text_color == 0x101216);
  assert(coverPanel->bg_color == 0xFFFFFF);
  assert(findLabel(lv_test_root(), "NOW PLAYING")->text_color == 0x101216);
  assert(findLabel(lv_test_root(), "Factory Reset?")->text_color == 0x101216);
  assert(lv_obj_get_child(findButtonByText(lv_test_root(), LV_SYMBOL_PAUSE), 0)->text_color == 0x101216);
  state.appearance.theme = 0;
  streamdeck::normalizeAppearance(state.appearance);
  streamdeck::uiRefresh(state);
  assert(lv_test_root()->bg_color == 0x101216);
  assert(coverPanel->bg_color == 0x1C2026);
  assert(findLabel(lv_test_root(), "NOW PLAYING")->text_color == 0xF3F5F7);
  streamdeck::uiSetPage(streamdeck::Page::Info);
  streamdeck::uiRefresh(state);
  assert(findLabel(lv_test_root(), "FW 1.0.0") != nullptr);
  assert(findLabel(lv_test_root(), "Wi-Fi  LabWiFi  -53 dBm") != nullptr);
  assert(findLabel(lv_test_root(), "IP      192.168.50.205") != nullptr);
  assert(findLabel(lv_test_root(), "PC      Connected") != nullptr);
  assert(findLabel(lv_test_root(), "Agent   192.168.50.190") != nullptr);
  state.appearance.pageOrder[0] = streamdeck::Page::Media;
  state.appearance.pageOrder[1] = streamdeck::Page::Info;
  state.appearance.pageOrder[2] = streamdeck::Page::Shortcuts;
  state.appearance.pageOrder[3] = streamdeck::Page::Pc;
  state.appearance.pageVisible[streamdeck::pageIndex(streamdeck::Page::Shortcuts)] = false;
  streamdeck::uiSetPage(streamdeck::Page::Media);
  streamdeck::uiNavigate(1);
  assert(state.ui.page == streamdeck::Page::Info);
  streamdeck::uiSetPage(streamdeck::Page::Shortcuts);
  assert(state.ui.page == streamdeck::Page::Info);

  streamdeck::resetShortcuts(state.shortcuts);
  std::strcpy(state.shortcuts.names[0], "Keep A");
  std::strcpy(state.shortcuts.ids[0], "shortcut-a");
  std::strcpy(state.shortcuts.names[1], "Delete B");
  std::strcpy(state.shortcuts.ids[1], "shortcut-b");
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), "Keep A") != nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") != nullptr);
  streamdeck::resetShortcuts(state.shortcuts);
  std::strcpy(state.shortcuts.names[0], "Keep A");
  std::strcpy(state.shortcuts.ids[0], "shortcut-a");
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), "Keep A") != nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") == nullptr);
  assert(findButtonByText(lv_test_root(), "No shortcut") != nullptr);
  streamdeck::resetShortcuts(state.shortcuts);
  streamdeck::uiRefresh(state);
  assert(findButtonByText(lv_test_root(), "Keep A") == nullptr);
  assert(findButtonByText(lv_test_root(), "Delete B") == nullptr);
  assert(findButtonByText(lv_test_root(), "No shortcut") != nullptr);

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

  lv_obj_t* restart = findButtonByText(lv_test_root(), "Restart");
  lv_obj_t* factoryReset = findButtonByText(lv_test_root(), "Factory Reset");
  assert(restart != nullptr);
  assert(factoryReset != nullptr);

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
}
