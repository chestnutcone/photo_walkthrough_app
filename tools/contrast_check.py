#!/usr/bin/env python3
"""WCAG 2.x contrast ratios of the Vela text tokens (app/lib/core/theme/tokens.dart) over each surface."""
import re
import sys
from pathlib import Path

src = (Path(__file__).resolve().parent.parent / "app/lib/core/theme/tokens.dart").read_text()
colors = {m[0]: int(m[1], 16) for m in re.findall(r"static const (\w+) = Color\(0x([0-9A-Fa-f]{8})\)", src)}


def rgb(v):
    return (v >> 16 & 255, v >> 8 & 255, v & 255)


def lum(c):
    def f(x):
        x /= 255
        return x / 12.92 if x <= 0.03928 else ((x + 0.055) / 1.055) ** 2.4
    r, g, b = map(f, c)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def ratio(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


texts = ["ink", "secondary", "muted", "faint", "accent", "danger"]
surfaces = ["background", "sheet", "input"]
fail = 0
print(f"{'text':10}" + "".join(f"{s:>12}" for s in surfaces))
for t in texts:
    row = f"{t:10}"
    for s in surfaces:
        r = ratio(rgb(colors[t]), rgb(colors[s]))
        mark = "" if r >= 4.5 else "*"
        if r < 4.5:
            fail += 1
        row += f"{r:>11.2f}{mark or ' '}"
    print(row)
print("* below 4.5:1 (normal text). Large text (>=18.66px bold / 24px) needs 3:1.")
sys.exit(1 if fail else 0)
