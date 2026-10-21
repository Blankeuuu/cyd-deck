#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "lvgl.h"
#include "src/font/lv_font_fmt_txt.h"

const void* lv_font_get_bitmap_fmt_txt(lv_font_glyph_dsc_t* g_dsc, lv_draw_buf_t* draw_buf) {
  (void)g_dsc;
  (void)draw_buf;
  return 0;
}

bool lv_font_get_glyph_dsc_fmt_txt(const lv_font_t* font, lv_font_glyph_dsc_t* dsc_out,
                                   uint32_t letter, uint32_t letter_next) {
  (void)font;
  (void)dsc_out;
  (void)letter;
  (void)letter_next;
  return false;
}

LV_FONT_DECLARE(streamdeck_polish_14)
LV_FONT_DECLARE(streamdeck_polish_16)
LV_FONT_DECLARE(streamdeck_polish_20)
LV_FONT_DECLARE(cyd_icons_20)

static bool cmap_contains(const lv_font_fmt_txt_cmap_t* cmap, uint32_t codepoint) {
  if (codepoint < cmap->range_start || codepoint >= cmap->range_start + cmap->range_length) return false;
  const uint32_t relative = codepoint - cmap->range_start;
  if (cmap->unicode_list == NULL) return true;
  for (uint32_t i = 0; i < cmap->list_length; ++i) {
    if (cmap->unicode_list[i] == relative) return true;
  }
  return false;
}

static void assert_glyph_present_in_actual_font_data(const lv_font_t* font, uint32_t codepoint) {
  const lv_font_fmt_txt_dsc_t* dsc = (const lv_font_fmt_txt_dsc_t*)font->dsc;
  assert(dsc != NULL);
  assert(dsc->cmaps != NULL);
  for (uint32_t i = 0; i < dsc->cmap_num; ++i) {
    if (cmap_contains(&dsc->cmaps[i], codepoint)) return;
  }
  assert(!"missing glyph in generated LVGL cmap");
}

static void assert_polish_alphabet_present(const lv_font_t* font) {
  static const uint32_t polish[] = {
      0x0104, 0x0105, 0x0106, 0x0107, 0x0118, 0x0119,
      0x0141, 0x0142, 0x0143, 0x0144, 0x00D3, 0x00F3,
      0x015A, 0x015B, 0x0179, 0x017A, 0x017B, 0x017C};
  for (uint32_t i = 0; i < sizeof(polish) / sizeof(polish[0]); ++i) {
    assert_glyph_present_in_actual_font_data(font, polish[i]);
  }
}

/* Width from actual font descriptors, not the UI fake's estimated advance. */
static unsigned ascii_width(const lv_font_t* font, const char* text) {
  const lv_font_fmt_txt_dsc_t* dsc = (const lv_font_fmt_txt_dsc_t*)font->dsc;
  unsigned width16 = 0;
  for (const char* c = text; *c; ++c) {
    bool found = false;
    for (unsigned i = 0; i < dsc->cmap_num; ++i) {
      const lv_font_fmt_txt_cmap_t* cmap = &dsc->cmaps[i];
      if (!cmap_contains(cmap, (unsigned char)*c)) continue;
      assert(cmap->unicode_list == NULL); /* All checked strings are ASCII in FORMAT0_TINY. */
      width16 += dsc->glyph_dsc[cmap->glyph_id_start + (unsigned char)*c - cmap->range_start].adv_w;
      found = true; break;
    }
    assert(found);
  }
  return (width16 + 15) / 16;
}

