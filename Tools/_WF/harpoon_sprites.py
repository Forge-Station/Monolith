#!/usr/bin/env python3
"""Generates the harpoon turret and harpoon RSIs.

32x32, drawn facing south (world rotation 0), cassette-futurism palette: worn steel, oxide
orange and a little amber. Run from the repo root:

    python Tools/_WF/harpoon_sprites.py
"""

import json
import os

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
TEXTURES = os.path.join(ROOT, "Resources", "Textures", "_WF", "Tether")

STEEL_DARK = (58, 62, 70, 255)
STEEL = (110, 116, 126, 255)
STEEL_LIGHT = (163, 170, 180, 255)
OXIDE = (176, 84, 42, 255)
AMBER = (232, 176, 70, 255)
SHADOW = (34, 36, 42, 255)

LICENSE = "CC-BY-SA-3.0"
COPYRIGHT = "Wolfgate"


def blank():
    return Image.new("RGBA", (32, 32), (0, 0, 0, 0))


def turret_base():
    """A bolted deck plate with the launcher rail running south."""
    img = blank()
    d = ImageDraw.Draw(img)

    # Mounting plate.
    d.ellipse((6, 8, 25, 27), fill=STEEL_DARK)
    d.ellipse((8, 10, 23, 25), fill=STEEL)
    for x, y in ((9, 11), (22, 11), (9, 24), (22, 24)):
        d.point((x, y), fill=STEEL_LIGHT)

    # Winch drum across the mount.
    d.rectangle((11, 14, 20, 19), fill=STEEL_DARK)
    d.rectangle((12, 15, 19, 18), fill=OXIDE)
    d.line((12, 16, 19, 16), fill=AMBER)

    # Rail and barrel, pointing south.
    d.rectangle((14, 18, 17, 30), fill=STEEL_DARK)
    d.rectangle((15, 18, 16, 29), fill=STEEL_LIGHT)
    d.rectangle((13, 26, 18, 28), fill=STEEL)
    d.line((13, 30, 18, 30), fill=SHADOW)

    # Seat pad behind the breech.
    d.rectangle((13, 5, 18, 9), fill=STEEL_DARK)
    d.rectangle((14, 6, 17, 8), fill=OXIDE)
    return img


def harpoon():
    """The spike itself: head south, barbs, cable eye in the tail."""
    img = blank()
    d = ImageDraw.Draw(img)

    d.polygon(((16, 30), (13, 24), (19, 24)), fill=STEEL_LIGHT)
    d.line((14, 24, 11, 20), fill=STEEL)
    d.line((18, 24, 21, 20), fill=STEEL)
    d.rectangle((15, 8, 17, 25), fill=STEEL)
    d.line((16, 9, 16, 25), fill=STEEL_LIGHT)
    d.rectangle((14, 6, 18, 9), fill=STEEL_DARK)
    d.ellipse((14, 3, 18, 7), outline=OXIDE)
    return img


def icon(kind):
    """Small control icons for the operator's actions."""
    img = blank()
    d = ImageDraw.Draw(img)
    d.ellipse((4, 4, 27, 27), fill=SHADOW)
    d.ellipse((6, 6, 25, 25), fill=STEEL_DARK)

    if kind == "reel-in":
        d.ellipse((11, 11, 20, 20), outline=AMBER)
        d.polygon(((16, 6), (12, 12), (20, 12)), fill=AMBER)
    elif kind == "pay-out":
        d.ellipse((11, 11, 20, 20), outline=STEEL_LIGHT)
        d.polygon(((16, 25), (12, 19), (20, 19)), fill=STEEL_LIGHT)
    else:
        d.line((9, 22, 22, 9), fill=OXIDE, width=2)
        d.line((9, 9, 22, 22), fill=OXIDE, width=2)

    return img


def write_rsi(name, states):
    folder = os.path.join(TEXTURES, name + ".rsi")
    os.makedirs(folder, exist_ok=True)
    for state, image in states.items():
        image.save(os.path.join(folder, state + ".png"))

    meta = {
        "version": 1,
        "license": LICENSE,
        "copyright": COPYRIGHT,
        "size": {"x": 32, "y": 32},
        "states": [{"name": state} for state in states],
    }
    with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as file:
        json.dump(meta, file, indent=4)
        file.write("\n")


def main():
    write_rsi("harpoon_turret", {
        "base": turret_base(),
        "icon-reel-in": icon("reel-in"),
        "icon-pay-out": icon("pay-out"),
        "icon-release": icon("release"),
    })
    write_rsi("harpoon_projectile", {"harpoon": harpoon()})


if __name__ == "__main__":
    main()
