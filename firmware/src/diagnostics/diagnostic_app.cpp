#include "diagnostic_app.h"

#include <Arduino.h>
#include <Preferences.h>
#include <WiFi.h>

#include "board_pins.h"
#include "affine_calibration.h"
#include "display/display_hal.h"
#include "storage/sd_diagnostic.h"
#include "touch/xpt2046.h"

namespace {
constexpr uint16_t kBlack = 0x0000;
constexpr uint16_t kWhite = 0xFFFF;
constexpr uint16_t kRed = 0xF800;
constexpr uint16_t kGreen = 0x07E0;
constexpr uint16_t kBlue = 0x001F;
constexpr uint16_t kCyan = 0x07FF;
constexpr uint16_t kYellow = 0xFFE0;
constexpr uint16_t kMagenta = 0xF81F;
constexpr uint16_t kOrange = 0xFD20;
constexpr uint16_t kGray = 0x7BEF;
constexpr uint32_t kPwmFrequencyHz = 5000;
constexpr uint8_t kPwmResolutionBits = 8;
constexpr uint8_t kInitialBacklightPercent = 86;

DisplayHal display;
Xpt2046 touch;
SdDiagnostic sd;
Preferences preferences;

enum class Screen : uint8_t { Summary, Display, Touch, Calibration };
Screen screen = Screen::Summary;

using AffineCalibration = touchcal::Coefficients;
AffineCalibration calibration;
AffineCalibration candidateCalibration;

uint32_t lastUi = 0;
uint32_t lastSerial = 0;
uint32_t lastTouch = 0;
uint8_t colorPage = 0;
uint8_t backlightPercent = kInitialBacklightPercent;
bool backlightOn = true;
bool touchLatched = false;
RawTouch lastRaw{};
bool hasTouch = false;
touchcal::Point calibrationRaw[5]{};
uint8_t calibrationStep = 0;
char serialLine[48]{};
uint8_t serialLength = 0;

constexpr uint16_t kColors[] = {kRed, kGreen, kBlue, kWhite, kBlack};
constexpr const char* kColorNames[] = {"RED", "GREEN", "BLUE", "WHITE", "BLACK"};
constexpr int16_t kTargetX[] = {12, 307, 12, 307, 160};
constexpr int16_t kTargetY[] = {12, 12, 227, 227, 120};

void drawButton(int x, int y, int width, int height, const char* label) {
  display.drawRoundRect(x, y, width, height, 5, kGray);
  display.setTextColor(kWhite, kBlack);
  display.setTextSize(1);
  display.drawCenterString(label, x + width / 2, y + height / 2);
}

void drawSummary() {
  display.fillScreen(kBlack);
  display.setTextColor(kCyan, kBlack);
  display.setTextSize(2);
  display.drawString("CYD HARDWARE TEST", 8, 7);
  display.setTextSize(1);
  display.setTextColor(kWhite, kBlack);

  display.drawString(String("Display       ") + DisplayHal::driverName() + " (compile variant)", 8, 38);
  display.drawString(String("Resolution    ") + display.width() + " x " + display.height(), 8, 52);
  display.drawString(String("Touch         ") + (hasTouch ? "XPT2046 OK" : "XPT2046 waiting"), 8, 68);
  display.drawString(String("Calibration   ") + (calibration.valid ? "NVS saved" : "not calibrated"), 8, 82);
  display.drawString(String("microSD       ") + (sd.status().mounted ? "mounted" : sd.status().message), 8, 98);
  display.drawString(String("SD test       ") + (sd.status().testPassed ? "PASS" : "not run / failed"), 8, 112);
  display.drawString(String("Capacity      ") + static_cast<uint32_t>(sd.status().totalBytes / 1048576) + " MB", 8, 126);
  display.drawString(String("SD free       ") + static_cast<uint32_t>((sd.status().totalBytes - sd.status().usedBytes) / 1048576) + " MB", 8, 140);
  display.drawString(String("Wi-Fi         ") + (WiFi.status() == WL_CONNECTED ? "connected" : "disconnected"), 8, 156);
  display.drawString(String("RSSI          ") + (WiFi.status() == WL_CONNECTED ? String(WiFi.RSSI()) + " dBm" : "N/A"), 8, 170);
  display.drawString(String("Heap Free     ") + ESP.getFreeHeap(), 8, 184);
  display.drawString(String("Min Heap      ") + ESP.getMinFreeHeap() + " Largest " + ESP.getMaxAllocHeap(), 8, 198);

  drawButton(5, 218, 96, 19, "DISPLAY TEST");
  drawButton(112, 218, 96, 19, "TOUCH TEST");
  drawButton(219, 218, 96, 19, "SD TEST");
}

void drawDisplayTest() {
  display.fillScreen(kColors[colorPage]);
  display.setTextColor(colorPage == 3 ? kBlack : kWhite, kColors[colorPage]);
  display.setTextSize(2);
  display.drawString(kColorNames[colorPage], 8, 8);
  display.setTextSize(1);
  display.drawString("Tap to cycle RED GREEN BLUE WHITE BLACK", 8, 36);
  display.drawRect(8, 60, 100, 60, kYellow);
  display.fillRect(18, 70, 35, 35, kMagenta);
  display.drawCircle(155, 90, 30, kCyan);
  display.drawLine(205, 60, 300, 120, kOrange);
  for (int x = 8; x < 312; ++x) {
    display.drawFastVLine(x, 132, 18,
        display.color565(x * 255 / 320, 255 - x * 255 / 320, (x * 2) % 256));
  }
  display.drawRect(1, 1, 318, 238, kGreen);
  display.drawString("320 x 240 corner / offset / color check", 8, 160);
  drawButton(8, 211, 98, 22, "BACK");
  drawButton(111, 211, 98, 22, "NEXT COLOR");
}

void drawTouchTest() {
  display.fillScreen(kBlack);
  display.setTextColor(kWhite, kBlack);
  display.setTextSize(2);
  display.drawString("TOUCH RAW / CALIBRATION", 8, 8);
  display.setTextSize(1);
  display.drawString("Touch points; raw X/Y/Z are printed to Serial.", 8, 35);
  display.drawString(String("Raw X: ") + (hasTouch ? String(lastRaw.x) : "--"), 12, 66);
  display.drawString(String("Raw Y: ") + (hasTouch ? String(lastRaw.y) : "--"), 12, 82);
  display.drawString(String("Pressure: ") + (hasTouch ? String(lastRaw.z) : "--"), 12, 98);
  display.drawString(String("Calibration: ") + (calibration.valid ? "saved in NVS" : "not calibrated"), 12, 116);
  display.drawString("Serial command 'calibrate' also starts calibration.", 12, 132);
  display.drawCircle(13, 56, 5, kRed);
  display.drawCircle(306, 56, 5, kRed);
  display.drawCircle(13, 188, 5, kRed);
  display.drawCircle(306, 188, 5, kRed);
  drawButton(8, 204, 95, 27, "BACK");
  drawButton(112, 204, 95, 27, "CALIBRATE");
  drawButton(216, 204, 96, 27, backlightOn ? "LIGHT ON" : "LIGHT OFF");
}

void drawCalibrationTarget() {
  display.fillScreen(kBlack);
  display.drawCircle(kTargetX[calibrationStep], kTargetY[calibrationStep], 9, kRed);
  display.drawFastHLine(kTargetX[calibrationStep] - 14, kTargetY[calibrationStep], 29, kWhite);
  display.drawFastVLine(kTargetX[calibrationStep], kTargetY[calibrationStep] - 14, 29, kWhite);
  display.setTextColor(kWhite, kBlack);
  display.setTextSize(1);
  display.drawString(String("Point ") + (calibrationStep + 1) +
      "/5: tap once, then release", 8, 104);
}

bool solveAffine() {
  const touchcal::Point target[3] = {
    touchcal::rotate180({static_cast<float>(kTargetX[0]), static_cast<float>(kTargetY[0])}, board::screenWidth, board::screenHeight),
    touchcal::rotate180({static_cast<float>(kTargetX[1]), static_cast<float>(kTargetY[1])}, board::screenWidth, board::screenHeight),
    touchcal::rotate180({static_cast<float>(kTargetX[2]), static_cast<float>(kTargetY[2])}, board::screenWidth, board::screenHeight)
  };
  return touchcal::fitThree(calibrationRaw, target, candidateCalibration);
}

void saveCalibration() {
  preferences.begin("touch", false);
  preferences.putFloat("xx", candidateCalibration.xFromRawX);
  preferences.putFloat("xy", candidateCalibration.xFromRawY);
  preferences.putFloat("xo", candidateCalibration.xOffset);
  preferences.putFloat("yx", candidateCalibration.yFromRawX);
  preferences.putFloat("yy", candidateCalibration.yFromRawY);
  preferences.putFloat("yo", candidateCalibration.yOffset);
  preferences.putBool("valid", true);
  preferences.putUChar("rotation", board::landscapeRotation);
  preferences.putUChar("mapver", touchcal::kCalibrationMapVersion);
  preferences.end();
  candidateCalibration.valid = true;
  calibration = candidateCalibration;
}

void startCalibration() {
  screen = Screen::Calibration;
  calibrationStep = 0;
  candidateCalibration = calibration;
  Serial.println("[TOUCH] calibration started: tap targets once and release each time");
  drawCalibrationTarget();
}

void acceptCalibrationTap(const RawTouch& sample) {
  calibrationRaw[calibrationStep] = {
    static_cast<float>(sample.x), static_cast<float>(sample.y)
  };

  if (calibrationStep == 2 && !solveAffine()) {
    Serial.println("[TOUCH] calibration rejected: first three points are degenerate");
    screen = Screen::Touch;
    drawTouchTest();
    return;
  }

  if (calibrationStep >= 3) {
    const touchcal::Point target[5] = {
      touchcal::rotate180({static_cast<float>(kTargetX[0]), static_cast<float>(kTargetY[0])}, board::screenWidth, board::screenHeight),
      touchcal::rotate180({static_cast<float>(kTargetX[1]), static_cast<float>(kTargetY[1])}, board::screenWidth, board::screenHeight),
      touchcal::rotate180({static_cast<float>(kTargetX[2]), static_cast<float>(kTargetY[2])}, board::screenWidth, board::screenHeight),
      touchcal::rotate180({static_cast<float>(kTargetX[3]), static_cast<float>(kTargetY[3])}, board::screenWidth, board::screenHeight),
      touchcal::rotate180({static_cast<float>(kTargetX[4]), static_cast<float>(kTargetY[4])}, board::screenWidth, board::screenHeight)
    };
    const bool accepted = calibrationStep == 4
        ? touchcal::fitFive(calibrationRaw, target, candidateCalibration, 30.0f)
        : touchcal::validate(candidateCalibration, calibrationRaw, target,
                              calibrationStep + 1, 30.0f);
    if (!accepted) {
      Serial.printf("[TOUCH] calibration rejected at point %u; prior saved mapping retained\\n",
          calibrationStep + 1);
      screen = Screen::Touch;
      drawTouchTest();
      return;
    }
  }

  if (calibrationStep == 4) {
    saveCalibration();
    Serial.println("[TOUCH] affine calibration validated and saved to NVS");
    screen = Screen::Touch;
    drawTouchTest();
    return;
  }
  ++calibrationStep;
  drawCalibrationTarget();
}

void loadCalibration() {
  preferences.begin("touch", true);
  calibration.xFromRawX = preferences.getFloat("xx", calibration.xFromRawX);
  calibration.xFromRawY = preferences.getFloat("xy", calibration.xFromRawY);
  calibration.xOffset = preferences.getFloat("xo", calibration.xOffset);
  calibration.yFromRawX = preferences.getFloat("yx", calibration.yFromRawX);
  calibration.yFromRawY = preferences.getFloat("yy", calibration.yFromRawY);
  calibration.yOffset = preferences.getFloat("yo", calibration.yOffset);
  calibration.valid = preferences.getBool("valid", false) &&
      preferences.getUChar("rotation", 0) == board::landscapeRotation &&
      preferences.getUChar("mapver", 0) == touchcal::kCalibrationMapVersion;
  preferences.end();
}

void mapTouch(const RawTouch& sample, int16_t& x, int16_t& y) {
  if (!calibration.valid) {
    x = -1;
    y = -1;
    return;
  }
  const touchcal::Point mapped = touchcal::map(calibration, {
    static_cast<float>(sample.x), static_cast<float>(sample.y)
  });
  const touchcal::Point rotated = touchcal::rotate180(mapped, board::screenWidth, board::screenHeight);
  x = constrain(static_cast<int16_t>(rotated.x), 0, board::screenWidth - 1);
  y = constrain(static_cast<int16_t>(rotated.y), 0, board::screenHeight - 1);
}

void handleTap(const RawTouch& sample) {
  if (screen == Screen::Calibration) {
    acceptCalibrationTap(sample);
    return;
  }

  int16_t x = -1, y = -1;
  mapTouch(sample, x, y);
  if (!calibration.valid) {
    Serial.println("[TOUCH] no mapped action until calibration is complete");
    return;
  }
  if (screen == Screen::Display) {
    if (y > 205 && x < 105) {
      screen = Screen::Summary;
      drawSummary();
    } else {
      colorPage = (colorPage + 1) % 5;
      drawDisplayTest();
    }
    return;
  }
  if (screen == Screen::Touch) {
    if (y > 195 && x < 108) {
      screen = Screen::Summary;
      drawSummary();
    } else if (y > 195 && x < 212) {
      startCalibration();
    } else if (y > 195) {
      backlightOn = !backlightOn;
      analogWrite(board::backlight, backlightOn ? (255 * backlightPercent) / 100 : 0);
      drawTouchTest();
    }
    return;
  }
  if (y > 210) {
    if (x < 106) {
      screen = Screen::Display;
      colorPage = 0;
      drawDisplayTest();
    } else if (x < 213) {
      screen = Screen::Touch;
      drawTouchTest();
    } else {
      sd.runSafeTest();
      Serial.printf("[SD] %s; test=%s total=%llu used=%llu\n",
          sd.status().message.c_str(), sd.status().testPassed ? "PASS" : "FAIL",
          static_cast<unsigned long long>(sd.status().totalBytes),
          static_cast<unsigned long long>(sd.status().usedBytes));
      drawSummary();
    }
  }
  Serial.printf("[TOUCH] mapped x=%d y=%d raw=%u,%u z=%u\n",
      x, y, sample.x, sample.y, sample.z);
}

void setBacklight(uint8_t percent) {
  backlightPercent = constrain(percent, 0, 100);
  analogWrite(board::backlight, backlightOn ? (255 * backlightPercent) / 100 : 0);
}

void handleSerialCommand(const char* command) {
  if (strcmp(command, "display") == 0) {
    screen = Screen::Display;
    colorPage = 0;
    drawDisplayTest();
  } else if (strcmp(command, "touch") == 0) {
    screen = Screen::Touch;
    drawTouchTest();
  } else if (strcmp(command, "calibrate") == 0) {
    startCalibration();
  } else if (strcmp(command, "sd") == 0) {
    sd.runSafeTest();
    drawSummary();
  } else if (strcmp(command, "light on") == 0) {
    backlightOn = true;
    setBacklight(backlightPercent);
  } else if (strcmp(command, "light off") == 0) {
    backlightOn = false;
    analogWrite(board::backlight, 0);
  } else if (strncmp(command, "light ", 6) == 0) {
    setBacklight(atoi(command + 6));
  } else if (strcmp(command, "help") == 0) {
    Serial.println("commands: display, touch, calibrate, sd, light on|off|0..100, help");
  }
}

void pollSerial() {
  while (Serial.available() > 0) {
    const char ch = static_cast<char>(Serial.read());
    if (ch == '\r') continue;
    if (ch == '\n') {
      serialLine[serialLength] = '\0';
      handleSerialCommand(serialLine);
      serialLength = 0;
    } else if (serialLength + 1 < sizeof(serialLine)) {
      serialLine[serialLength++] = ch;
    } else {
      serialLength = 0;
      Serial.println("[SERIAL] input too long; discarded");
    }
  }
}
}  // namespace

