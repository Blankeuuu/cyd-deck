#include "xpt2046.h"
#include "board_pins.h"

namespace {
uint16_t readConversion(uint8_t command) {
  digitalWrite(board::touchCs, LOW);
  for (int bit = 7; bit >= 0; --bit) {
    digitalWrite(board::touchMosi, (command >> bit) & 1);
    digitalWrite(board::touchClk, HIGH);
    delayMicroseconds(2);
    digitalWrite(board::touchClk, LOW);
    delayMicroseconds(2);
  }

  // The XPT2046 shifts the 12-bit result in a 16-clock frame.
  uint16_t frame = 0;
  for (int bit = 0; bit < 16; ++bit) {
    digitalWrite(board::touchClk, HIGH);
    delayMicroseconds(2);
    frame = static_cast<uint16_t>((frame << 1) | digitalRead(board::touchMiso));
    digitalWrite(board::touchClk, LOW);
    delayMicroseconds(2);
  }
  digitalWrite(board::touchCs, HIGH);
  return (frame >> 3) & 0x0FFF;
}
}

void Xpt2046::begin() {
  pinMode(board::touchClk, OUTPUT);
  pinMode(board::touchMosi, OUTPUT);
  pinMode(board::touchMiso, INPUT);
  pinMode(board::touchCs, OUTPUT);
  // GPIO36 has no internal pull-up/down; IRQ level is sampled as provided by module.
  pinMode(board::touchIrq, INPUT);
  digitalWrite(board::touchClk, LOW);
  digitalWrite(board::touchCs, HIGH);
}

bool Xpt2046::pressed() const {
  return digitalRead(board::touchIrq) == LOW;
}

bool Xpt2046::read(RawTouch& sample) {
  if (!pressed()) return false;
  uint32_t x = 0, y = 0, z1 = 0, z2 = 0;
  constexpr int sampleCount = 5;
  for (int i = 0; i < sampleCount; ++i) {
    y += readConversion(0x90);
    x += readConversion(0xD0);
    z1 += readConversion(0xB0);
    z2 += readConversion(0xC0);
  }
  sample.x = x / sampleCount;
  sample.y = y / sampleCount;
  const uint32_t a = z1 / sampleCount;
  const uint32_t b = z2 / sampleCount;
  sample.z = a + 4095 - b;
  return sample.z > 80;
}
