#pragma once
#include <Arduino.h>
struct RawTouch{uint16_t x=0,y=0,z=0;};
class Xpt2046{public:void begin();bool read(RawTouch& sample);bool pressed()const;};
