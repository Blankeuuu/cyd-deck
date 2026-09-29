#include <cassert>
#include <cmath>
#include <iostream>
#include "../firmware/include/affine_calibration.h"

using touchcal::Coefficients;
using touchcal::Point;

void expectNear(Point actual, Point expected) {
  assert(std::fabs(actual.x - expected.x) <= 1.0f);
  assert(std::fabs(actual.y - expected.y) <= 1.0f);
}

void verifyCase(const Point raw[5], const Point target[5]) {
  Coefficients fitted;
  assert(touchcal::fitThree(raw, target, fitted));
  assert(touchcal::validate(fitted, raw, target, 5, 30.0f));
  expectNear(touchcal::map(fitted, raw[0]), target[0]);
  expectNear(touchcal::map(fitted, raw[1]), target[1]);
  expectNear(touchcal::map(fitted, raw[2]), target[2]);
  expectNear(touchcal::map(fitted, raw[3]), target[3]);
  expectNear(touchcal::map(fitted, raw[4]), target[4]);
}

int main() {
  static_assert(touchcal::kCalibrationMapVersion == 2, "touch calibration map version must invalidate pre-rotate180 coefficients");
  const Point screen[5] = {{12,12},{307,12},{12,227},{307,227},{160,120}};
  const Point normal[5] = {{300,400},{3800,400},{300,3800},{3800,3800},{2050,2100}};
  const Point swapped[5] = {{400,300},{400,3800},{3800,300},{3800,3800},{2100,2050}};
  const Point inverted[5] = {{3800,3800},{300,3800},{3800,300},{300,300},{2050,2050}};
  verifyCase(normal, screen);
  verifyCase(swapped, screen);
  verifyCase(inverted, screen);

  Coefficients active;
  active.xOffset = 123.0f;
  const Coefficients before = active;
  const Point collinear[3] = {{100,100},{200,200},{300,300}};
  assert(!touchcal::fitThree(collinear, screen, active));
  assert(active.xOffset == before.xOffset);

  Coefficients candidate;
  assert(touchcal::fitThree(normal, screen, candidate));
  Point badFourth[5] = {normal[0],normal[1],normal[2],{500,600},normal[4]};
  assert(!touchcal::validate(candidate, badFourth, screen, 4, 30.0f));
  Point badFifth[5] = {normal[0],normal[1],normal[2],normal[3],{500,600}};
  assert(!touchcal::fitFive(badFifth, screen, active));
  assert(active.xOffset == before.xOffset);
  assert(touchcal::validate(candidate, normal, screen, 4, 30.0f));
  assert(touchcal::validate(candidate, normal, screen, 5, 30.0f));
  assert(touchcal::fitFive(normal, screen, active));
  assert(touchcal::validate(active, normal, screen, 5, 30.0f));
  assert(touchcal::rotate180({0, 0}, 320, 240).x == 319);
  assert(touchcal::rotate180({0, 0}, 320, 240).y == 239);
  assert(touchcal::rotate180({319, 239}, 320, 240).x == 0);
  assert(touchcal::rotate180({319, 239}, 320, 240).y == 0);
  const Point visibleTarget{12, 12};
  const Point calibrationTarget = touchcal::rotate180(visibleTarget, 320, 240);
  assert(touchcal::rotate180(calibrationTarget, 320, 240).x == visibleTarget.x);
  assert(touchcal::rotate180(calibrationTarget, 320, 240).y == visibleTarget.y);
  std::cout << "affine calibration tests passed: normal/swapped/inverted, degenerate, 4/5-point validation, preserve-active-on-failure\n";
}
