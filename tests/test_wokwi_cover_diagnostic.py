from __future__ import annotations

import os
import re
import subprocess
import sys
import tempfile
import unittest
from io import BytesIO
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tests" / "inspect_wokwi_cover_diagnostic.py"
FIXTURE = ROOT / "tests" / "fixtures" / "wokwi_cover_rgb_jpeg.inc"


def make_logical_screenshot() -> Image.Image:
    encoded = bytes(int(value, 16) for value in re.findall(r"0x([0-9a-fA-F]{2})", FIXTURE.read_text()))
    cover = Image.open(BytesIO(encoded)).convert("RGB")
    screenshot = Image.new("RGB", (320, 240), (11, 13, 18))
    screenshot.paste(cover, (12, 40))
    return screenshot


class WokwiCoverDiagnosticTests(unittest.TestCase):
    def run_diagnostic(self, screenshot: Image.Image) -> subprocess.CompletedProcess[str]:
        with tempfile.TemporaryDirectory(dir=os.environ.get("TMPDIR")) as directory:
            path = Path(directory) / "screenshot.png"
            screenshot.save(path)
            return subprocess.run(
                [sys.executable, str(SCRIPT), str(path)],
                capture_output=True,
                text=True,
                check=False,
            )

    def test_accepts_valid_logical_screenshot(self) -> None:
        result = self.run_diagnostic(make_logical_screenshot())
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_accepts_valid_portrait_wokwi_screenshot(self) -> None:
        portrait = make_logical_screenshot().transpose(Image.Transpose.ROTATE_90)
        result = self.run_diagnostic(portrait)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_rejects_blank_screenshot(self) -> None:
        blank = Image.new("RGB", (320, 240), (11, 13, 18))
        result = self.run_diagnostic(blank)
        self.assertNotEqual(result.returncode, 0)

    def test_rejects_channel_swapped_screenshot(self) -> None:
        screenshot = make_logical_screenshot()
        red, green, blue = screenshot.split()
        swapped = Image.merge("RGB", (blue, green, red))
        result = self.run_diagnostic(swapped)
        self.assertNotEqual(result.returncode, 0)


if __name__ == "__main__":
    unittest.main()
