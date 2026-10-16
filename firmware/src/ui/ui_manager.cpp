#include "ui/ui_manager.h"

#include <cstdio>
#include <cmath>
#include <cstring>
#include <lvgl.h>

LV_FONT_DECLARE(streamdeck_polish_14)
LV_FONT_DECLARE(streamdeck_polish_16)
LV_FONT_DECLARE(streamdeck_polish_20)
LV_FONT_DECLARE(cyd_icons_20)

namespace {
using namespace streamdeck;

AppState* statePtr = nullptr;
IntentQueue* queuePtr = nullptr;

lv_obj_t* root = nullptr;
lv_obj_t* pages[kPageCount + 1]{};
lv_obj_t* settingsButton = nullptr;
lv_obj_t* homeClock = nullptr;
lv_obj_t* homeDate = nullptr;
lv_obj_t* homePlayer = nullptr;
lv_obj_t* homeClockCard = nullptr;
lv_obj_t* homeCoverImage = nullptr;
lv_obj_t* homePlayButton = nullptr;
lv_obj_t* homeTitle = nullptr;
lv_obj_t* homeArtist = nullptr;
lv_obj_t* homePlayLabel = nullptr;
lv_obj_t* homeShortcutLabels[4]{};
lv_obj_t* homeShortcutButtons[4]{};
lv_obj_t* homeShortcutIcons[4]{};
lv_image_dsc_t shortcutImageDescriptors[6]{};
uint32_t shortcutImageRevisions[6]{};
lv_obj_t* dots[kPageCount]{};
lv_obj_t* buttons[24]{};
lv_obj_t* labels[96]{};
bool mutedLabels[96]{};
bool whiteLabels[96]{};
uint8_t buttonCount = 0;
uint8_t labelCount = 0;
uint8_t sharedButtonCount = 0;
uint8_t sharedLabelCount = 0;
// The descriptor and pixels are owned by app.cpp, independently of Media's widgets.
const lv_image_dsc_t* coverSource = nullptr;
uint32_t appliedBackground = 0xFFFFFFFF;
uint32_t appliedAccent = 0xFFFFFFFF;
uint8_t appliedTheme = 0xFF;
uint8_t appliedButtonStyle = 0xFF;
uint8_t appliedTileColorScheme = 0xFF;
uint8_t appliedShortcutsLayout = 0xFF;
lv_obj_t* brightnessLabel = nullptr;
lv_obj_t* brightnessSlider = nullptr;
lv_obj_t* touchTestPanel = nullptr;
lv_obj_t* touchTestLabel = nullptr;
lv_obj_t* standbyPanel = nullptr;
lv_obj_t* standbyClock = nullptr;
lv_obj_t* standbyDate = nullptr;
lv_obj_t* navBar = nullptr;
lv_obj_t* navButtons[kPageCount]{};
lv_obj_t* navLabels[kPageCount]{};
lv_obj_t* navClockLabel = nullptr;
lv_obj_t* coverPanel = nullptr;
lv_obj_t* overlay = nullptr;
lv_obj_t* resetDialog = nullptr;
lv_obj_t* mediaTitle = nullptr;
lv_obj_t* mediaArtist = nullptr;
lv_obj_t* mediaStatus = nullptr;
lv_obj_t* mediaChip = nullptr;
lv_obj_t* mediaHint = nullptr;
lv_obj_t* mediaTimeLeft = nullptr;
lv_obj_t* mediaTimeRight = nullptr;
lv_obj_t* coverBadge = nullptr;
lv_obj_t* coverStatus = nullptr;
lv_obj_t* coverImage = nullptr;
lv_obj_t* prevButton = nullptr;
lv_obj_t* playButton = nullptr;
lv_obj_t* nextButton = nullptr;
lv_obj_t* playButtonLabel = nullptr;
lv_obj_t* mediaProgress = nullptr;
lv_obj_t* muteButton = nullptr;
lv_obj_t* volumeButtonLabel = nullptr;
lv_obj_t* mediaVolumeSlider = nullptr;
lv_obj_t* volumeSliderLabel = nullptr;
lv_obj_t* overlayStatus = nullptr;
lv_obj_t* overlayVolumePercent = nullptr;
lv_obj_t* shortcutButtons[6]{};
lv_obj_t* shortcutLabels[6]{};
lv_obj_t* shortcutIcons[6]{};
lv_obj_t* pcMetricCards[4]{};
lv_obj_t* pcMetricTitles[4]{};
lv_obj_t* pcMetricValues[4]{};
lv_obj_t* pcMetricBars[4]{};
lv_obj_t* pcConnection = nullptr;
lv_obj_t* pcChip = nullptr;
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
lv_obj_t* infoChip = nullptr;

constexpr uint32_t kBg = 0x0B0D12;
constexpr uint32_t kPanel = 0x1D1D1F;
constexpr uint32_t kButton = 0x272729;
constexpr uint32_t kText = 0xF7F8FA;
constexpr uint32_t kMuted = 0xB2BAC6;
constexpr uint32_t kAccent = 0xD43B48;
constexpr uint32_t kFeedback = 0x204B72;
constexpr uint32_t kOk = 0x14583E;
constexpr uint32_t kLightText = 0x101216;
constexpr uint32_t kLightPanel = 0xFFFFFF;
constexpr uint32_t kLightButton = 0xFAFAFC;
constexpr uint32_t kLightMutedButton = 0xE8EAED;
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
  if (labelCount < 96) { mutedLabels[labelCount] = color == kMuted; whiteLabels[labelCount] = false; labels[labelCount++] = label; }
  lv_label_set_text(label, text);
  lv_label_set_long_mode(label, LV_LABEL_LONG_MODE_CLIP);
  setStaticBounds(label, x, y, w, h);
  lv_obj_set_style_text_color(label, lv_color_hex(color), 0);
  lv_obj_set_style_text_font(label,
      size >= 20 ? &streamdeck_polish_20 :
      (size >= 16 ? &streamdeck_polish_16 : &streamdeck_polish_14), 0);
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

void setChip(lv_obj_t* chip, const char* text, uint32_t color) {
  if (!chip) return;
  if (!lv_color_eq(lv_obj_get_style_bg_color(chip, 0), lv_color_hex(color)))
    lv_obj_set_style_bg_color(chip, lv_color_hex(color), 0);
  setLabelText(lv_obj_get_child(chip, 0), text);
}

void applyDynamicTextColors() {
  for (uint8_t i = 0; i < labelCount; ++i) {
    const uint32_t color = whiteLabels[i] ? 0xFFFFFF : labels[i] == coverBadge
        ? (statePtr ? statePtr->appearance.accent : kAccent)
        : labels[i] == homeDate ? (statePtr && statePtr->appearance.theme == 1 ? 0x58616E : 0xA1A1AA)
        : (mutedLabels[i] ? (statePtr && statePtr->appearance.theme == 1 ? 0x58616E : kMuted) : textColor());
    lv_obj_set_style_text_color(labels[i], lv_color_hex(color), 0);
  }
  for (uint8_t i = 0; i < buttonCount; ++i) {
    lv_obj_t* label = lv_obj_get_child(buttons[i], 0);
    if (label) lv_obj_set_style_text_color(label, lv_color_hex(buttons[i] == homePlayButton ? 0xFFFFFF : textColor()), 0);
  }
}

uint32_t resolveTileFillColor(uint32_t accent, uint8_t theme, uint8_t style, uint8_t scheme) {
  if (style == 2) return accent;
  if (theme == 1) return kLightButton;
  if (scheme == 2) return 0x121316;
  if (scheme == 1) {
    const uint32_t r = std::min<uint32_t>(255, 22 + (((accent >> 16) & 0xFF) * 20) / 100);
    const uint32_t g = std::min<uint32_t>(255, 24 + (((accent >> 8) & 0xFF) * 20) / 100);
    const uint32_t b = std::min<uint32_t>(255, 30 + ((accent & 0xFF) * 20) / 100);
    return (r << 16) | (g << 8) | b;
  }
  return kButton;
}

void styleButton(lv_obj_t* button) {
  const uint32_t color = statePtr ? statePtr->appearance.accent : kButton;
  const uint8_t style = statePtr ? statePtr->appearance.buttonStyle : 1;
  const uint8_t theme = statePtr ? statePtr->appearance.theme : 0;
  const uint8_t scheme = statePtr ? statePtr->appearance.tileColorScheme : 0;
  const uint32_t fillColor = resolveTileFillColor(color, theme, style, scheme);
  lv_obj_set_style_bg_color(button, lv_color_hex(fillColor), 0);
  lv_obj_set_style_bg_opa(button, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(button, style == 0 ? 6 : (style == 2 ? 18 : 14), 0);
  lv_obj_set_style_border_width(button, style == 2 ? 0 : 1, 0);
  lv_obj_set_style_border_color(button, lv_color_hex(theme == 1 ? 0xD8DAE0 : 0x303036), 0);
}

void styleMediaTransportButton(lv_obj_t* button) {
  if (!button) return;
  const uint32_t color = statePtr && statePtr->appearance.theme == 1 ? kLightMutedButton : kButton;
  lv_obj_set_style_bg_color(button, lv_color_hex(button == playButton && statePtr ? statePtr->appearance.accent : color), 0);
  lv_obj_set_style_radius(button, 22, 0);
  lv_obj_set_style_border_width(button, 0, 0);
}

lv_obj_t* makeChip(lv_obj_t* parent, const char* text, int x, int y, int w,
                   uint32_t color = kButton) {
  lv_obj_t* chip = lv_obj_create(parent);
  setStaticBounds(chip, x, y, w, 22);
  lv_obj_set_style_bg_color(chip, lv_color_hex(color), 0);
  lv_obj_set_style_bg_opa(chip, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(chip, 11, 0);
  lv_obj_set_style_border_width(chip, 0, 0);
  makeLabel(chip, text, 8, 2, w - 16, 18, 0xFFFFFF, 14);
  if (labelCount > 0) whiteLabels[labelCount - 1] = true;
  return chip;
}

void applyAppearanceStyles(const AppState& state) {
  const bool themeChanged = appliedTheme != state.appearance.theme;
  const bool buttonsChanged = themeChanged || appliedAccent != state.appearance.accent
      || appliedButtonStyle != state.appearance.buttonStyle
      || appliedTileColorScheme != state.appearance.tileColorScheme;
  const uint32_t safeBg = sanitizeCydBackground(state.appearance.background, state.appearance.theme);
  if (appliedBackground != safeBg) {
    lv_obj_set_style_bg_color(root, lv_color_hex(safeBg), 0);
    appliedBackground = safeBg;
  }
  if (themeChanged) {
    const uint32_t panel = state.appearance.theme == 1 ? kLightPanel : kPanel;
    if (coverPanel) lv_obj_set_style_bg_color(coverPanel, lv_color_hex(panel), 0);
    if (homeClockCard) lv_obj_set_style_bg_color(homeClockCard, lv_color_hex(panel), 0);
    if (touchTestPanel) lv_obj_set_style_bg_color(touchTestPanel, lv_color_hex(panel), 0);
    if (navBar) lv_obj_set_style_bg_color(navBar, lv_color_hex(panel), 0);
    lv_obj_set_style_bg_color(overlay, lv_color_hex(panel), 0);
    lv_obj_set_style_bg_color(resetDialog, lv_color_hex(panel), 0);
    if (mediaChip) lv_obj_set_style_bg_color(mediaChip, lv_color_hex(kButton), 0);
    if (pcChip) lv_obj_set_style_bg_color(pcChip, lv_color_hex(kButton), 0);
    if (infoChip) lv_obj_set_style_bg_color(infoChip, lv_color_hex(kButton), 0);
    for (lv_obj_t* card : pcMetricCards) {
      if (card) lv_obj_set_style_bg_color(card, lv_color_hex(panel), 0);
    }
  }
  if (buttonsChanged && mediaVolumeSlider) {
    lv_obj_set_style_bg_color(mediaVolumeSlider, lv_color_hex(state.appearance.theme == 1 ? kLightMutedButton : kButton), LV_PART_MAIN);
    lv_obj_set_style_bg_color(mediaVolumeSlider, lv_color_hex(state.appearance.accent), LV_PART_INDICATOR);
    lv_obj_set_style_bg_color(mediaVolumeSlider, lv_color_hex(state.appearance.accent), LV_PART_KNOB);
  }
  if (buttonsChanged) {
    for (uint8_t i = 0; i < buttonCount; ++i) styleButton(buttons[i]);
    if (state.ui.page == Page::Media) {
      styleMediaTransportButton(prevButton);
      styleMediaTransportButton(playButton);
      styleMediaTransportButton(nextButton);
    }
    if (homePlayButton) {
      lv_obj_set_style_bg_color(homePlayButton, lv_color_hex(0x202024), 0);
      lv_obj_set_style_bg_opa(homePlayButton, 230, 0);
      lv_obj_set_style_radius(homePlayButton, 18, 0);
      lv_obj_set_style_border_width(homePlayButton, 0, 0);
    }
    applyDynamicTextColors();
    appliedAccent = state.appearance.accent;
    appliedTheme = state.appearance.theme;
    appliedButtonStyle = state.appearance.buttonStyle;
    appliedTileColorScheme = state.appearance.tileColorScheme;
  }
}

lv_obj_t* button(lv_obj_t* parent, const char* text, int x, int y, int w, int h,
                 lv_event_cb_t cb, void* data, lv_obj_t** labelOut = nullptr) {
  lv_obj_t* b = lv_btn_create(parent);
  setStaticBounds(b, x, y, w, h);
  styleButton(b);
  if (buttonCount < 24) buttons[buttonCount++] = b;
  if (cb) lv_obj_add_event_cb(b, cb, LV_EVENT_CLICKED, data);
  lv_obj_t* label = lv_label_create(b);
  lv_label_set_text(label, text);
  lv_label_set_long_mode(label, LV_LABEL_LONG_MODE_CLIP);
  lv_obj_set_width(label, w - 12);
  lv_obj_set_style_text_align(label, LV_TEXT_ALIGN_CENTER, 0);
  disableScrolling(label);
  lv_obj_set_style_text_color(label, lv_color_hex(textColor()), 0);
  lv_obj_set_style_text_font(label, &streamdeck_polish_16, 0);
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
  if (!queuePtr) return;
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

void muteClicked(lv_event_t*) {
  if (queuePtr) queuePtr->push({IntentType::ToggleMute, 0});
}

void volumeSliderChanged(lv_event_t* e) {
  if (!queuePtr) return;
  lv_obj_t* slider = static_cast<lv_obj_t*>(lv_event_get_target(e));
  const int val = lv_slider_get_value(slider);
  char percent[8];
  std::snprintf(percent, sizeof(percent), "%d%%", val);
  setLabelText(volumeSliderLabel, percent);
  static uint32_t lastVolumeSentAt = 0;
  const uint32_t now = lv_tick_get();
  const lv_event_code_t code = lv_event_get_code(e);
  if (code == LV_EVENT_RELEASED || (now - lastVolumeSentAt >= 60)) {
    lastVolumeSentAt = now;
    queuePtr->push({IntentType::SetVolume, clampVolume(val)});
  }
}

[[maybe_unused]] void toggleOverlay(lv_event_t*) {
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

const char* pageTabTitle(Page page) {
  switch (page) {
    case Page::Media: return "Media";
    case Page::Shortcuts: return "Deck";
    case Page::Pc: return "PC";
    case Page::Info: return "Info";
    case Page::Home: return "Home";
  }
  return "Info";
}

void navTabClicked(lv_event_t* e) {
  const int slot = static_cast<int>(reinterpret_cast<intptr_t>(lv_event_get_user_data(e)));
  if (!statePtr || slot < 0 || slot >= kPageCount) return;
  static constexpr Page dockOrder[] = {Page::Home, Page::Shortcuts, Page::Media, Page::Pc};
  const Page targetPage = dockOrder[slot];
  if (targetPage == Page::Home || statePtr->appearance.pageVisible[pageIndex(targetPage)]) {
    uiSetPage(targetPage);
  }
}

void settingsClicked(lv_event_t*) { uiSetPage(Page::Info); }

void buildNavBar() {
  navBar = lv_obj_create(root);
  setStaticBounds(navBar, 8, 202, 304, 34);
  lv_obj_set_style_bg_color(navBar, lv_color_hex(kPanel), 0);
  lv_obj_set_style_radius(navBar, 17, 0);
  lv_obj_set_style_bg_opa(navBar, 240, 0);
  lv_obj_set_style_border_width(navBar, 0, 0);
  static constexpr Page dockOrder[] = {Page::Home, Page::Shortcuts, Page::Media, Page::Pc};
  constexpr int gap = 6;
  constexpr int horizontalPadding = 10;
  int widestDigit = 0;
  for (char digit = '0'; digit <= '9'; ++digit) {
    const int width = lv_text_get_width(&digit, 1, &streamdeck_polish_14, 0);
    if (width > widestDigit) widestDigit = width;
  }
  const int clockWidth = 4 * widestDigit + lv_text_get_width(":", 1, &streamdeck_polish_14, 0);
  int widths[kPageCount]{};
  int groupWidth = 30 + kPageCount * gap;
  for (int i = 0; i < kPageCount; ++i) {
    const char* title = dockOrder[i] == Page::Home ? "88:88" : pageTabTitle(dockOrder[i]);
    widths[i] = (dockOrder[i] == Page::Home ? clockWidth : lv_text_get_width(title, std::strlen(title), &streamdeck_polish_14, 0)) + horizontalPadding * 2;
    groupWidth += widths[i];
  }
  int x = (304 - groupWidth) / 2;
  for (int i = 0; i < kPageCount; ++i) {
    navButtons[i] = lv_btn_create(navBar);
    setStaticBounds(navButtons[i], x, 3, widths[i], 28);
    lv_obj_set_style_radius(navButtons[i], 14, 0);
    lv_obj_set_style_border_width(navButtons[i], 0, 0);
    lv_obj_add_event_cb(navButtons[i], navTabClicked, LV_EVENT_CLICKED,
        reinterpret_cast<void*>(static_cast<intptr_t>(i)));
    navLabels[i] = lv_label_create(navButtons[i]);
    setStaticBounds(navLabels[i], horizontalPadding, 5, widths[i] - horizontalPadding * 2, 18);
    lv_label_set_text(navLabels[i], dockOrder[i] == Page::Home ? "--:--" : pageTabTitle(dockOrder[i]));
    lv_label_set_long_mode(navLabels[i], LV_LABEL_LONG_MODE_CLIP);
    lv_obj_set_style_text_align(navLabels[i], LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_style_text_font(navLabels[i], &streamdeck_polish_14, 0);
    x += widths[i] + gap;
  }
  settingsButton = button(navBar, LV_SYMBOL_SETTINGS, x, 3, 30, 28, settingsClicked, nullptr);
  lv_obj_set_style_text_font(lv_obj_get_child(settingsButton, 0), &lv_font_montserrat_16, 0);
  navClockLabel = navLabels[0];

}

void buildMediaPage() {
  coverPanel = lv_obj_create(pages[0]);
  setStaticBounds(coverPanel, 12, 12, 128, 128);
  lv_obj_set_style_bg_color(coverPanel, lv_color_hex(kPanel), 0);
  lv_obj_set_style_bg_opa(coverPanel, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(coverPanel, 12, 0);
  lv_obj_set_style_clip_corner(coverPanel, true, 0);
  lv_obj_set_style_border_width(coverPanel, 0, 0);
  lv_obj_add_flag(coverPanel, LV_OBJ_FLAG_CLICKABLE);
  lv_obj_add_event_cb(coverPanel, enqueue, LV_EVENT_CLICKED,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::TogglePlayback)));
  coverBadge = makeLabel(coverPanel, "SD", 39, 39, 50, 30, kAccent, 20);
  coverStatus = makeLabel(coverPanel, "Artwork", 8, 74, 112, 20, kMuted, 14);
  lv_obj_set_style_text_align(coverBadge, LV_TEXT_ALIGN_CENTER, 0);
  lv_obj_set_style_text_align(coverStatus, LV_TEXT_ALIGN_CENTER, 0);
  coverImage = lv_image_create(coverPanel);
  setStaticBounds(coverImage, 0, 0, 128, 128);
  if (coverSource) lv_image_set_src(coverImage, coverSource);
  lv_obj_add_flag(coverImage, LV_OBJ_FLAG_HIDDEN);

  lv_obj_t* mediaHeader = makeLabel(pages[0], "Now playing", 12, 7, 170, 22, kText, 20);
  lv_obj_add_flag(mediaHeader, LV_OBJ_FLAG_HIDDEN);
  mediaChip = makeChip(pages[0], "PC OFFLINE", 208, 7, 100, kButton);
  lv_obj_add_flag(mediaChip, LV_OBJ_FLAG_HIDDEN);
  mediaTitle = makeLabel(pages[0], "", 152, 12, 156, 24, kText, 20);
  lv_label_set_long_mode(mediaTitle, LV_LABEL_LONG_DOT);
  mediaArtist = makeLabel(pages[0], "", 152, 40, 156, 18, kMuted, 14);
  lv_label_set_long_mode(mediaArtist, LV_LABEL_LONG_DOT);
  mediaStatus = makeLabel(pages[0], "", 152, 62, 156, 16, kMuted, 14);
  mediaProgress = lv_bar_create(pages[0]);
  setStaticBounds(mediaProgress, 152, 84, 156, 3);
  lv_bar_set_range(mediaProgress, 0, 10000);
  mediaTimeLeft = makeLabel(pages[0], "0:00", 152, 92, 60, 16, kMuted, 14);
  mediaTimeRight = makeLabel(pages[0], "0:00", 250, 92, 58, 16, kMuted, 14);
  lv_obj_set_style_text_align(mediaTimeRight, LV_TEXT_ALIGN_RIGHT, 0);

  prevButton = button(pages[0], LV_SYMBOL_PREV, 152, 116, 48, 36, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::Previous)));
  playButton = button(pages[0], LV_SYMBOL_PLAY, 206, 116, 48, 36, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::TogglePlayback)),
      &playButtonLabel);
  nextButton = button(pages[0], LV_SYMBOL_NEXT, 260, 116, 48, 36, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::Next)));
  styleMediaTransportButton(prevButton);
  styleMediaTransportButton(playButton);
  styleMediaTransportButton(nextButton);

  muteButton = button(pages[0], LV_SYMBOL_VOLUME_MAX, 12, 164, 48, 32, muteClicked, nullptr,
      &volumeButtonLabel);
  lv_obj_set_style_text_font(volumeButtonLabel, &lv_font_montserrat_16, 0);
  lv_obj_set_style_border_width(muteButton, 0, 0);
  lv_obj_set_style_shadow_width(muteButton, 0, 0);
  mediaVolumeSlider = lv_slider_create(pages[0]);
  setStaticBounds(mediaVolumeSlider, 76, 178, 174, 6);
  // Thin visual track with a generous touch target on the resistive screen.
  lv_obj_set_ext_click_area(mediaVolumeSlider, 16);
  lv_obj_set_style_pad_all(mediaVolumeSlider, 5, LV_PART_KNOB);
  lv_obj_set_style_radius(mediaVolumeSlider, 8, LV_PART_KNOB);
  lv_obj_set_style_radius(mediaVolumeSlider, 3, LV_PART_INDICATOR);
  lv_slider_set_range(mediaVolumeSlider, 0, 100);
  lv_obj_set_style_radius(mediaVolumeSlider, 10, 0);
  lv_obj_add_event_cb(mediaVolumeSlider, volumeSliderChanged, LV_EVENT_VALUE_CHANGED, nullptr);
  lv_obj_add_event_cb(mediaVolumeSlider, volumeSliderChanged, LV_EVENT_RELEASED, nullptr);

  volumeSliderLabel = makeLabel(pages[0], "", 264, 172, 44, 20, kText, 16);
  lv_obj_set_style_text_align(volumeSliderLabel, LV_TEXT_ALIGN_RIGHT, 0);
  lv_obj_clear_flag(volumeSliderLabel, LV_OBJ_FLAG_CLICKABLE);

  mediaHint = makeLabel(pages[0], "", 12, 220, 296, 16, kMuted, 14);
  lv_obj_add_flag(mediaHint, LV_OBJ_FLAG_HIDDEN);
}

