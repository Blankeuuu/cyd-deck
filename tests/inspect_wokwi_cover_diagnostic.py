#!/usr/bin/env python3
"""Validate the Wokwi cover RGB diagnostic screenshot.

The simulator-only diagnostic renders a 126x126 JPEG with vertical red, green,
and blue bands into the media cover panel at screen coordinates x=12..137,
y=40..165. This script samples the band centers from a Wokwi screenshot and
fails if channel order or byte order is wrong.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image


LOGICAL_SIZE = (320, 240)
PORTRAIT_SIZE = (240, 320)
MIN_BRIGHTNESS = 128
SAMPLES = (
    (33, 103, "red"),
    (75, 103, "green"),
    (117, 103, "blue"),
)


def dominant_channel(rgb: tuple[int, int, int], expected: str) -> bool:
    r, g, b = rgb
    if expected == "red":
        return r > g + 60 and r > b + 60
    if expected == "green":
        return g > r + 45 and g > b + 45
    if expected == "blue":
        return b > r + 45 and b > g + 45
    raise ValueError(expected)


def sample_matches(image: Image.Image, sample: tuple[int, int, str]) -> bool:
    x, y, expected = sample
    rgb = image.getpixel((x, y))
    return max(rgb) >= MIN_BRIGHTNESS and dominant_channel(rgb, expected)


def logical_view(image: Image.Image) -> Image.Image:
    if image.size == LOGICAL_SIZE:
        return image
    if image.size != PORTRAIT_SIZE:
        raise ValueError(
            f"expected screenshot size {LOGICAL_SIZE} or {PORTRAIT_SIZE}, got {image.size}"
        )

    candidates = (
        image.transpose(Image.Transpose.ROTATE_90),
        image.transpose(Image.Transpose.ROTATE_270),
    )
    return max(
        candidates,
        key=lambda candidate: sum(sample_matches(candidate, sample) for sample in SAMPLES),
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("screenshot", type=Path)
    args = parser.parse_args()

    image = Image.open(args.screenshot).convert("RGB")
    try:
        image = logical_view(image)
    except ValueError as error:
        print(f"Wokwi cover RGB diagnostic FAILED: {error}", file=sys.stderr)
        return 1
    errors: list[str] = []
    for x, y, expected in SAMPLES:
        rgb = image.getpixel((x, y))
        if max(rgb) < MIN_BRIGHTNESS:
            errors.append(f"sample {expected} at ({x},{y}) was too dark: {rgb}")
        elif not dominant_channel(rgb, expected):
            errors.append(f"sample {expected} at ({x},{y}) had wrong channel order: {rgb}")
        print(f"{expected}@{x},{y}={rgb}")

    if errors:
        print("Wokwi cover RGB diagnostic FAILED:", file=sys.stderr)
        for error in errors:
            print(f"  {error}", file=sys.stderr)
        return 1

    print("Wokwi cover RGB diagnostic PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
