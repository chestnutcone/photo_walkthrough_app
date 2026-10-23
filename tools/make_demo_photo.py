#!/usr/bin/env python3
"""Draws the onboarding demo photograph (app/assets/demo/demo_photo.png).

Original, programmatic artwork: a dusk sky, a low sun, sea bands and a sail.
No third-party material is used, so there are no rights to clear.
Pure standard library. Usage: make_demo_photo.py OUTPUT.png
"""
import math
import struct
import sys
import zlib

W, H = 720, 960  # 3:4


def lerp(a, b, t):
    return a + (b - a) * t


def mix(c1, c2, t):
    return tuple(lerp(c1[i], c2[i], t) for i in range(3))


SKY_TOP = (38, 33, 61)
SKY_MID = (178, 92, 96)
SKY_LOW = (245, 176, 104)
SEA_TOP = (92, 66, 96)
SEA_BOT = (18, 20, 36)
HORIZON = 0.62
SUN = (0.40, 0.58, 0.075)  # x, y, radius as fractions of width
SAIL = [(0.55, 0.30), (0.55, 0.62), (0.80, 0.62)]  # triangle, fractions


def inside_triangle(px, py, tri):
    (x1, y1), (x2, y2), (x3, y3) = tri
    d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
    a = ((y2 - y3) * (px - x3) + (x3 - x2) * (py - y3)) / d
    b = ((y3 - y1) * (px - x3) + (x1 - x3) * (py - y3)) / d
    return a >= 0 and b >= 0 and a + b <= 1


def pixel(x, y):
    u, v = x / W, y / H
    if v < HORIZON:
        t = v / HORIZON
        col = mix(SKY_TOP, SKY_MID, min(1, t * 1.6)) if t < 0.625 else mix(SKY_MID, SKY_LOW, (t - 0.625) / 0.375)
    else:
        t = (v - HORIZON) / (1 - HORIZON)
        col = mix(SEA_TOP, SEA_BOT, t)
        # horizontal swell lines
        wave = math.sin((v * 140) + math.sin(u * 9) * 1.5)
        col = tuple(c + wave * 6 * (1 - t) for c in col)
        # sun glitter column
        d = abs(u - SUN[0])
        glow = max(0.0, 1 - d / 0.12) * (1 - t) ** 1.5
        glow *= 0.5 + 0.5 * math.sin(v * 260)
        col = mix(col, SKY_LOW, max(0.0, glow) * 0.55)
    # sun disc and halo
    dx, dy = (u - SUN[0]) * W, (v - SUN[1]) * H
    dist = math.hypot(dx, dy)
    r = SUN[2] * W
    if dist < r:
        col = mix((255, 226, 168), (255, 205, 130), dist / r)
    else:
        col = mix(col, SKY_LOW, max(0.0, 1 - (dist - r) / (r * 4)) ** 2 * 0.45)
    # sail and hull
    if inside_triangle(u, v, SAIL):
        shade = (u - SAIL[0][0]) / (SAIL[2][0] - SAIL[0][0])
        col = mix((24, 20, 30), (70, 40, 50), shade)
    elif 0.55 <= u <= 0.82 and HORIZON - 0.004 <= v <= HORIZON + 0.012:
        col = (16, 14, 22)
    # vignette
    vig = 1 - 0.35 * ((u - 0.5) ** 2 + (v - 0.5) ** 2) * 2
    return tuple(max(0, min(255, int(c * vig))) for c in col)


def png(path):
    raw = bytearray()
    for y in range(H):
        raw.append(0)
        for x in range(W):
            raw.extend(pixel(x, y))

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))


if __name__ == "__main__":
    png(sys.argv[1])
