"""Draws the medical HUD's cardiac-arrest icon from the stock critical one.

Usage: python Tools/_WF/Wolfmed/gen_arrest_icon.py

Same 8x8 frame and palette as Interface/Misc/health_icons.rsi Critical, with the cross replaced by a flat
trace across the middle: a medic reads "no pulse" rather than "hurt" at a glance.
"""
import json
import os

from PIL import Image

SOURCE = "Resources/Textures/Interface/Misc/health_icons.rsi/Critical.png"
OUT = "Resources/Textures/_WF/Wolfmed/Interface/health_icons.rsi"

META = {
    "version": 1,
    "size": {"x": 8, "y": 8},
    "license": "CC-BY-SA-3.0",
    "copyright": (
        "Flatline derived from Interface/Misc/health_icons.rsi Critical, taken from /tg/station at commit "
        "https://github.com/tgstation/tgstation/commit/20ae083f140ac5b4da7e8bc40f95349001b6c086, "
        "modified for Wolfgate (Wolfmed)"
    ),
    "states": [{"name": "Flatline"}],
}

FILL = (0xFF, 0x00, 0x33, 0xFF)
LINE = (0xFF, 0xFF, 0xFF, 0xFF)


def main():
    icon = Image.open(SOURCE).convert("RGBA")

    # The cross comes out, the fill goes back in, and one white row goes across.
    for y in range(7):
        for x in range(1, 6):
            if icon.getpixel((x, y)) == LINE:
                icon.putpixel((x, y), FILL)

    for x in range(1, 6):
        icon.putpixel((x, 3), LINE)

    os.makedirs(OUT, exist_ok=True)
    icon.save(os.path.join(OUT, "Flatline.png"))
    with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as handle:
        json.dump(META, handle, indent=4)
        handle.write("\n")

    print("wrote", OUT)


if __name__ == "__main__":
    main()
