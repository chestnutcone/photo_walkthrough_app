#!/usr/bin/env python3
"""Seeds a local Development API (scripts/run-local-api.sh) with two artists, their published stories,
replies and follows, using only the public HTTP API (fake SSO). Original generated artwork, no third-party material.

Usage: tools/seed_local.py [--api http://127.0.0.1:5080]   (needs Pillow; idempotent: skips if artist 'maren' exists)
Writes the seeded ids to .local/seed.json.
"""
import argparse
import io
import json
import math
import os
import random
import sys
import time
import urllib.error
import urllib.request

from PIL import Image, ImageDraw


def art(seed, size, exif_orientation=None):
    """A deterministic dusk-like abstract picture; returns JPEG bytes."""
    rnd = random.Random(seed)
    w, h = size
    img = Image.new("RGB", size)
    px = img.load()
    top = tuple(rnd.randint(10, 80) for _ in range(3))
    bot = tuple(rnd.randint(120, 250) for _ in range(3))
    for y in range(h):
        t = y / (h - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    d = ImageDraw.Draw(img)
    for _ in range(9):
        r = rnd.randint(min(w, h) // 14, min(w, h) // 5)
        cx, cy = rnd.randint(0, w), rnd.randint(0, h)
        col = tuple(rnd.randint(30, 255) for _ in range(3))
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col)
    for i in range(0, w, max(40, w // 12)):
        d.line([(i, h), (w - i, h * 0.6 + 40 * math.sin(i))], fill=(255, 255, 255), width=2)
    buf = io.BytesIO()
    if exif_orientation:
        exif = Image.Exif()
        exif[0x0112] = exif_orientation
        img.save(buf, "JPEG", quality=85, exif=exif)
    else:
        img.save(buf, "JPEG", quality=85)
    return buf.getvalue()


class Api:
    def __init__(self, base):
        self.base = base.rstrip("/")

    def call(self, method, path, body=None, token=None, raw=None, headers=None):
        h = {"Content-Type": "application/json"}
        if token:
            h["Authorization"] = f"Bearer {token}"
        h.update(headers or {})
        data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
        req = urllib.request.Request(self.base + path if path.startswith("/") else path, data=data, method=method, headers=h)
        for attempt in range(6):
            try:
                with urllib.request.urlopen(req) as r:
                    txt = r.read().decode()
                    return json.loads(txt) if txt else None
            except urllib.error.HTTPError as e:
                if e.code == 429 and attempt < 5:
                    time.sleep(int(e.headers.get("Retry-After", "2")))
                    continue
                raise RuntimeError(f"{method} {path} -> {e.code} {e.read().decode()[:300]}")

    def sso(self, sub, email):
        r = self.call("POST", "/v1/auth/sso", {"provider": "fake", "idToken": f"fake:{sub}:{email}", "nonce": "n"})
        return r["data"]["accessToken"]


ARTISTS = [
    dict(sub="maren", email="maren@example.com", name="Maren Holt", handle="maren", city="Lisbon", discipline="Street photographer",
         bio="Chasing the last light over rooftops.", stories=[
             ("Harbour at dusk", (1600, 1067), None, "A slow evening on the quay.", [
                 (0.30, 0.40, 2.4, "The mast", "Rigging against the sky."),
                 (0.62, 0.55, None, "The wake", "Stay with the wide view."),
                 (0.80, 0.30, 2.4, "The gull", "One bird, one frame."),
                 (0.45, 0.80, None, "The dock", "Where the day ended.")]),
             ("Portrait of a market", (1067, 1600), None, "Early morning stalls.", [
                 (0.50, 0.25, 2.4, "Awning", "Faded stripes."),
                 (0.40, 0.60, 2.4, "Scale", "Weighed in grams."),
                 (0.70, 0.85, None, "Crates", "Stacked high.")]),
             ("Turned window", (1500, 1000), 6, "Shot sideways, stored rotated.", [
                 (0.50, 0.50, 2.4, "Glass", "A reflection."),
                 (0.20, 0.30, None, "Frame", "The wooden edge."),
                 (0.75, 0.70, 2.4, "Sill", "Dust and light.")])]),
    dict(sub="theo", email="theo@example.com", name="Theo Marsh", handle="theo", city="Porto", discipline="Landscape photographer",
         bio="Fog, ridgelines, patience.", stories=[
             ("Ridge in fog", (1600, 900), None, "Three hours of waiting.", [
                 (0.25, 0.45, 2.4, "First ridge", "Barely there."),
                 (0.55, 0.50, None, "Second ridge", "Softer still."),
                 (0.85, 0.40, 2.4, "The tree", "A lone silhouette."),
                 (0.50, 0.75, None, "Valley", "Cold air pooling."),
                 (0.10, 0.20, 2.4, "Sky", "Opening up.")]),
             ("Salt flats", (1200, 1200), None, "Square and endless.", [
                 (0.50, 0.50, 2.4, "Centre", "Nothing and everything."),
                 (0.30, 0.70, None, "Cracks", "Patterns in the crust."),
                 (0.80, 0.20, 2.4, "Horizon", "A thin line."),
                 (0.20, 0.25, None, "Haze", "Heat shimmer.")])]),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", default="http://127.0.0.1:5080")
    a = ap.parse_args()
    api = Api(a.api)
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
    out = {"artists": {}, "works": []}
    try:
        api.call("GET", "/v1/artists/maren")
        print("already seeded"); return
    except RuntimeError:
        pass
    tokens = {}
    for art_def in ARTISTS:
        tok = api.sso(art_def["sub"], art_def["email"])
        tokens[art_def["handle"]] = tok
        me = api.call("POST", "/v1/me/onboarding", dict(displayName=art_def["name"], handle=art_def["handle"], ageConfirmed=True,
                                                         termsAccepted=True, followArtistIds=[]), tok)["data"]
        api.call("PATCH", "/v1/me/profile", dict(bio=art_def["bio"], city=art_def["city"], discipline=art_def["discipline"]), tok)
        out["artists"][art_def["handle"]] = dict(id=me["id"], token=None)
        for n, (title, size, orient, intro, chapters) in enumerate(art_def["stories"]):
            d = api.call("POST", "/v1/works", {"contentType": "image/jpeg", "title": title}, tok)["data"]
            api.call("PUT", d["uploadUrl"], raw=art(hash((art_def["handle"], n)) & 0xFFFF, size, orient),
                     headers={"x-ms-blob-type": "BlockBlob", "Content-Type": "image/jpeg"})
            chs = [dict(x=x, y=y, zoom=z, title=t, body=b) for (x, y, z, t, b) in chapters]
            api.call("PUT", f"/v1/works/{d['workId']}", dict(title=title, intro=intro, altText=f"Generated artwork: {title}", chapters=chs), tok)
            api.call("POST", f"/v1/works/{d['workId']}/publish", None, tok)
            for _ in range(60):
                w = api.call("GET", f"/v1/works/{d['workId']}", token=tok)["data"]
                if w.get("status") == "published":
                    break
                time.sleep(1)
            else:
                raise RuntimeError(f"work {title} did not publish: {w.get('status')}")
            out["works"].append(dict(id=d["workId"], title=title, artist=art_def["handle"]))
            print("published", title)
    # follows and replies between the two artists, plus a plain reader
    ids = {h: v["id"] for h, v in out["artists"].items()}
    api.call("PUT", f"/v1/artists/{ids['theo']}/follow", None, tokens["maren"])
    first_maren = next(w for w in out["works"] if w["artist"] == "maren")
    first_theo = next(w for w in out["works"] if w["artist"] == "theo")
    api.call("POST", f"/v1/works/{first_maren['id']}/chapters/0/replies", {"body": "That mast catches the light beautifully."}, tokens["theo"])
    api.call("POST", f"/v1/works/{first_maren['id']}/chapters/1/replies", {"body": "Love the wide framing here."}, tokens["theo"])
    api.call("POST", f"/v1/works/{first_theo['id']}/chapters/0/replies", {"body": "How long did you wait for this fog?"}, tokens["maren"])
    os.makedirs(os.path.join(root, ".local"), exist_ok=True)
    with open(os.path.join(root, ".local", "seed.json"), "w") as f:
        json.dump(out, f, indent=2)
    print("seeded", len(out["works"]), "works")


if __name__ == "__main__":
    sys.exit(main())
