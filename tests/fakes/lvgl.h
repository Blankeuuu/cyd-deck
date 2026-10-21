#pragma once

#include <cstdint>
#include <cstddef>
#include <string>
#include <vector>
#include <algorithm>
inline void lv_obj_move_foreground(void*) {}

#define LV_SYMBOL_SETTINGS "[settings]"
#define LV_SYMBOL_AUDIO "[audio]"
#define LV_SYMBOL_PREV "[prev]"
#define LV_SYMBOL_PLAY "[play]"
#define LV_SYMBOL_PAUSE "[pause]"
#define LV_SYMBOL_NEXT "[next]"
#define LV_SYMBOL_VOLUME_MAX "[volume]"
#define LV_SYMBOL_MUTE "[mute]"

struct lv_font_t {};
#define LV_FONT_DECLARE(font_name) extern lv_font_t font_name;
inline lv_font_t lv_font_montserrat_16;
inline lv_font_t lv_font_montserrat_20;
inline lv_font_t cyd_icons_20;
inline lv_font_t lv_font_montserrat_48;
inline lv_font_t lv_font_montserrat_40;
constexpr int LV_IMAGE_ALIGN_COVER = 1;
inline lv_font_t streamdeck_polish_14;
inline lv_font_t streamdeck_polish_16;
inline lv_font_t streamdeck_polish_20;

using lv_color_t = uint32_t;

enum lv_color_format_t {
  LV_COLOR_FORMAT_RGB565 = 0,
  LV_COLOR_FORMAT_RGB565A8 = 1,
};

constexpr uint32_t LV_IMAGE_HEADER_MAGIC = 0x19;

struct lv_image_header_t {
  uint32_t magic = LV_IMAGE_HEADER_MAGIC;
  lv_color_format_t cf = LV_COLOR_FORMAT_RGB565;
  uint32_t flags = 0;
  uint32_t w = 0;
  uint32_t h = 0;
  uint32_t stride = 0;
};

struct lv_image_dsc_t {
  lv_image_header_t header;
  uint32_t data_size = 0;
  const uint8_t* data = nullptr;
};

enum {
  LV_LABEL_LONG_DOT = 0,
  LV_LABEL_LONG_MODE_CLIP = 4,
  LV_LABEL_LONG_MODE_WRAP = 5,
  LV_LABEL_LONG_MODE_DOTS = 6,
  LV_TEXT_ALIGN_AUTO = 0,
  LV_TEXT_ALIGN_LEFT = 1,
  LV_TEXT_ALIGN_CENTER = 2,
  LV_TEXT_ALIGN_RIGHT = 3,
  LV_OPA_COVER = 255,
  LV_OPA_TRANSP = 0,
  LV_RADIUS_CIRCLE = 32767,
  LV_EVENT_CLICKED = 1,
  LV_EVENT_RELEASED = 2,
  LV_EVENT_LONG_PRESSED = 3,
  LV_EVENT_VALUE_CHANGED = 4,
  LV_ANIM_OFF = 0,
  LV_OBJ_FLAG_HIDDEN = 1 << 0,
  LV_OBJ_FLAG_SCROLLABLE = 1 << 1,
  LV_OBJ_FLAG_CLICKABLE = 1 << 2,
  LV_SCROLLBAR_MODE_OFF = 0,
  LV_SCROLLBAR_MODE_AUTO = 1,
  LV_PART_MAIN = 0,
  LV_PART_INDICATOR = 1,
  LV_PART_KNOB = 2,
};

inline uint32_t lv_test_tick = 0;
inline uint32_t lv_tick_get() { return lv_test_tick; }

struct lv_event_t {
  void* user_data = nullptr;
  struct lv_obj_t* target = nullptr;
};

using lv_event_code_t = int;

struct lv_indev_t {
  uint16_t long_press_time = 400;
};

using lv_event_cb_t = void (*)(lv_event_t*);

struct lv_obj_t {
  enum class Type { Screen, Object, Label, Button, Bar, Slider, Image };

  Type type = Type::Object;
  lv_obj_t* parent = nullptr;
  std::vector<lv_obj_t*> children;
  int x = 0;
  int y = 0;
  int w = 0;
  int h = 0;
  uint32_t flags = LV_OBJ_FLAG_SCROLLABLE;
  int scrollbar_mode = LV_SCROLLBAR_MODE_AUTO;
  int pad_all = -1;
  int radius = 0;
  bool clip_corner = false;
  int ext_click_area = 0;
  int border_width = 0;
  lv_color_t border_color = 0;
  int value = 0;
  uint32_t state = 0;
  const lv_font_t* font = nullptr;
  lv_color_t bg_color = 0;
  lv_color_t indicator_color = 0;
  lv_color_t knob_color = 0;
  lv_color_t text_color = 0;
  int event_code = 0;
  int text_set_count = 0;
  int text_align = 0;
  int long_mode = LV_LABEL_LONG_MODE_CLIP;
  void* event_user_data = nullptr;
  lv_event_cb_t event_cb = nullptr;
  std::string text;
  const void* image_src = nullptr;
  int image_align = 0;
};

