#!/usr/bin/env python3
"""shot.py OUT.png [WIDTH]: screenshot of the emulator via adb, downscaled to WIDTH (default 540) as a small PNG."""
import io
import os
import subprocess
import sys

from PIL import Image

out = sys.argv[1]
width = int(sys.argv[2]) if len(sys.argv) > 2 else 540
adb = os.path.expanduser("~/android-sdk/platform-tools/adb")
# A loaded headless emulator sometimes raises "System UI isn't responding"; close it so it does not cover the app.
win = subprocess.run([adb, "shell", "dumpsys", "window", "windows"], capture_output=True, text=True).stdout
if "Application Not Responding" in win:
    subprocess.run([adb, "shell", "am", "broadcast", "-a", "android.intent.action.CLOSE_SYSTEM_DIALOGS"], capture_output=True)
raw = subprocess.run([adb, "exec-out", "screencap", "-p"], check=True, capture_output=True).stdout
img = Image.open(io.BytesIO(raw)).convert("RGB")
img = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
img.save(out, optimize=True)