uint8_t resolveEffectiveShortcutsLayout(const AppState& s) {
  if (s.appearance.shortcutsLayout >= 1 && s.appearance.shortcutsLayout <= 4) {
    return s.appearance.shortcutsLayout;
  }
  int activeCount = 0;
  for (int i = 0; i < 6; ++i) {
    if (s.shortcuts.ids[i][0] != '\0' ||
        (s.shortcuts.names[i][0] != '\0' && std::strcmp(s.shortcuts.names[i], "No shortcut") != 0)) {
      ++activeCount;
    }
  }
  if (activeCount == 1) return 1; // hero-1 (full-screen 6x button size)
  if (activeCount == 2) return 2; // split-2 (two 3x half-screen buttons)
  if (activeCount == 3 || activeCount == 4) return 3; // quad-4 (2x2 large buttons)
  return 4; // grid-6 (3x2 symmetrical Control Center tiles)
}

void applyShortcutsLayout(const AppState& s) {
  if (s.ui.page != Page::Shortcuts || !pages[1]) return;
  const uint8_t effective = resolveEffectiveShortcutsLayout(s);
  if (appliedShortcutsLayout == effective) return;
  appliedShortcutsLayout = effective;

  for (int i = 0; i < 6; ++i) {
    if (!shortcutButtons[i] || !shortcutLabels[i]) continue;
    int x = 11 + (i % 3) * 102;
    int y = 12 + (i / 3) * 94;
    int w = 94;
    int h = 86;
    bool visible = true;

    if (effective == 1) {
      if (i == 0) { x = 12; y = 12; w = 296; h = 180; visible = true; }
      else { visible = false; }
    } else if (effective == 2) {
      if (i < 2) { x = 12 + i * 152; y = 12; w = 144; h = 180; visible = true; }
      else { visible = false; }
    } else if (effective == 3) {
      if (i < 4) { x = 12 + (i % 2) * 152; y = 12 + (i / 2) * 94; w = 144; h = 86; visible = true; }
      else { visible = false; }
    }

    setStaticBounds(shortcutButtons[i], x, y, w, h);
    lv_obj_set_width(shortcutLabels[i], w - 12);
    lv_obj_set_style_text_font(shortcutLabels[i],
        effective <= 2 ? &streamdeck_polish_20 : &streamdeck_polish_16, 0);
    if (visible) lv_obj_clear_flag(shortcutButtons[i], LV_OBJ_FLAG_HIDDEN);
    else lv_obj_add_flag(shortcutButtons[i], LV_OBJ_FLAG_HIDDEN);
  }
}