void diagnosticBegin() {
  Serial.begin(115200);
  pinMode(board::backlight, OUTPUT);
  analogWriteFrequency(kPwmFrequencyHz);
  analogWriteResolution(kPwmResolutionBits);
  setBacklight(kInitialBacklightPercent);

  display.init();
  display.setRotation(board::landscapeRotation);
  display.setColorDepth(16);
  touch.begin();
  loadCalibration();

  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(false);
  sd.begin();

  Serial.printf("[BOOT] diagnostic driver=%s resolution=%dx%d (physical result unverified)\n",
      DisplayHal::driverName(), display.width(), display.height());
  Serial.printf("[MEM] free=%u min=%u largest=%u\n",
      ESP.getFreeHeap(), ESP.getMinFreeHeap(), ESP.getMaxAllocHeap());
  if (!calibration.valid) {
    startCalibration();
  } else {
    drawSummary();
  }
}

void diagnosticLoop() {
  const uint32_t now = millis();
  if (!touch.pressed()) {
    touchLatched = false;
  } else if (!touchLatched && now - lastTouch > 80) {
    RawTouch sample;
    if (touch.read(sample)) {
      touchLatched = true;
      lastTouch = now;
      lastRaw = sample;
      hasTouch = true;
      Serial.printf("[TOUCH] raw x=%u y=%u z=%u\n", sample.x, sample.y, sample.z);
      handleTap(sample);
    }
  }

  if (screen == Screen::Display && now - lastUi > 1800) {
    lastUi = now;
    colorPage = (colorPage + 1) % 5;
    drawDisplayTest();
  }
  if (screen == Screen::Touch && touch.pressed() && now - lastUi > 200) {
    RawTouch sample;
    if (touch.read(sample)) {
      lastRaw = sample; hasTouch = true; lastUi = now;
      display.fillRect(8, 60, 210, 58, kBlack);
      display.setTextColor(kWhite, kBlack); display.setTextSize(1);
      display.drawString(String("IRQ: ") + (touch.pressed() ? "LOW" : "HIGH"), 12, 52);
      display.drawString(String("Raw X/Y/Z: ") + sample.x + "/" + sample.y + "/" + sample.z, 12, 72);
      if (calibration.valid) {
        int16_t px, py; mapTouch(sample, px, py);
        display.drawString(String("Mapped X/Y: ") + px + "/" + py, 12, 88);
      }
    }
  }
  if (screen == Screen::Summary && now - lastUi > 1000) {
    lastUi = now;
    drawSummary();
  }
  if (now - lastSerial > 5000) {
    lastSerial = now;
    Serial.printf("[MEM] free=%u min=%u largest=%u sd=%s wifi=%s\n",
        ESP.getFreeHeap(), ESP.getMinFreeHeap(), ESP.getMaxAllocHeap(),
        sd.status().mounted ? "mounted" : "absent",
        WiFi.status() == WL_CONNECTED ? "connected" : "disconnected");
  }
  pollSerial();
}
