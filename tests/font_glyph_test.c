#include <assert.h>
#include <stdbool.h>
#include <stdint.h>

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

int main(void) {
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