void buildShortcutsPage() {
  lv_obj_t* shortcutsHeader = makeLabel(pages[1], "Shortcuts", 12, 7, 150, 22, kText, 20);
  lv_obj_add_flag(shortcutsHeader, LV_OBJ_FLAG_HIDDEN);
  lv_obj_t* shortcutsChip = makeChip(pages[1], "6 KEYS", 238, 7, 70, kButton);
  lv_obj_add_flag(shortcutsChip, LV_OBJ_FLAG_HIDDEN);
  appliedShortcutsLayout = 0xFF;
  for (int i = 0; i < 6; ++i) {
    const int x = 11 + (i % 3) * 102;
    const int y = 12 + (i / 3) * 94;
    shortcutButtons[i] = button(pages[1], "", x, y, 94, 86, shortcut,
        reinterpret_cast<void*>(static_cast<intptr_t>(i)), &shortcutLabels[i]);
    lv_obj_set_align(shortcutLabels[i], LV_ALIGN_TOP_LEFT);
    lv_label_set_long_mode(shortcutLabels[i], LV_LABEL_LONG_MODE_WRAP);
    shortcutIcons[i] = makeLabel(shortcutButtons[i], "", 6, 6, 24, 24, kText, 20);
    lv_obj_set_style_text_font(shortcutIcons[i], &cyd_icons_20, 0);
    lv_obj_set_style_text_align(shortcutIcons[i], LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_t* image = lv_image_create(shortcutIcons[i]);
    setStaticBounds(image, 0, 0, 24, 24);
    lv_obj_clear_flag(image, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_add_flag(image, LV_OBJ_FLAG_HIDDEN);
    shortcutImageRevisions[i] = 0;
  }
  appliedShortcutsLayout = 4;
}

const char* shortcutSymbolFor(const char* icon) {
  if (!icon || !icon[0] || std::strcmp(icon, "empty") == 0) return nullptr;
  struct Mapping { const char* name; const char* symbol; };
  static const Mapping mapping[] = {
    {"play", LV_SYMBOL_PLAY}, {"media", LV_SYMBOL_PLAY}, {"pause", LV_SYMBOL_PAUSE},
    {"prev", LV_SYMBOL_PREV}, {"previous", LV_SYMBOL_PREV}, {"next", LV_SYMBOL_NEXT},
    {"volume", LV_SYMBOL_VOLUME_MAX}, {"audio", LV_SYMBOL_AUDIO}, {"mute", LV_SYMBOL_MUTE},
    {"hotkey", LV_SYMBOL_KEYBOARD}, {"keyboard", LV_SYMBOL_KEYBOARD}, {"key", LV_SYMBOL_KEYBOARD},
    {"folder", LV_SYMBOL_DIRECTORY}, {"gear", LV_SYMBOL_SETTINGS}, {"tools", LV_SYMBOL_SETTINGS},
    {"browser", "\xEF\x82\xAC"}, {"web", "\xEF\x82\xAC"}, {"url", "\xEF\x82\xAC"},
    {"terminal", "\xEF\x84\xA0"}, {"script", "\xEF\x84\xA0"}, {"powershell", "\xEF\x84\xA0"},
    {"app", "\xEF\x8B\x90"}, {"shortcut", LV_SYMBOL_FILE}, {"file", LV_SYMBOL_FILE},
    {"camera", "\xEF\x80\xB0"}, {"mic", "\xEF\x84\xB0"}, {"microphone", "\xEF\x84\xB0"},
    {"lock", "\xEF\x80\xA3"}, {"star", "\xEF\x80\x85"}, {"check", LV_SYMBOL_OK}, {"home", LV_SYMBOL_HOME}, {"right", LV_SYMBOL_RIGHT}
  };
  for (const auto& entry : mapping) if (std::strcmp(icon, entry.name) == 0) return entry.symbol;
  return LV_SYMBOL_FILE;
}


void setBoundsIfChanged(lv_obj_t* object, int x, int y, int width, int height) {
  if (lv_obj_get_x(object) == x && lv_obj_get_y(object) == y &&
      lv_obj_get_width(object) == width && lv_obj_get_height(object) == height) return;
  setStaticBounds(object, x, y, width, height);
}

void updateShortcutTile(const ShortcutState& shortcuts, int index, lv_obj_t* tile,
    lv_obj_t* label, lv_obj_t* icon, bool compact) {
  if (!tile || !label || !icon) return;
  const char* symbol = shortcutSymbolFor(shortcuts.icons[index]);
  const bool showIcon = (symbol || shortcuts.hasIconPixels[index]) && std::strcmp(shortcuts.displayModes[index], "label") != 0;
  const bool showLabel = !showIcon || std::strcmp(shortcuts.displayModes[index], "icon") != 0;
  setLabelText(icon, shortcuts.hasIconPixels[index] ? "" : (symbol ? symbol : ""));
  lv_obj_t* image = lv_obj_get_child(icon, 0);
  if (shortcuts.hasIconPixels[index]) {
    auto& descriptor = shortcutImageDescriptors[index];
    if (shortcutImageRevisions[index] != shortcuts.iconRevision || descriptor.data != shortcuts.iconPixels[index]) {
      lv_image_cache_drop(&descriptor);
      descriptor = {};
      descriptor.header.magic = LV_IMAGE_HEADER_MAGIC;
      descriptor.header.cf = LV_COLOR_FORMAT_RGB565A8;
      descriptor.header.w = descriptor.header.h = 24;
      descriptor.header.stride = 48;
      descriptor.data_size = 1728;
      descriptor.data = shortcuts.iconPixels[index];
      shortcutImageRevisions[index] = shortcuts.iconRevision;
      lv_obj_invalidate(image);
    }
    if (lv_image_get_src(image) != &descriptor) lv_image_set_src(image, &descriptor);
    lv_obj_clear_flag(image, LV_OBJ_FLAG_HIDDEN);
  } else lv_obj_add_flag(image, LV_OBJ_FLAG_HIDDEN);
  setLabelText(label, shortcuts.names[index][0] ? shortcuts.names[index] : "No shortcut");
  if (showIcon) lv_obj_clear_flag(icon, LV_OBJ_FLAG_HIDDEN); else lv_obj_add_flag(icon, LV_OBJ_FLAG_HIDDEN);
  if (showLabel) lv_obj_clear_flag(label, LV_OBJ_FLAG_HIDDEN); else lv_obj_add_flag(label, LV_OBJ_FLAG_HIDDEN);
  const int width = lv_obj_get_width(tile), height = lv_obj_get_height(tile);
  if (compact) {
    if (showIcon && showLabel) {
      const char* text = lv_label_get_text(label);
      const lv_font_t* font = lv_obj_get_style_text_font(label, 0);
      if (!font) font = &streamdeck_polish_14;
      const uint32_t textLen = text ? static_cast<uint32_t>(std::strlen(text)) : 0;
      int textWidth = textLen > 0 ? lv_text_get_width(text, textLen, font, 0) : 0;
      const int gap = 6;
      const int iconWidth = 24;
      const int padding = 6;
      const int maxTextWidth = width - iconWidth - gap - (padding * 2);
      if (textWidth > maxTextWidth) textWidth = maxTextWidth;
      const int contentWidth = iconWidth + gap + textWidth;
      int startX = (width - contentWidth) / 2;
      if (startX < padding) startX = padding;
      setBoundsIfChanged(icon, startX, (height - 24) / 2, 24, 24);
      const int labelX = startX + iconWidth + gap;
      const int labelW = width - labelX - padding;
      const int labelH = 20;
      setBoundsIfChanged(label, labelX, (height - labelH) / 2, labelW, labelH);
      lv_obj_set_style_text_align(label, LV_TEXT_ALIGN_LEFT, 0);
    } else if (showIcon) {
      setBoundsIfChanged(icon, (width - 24) / 2, (height - 24) / 2, 24, 24);
    } else if (showLabel) {
      const int labelH = 20;
      setBoundsIfChanged(label, 6, (height - labelH) / 2, width - 12, labelH);
      lv_obj_set_style_text_align(label, LV_TEXT_ALIGN_CENTER, 0);
    }
  } else {
    const int labelHeight = height >= 120 ? 52 : 38;
    const int groupHeight = labelHeight + 28;
    setBoundsIfChanged(icon, (width - 24) / 2, showLabel ? (height - groupHeight) / 2 : (height - 24) / 2, 24, 24);
    setBoundsIfChanged(label, 6, showIcon ? (height - groupHeight) / 2 + 28 : (height - labelHeight) / 2, width - 12, labelHeight);
    lv_obj_set_style_text_align(label, LV_TEXT_ALIGN_CENTER, 0);
  }
}

void homeMediaClicked(lv_event_t*) { uiSetPage(Page::Media); }

void buildHomePage() {
  lv_obj_t* page = pages[pageIndex(Page::Home)];
  // Two quiet cards above four 44px targets: no object crosses the dock safe area.
  homePlayer = lv_obj_create(page);
  setStaticBounds(homePlayer, 8, 8, 148, 84);
  lv_obj_set_style_bg_color(homePlayer, lv_color_hex(kPanel), 0);
  lv_obj_set_style_bg_opa(homePlayer, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(homePlayer, 16, 0);
  lv_obj_set_style_clip_corner(homePlayer, true, 0);
  lv_obj_set_style_border_width(homePlayer, 0, 0);
  lv_obj_add_flag(homePlayer, LV_OBJ_FLAG_CLICKABLE);
  lv_obj_add_event_cb(homePlayer, homeMediaClicked, LV_EVENT_CLICKED, nullptr);
  homeCoverImage = lv_image_create(homePlayer);
  if (coverSource) lv_image_set_src(homeCoverImage, coverSource);
  setStaticBounds(homeCoverImage, 0, 0, 148, 84);
  lv_image_set_inner_align(homeCoverImage, LV_IMAGE_ALIGN_COVER);
  lv_obj_clear_flag(homeCoverImage, LV_OBJ_FLAG_CLICKABLE);
  lv_obj_add_flag(homeCoverImage, LV_OBJ_FLAG_HIDDEN);
  lv_obj_t* scrim = lv_obj_create(homePlayer);
  setStaticBounds(scrim, 0, 0, 148, 84);
  lv_obj_set_style_bg_color(scrim, lv_color_hex(0x000000), 0);
  // Even white artwork stays below #575757: white captions exceed 7:1 contrast.
  lv_obj_set_style_bg_opa(scrim, 168, 0);
  lv_obj_set_style_border_width(scrim, 0, 0);
  lv_obj_set_style_radius(scrim, 16, 0);
  lv_obj_clear_flag(scrim, LV_OBJ_FLAG_CLICKABLE);
  homeTitle = makeLabel(homePlayer, "Ready to play", 10, 6, 128, 22, 0xFFFFFF, 16);
  whiteLabels[labelCount - 1] = true;
  lv_label_set_long_mode(homeTitle, LV_LABEL_LONG_DOT);
  homeArtist = makeLabel(homePlayer, "", 10, 30, 128, 18, 0xFFFFFF, 14);
  whiteLabels[labelCount - 1] = true;
  lv_label_set_long_mode(homeArtist, LV_LABEL_LONG_DOT);
  homePlayButton = button(homePlayer, LV_SYMBOL_PLAY, 98, 44, 42, 36, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::TogglePlayback)), &homePlayLabel);

  homeClockCard = lv_obj_create(page);
  setStaticBounds(homeClockCard, 164, 8, 148, 84);
  lv_obj_set_style_bg_opa(homeClockCard, LV_OPA_COVER, 0);
  lv_obj_set_style_radius(homeClockCard, 16, 0);
  lv_obj_set_style_border_width(homeClockCard, 0, 0);
  // Font40 digits occupy y=19..48; date glyphs start at y=54 (6px optical gap).
  homeClock = makeLabel(homeClockCard, "--:--", 8, 12, 132, 44, kText, 20);
  lv_obj_set_style_text_font(homeClock, &lv_font_montserrat_40, 0);
  lv_obj_set_style_text_align(homeClock, LV_TEXT_ALIGN_CENTER, 0);
  homeDate = makeLabel(homeClockCard, "", 8, 51, 132, 18, kMuted, 14);
  lv_obj_set_style_text_align(homeDate, LV_TEXT_ALIGN_CENTER, 0);
  lv_label_set_long_mode(homeDate, LV_LABEL_LONG_MODE_CLIP);
  for (int i = 0; i < 4; ++i) {
    homeShortcutButtons[i] = button(page, "", 8 + (i % 2) * 156, 100 + (i / 2) * 50, 148, 44, shortcut,
        reinterpret_cast<void*>(static_cast<intptr_t>(i)), &homeShortcutLabels[i]);
    lv_obj_set_style_text_font(homeShortcutLabels[i], &streamdeck_polish_14, 0);
    lv_obj_set_align(homeShortcutLabels[i], LV_ALIGN_TOP_LEFT);
    lv_label_set_long_mode(homeShortcutLabels[i], LV_LABEL_LONG_MODE_WRAP);
    homeShortcutIcons[i] = makeLabel(homeShortcutButtons[i], "", 12, 10, 24, 24, kText, 20);
    lv_obj_set_style_text_font(homeShortcutIcons[i], &cyd_icons_20, 0);
    lv_obj_set_style_text_align(homeShortcutIcons[i], LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_t* image = lv_image_create(homeShortcutIcons[i]);
    setStaticBounds(image, 0, 0, 24, 24);
    lv_obj_clear_flag(image, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_add_flag(image, LV_OBJ_FLAG_HIDDEN);
    shortcutImageRevisions[i] = 0;
  }
}

uint32_t contrastingText(uint32_t fill) {
  auto linear = [](uint32_t channel) {
    const float value = channel / 255.0f;
    return value <= 0.04045f ? value / 12.92f : std::pow((value + 0.055f) / 1.055f, 2.4f);
  };
  const float luminance = .2126f * linear((fill >> 16) & 255) + .7152f * linear((fill >> 8) & 255) + .0722f * linear(fill & 255);
  return luminance > .192f ? kLightText : kText;
}

void applyShortcutColors(const AppState& s) {
  for (int i = 0; i < 6; ++i) {
    lv_obj_t* tile = shortcutButtons[i] ? shortcutButtons[i] : (i < 4 ? homeShortcutButtons[i] : nullptr);
    lv_obj_t* label = shortcutLabels[i] ? shortcutLabels[i] : (i < 4 ? homeShortcutLabels[i] : nullptr);
    if (!tile || !label) continue;
    lv_obj_t* icon = shortcutIcons[i] ? shortcutIcons[i] : (i < 4 ? homeShortcutIcons[i] : nullptr);
    uint32_t fill = resolveTileFillColor(s.appearance.accent, s.appearance.theme, s.appearance.buttonStyle, s.appearance.tileColorScheme);
    if (s.shortcuts.colors[i][0] == '#' && std::strlen(s.shortcuts.colors[i]) == 7)
      fill = static_cast<uint32_t>(std::strtoul(s.shortcuts.colors[i] + 1, nullptr, 16));
    static uint32_t lastFill[6] = {0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF,0xFFFFFFFF};
    static uint32_t foreground[6]{};
    if (fill != lastFill[i]) { foreground[i] = contrastingText(fill); lastFill[i] = fill; }
    if (!lv_color_eq(lv_obj_get_style_bg_color(tile, 0), lv_color_hex(fill))) lv_obj_set_style_bg_color(tile, lv_color_hex(fill), 0);
    if (!lv_color_eq(lv_obj_get_style_text_color(label, 0), lv_color_hex(foreground[i])))
      lv_obj_set_style_text_color(label, lv_color_hex(foreground[i]), 0);
    if (icon && !lv_color_eq(lv_obj_get_style_text_color(icon, 0), lv_color_hex(foreground[i])))
      lv_obj_set_style_text_color(icon, lv_color_hex(foreground[i]), 0);
  }
}

void buildPcPage() {
  lv_obj_t* pcHeader = makeLabel(pages[2], "PC DOCK", 12, 7, 150, 22, kText, 20);
  lv_obj_add_flag(pcHeader, LV_OBJ_FLAG_HIDDEN);
  pcChip = makeChip(pages[2], "OFFLINE", 226, 7, 82, kButton);
  lv_obj_add_flag(pcChip, LV_OBJ_FLAG_HIDDEN);

  static const char* const kMetricNames[4] = {"CPU", "RAM", "GPU", "TEMP"};
  static constexpr uint32_t kMetricColors[4] = {0x0A84FF, 0x30D158, 0xBF5AF2, 0xFF9F0A};
  const uint32_t panelColor = statePtr && statePtr->appearance.theme == 1 ? kLightPanel : kPanel;

  for (int i = 0; i < 4; ++i) {
    const int x = 12 + (i % 2) * 152;
    const int y = 12 + (i / 2) * 94;
    pcMetricCards[i] = lv_obj_create(pages[2]);
    setStaticBounds(pcMetricCards[i], x, y, 144, 86);
    lv_obj_set_style_bg_color(pcMetricCards[i], lv_color_hex(panelColor), 0);
    lv_obj_set_style_bg_opa(pcMetricCards[i], LV_OPA_COVER, 0);
    lv_obj_set_style_radius(pcMetricCards[i], 14, 0);
    lv_obj_set_style_border_width(pcMetricCards[i], 1, 0);
    lv_obj_set_style_border_color(pcMetricCards[i], lv_color_hex(0x303036), 0);

    pcMetricTitles[i] = makeLabel(pcMetricCards[i], kMetricNames[i], 10, 6, 56, 16, kMuted, 14);
    pcMetricValues[i] = makeLabel(pcMetricCards[i], "0%", 68, 4, 66, 22, kText, 20);
    lv_obj_set_style_text_align(pcMetricValues[i], LV_TEXT_ALIGN_RIGHT, 0);

    pcMetricBars[i] = lv_bar_create(pcMetricCards[i]);
    setStaticBounds(pcMetricBars[i], 10, 64, 124, 6);
    lv_bar_set_range(pcMetricBars[i], 0, 100);
    lv_obj_set_style_radius(pcMetricBars[i], 4, LV_PART_MAIN);
    lv_obj_set_style_radius(pcMetricBars[i], 4, LV_PART_INDICATOR);
    lv_obj_set_style_bg_color(pcMetricBars[i], lv_color_hex(0x272729), LV_PART_MAIN);
    lv_obj_set_style_bg_color(pcMetricBars[i], lv_color_hex(kMetricColors[i]), LV_PART_INDICATOR);
  }

  pcConnection = makeLabel(pages[2], "", 12, 184, 166, 18, kMuted, 14);
  pcAudio = makeLabel(pages[2], "", 12, 204, 166, 18, kMuted, 14);
  heapFree = makeLabel(pages[2], "", 12, 184, 166, 18, kMuted, 14);
  lv_obj_add_flag(heapFree, LV_OBJ_FLAG_HIDDEN);
  heapMin = makeLabel(pages[2], "", 12, 184, 166, 18, kMuted, 14);
  lv_obj_add_flag(heapMin, LV_OBJ_FLAG_HIDDEN);
  heapLargest = makeLabel(pages[2], "", 12, 184, 166, 18, kMuted, 14);
  lv_obj_add_flag(heapLargest, LV_OBJ_FLAG_HIDDEN);
  otaStatus = makeLabel(pages[2], "", 12, 184, 166, 18, kMuted, 14);
  lv_obj_add_flag(otaStatus, LV_OBJ_FLAG_HIDDEN);
  lv_obj_add_flag(pcConnection, LV_OBJ_FLAG_HIDDEN);
  lv_obj_add_flag(pcAudio, LV_OBJ_FLAG_HIDDEN);
}

void brightnessChanged(lv_event_t* e) {
  const int value = lv_slider_get_value(static_cast<lv_obj_t*>(lv_event_get_target(e)));
  queuePtr->push({IntentType::SetBrightness, static_cast<int16_t>(value)});
}

void closeTouchTest(lv_event_t*) {
  lv_obj_add_flag(touchTestPanel, LV_OBJ_FLAG_HIDDEN);
}

void buildInfoPage() {
  lv_obj_t* infoHeader = makeLabel(pages[3], "Device", 12, 7, 110, 22, kText, 20);
  lv_obj_add_flag(infoHeader, LV_OBJ_FLAG_HIDDEN);
  infoChip = makeChip(pages[3], "LOCAL", 238, 7, 70, kButton);
  lv_obj_add_flag(infoChip, LV_OBJ_FLAG_HIDDEN);
  infoFw = makeLabel(pages[3], "", 12, 4, 296, 18, kText, 14);
  infoWifi = makeLabel(pages[3], "", 12, 22, 296, 18, kMuted, 14);
  infoIp = makeLabel(pages[3], "", 12, 40, 296, 18, kMuted, 14);
  infoPc = makeLabel(pages[3], "", 12, 58, 296, 18, kMuted, 14);
  infoAgent = nullptr;
  infoSd = makeLabel(pages[3], "", 12, 76, 296, 18, kMuted, 14);
  infoUptime = nullptr;
  button(pages[3], "Calibrate touch", 12, 100, 144, 32, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::Recalibrate)));
  button(pages[3], "Touch test", 164, 100, 144, 32, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::TestTouch)));
  button(pages[3], "Restart", 12, 140, 144, 28, enqueue,
      reinterpret_cast<void*>(static_cast<intptr_t>(IntentType::RestartDevice)));
  lv_obj_t* reset = button(pages[3], "Reset (hold)", 164, 140, 144, 28, nullptr, nullptr);
  lv_obj_add_event_cb(reset, showFactoryResetDialog, LV_EVENT_LONG_PRESSED, nullptr);
  brightnessLabel = makeLabel(pages[3], "Brightness", 12, 176, 98, 18, kMuted, 14);
  brightnessSlider = lv_slider_create(pages[3]);
  setStaticBounds(brightnessSlider, 122, 184, 174, 5);
  lv_slider_set_range(brightnessSlider, 10, 100);
  lv_slider_set_value(brightnessSlider, statePtr->ui.localBrightness ? statePtr->ui.localBrightness : statePtr->appearance.brightness, LV_ANIM_OFF);
  lv_obj_set_ext_click_area(brightnessSlider, 10);
  lv_obj_add_event_cb(brightnessSlider, brightnessChanged, LV_EVENT_VALUE_CHANGED, nullptr);

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
  button(resetDialog, "CANCEL", 16, 112, 98, 40, hideFactoryResetDialog, nullptr);
  button(resetDialog, "CONFIRM", 132, 112, 120, 40, confirmFactoryReset, nullptr);
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
  button(overlay, "+", 15, 62, 54, 40, volumeStep,
      reinterpret_cast<void*>(static_cast<intptr_t>(5)));
  overlayVolumePercent = makeLabel(overlay, "", 82, 72, 68, 20, kText, 16);
  button(overlay, "-", 161, 62, 54, 40, volumeStep,
      reinterpret_cast<void*>(static_cast<intptr_t>(-5)));
  lv_obj_add_flag(overlay, LV_OBJ_FLAG_HIDDEN);
}