inline int lv_test_bg_writes = 0;
inline int lv_test_position_writes = 0;
inline std::vector<lv_obj_t*> lv_test_allocations;
inline lv_indev_t lv_test_indev;
inline lv_obj_t lv_test_screen = [] {
  lv_obj_t screen;
  screen.type = lv_obj_t::Type::Screen;
  return screen;
}();

inline lv_obj_t* lv_test_create(lv_obj_t* parent, lv_obj_t::Type type) {
  auto* object = new lv_obj_t{};
  object->type = type;
  object->parent = parent;
  if (parent) parent->children.push_back(object);
  lv_test_allocations.push_back(object);
  return object;
}

inline void lv_test_reset() {
  lv_test_tick = 0;
  for (lv_obj_t* object : lv_test_allocations) delete object;
  lv_test_allocations.clear();
  lv_test_indev = lv_indev_t{};
  lv_test_screen = lv_obj_t{};
  lv_test_screen.type = lv_obj_t::Type::Screen;
}

inline lv_obj_t* lv_test_root() { return &lv_test_screen; }

inline lv_color_t lv_color_hex(uint32_t color) { return color; }
inline lv_obj_t* lv_scr_act() { return &lv_test_screen; }
inline lv_obj_t* lv_obj_create(lv_obj_t* parent) { return lv_test_create(parent, lv_obj_t::Type::Object); }
inline lv_obj_t* lv_label_create(lv_obj_t* parent) { return lv_test_create(parent, lv_obj_t::Type::Label); }
inline lv_obj_t* lv_btn_create(lv_obj_t* parent) { return lv_test_create(parent, lv_obj_t::Type::Button); }
inline lv_obj_t* lv_bar_create(lv_obj_t* parent) { return lv_test_create(parent, lv_obj_t::Type::Bar); }
inline lv_obj_t* lv_slider_create(lv_obj_t* parent) { return lv_test_create(parent, lv_obj_t::Type::Slider); }
inline lv_obj_t* lv_image_create(lv_obj_t* parent) { return lv_test_create(parent, lv_obj_t::Type::Image); }
inline void lv_image_set_inner_align(lv_obj_t* object, int align) { object->image_align = align; }
inline void lv_image_set_src(lv_obj_t* object, const void* src) { object->image_src = src; }
inline void lv_obj_delete(lv_obj_t* object) {
  while (!object->children.empty()) lv_obj_delete(object->children.back());
  if (object->parent) {
    auto& siblings = object->parent->children;
    siblings.erase(std::remove(siblings.begin(), siblings.end(), object), siblings.end());
  }
  lv_test_allocations.erase(std::remove(lv_test_allocations.begin(), lv_test_allocations.end(), object), lv_test_allocations.end());
  delete object;
}
inline void lv_obj_move_to_index(lv_obj_t* object, int index) {
  auto& siblings = object->parent->children;
  siblings.erase(std::remove(siblings.begin(), siblings.end(), object), siblings.end());
  siblings.insert(siblings.begin() + index, object);
}

inline void lv_obj_set_pos(lv_obj_t* object, int x, int y) { ++lv_test_position_writes; object->x = x; object->y = y; }
inline void lv_obj_set_size(lv_obj_t* object, int w, int h) { object->w = w; object->h = h; }
inline void lv_obj_set_width(lv_obj_t* object, int w) { object->w = w; }
inline void lv_obj_center(lv_obj_t*) {}
inline void lv_obj_add_flag(lv_obj_t* object, uint32_t flag) { object->flags |= flag; }
inline void lv_obj_clear_flag(lv_obj_t* object, uint32_t flag) { object->flags &= ~flag; }
inline void lv_obj_set_scrollbar_mode(lv_obj_t* object, int mode) { object->scrollbar_mode = mode; }
inline void lv_obj_set_style_pad_all(lv_obj_t* object, int pad, int part) { if (part == 0) object->pad_all = pad; }
inline void lv_obj_set_style_clip_corner(lv_obj_t* object, bool clip, int) { object->clip_corner = clip; }
inline void lv_obj_set_ext_click_area(lv_obj_t* object, int area) { object->ext_click_area = area; }

