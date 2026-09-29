#pragma once
#include <LovyanGFX.hpp>
class DisplayHal final : public lgfx::LGFX_Device {
 lgfx::Bus_SPI bus_;
#if defined(DISPLAY_DRIVER_ILI9341)
 lgfx::Panel_ILI9341 panel_;
 static constexpr const char* driver_="ILI9341";
#elif defined(DISPLAY_DRIVER_ST7789)
 lgfx::Panel_ST7789 panel_;
 static constexpr const char* driver_="ST7789";
#else
#error "Select a display driver environment"
#endif
 public:
 DisplayHal();
 static constexpr const char* driverName(){return driver_;}
};