void clearPageWidgets() {
  // Shared overlays occupy the prefix; discard every reference to the deleted page.
  while (buttonCount > sharedButtonCount) buttons[--buttonCount] = nullptr;
  while (labelCount > sharedLabelCount) labels[--labelCount] = nullptr;
  homePlayer = homeClockCard = homeCoverImage = homePlayButton = nullptr;
  homeClock = homeDate = homeTitle = homeArtist = homePlayLabel = nullptr;
  for (auto& label : homeShortcutLabels) label = nullptr;
  for (auto& tile : homeShortcutButtons) tile = nullptr;
  for (auto& icon : homeShortcutIcons) icon = nullptr;
  brightnessLabel = brightnessSlider = nullptr;
  coverPanel = mediaTitle = mediaArtist = mediaStatus = mediaChip = mediaHint = nullptr;
  mediaTimeLeft = mediaTimeRight = coverBadge = coverStatus = coverImage = nullptr;
  prevButton = playButton = nextButton = nullptr;
  playButtonLabel = mediaProgress = muteButton = volumeButtonLabel = mediaVolumeSlider = volumeSliderLabel = nullptr;
  for (auto& btn : shortcutButtons) btn = nullptr;
  for (auto& label : shortcutLabels) label = nullptr;
  for (auto& icon : shortcutIcons) icon = nullptr;
  for (int i = 0; i < 4; ++i) {
    pcMetricCards[i] = pcMetricTitles[i] = pcMetricValues[i] = pcMetricBars[i] = nullptr;
  }
  appliedShortcutsLayout = 0xFF;
  pcConnection = pcChip = pcAudio = heapFree = heapMin = heapLargest = otaStatus = nullptr;
  infoFw = infoWifi = infoIp = infoPc = infoAgent = infoSd = infoUptime = infoChip = nullptr;
}

