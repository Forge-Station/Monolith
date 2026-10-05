#!/usr/bin/env python3
"""Generates the atmospheric-flight placeholder audio.

Six callout chimes plus a landing thud and a looping scrape, each a short, distinct
tone so the alarms can be told apart by ear long before real voice lines exist.
Falls back to copying /Audio/Announcements/attention.ogg under every name if ffmpeg
is not on PATH.

The two flight ambience loops are not generated at all: a synthesised tone makes a
poor continuous bed, so each is copied from a real loop the tree already ships.

Nothing is ever overwritten - a name that already exists is left alone, because by
then it is the maintainer's own recording.

Usage: python Tools/_WF/Planets/gen_flight_placeholders.py
Writes into Resources/Audio/_WF/Planets/Flight/
"""

import os
import shutil
import subprocess
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
OUT_DIR = os.path.join(ROOT, "Resources", "Audio", "_WF", "Planets", "Flight")
FALLBACK = os.path.join(ROOT, "Resources", "Audio", "Announcements", "attention.ogg")

# name -> (ffmpeg lavfi expression, seconds). Pitch and rhythm climb with urgency.
TONES = {
    "lift_lost": ("sine=frequency=520:duration=0.5,tremolo=f=4:d=0.6", 0.5),
    "dont_sink": ("sine=frequency=420:duration=0.7,tremolo=f=3:d=0.5", 0.7),
    "sink_rate": ("sine=frequency=620:duration=0.7,tremolo=f=6:d=0.7", 0.7),
    "terrain": ("sine=frequency=760:duration=0.6,tremolo=f=8:d=0.8", 0.6),
    "too_low_terrain": ("sine=frequency=900:duration=0.8,tremolo=f=10:d=0.8", 0.8),
    "pull_up": ("sine=frequency=1100:duration=0.9,tremolo=f=14:d=0.9", 0.9),
    "hard_landing": ("anoisesrc=color=brown:duration=1.2:amplitude=0.8", 1.2),
    "skid": ("anoisesrc=color=pink:duration=2.0:amplitude=0.4", 2.0),
}

# name -> the shipped loop it stands in for. Copied verbatim, so the stand-in keeps the
# source file's own licence; see the Flight attributions.yml.
BORROWED = {
    "atmo_wind": os.path.join(ROOT, "Resources", "Audio", "Effects", "Weather", "wind_2_1.ogg"),
    "fall_rumble": os.path.join(ROOT, "Resources", "Audio", "Effects", "space_wind.ogg"),
}


def borrow():
    """Copies the ambience loops in, skipping any that already exist."""
    for name, source in BORROWED.items():
        path = os.path.join(OUT_DIR, name + ".ogg")
        if os.path.exists(path):
            print("kept", name + ".ogg")  # a real recording is already in place; never overwrite it
            continue
        if not os.path.exists(source):
            print("missing source for", name + ".ogg", "-", source)
            continue
        shutil.copyfile(source, path)
        print("borrowed", name + ".ogg", "from", os.path.relpath(source, ROOT))


def have_ffmpeg():
    try:
        subprocess.run(["ffmpeg", "-version"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        return True
    except Exception:
        return False


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    borrow()

    if not have_ffmpeg():
        if not os.path.exists(FALLBACK):
            sys.exit("no ffmpeg and no %s to copy" % FALLBACK)
        for name in TONES:
            path = os.path.join(OUT_DIR, name + ".ogg")
            if os.path.exists(path):
                print("kept", name + ".ogg")  # a real recording is already in place; never overwrite it
                continue
            shutil.copyfile(FALLBACK, path)
            print("copied", name + ".ogg")
        return

    for name, (expr, seconds) in TONES.items():
        path = os.path.join(OUT_DIR, name + ".ogg")
        if os.path.exists(path):
            print("kept", name + ".ogg")  # a real recording is already in place; never overwrite it
            continue
        subprocess.run([
            "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", expr,
            "-t", str(seconds),
            "-af", "afade=t=in:st=0:d=0.05,afade=t=out:st=%s:d=0.1" % max(0.0, seconds - 0.1),
            "-ac", "1", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "3",
            path,
        ], check=True)
        print("wrote", name + ".ogg")


if __name__ == "__main__":
    main()
