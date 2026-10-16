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
  assert(appearance.pageOrder[2] == Page::Shortcuts);

  AppearanceState defaults;
  normalizeAppearance(defaults);
  assert(kPageCount == 4);
  assert(defaults.pageOrder[0] == Page::Media);
  assert(defaults.pageOrder[1] == Page::Shortcuts);

  UiState brightnessUi;
  brightnessUi.localBrightness = 42;
  applyBrightnessRevision(brightnessUi, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false);
  assert(brightnessUi.localBrightness == 0); // New saved settings, including a save made offline.
  brightnessUi.localBrightness = 55;
  applyBrightnessRevision(brightnessUi, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false);
  assert(brightnessUi.localBrightness == 55); // Reconnect preserves local correction.
  applyBrightnessRevision(brightnessUi, nullptr, false);
  assert(brightnessUi.localBrightness == 55); // Legacy hydration also preserves it.
  applyBrightnessRevision(brightnessUi, "malformed", false);
  assert(brightnessUi.localBrightness == 55);
  applyBrightnessRevision(brightnessUi, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true);
  assert(brightnessUi.localBrightness == 0); // Explicit Sync reapplies even the same value.

  MediaState timeline;
  timeline.positionMs = 27000;
  timeline.durationMs = 30000;
  timeline.playing = true;
  timeline.timelineAtMs = 1000;
  timeline.timelineAnchored = true;
  assert(mediaPositionAt(timeline, 1100) == 27100);
  assert(mediaPositionAt(timeline, 9000) == 30000);
  timeline.playing = false;
  assert(mediaPositionAt(timeline, 9000) == 27000);
  timeline.playing = true;
  timeline.timelineAtMs = UINT32_MAX - 50;
  assert(mediaPositionAt(timeline, 49) == 27100);

  AppState idleState;
  idleState.appearance.dimAfterSeconds = 30;
  idleState.appearance.screenOffAfterSeconds = 60;
  DisplayIdleDecision idle = computeDisplayIdleDecision(idleState, 29, false, false);
  assert(!idle.dimmed && !idle.off && !idle.resetIdleTimer);
  idle = computeDisplayIdleDecision(idleState, 30, false, false);
  assert(idle.dimmed && !idle.off && !idle.resetIdleTimer);
  idle = computeDisplayIdleDecision(idleState, 60, false, true);
  assert(!idle.dimmed && idle.off && !idle.resetIdleTimer);

  idleState.connection.agentConnected = true;
  idle = computeDisplayIdleDecision(idleState, 3600, false, false);
  assert(!idle.dimmed && !idle.off && idle.resetIdleTimer);
  idle = computeDisplayIdleDecision(idleState, 3600, true, false);
  assert(!idle.dimmed && !idle.off && idle.resetIdleTimer);
  idle = computeDisplayIdleDecision(idleState, 3600, false, true);
  assert(!idle.dimmed && !idle.off && idle.resetIdleTimer);
  idleState.connection.agentConnected = false;
  idleState.connection.wifiConnected = true;
  idle = computeDisplayIdleDecision(idleState, 29, false, false);
  assert(!idle.dimmed && !idle.off);
  idle = computeDisplayIdleDecision(idleState, 30, false, false);
  assert(idle.dimmed && !idle.off);
  idleState.connection.agentConnected = false;
  idle = computeDisplayIdleDecision(idleState, 0, false, false);
  assert(!idle.dimmed && !idle.off && !idle.resetIdleTimer);

  constexpr uint32_t wifiTimeoutMs = 20000;
  assert(decidePendingSetupResult(false, false, 19999, wifiTimeoutMs) == PendingSetupDecision::Wait);
  assert(decidePendingSetupResult(false, false, wifiTimeoutMs, wifiTimeoutMs) == PendingSetupDecision::Failure);
  assert(decidePendingSetupResult(true, false, wifiTimeoutMs, wifiTimeoutMs) == PendingSetupDecision::Failure);
  assert(decidePendingSetupResult(true, true, 0, wifiTimeoutMs) == PendingSetupDecision::Success);

  HttpContentLength contentLength = parseHttpContentLength(
      "Host: 192.168.4.1\015\012Content-Length: 42\015\012", 1536);
  assert(contentLength.valid && contentLength.present && contentLength.length == 42);
  contentLength = parseHttpContentLength(
      "host: 192.168.4.1\015\012content-length: 123\015\012", 1536);
  assert(contentLength.valid && contentLength.present && contentLength.length == 123);
  contentLength = parseHttpContentLength(
      "HOST: 192.168.4.1\015\012CONTENT-LENGTH:\t7\015\012", 1536);
  assert(contentLength.valid && contentLength.present && contentLength.length == 7);
  contentLength = parseHttpContentLength(
      "Host: 192.168.4.1\015\012\015\012", 1536);
  assert(contentLength.valid && !contentLength.present && contentLength.length == 0);
  contentLength = parseHttpContentLength(
      "Host: x\015\012Content-Length: abc\015\012", 1536);
  assert(!contentLength.valid);
  contentLength = parseHttpContentLength(
      "Host: x\015\012Content-Length: 1537\015\012", 1536);
  assert(!contentLength.valid);
  contentLength = parseHttpContentLength(
      "Host: x\015\012Content-Length: 999999999999999999999999\015\012", 1536);
  assert(!contentLength.valid);
  contentLength = parseHttpContentLength(
      "Content-Length: 4\015\012Content-Length: 5\015\012", 1536);
  assert(!contentLength.valid);
  contentLength = parseHttpContentLength(
      "Content-Length: 4\015\012Content-Length: 4\015\012", 1536);
  assert(contentLength.valid && contentLength.present && contentLength.length == 4);

  const char* completeGet = "GET / HTTP/1.1\015\012Host: x\015\012\015\012";
  const char* truncatedPost = "POST /save HTTP/1.1\015\012Host: x\015\012Content-Length: 5\015\012\015\012abc";
  const char* completePost = "POST /save HTTP/1.1\015\012Host: x\015\012Content-Length: 5\015\012\015\012abcde";
  const char* invalidPost = "POST /save HTTP/1.1\015\012Host: x\015\012Content-Length: nope\015\012\015\012";
  const char* missingLengthPost = "POST /save HTTP/1.1\015\012Host: x\015\012\015\012key=wrong";
  const char* undeclaredPost = "POST /save HTTP/1.1\015\012Content-Length: 0\015\012\015\012key=secret";
  const char* oversizedPost = "POST /save HTTP/1.1\015\012Content-Length: 3\015\012\015\012abc&ssid=x";
  assert(decideHttpRequestRead(completeGet, std::strlen(completeGet), 1536) ==
      HttpRequestReadDecision::Complete);
  assert(decideHttpRequestRead(truncatedPost, std::strlen(truncatedPost), 1536) ==
      HttpRequestReadDecision::Incomplete);
  assert(decideHttpRequestRead(completePost, std::strlen(completePost), 1536) ==
      HttpRequestReadDecision::Complete);
  assert(decideHttpRequestRead(invalidPost, std::strlen(invalidPost), 1536) ==
      HttpRequestReadDecision::Invalid);
  assert(decideHttpRequestRead(missingLengthPost, std::strlen(missingLengthPost), 1536) ==
      HttpRequestReadDecision::Invalid);
  assert(decideHttpRequestRead(undeclaredPost, std::strlen(undeclaredPost), 1536) ==
      HttpRequestReadDecision::Invalid);
  assert(decideHttpRequestRead(oversizedPost, std::strlen(oversizedPost), 1536) ==
      HttpRequestReadDecision::Invalid);
}
