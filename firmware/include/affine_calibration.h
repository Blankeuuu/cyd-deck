#pragma once

#include <cstddef>
#include <cmath>

namespace touchcal {

static constexpr uint8_t kCalibrationMapVersion = 2;

struct Point {
  float x;
  float y;
};

struct Coefficients {
  float xFromRawX = 0;
  float xFromRawY = 0;
  float xOffset = 0;
  float yFromRawX = 0;
  float yFromRawY = 0;
  float yOffset = 0;
  bool valid = false;
};

inline Point map(const Coefficients& c, const Point& raw) {
  return {
    c.xFromRawX * raw.x + c.xFromRawY * raw.y + c.xOffset,
    c.yFromRawX * raw.x + c.yFromRawY * raw.y + c.yOffset
  };
}

inline Point rotate180(const Point& point, float width, float height) {
  return {
    width - 1.0f - point.x,
    height - 1.0f - point.y
  };
}

// Fits an affine map from three non-collinear raw/screen point pairs.
// Supports arbitrary screen-point geometry, not only axis-aligned targets.
// The output is assigned only on success.
inline bool fitThree(const Point raw[3], const Point screen[3],
                     Coefficients& output, float determinantFloor = 10000.0f) {
  const float ax = raw[0].x, ay = raw[0].y;
  const float bx = raw[1].x, by = raw[1].y;
  const float cx = raw[2].x, cy = raw[2].y;
  const float determinant = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
  if (std::fabs(determinant) < determinantFloor) return false;

  const float inv00 = (cy - ay) / determinant;
  const float inv01 = -(cx - ax) / determinant;
  const float inv10 = -(by - ay) / determinant;
  const float inv11 = (bx - ax) / determinant;
  const float screenDx1 = screen[1].x - screen[0].x;
  const float screenDx2 = screen[2].x - screen[0].x;
  const float screenDy1 = screen[1].y - screen[0].y;
  const float screenDy2 = screen[2].y - screen[0].y;

  Coefficients candidate;
  candidate.xFromRawX = screenDx1 * inv00 + screenDx2 * inv10;
  candidate.xFromRawY = screenDx1 * inv01 + screenDx2 * inv11;
  candidate.xOffset = screen[0].x -
      candidate.xFromRawX * ax - candidate.xFromRawY * ay;
  candidate.yFromRawX = screenDy1 * inv00 + screenDy2 * inv10;
  candidate.yFromRawY = screenDy1 * inv01 + screenDy2 * inv11;
  candidate.yOffset = screen[0].y -
      candidate.yFromRawX * ax - candidate.yFromRawY * ay;
  candidate.valid = true;
  output = candidate;
  return true;
}

inline bool validate(const Coefficients& candidate, const Point* raw,
                    const Point* screen, std::size_t count, float maxError) {
  if (!candidate.valid || count == 0) return false;
  for (std::size_t i = 0; i < count; ++i) {
    const Point projected = map(candidate, raw[i]);
    if (std::fabs(projected.x - screen[i].x) > maxError ||
        std::fabs(projected.y - screen[i].y) > maxError) {
      return false;
    }
  }
  return true;
}


inline bool fitFive(const Point raw[5], const Point screen[5],
                    Coefficients& output, float maxError = 30.0f) {
  Coefficients candidate;
  if (!fitThree(raw, screen, candidate)) return false;
  if (!validate(candidate, raw, screen, 5, maxError)) return false;
  output = candidate;
  return true;
}

}  // namespace touchcal