inline lv_color_t lv_obj_get_style_bg_color(lv_obj_t* object, int) { return object->bg_color; }
inline lv_color_t lv_obj_get_style_text_color(lv_obj_t* object, int) { return object->text_color; }
inline bool lv_color_eq(lv_color_t a, lv_color_t b) { return a == b; }
inline void lv_obj_set_style_bg_color(lv_obj_t* object, lv_color_t color, int part) {
  ++lv_test_bg_writes;
  if (part == LV_PART_MAIN) object->bg_color = color;
  else if (part == LV_PART_INDICATOR) object->indicator_color = color;
  else if (part == LV_PART_KNOB) object->knob_color = color;
}
inline void lv_obj_set_style_bg_opa(lv_obj_t*, int, int) {}
inline void lv_obj_set_style_radius(lv_obj_t* object, int radius, int part) { if (part == 0) object->radius = radius; }
inline void lv_obj_set_style_border_color(lv_obj_t* object, lv_color_t color, int) { object->border_color = color; }
inline void lv_obj_set_style_border_width(lv_obj_t* object, int width, int) { object->border_width = width; }
inline void lv_obj_set_style_text_align(lv_obj_t* object, int align, int) { object->text_align = align; }
inline void lv_obj_set_style_text_color(lv_obj_t* object, lv_color_t color, int) { object->text_color = color; }
inline void lv_obj_set_style_text_font(lv_obj_t* object, lv_font_t* font, int) { object->font = font; }
inline const lv_font_t* lv_obj_get_style_text_font(const lv_obj_t* object, int) { return object ? object->font : nullptr; }
inline void lv_obj_set_style_shadow_width(lv_obj_t*, int, int) {}
constexpr uint32_t LV_STATE_PRESSED = 0x20;
inline bool lv_obj_has_state(lv_obj_t* object, uint32_t state) { return (object->state & state) == state; }
inline lv_obj_t* lv_obj_get_child(const lv_obj_t* object, int index) {
  return object && index >= 0 && static_cast<std::size_t>(index) < object->children.size()
      ? object->children[static_cast<std::size_t>(index)] : nullptr;
}

inline void lv_label_set_text(lv_obj_t* object, const char* text) { object->text = text ? text : ""; ++object->text_set_count; }
inline const char* lv_label_get_text(const lv_obj_t* object) { return object->text.c_str(); }
inline void lv_label_set_long_mode(lv_obj_t* obj, int mode) { obj->long_mode = mode; }
inline void lv_obj_add_event_cb(lv_obj_t* object, lv_event_cb_t cb, int code, void* user_data) {
  object->event_cb = cb;
  object->event_code = code;
  object->event_user_data = user_data;
}
inline void* lv_event_get_user_data(lv_event_t* event) { return event->user_data; }
inline lv_obj_t* lv_event_get_target(lv_event_t* event) { return event->target; }
inline lv_event_code_t lv_event_get_code(lv_event_t* event) {
  return event && event->target ? event->target->event_code : 0;
}
inline lv_indev_t* lv_indev_get_next(lv_indev_t* indev) { return indev ? nullptr : &lv_test_indev; }
inline void lv_indev_set_long_press_time(lv_indev_t* indev, uint16_t long_press_time) {
  if (indev) indev->long_press_time = long_press_time;
}
inline void lv_test_send_event(lv_obj_t* object, int code) {
  if (!object->event_cb || object->event_code != code) return;
  lv_event_t event{object->event_user_data, object};
  object->event_cb(&event);
}

inline void lv_bar_set_range(lv_obj_t*, int, int) {}
inline void lv_bar_set_value(lv_obj_t* object, int value, int) { object->value = value; }
inline void lv_slider_set_range(lv_obj_t*, int, int) {}
inline int lv_slider_get_value(lv_obj_t* object) { return object->value; }
inline void lv_slider_set_value(lv_obj_t* object, int value, int) { object->value = value; }

inline bool lv_obj_has_flag(const lv_obj_t* object, uint32_t flag) { return (object->flags & flag) != 0; }

inline int32_t lv_text_get_width(const char*, uint32_t length, const lv_font_t*, int32_t) { return static_cast<int32_t>(length) * 8; }
#define LV_SYMBOL_KEYBOARD "[keyboard]"
#define LV_SYMBOL_DIRECTORY "[directory]"
#define LV_SYMBOL_HOME "[home]"
#define LV_SYMBOL_RIGHT "[right]"
#define LV_SYMBOL_FILE "[file]"
#define LV_SYMBOL_EYE_CLOSE "[eye_close]"
#define LV_SYMBOL_OK "[ok]"

inline int lv_obj_get_x(const lv_obj_t* object) { return object->x; }
inline int lv_obj_get_y(const lv_obj_t* object) { return object->y; }
inline int lv_obj_get_width(const lv_obj_t* object) { return object->w; }
inline int lv_obj_get_height(const lv_obj_t* object) { return object->h; }
constexpr int LV_ALIGN_TOP_LEFT = 0;
inline void lv_obj_set_align(lv_obj_t*, int) {}

inline const void* lv_image_get_src(lv_obj_t* image) { return image->image_src; }
inline void lv_image_cache_drop(const void*) {}

inline void lv_obj_invalidate(lv_obj_t*) {}
