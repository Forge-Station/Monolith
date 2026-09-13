"""Builds Resources/Textures/_WF/Mobs/Customization/Markings/Genital/sheath.rsi from NovaSector's sheath art.

Run from anywhere (needs Pillow):
    python Tools/_WF/genitals/convert_sheath.py              convert ../NovaSector sheath_onmob.dmi
    python Tools/_WF/genitals/convert_sheath.py --from DIR   copy an already converted RSI (e.g. a review candidate)

One RSI state per DMI colour layer; a missing layer becomes an empty padding state, so every sheath style has the
same outer + inner pair. No pixel offset is applied: the art lines up with the Wolfgate genital art at (0,0).
For Wolfgate the lower slit pixel of sheath_slit_1 is then moved from the outer to the inner layer (move_slit_pixel).
"""
import json
import os
import shutil
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dmi  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SRC = os.path.join(ROOT, "..", "NovaSector", "modular_nova", "master_files", "icons", "mob", "sprite_accessory",
                   "genitals", "sheath_onmob.dmi")
OUT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Mobs", "Customization", "Markings", "Genital", "sheath.rsi")
DX, DY = 0, 0

# Nova name -> Wolfgate name. secondary = sheath body (sheath colour), primary = opening / emerging tip (penis colour).
MAP = {}
for style in ("normal", "slit"):
    for stage in (0, 1):
        MAP[f"m_sheath_{style}_{stage}_FRONT_UNDER_secondary"] = f"sheath_{style}_{stage}_FRONT"
        MAP[f"m_sheath_{style}_{stage}_FRONT_UNDER_primary"] = f"sheath_{style}_{stage}_FRONT_inner"

COPYRIGHT = (
    "Converted from NovaSector (https://github.com/NovaSector/NovaSector) "
    "modular_nova/master_files/icons/mob/sprite_accessory/genitals/sheath_onmob.dmi at commit "
    "cbf8dc1ad6f63825326ed0f3d301788e3817236a; the same pixels exist in Skyrat-tg "
    "(https://github.com/Skyrat-SS13/Skyrat-tg) modular_skyrat/master_files/icons/mob/sprite_accessory/"
    "genitals/penis_onmob.dmi (m_penis_sheath_*, m_penis_slit_*) at commit "
    "85d18769f98d261404216a80dc163e1148e13099, where the sheath art was first added in PR #1032. "
    "Colour layers split into separate states: _FRONT is the source secondary layer, _FRONT_inner the primary layer. "
    "sheath_slit_0_FRONT is an empty padding state. "
    "For Wolfgate the lower slit pixel of sheath_slit_1 was moved to the inner layer."
)


def to_rsi_png(dirs_images, w=32, h=32):
    """Static state, 4 directions -> 2x2 sheet in S, N, E, W order (RSI is direction-major)."""
    sheet = Image.new("RGBA", (w * 2, h * 2), (0, 0, 0, 0))
    for i, im in enumerate(dirs_images):
        shifted = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        shifted.paste(im, (DX, DY), im)
        sheet.paste(shifted, ((i % 2) * w, (i // 2) * h))
    return sheet


def convert(out):
    w, h, sts = dmi.load_dmi(SRC)
    have = {s["name"]: s for s in sts}
    for src, dst in MAP.items():
        if src in have:
            assert have[src]["frames"] == 1 and have[src]["dirs"] == 4, src
            img = to_rsi_png(have[src]["img"][0])
        else:
            img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))  # padding: the source has no such colour layer
        img.save(os.path.join(out, dst + ".png"), optimize=True)


def move_slit_pixel(out):
    """Moves every opaque pixel of sheath_slit_1_FRONT into sheath_slit_1_FRONT_inner; the outer becomes padding."""
    outer_path = os.path.join(out, "sheath_slit_1_FRONT.png")
    inner_path = os.path.join(out, "sheath_slit_1_FRONT_inner.png")
    outer = Image.open(outer_path).convert("RGBA")
    inner = Image.open(inner_path).convert("RGBA")
    moved = []
    for y in range(outer.height):
        for x in range(outer.width):
            px = outer.getpixel((x, y))
            if px[3] == 0:
                continue
            if inner.getpixel((x, y))[3] == 0:
                inner.putpixel((x, y), px)
            outer.putpixel((x, y), (0, 0, 0, 0))
            moved.append((x, y))
    outer.save(outer_path, optimize=True)
    inner.save(inner_path, optimize=True)
    return moved


def write_meta(out):
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": 32, "y": 32},
        "states": [{"name": dst, "directions": 4} for dst in MAP.values()],
    }
    # CRLF like the rest of the working tree; git normalises it on commit.
    with open(os.path.join(out, "meta.json"), "w", encoding="utf-8", newline="\r\n") as f:
        json.dump(meta, f, indent=4)
        f.write("\n")


def main(argv):
    source_rsi = None
    if len(argv) == 2 and argv[0] == "--from":
        source_rsi = argv[1]
    elif argv:
        print(__doc__)
        return 2

    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)

    if source_rsi:
        for dst in MAP.values():
            shutil.copyfile(os.path.join(source_rsi, dst + ".png"), os.path.join(OUT, dst + ".png"))
    else:
        convert(OUT)

    moved = move_slit_pixel(OUT)
    write_meta(OUT)
    print("wrote", OUT, "-", len(MAP), "states; slit pixels moved to the inner layer:", moved)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
