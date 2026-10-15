#include "ui/lvgl_port.h"
#include <Arduino.h>
#include <draw/sw/lv_draw_sw_utils.h>
#include <esp_heap_caps.h>
#include <cstring>
namespace {
DisplayHal* activeDisplay=nullptr;
lv_color_t* drawBuffer=nullptr;
constexpr uint16_t kDrawBufferPixels = 320 * 10;
uint32_t tickNow() { return millis(); }
void flush(lv_display_t* display, const lv_area_t* area, uint8_t* px) {
  (void)display;
  const int32_t width=area->x2-area->x1+1;
  const int32_t height=area->y2-area->y1+1;
  lv_draw_sw_rgb565_swap(px,width*height);
  activeDisplay->pushImage(area->x1,area->y1,width,height,reinterpret_cast<uint16_t*>(px));
  lv_display_flush_ready(display);
}
}
namespace streamdeck {
void lvglPortBegin(DisplayHal& display) {
  activeDisplay=&display;
  lv_init();
  lv_tick_set_cb(tickNow);
  drawBuffer = static_cast<lv_color_t*>(heap_caps_malloc(
      sizeof(lv_color_t) * kDrawBufferPixels, MALLOC_CAP_INTERNAL | MALLOC_CAP_DMA));
  if (!drawBuffer) abort();
  lv_display_t* d=lv_display_create(320,240);
  lv_display_set_color_format(d,LV_COLOR_FORMAT_RGB565);
  lv_display_set_buffers(d,drawBuffer,nullptr,
      sizeof(lv_color_t) * kDrawBufferPixels, LV_DISPLAY_RENDER_MODE_PARTIAL);
  lv_display_set_flush_cb(d,flush);
}
}
