#include "display_hal.h"
#include "board_pins.h"

DisplayHal::DisplayHal() {
  auto bus = bus_.config();
  bus.spi_host = SPI2_HOST;
  bus.spi_mode = 0;
  bus.freq_write = 40000000;
  bus.freq_read = 16000000;
  bus.spi_3wire = true;
  bus.use_lock = true;
  bus.dma_channel = SPI_DMA_CH_AUTO;
  bus.pin_sclk = board::tftSclk;
  bus.pin_mosi = board::tftMosi;
  bus.pin_miso = board::tftMiso;
  bus.pin_dc = board::tftDc;
  bus_.config(bus);
  panel_.setBus(&bus_);

  // CYD panel RAM is portrait-native (240x320); board::landscapeRotation = 3 exposes 320x240.
  auto panel = panel_.config();
  panel.pin_cs = board::tftCs;
  panel.pin_rst = -1;
  panel.pin_busy = -1;
  panel.memory_width = 240;
  panel.memory_height = 320;
  panel.panel_width = 240;
  panel.panel_height = 320;
  panel.offset_x = 0;
  panel.offset_y = 0;
  panel.offset_rotation = 0;
  panel.dummy_read_pixel = 8;
  panel.dummy_read_bits = 1;
  panel.readable = true;
  panel.invert = false;
  panel.rgb_order = false;
  panel.dlen_16bit = false;
  panel.bus_shared = false;
  panel_.config(panel);
  setPanel(&panel_);
}
