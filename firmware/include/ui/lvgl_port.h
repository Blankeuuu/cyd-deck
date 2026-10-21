#pragma once
#include <cstdint>
#include <lvgl.h>
#include "display/display_hal.h"
namespace streamdeck {
void lvglPortBegin(DisplayHal& display);
uint32_t lvglPortFrameSequence();
}