int main(void) {
  assert_glyph_present_in_actual_font_data(&lv_font_montserrat_20, 0x00B0);
  assert_glyph_present_in_actual_font_data(&cyd_icons_20, 0xF11C); /* hotkey keyboard */
  assert_glyph_present_in_actual_font_data(&cyd_icons_20, 0xF07B); /* folder */
  assert_glyph_present_in_actual_font_data(&cyd_icons_20, 0xF013); /* settings */
  const uint32_t shortcut_glyphs[] = {0xF005, 0xF023, 0xF0AC, 0xF120, 0xF15B, 0xF2D0};
  for (unsigned i = 0; i < sizeof(shortcut_glyphs) / sizeof(shortcut_glyphs[0]); ++i)
    assert_glyph_present_in_actual_font_data(&cyd_icons_20, shortcut_glyphs[i]);
  const lv_font_fmt_txt_dsc_t* icons = (const lv_font_fmt_txt_dsc_t*)cyd_icons_20.dsc;
  for (unsigned i = 1; i <= icons->cmaps[0].list_length; ++i) {
    assert(icons->glyph_dsc[i].box_w <= 24 && icons->glyph_dsc[i].box_h <= 24);
    assert(icons->glyph_dsc[i].box_w > 0 && icons->glyph_dsc[i].box_h > 0);
  }
  unsigned widest_digit = 0;
  for (char digit = '0'; digit <= '9'; ++digit) {
    char text[] = {digit, 0};
    unsigned width = ascii_width(&streamdeck_polish_14, text);
    if (width > widest_digit) widest_digit = width;
  }
  unsigned clock_width = 4 * widest_digit + ascii_width(&streamdeck_polish_14, ":");
  unsigned dock_width = 30 + 4 * 6;
  const char* nav[] = {"88:88", "Deck", "Media", "PC"};
  for (unsigned i = 0; i < 4; ++i) dock_width += (i == 0 ? clock_width : ascii_width(&streamdeck_polish_14, nav[i])) + 20;
  assert(dock_width <= 304);
  for (unsigned minute = 0; minute < 1440; ++minute) {
    char time[6]; snprintf(time, sizeof(time), "%02u:%02u", minute / 60, minute % 60);
    assert(ascii_width(&streamdeck_polish_14, time) <= clock_width);
  }
  const char* days[] = {"Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"};
  const char* months[] = {"Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"};
  for (unsigned d = 0; d < 7; ++d) for (unsigned m = 0; m < 12; ++m) for (unsigned day = 1; day <= 31; ++day) {
    char date[20]; snprintf(date, sizeof(date), "%s, %s %u", days[d], months[m], day);
    assert(ascii_width(&streamdeck_polish_14, date) <= 132);
  }
  assert(ascii_width(&streamdeck_polish_14, "TEMP") <= 56);
  assert(ascii_width(&streamdeck_polish_20, "100%") <= 66);
  assert(ascii_width(&streamdeck_polish_16, "Calibrate touch") <= 132);
  assert(ascii_width(&streamdeck_polish_14, "Waiting for PC") <= 112);
  assert(ascii_width(&streamdeck_polish_16, "Ready to play") <= 128);
  assert(ascii_width(&streamdeck_polish_14, "Offline") <= 156);
  const char* settings_tabs[] = {"Display", "Touch", "Device"};
  for (unsigned i = 0; i < 3; ++i)
    assert(ascii_width(&streamdeck_polish_16, settings_tabs[i]) <= 84);
  assert(ascii_width(&streamdeck_polish_16, "Factory reset") <= 136);
  assert(ascii_width(&streamdeck_polish_16, "Reset device") <= 104);
  assert(ascii_width(&streamdeck_polish_16, "Connection & status") <= 292);
  assert(ascii_width(&streamdeck_polish_14, "Test response or align five targets.") <= 296);
  assert(ascii_width(&streamdeck_polish_14, "Factory reset: hold for 3 seconds.") <= 296);
  assert(ascii_width(&streamdeck_polish_14, "Erases Wi-Fi, pairing, calibration") <= 248);
  assert(ascii_width(&streamdeck_polish_14, "The device will reconnect shortly.") <= 248);
  assert(ascii_width(&streamdeck_polish_20, "Windows PC") <= 184);
  assert(ascii_width(&streamdeck_polish_16, "Memory (RAM)") <= 176);
  assert(ascii_width(&streamdeck_polish_14, "Waiting for new PC data") <= 296);
  assert(ascii_width(&streamdeck_polish_14, "10.0 / 16.0 GB used") <= 280);

  assert_glyph_present_in_actual_font_data(&lv_font_montserrat_16, 0xF026); /* mute icon */
  assert_glyph_present_in_actual_font_data(&lv_font_montserrat_16, 0xF028); /* speaker icon */
  assert_polish_alphabet_present(&streamdeck_polish_14);
  assert_polish_alphabet_present(&streamdeck_polish_16);
  assert_polish_alphabet_present(&streamdeck_polish_20);

  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0x005A); /* Z */
  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0xF001); /* LV_SYMBOL_AUDIO */
  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0xF028); /* LV_SYMBOL_VOLUME_MAX */
  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0xF048); /* LV_SYMBOL_PREV */
  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0xF04B); /* LV_SYMBOL_PLAY */
  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0xF04C); /* LV_SYMBOL_PAUSE */
  assert_glyph_present_in_actual_font_data(&streamdeck_polish_16, 0xF051); /* LV_SYMBOL_NEXT */
  return 0;
}
