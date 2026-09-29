"""Bounded serial capture without asserting ESP32 reset/boot lines."""
import argparse
import time
from pathlib import Path

import serial

parser = argparse.ArgumentParser()
parser.add_argument("--port", default="COM4")
parser.add_argument("--seconds", type=float, default=30)
parser.add_argument("--command", action="append", default=[])
parser.add_argument("--output", required=True)
args = parser.parse_args()
output = Path(args.output)
output.parent.mkdir(parents=True, exist_ok=True)
with serial.Serial(port=None, baudrate=115200, timeout=0.2) as connection:
    connection.dtr = False
    connection.rts = False
    connection.port = args.port
    connection.open()
    for command in args.command:
        connection.write((command + "\n").encode("utf-8"))
    deadline = time.monotonic() + args.seconds
    with output.open("a", encoding="utf-8") as log:
        while time.monotonic() < deadline:
            line = connection.readline().decode("utf-8", errors="replace")
            if line:
                log.write(line)
                log.flush()
                print(line, end="", flush=True)