void buildPage(Page page) {
  // Free the previous tree before allocating its replacement: no two pages coexist.
  for (auto& oldPage : pages) {
    if (oldPage) lv_obj_delete(oldPage);
    oldPage = nullptr;
  }
  clearPageWidgets();
  lv_obj_t* container = lv_obj_create(root);
  pages[pageIndex(page)] = container;
  setStaticBounds(container, 0, 0, kScreenWidth, kScreenHeight);
  lv_obj_set_style_bg_opa(container, LV_OPA_TRANSP, 0);
  lv_obj_set_style_border_width(container, 0, 0);
  // Keep navigation dots, shared dialogs and app-owned overlays above the page.
  lv_obj_move_to_index(container, 0);
  switch (page) {
    case Page::Home: buildHomePage(); break;
    case Page::Media: buildMediaPage(); break;
    case Page::Shortcuts: buildShortcutsPage(); break;
    case Page::Pc: buildPcPage(); break;
    case Page::Info: buildInfoPage(); break;
  }
  // Newly created widgets must receive the current appearance even if it did not change.
  appliedTheme = 0xFF;
}

void build() {
  root = lv_scr_act();
  setStaticBounds(root, 0, 0, kScreenWidth, kScreenHeight);
  lv_obj_set_style_bg_color(root, lv_color_hex(statePtr ? statePtr->appearance.background : kBg), 0);
  lv_obj_set_style_bg_opa(root, LV_OPA_COVER, 0);
  buildNavBar();
  for (int i = 0; i < kPageCount; ++i) {
    dots[i] = lv_obj_create(root);
    setStaticBounds(dots[i], 128 + i * 16, 231, 6, 6);
    lv_obj_set_style_radius(dots[i], LV_RADIUS_CIRCLE, 0);
    lv_obj_set_style_border_width(dots[i], 0, 0);
    lv_obj_add_flag(dots[i], LV_OBJ_FLAG_HIDDEN);
  }
  buildOverlay();
  buildFactoryResetDialog();
  touchTestPanel = lv_obj_create(root);
  setStaticBounds(touchTestPanel, 0, 0, 320, 240);
  lv_obj_set_style_bg_color(touchTestPanel, lv_color_hex(kBg), 0);
  lv_obj_set_style_bg_opa(touchTestPanel, LV_OPA_COVER, 0);
  makeLabel(touchTestPanel, "Touch test", 12, 12, 296, 24, kText, 20);
  touchTestLabel = makeLabel(touchTestPanel, "Touch anywhere to test", 12, 80, 296, 40, kText, 20);
  button(touchTestPanel, "Done", 104, 184, 112, 40, closeTouchTest, nullptr);
  lv_obj_add_flag(touchTestPanel, LV_OBJ_FLAG_HIDDEN);
  standbyPanel = lv_obj_create(root);
  setStaticBounds(standbyPanel, 0, 0, 320, 240);
  lv_obj_set_style_bg_color(standbyPanel, lv_color_hex(kBg), 0);
  lv_obj_set_style_bg_opa(standbyPanel, LV_OPA_COVER, 0);
  standbyClock = makeLabel(standbyPanel, "--:--", 16, 62, 288, 68, kText, 20);
  lv_obj_set_style_text_font(standbyClock, &lv_font_montserrat_48, 0);
  lv_obj_set_style_text_align(standbyClock, LV_TEXT_ALIGN_CENTER, 0);
  standbyDate = makeLabel(standbyPanel, "", 12, 136, 296, 24, kMuted, 16);
  lv_obj_set_style_text_align(standbyDate, LV_TEXT_ALIGN_CENTER, 0);
  for (uint8_t i = 0; i < labelCount; ++i) if (labels[i] == standbyClock || labels[i] == standbyDate) whiteLabels[i] = true;
  lv_obj_t* wake = makeLabel(standbyPanel, "Touch to wake", 12, 200, 296, 20, kMuted, 14);
  whiteLabels[labelCount - 1] = true;
  lv_obj_set_style_text_align(wake, LV_TEXT_ALIGN_CENTER, 0);
  lv_obj_add_flag(standbyPanel, LV_OBJ_FLAG_HIDDEN);
  sharedButtonCount = buttonCount;
  sharedLabelCount = labelCount;
}

}  // namespace

