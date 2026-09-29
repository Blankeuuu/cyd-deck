#include <cassert>
#include "board_pins.h"
#include "ui/app_state.h"
#include "ui/gesture_recognizer.h"
using namespace streamdeck;

static_assert(board::screenWidth == 320);
static_assert(board::screenHeight == 240);
static_assert(board::landscapeRotation == 3, "CYD ST7789 app uses the user-facing landscape orientation");

int main() {
  GestureRecognizer g;
  g.begin(200,100);
  assert(g.update(180,103,true)==0);
  assert(g.update(130,102,true)==1);
  assert(g.update(90,100,true)==0);
  assert(g.update(90,100,false)==0);
  g.begin(100,100);
  assert(g.update(103,165,true)==0);
  assert(g.update(103,180,false)==0);
  g.begin(10,10);
  assert(g.update(50,50,true)==0); // diagonal is not navigation
  assert(g.update(50,50,false)==0);
  IntentQueue q; Intent out;
  assert(q.push({IntentType::Next,0}));
  assert(q.push({IntentType::SetVolume,64}));
  assert(q.push({IntentType::RefreshUi,0}));
  assert(q.push({IntentType::RestartDevice,0}));
  assert(q.push({IntentType::FactoryReset,0}));
  assert(q.pop(out) && out.type==IntentType::Next);
  assert(q.pop(out) && out.type==IntentType::SetVolume && out.value==64);
  assert(q.pop(out) && out.type==IntentType::RefreshUi);
  assert(q.pop(out) && out.type==IntentType::RestartDevice);
  assert(q.pop(out) && out.type==IntentType::FactoryReset);
  assert(!q.pop(out));

  AppearanceState appearance;
  appearance.theme = 7;
  appearance.buttonStyle = 9;
  appearance.brightness = 3;
  appearance.dimBrightness = 99;
  appearance.dimAfterSeconds = 90000;
  appearance.screenOffAfterSeconds = 90000;
  appearance.pageOrder[0] = Page::Info;
  appearance.pageOrder[1] = Page::Info;
  appearance.pageOrder[2] = Page::Media;
  appearance.pageOrder[3] = static_cast<Page>(99);
  appearance.pageVisible[pageIndex(Page::Info)] = false;
  normalizeAppearance(appearance);
  assert(appearance.theme == 0);
  assert(appearance.buttonStyle == 1);
  assert(appearance.brightness == 10);
  assert(appearance.dimBrightness == 10);
  assert(appearance.dimAfterSeconds == 86400);
  assert(appearance.screenOffAfterSeconds == 86400);
  assert(appearance.pageVisible[pageIndex(Page::Info)]);
  assert(appearance.pageOrder[0] == Page::Info);
  assert(appearance.pageOrder[1] == Page::Media);
}
