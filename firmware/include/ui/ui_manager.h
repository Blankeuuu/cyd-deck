#pragma once
#include "ui/app_state.h"
#include "ui/gesture_recognizer.h"
#include <lvgl.h>
namespace streamdeck {
void uiShowTouchTest();
void uiReportTouch(int16_t x, int16_t y);
void uiBegin(AppState& state, IntentQueue& intents);
void uiRefresh(AppState& state);
void uiSetCoverImage(const lv_image_dsc_t* image);
void uiSetPage(Page page);
void uiNavigate(int8_t delta);
}