namespace streamdeck {

void uiBegin(AppState& state, IntentQueue& intents) {
  statePtr = &state;
  queuePtr = &intents;
  configureLongPressTiming();
  build();
  uiSetPage(state.ui.page);
}

void uiRefresh(AppState& s) {
  char buffer[64];
  const bool mediaOnline = s.connection.online;
  const uint32_t positionMs = mediaPositionAt(s.media, lv_tick_get());
  const uint32_t progress = s.media.durationMs
      ? static_cast<uint32_t>((static_cast<uint64_t>(positionMs) * 10000) / s.media.durationMs)
      : 0;
  const bool hasTrack = s.media.title[0] &&
      std::strcmp(s.media.title, "No track") != 0 &&
      std::strcmp(s.media.title, "Waiting for PC") != 0;

  applyAppearanceStyles(s);
  if (s.ui.standby && !s.connection.agentConnected) {
    char clock[8] = "--:--";
    if (s.system.hostLocalMinutes >= 0) {
      const uint32_t minutes = (s.system.hostLocalMinutes + (s.device.uptimeSeconds - s.system.hostTimeSyncedAtSec) / 60) % 1440;
      std::snprintf(clock, sizeof(clock), "%02u:%02u", static_cast<unsigned>(minutes / 60), static_cast<unsigned>(minutes % 60));
    }
    setLabelText(standbyClock, clock);
    setLabelText(standbyDate, s.system.hostDate);
    lv_obj_clear_flag(standbyPanel, LV_OBJ_FLAG_HIDDEN);
  } else lv_obj_add_flag(standbyPanel, LV_OBJ_FLAG_HIDDEN);

  if (navClockLabel || homeClock) {
    if (s.system.hostLocalMinutes >= 0 && s.system.hostLocalMinutes < 1440) {
      const uint32_t elapsedMin = s.device.uptimeSeconds >= s.system.hostTimeSyncedAtSec
          ? (s.device.uptimeSeconds - s.system.hostTimeSyncedAtSec) / 60UL
          : 0UL;
      const uint32_t totalMin = (static_cast<uint32_t>(s.system.hostLocalMinutes) + elapsedMin) % 1440UL;
      std::snprintf(buffer, sizeof(buffer), "%02lu:%02lu",
          static_cast<unsigned long>(totalMin / 60UL),
          static_cast<unsigned long>(totalMin % 60UL));
    } else {
      const uint32_t upMin = (s.device.uptimeSeconds / 60UL) % 1440UL;
      std::snprintf(buffer, sizeof(buffer), "%02lu:%02lu",
          static_cast<unsigned long>(upMin / 60UL),
          static_cast<unsigned long>(upMin % 60UL));
    }
    setLabelText(navClockLabel, s.system.hostLocalMinutes >= 0 ? buffer : "--:--");
    setLabelText(homeClock, s.system.hostLocalMinutes >= 0 ? buffer : "--:--");
  }

  setLabelText(homeTitle, hasTrack ? s.media.title : "Ready to play");
  setLabelText(homeArtist, hasTrack ? s.media.artist : (mediaOnline ? "Connected" : "Offline"));
  char dateText[20] = "";
  char weekday[16]{}, month[16]{};
  unsigned day = 0;
  if (std::sscanf(s.system.hostDate, "%15[^,], %u %15s", weekday, &day, month) == 3 && day >= 1 && day <= 31)
    std::snprintf(dateText, sizeof(dateText), "%.3s, %.3s %u", weekday, month, day);
  setLabelText(homeDate, dateText);
  setLabelText(homePlayLabel, s.media.playing ? LV_SYMBOL_PAUSE : LV_SYMBOL_PLAY);
  for (int i = 0; i < 4; ++i)
    updateShortcutTile(s.shortcuts, i, homeShortcutButtons[i], homeShortcutLabels[i], homeShortcutIcons[i], true);
  setChip(mediaChip, mediaOnline ? "PC ONLINE" : "PC OFFLINE",
      mediaOnline ? kOk : kButton);

  setLabelText(mediaTitle, hasTrack ? s.media.title : (mediaOnline ? "Ready to play" : "Waiting for PC"));
  setLabelText(mediaArtist, hasTrack ? (s.media.artist[0] ? s.media.artist : s.media.album) : "");
  setLabelText(mediaStatus,
      mediaOnline ? (s.media.playing ? "Playing" : (hasTrack ? "Paused" : "PC online")) : "Offline");
  const bool coverVisible = coverSource && s.media.cover.status == CoverStatus::Available && s.media.cover.rendered;
  setLabelText(coverBadge, coverVisible ? "ART" : LV_SYMBOL_AUDIO);
  if (homeCoverImage) {
    if (coverVisible) lv_obj_clear_flag(homeCoverImage, LV_OBJ_FLAG_HIDDEN);
    else lv_obj_add_flag(homeCoverImage, LV_OBJ_FLAG_HIDDEN);
  }
  if (coverImage) {
    if (coverVisible) lv_obj_clear_flag(coverImage, LV_OBJ_FLAG_HIDDEN);
    else lv_obj_add_flag(coverImage, LV_OBJ_FLAG_HIDDEN);
  }
  const char* coverText = hasTrack ? "No artwork" : (mediaOnline ? "Ready to play" : "Waiting for PC");
  if (s.media.cover.status == CoverStatus::Pending) coverText = "Loading...";
  else if (s.media.cover.status == CoverStatus::Available && s.media.cover.rendered) coverText = "Artwork ready";
  else if (s.media.cover.status == CoverStatus::Available) coverText = "No artwork";
  else if (s.media.cover.status == CoverStatus::Failed) coverText = "No artwork";
  setLabelText(coverStatus, coverText);
  for (lv_obj_t* placeholder : {coverBadge, coverStatus}) {
    if (!placeholder) continue;
    if (coverVisible) lv_obj_add_flag(placeholder, LV_OBJ_FLAG_HIDDEN);
    else lv_obj_clear_flag(placeholder, LV_OBJ_FLAG_HIDDEN);
  }
  setLabelText(mediaHint, mediaOnline ? "" : "Offline actions queue");
  setLabelText(playButtonLabel, s.media.playing ? LV_SYMBOL_PAUSE : LV_SYMBOL_PLAY);
  if (mediaProgress) lv_bar_set_value(mediaProgress, progress > 10000 ? 10000 : progress, LV_ANIM_OFF);
  formatTime(buffer, sizeof(buffer), positionMs);
  setLabelText(mediaTimeLeft, buffer);
  formatTime(buffer, sizeof(buffer), s.media.durationMs);
  setLabelText(mediaTimeRight, buffer);

  setLabelText(volumeButtonLabel, s.audio.muted ? LV_SYMBOL_MUTE : LV_SYMBOL_VOLUME_MAX);
  if (muteButton) {
    const uint32_t targetColor = s.audio.muted ? kFeedback : (statePtr && statePtr->appearance.theme == 1 ? kLightButton : kButton);
    if (!lv_color_eq(lv_obj_get_style_bg_color(muteButton, 0), lv_color_hex(targetColor))) {
      lv_obj_set_style_bg_color(muteButton, lv_color_hex(targetColor), 0);
    }
  }
  if (volumeButtonLabel) {
    const lv_color_t volumeText = lv_color_hex(s.audio.muted ? 0xFFFFFF : textColor());
    if (!lv_color_eq(lv_obj_get_style_text_color(volumeButtonLabel, 0), volumeText))
      lv_obj_set_style_text_color(volumeButtonLabel, volumeText, 0);
  }
  // An older Windows echo must not move the thumb under the user's finger.
  const bool draggingVolume = mediaVolumeSlider && lv_obj_has_state(mediaVolumeSlider, LV_STATE_PRESSED);
  if (mediaVolumeSlider && !draggingVolume)
    lv_slider_set_value(mediaVolumeSlider, s.audio.reportedVolume, LV_ANIM_OFF);
  const unsigned displayedVolume = draggingVolume ? lv_slider_get_value(mediaVolumeSlider) : s.audio.reportedVolume;
  std::snprintf(buffer, sizeof(buffer), "%u%%", displayedVolume);
  setLabelText(volumeSliderLabel, buffer);
  std::snprintf(buffer, sizeof(buffer), "%s %u%%",
      s.audio.available ? "Windows volume" : "Queued volume", s.audio.reportedVolume);
  setLabelText(overlayStatus, buffer);
  std::snprintf(buffer, sizeof(buffer), "%u%%", s.audio.reportedVolume);
  setLabelText(overlayVolumePercent, buffer);
  if (s.ui.volumeOpen) lv_obj_clear_flag(overlay, LV_OBJ_FLAG_HIDDEN);
  else lv_obj_add_flag(overlay, LV_OBJ_FLAG_HIDDEN);

  applyShortcutsLayout(s);
  for (int i = 0; i < 6; ++i)
    updateShortcutTile(s.shortcuts, i, shortcutButtons[i], shortcutLabels[i], shortcutIcons[i], false);
  applyShortcutColors(s);

  setChip(pcChip, s.connection.online ? "ONLINE" : "OFFLINE",
      s.connection.online ? kOk : kButton);
  setChip(infoChip, s.connection.wifiConnected ? "WIFI" : "LOCAL",
      s.connection.wifiConnected ? kFeedback : kButton);

  if (pcMetricValues[0]) {
    std::snprintf(buffer, sizeof(buffer), "%u%%", s.system.cpuPercent);
    setLabelText(pcMetricValues[0], buffer);
    if (pcMetricBars[0]) lv_bar_set_value(pcMetricBars[0], s.system.cpuPercent, LV_ANIM_OFF);

    std::snprintf(buffer, sizeof(buffer), "%u%%", s.system.ramPercent);
    setLabelText(pcMetricValues[1], buffer);
    if (pcMetricBars[1]) lv_bar_set_value(pcMetricBars[1], s.system.ramPercent, LV_ANIM_OFF);

    std::snprintf(buffer, sizeof(buffer), "%u%%", s.system.gpuPercent);
    setLabelText(pcMetricValues[2], buffer);
    if (pcMetricBars[2]) lv_bar_set_value(pcMetricBars[2], s.system.gpuPercent, LV_ANIM_OFF);

    const uint8_t tempVal = s.system.tempCelsius > 0
        ? s.system.tempCelsius
        : static_cast<uint8_t>(38 + (s.system.cpuPercent * 35) / 100);
    std::snprintf(buffer, sizeof(buffer), "%u C", tempVal);
    setLabelText(pcMetricValues[3], buffer);
    if (pcMetricBars[3]) lv_bar_set_value(pcMetricBars[3], tempVal > 100 ? 100 : tempVal, LV_ANIM_OFF);
  }

  setLabelText(pcConnection, s.connection.online ? "Agent online" : "Agent offline - actions will queue");
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

  std::snprintf(buffer, sizeof(buffer), "FW %s  |  Calibration: %s", s.device.firmwareVersion, s.ui.calibrationValid ? "OK" : "Needed");
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
  std::snprintf(buffer, sizeof(buffer), "Agent: %s  |  %s",
      s.connection.agentConnected ? "Connected" : "Offline", s.connection.agentHost[0] ? s.connection.agentHost : "USB / Wi-Fi");
  setLabelText(infoPc, buffer);
  std::snprintf(buffer, sizeof(buffer), "Agent   %s",
      s.connection.agentHost[0] ? s.connection.agentHost : "Unavailable");
  setLabelText(infoAgent, buffer);
  if (s.storage.mounted) {
    std::snprintf(buffer, sizeof(buffer), "SD %s / %lu MB  |  Up %lum",
        s.storage.ready ? "OK" : "Mounted",
        static_cast<unsigned long>(s.storage.freeMb), static_cast<unsigned long>(s.device.uptimeSeconds / 60));
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
    static constexpr Page dockOrder[] = {Page::Home, Page::Shortcuts, Page::Media, Page::Pc};
    const Page page = dockOrder[i];
    const bool isCurrent = s.ui.page == page;
    const bool isVisible = true;

    if (page != Page::Home) setLabelText(navLabels[i], pageTabTitle(page));

    if (isVisible) {
      lv_obj_clear_flag(navButtons[i], LV_OBJ_FLAG_HIDDEN);
    } else {
      lv_obj_add_flag(navButtons[i], LV_OBJ_FLAG_HIDDEN);
    }

    const uint32_t activeBg = s.appearance.accent;
    const uint32_t inactiveBg = s.appearance.theme == 1 ? kLightMutedButton : kButton;
    const uint32_t targetBg = isCurrent ? activeBg : inactiveBg;
    const uint32_t targetText = isCurrent ? contrastingText(activeBg) : (s.appearance.theme == 1 ? 0x58616E : kMuted);

    if (!lv_color_eq(lv_obj_get_style_bg_color(navButtons[i], 0), lv_color_hex(targetBg))) {
      lv_obj_set_style_bg_color(navButtons[i], lv_color_hex(targetBg), 0);
    }
    if (!lv_color_eq(lv_obj_get_style_text_color(navLabels[i], 0), lv_color_hex(targetText))) {
      lv_obj_set_style_text_color(navLabels[i], lv_color_hex(targetText), 0);
    }
  }


}

void uiShowTouchTest() {
  lv_obj_clear_flag(touchTestPanel, LV_OBJ_FLAG_HIDDEN);
}

void uiReportTouch(int16_t x, int16_t y) {
  if (!touchTestPanel || lv_obj_has_flag(touchTestPanel, LV_OBJ_FLAG_HIDDEN)) return;
  char text[40];
  std::snprintf(text, sizeof(text), "X: %d    Y: %d", x, y);
  setLabelText(touchTestLabel, text);
}

void uiSetCoverImage(const lv_image_dsc_t* image) {
  coverSource = image;
  for (lv_obj_t* widget : {coverImage, homeCoverImage}) {
    if (!widget) continue;
    lv_image_set_src(widget, image);
    if (!image) lv_obj_add_flag(widget, LV_OBJ_FLAG_HIDDEN);
  }
}

void uiSetPage(Page page) {
  if (!statePtr) return;
  if (page != Page::Home && (pageIndex(page) >= kPageCount || !statePtr->appearance.pageVisible[pageIndex(page)])) {
    page = Page::Info;
  }
  statePtr->ui.page = page;
  if (!pages[pageIndex(page)]) buildPage(page);
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
