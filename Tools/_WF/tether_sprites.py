#!/usr/bin/env python3
"""Builds the Tether stage 2A RSIs from user-supplied source art plus one
procedurally generated sprite for the installer gun.

Coil and anchor-eye art come from real 32x32 PNGs supplied by the user
(``Tools/_WF/tether_source``, or a directory given as the first argument); this script only copies them into RSI folders with a
single ``icon`` state and a meta.json, except for the tow cable coil, which
is a darker/heavier Pillow-recoloured copy of the steel cable art (no source
PNG exists for tow cable specifically).

The installer gun has no supplied source, so its RSI is drawn procedurally
with Pillow: a chunky rivet-gun silhouette in the muted industrial
cassette-futurism palette (greys, safety orange/yellow), matching the scale
and outline weight of the supplied rope/eye art and the existing grappling
gun icon.

Usage: python Tools/_WF/tether_sprites.py [source-dir]
Writes into Resources/Textures/_WF/Tether/*.rsi/
"""

import json
import os
import sys

from PIL import Image, ImageEnhance

FRAME = 32
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT_ROOT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Tether")
SOURCE_DIR = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "tether_source")

COPYRIGHT = "Made by the Wolfgate team"


def write_meta(out_dir, states):
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": FRAME, "y": FRAME},
        "states": states,
    }
    with open(os.path.join(out_dir, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


def build_single_icon_rsi(rsi_name, image):
    """Writes one RSI with a single ``icon`` state from an already-loaded image."""
    out_dir = os.path.join(OUT_ROOT, rsi_name)
    os.makedirs(out_dir, exist_ok=True)
    image = image.convert("RGBA")
    assert image.size == (FRAME, FRAME), f"{rsi_name}: expected {FRAME}x{FRAME}, got {image.size}"
    image.save(os.path.join(out_dir, "icon.png"))
    write_meta(out_dir, [{"name": "icon"}])
    print(f"wrote {rsi_name}/icon.png")


def load_source(filename):
    return Image.open(os.path.join(SOURCE_DIR, filename)).convert("RGBA")


def make_tow_cable_variant(steel_image):
    """A darker, heavier-looking copy of the steel cable art for the capital-ship cable."""
    darker = ImageEnhance.Brightness(steel_image).enhance(0.72)
    darker = ImageEnhance.Contrast(darker).enhance(1.15)
    darker = ImageEnhance.Color(darker).enhance(0.85)
    return darker


# --- Installer gun: procedural cassette-futurism rivet gun ---------------------------------

TRANSPARENT = (0, 0, 0, 0)
BODY_FILL = (110, 114, 120, 255)
BODY_BORDER = (58, 60, 64, 255)
BODY_HILITE = (150, 154, 160, 255)
GRIP_FILL = (70, 72, 76, 255)
BARREL_FILL = (86, 89, 94, 255)
BARREL_DARK = (48, 50, 53, 255)
ACCENT = (224, 132, 32, 255)  # safety orange
ACCENT_DIM = (168, 98, 24, 255)
RIVET = (198, 202, 208, 255)


def new_canvas():
    return Image.new("RGBA", (FRAME, FRAME), TRANSPARENT)


def px(img, x, y, color):
    if 0 <= x < FRAME and 0 <= y < FRAME:
        img.putpixel((x, y), color)


def fill_rect(img, x0, y0, x1, y1, color):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            px(img, x, y, color)


def rect_outline(img, x0, y0, x1, y1, color):
    for x in range(x0, x1 + 1):
        px(img, x, y0, color)
        px(img, x, y1, color)
    for y in range(y0, y1 + 1):
        px(img, x0, y, color)
        px(img, x1, y, color)


def draw_installer(facing="icon"):
    """A chunky rivet-gun tool: thick barrel over a body block and a downward grip."""
    img = new_canvas()

    # Barrel (points right/east for the icon and world sprite).
    fill_rect(img, 16, 9, 29, 14, BARREL_FILL)
    rect_outline(img, 16, 9, 29, 14, BARREL_DARK)
    fill_rect(img, 26, 10, 28, 13, BARREL_DARK)  # muzzle shadow
    px(img, 29, 11, RIVET)
    px(img, 29, 12, RIVET)

    # Body block.
    fill_rect(img, 6, 11, 18, 20, BODY_FILL)
    rect_outline(img, 6, 11, 18, 20, BODY_BORDER)
    fill_rect(img, 7, 12, 17, 13, BODY_HILITE)

    # Accent stripe + status rivet.
    fill_rect(img, 8, 15, 16, 16, ACCENT)
    fill_rect(img, 8, 15, 16, 15, ACCENT_DIM)
    px(img, 15, 13, RIVET)
    px(img, 9, 13, RIVET)

    # Grip.
    fill_rect(img, 8, 20, 13, 29, GRIP_FILL)
    rect_outline(img, 8, 20, 13, 29, BODY_BORDER)
    for y in (23, 25, 27):
        fill_rect(img, 9, y, 12, y, BARREL_DARK)

    # Trigger guard hint.
    rect_outline(img, 13, 19, 17, 23, BODY_BORDER)

    return img


def main():
    os.makedirs(OUT_ROOT, exist_ok=True)

    hemp = load_source("brown_rope.png")
    synthetic = load_source("nylon_rope.png")
    bungee = load_source("bungee_rope.png")
    steel = load_source("metal_rope.png")
    eye = load_source("tow_eye_metal.png")

    build_single_icon_rsi("rope_hemp.rsi", hemp)
    build_single_icon_rsi("rope_synthetic.rsi", synthetic)
    build_single_icon_rsi("rope_bungee.rsi", bungee)
    build_single_icon_rsi("rope_steel_cable.rsi", steel)
    build_single_icon_rsi("rope_tow_cable.rsi", make_tow_cable_variant(steel))
    build_single_icon_rsi("anchor_eye.rsi", eye)

    installer_dir = os.path.join(OUT_ROOT, "installer.rsi")
    os.makedirs(installer_dir, exist_ok=True)
    icon = draw_installer()
    icon.save(os.path.join(installer_dir, "icon.png"))
    # Simple in-hand sprites: same silhouette, mirrored for the left hand.
    inhand_right = icon
    inhand_left = icon.transpose(Image.FLIP_LEFT_RIGHT)
    inhand_right.save(os.path.join(installer_dir, "inhand-right.png"))
    inhand_left.save(os.path.join(installer_dir, "inhand-left.png"))
    write_meta(installer_dir, [
        {"name": "icon"},
        {"name": "inhand-right"},
        {"name": "inhand-left"},
    ])
    print("wrote installer.rsi/{icon,inhand-left,inhand-right}.png")


if __name__ == "__main__":
    main()
