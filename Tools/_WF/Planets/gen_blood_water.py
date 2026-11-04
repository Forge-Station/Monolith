#!/usr/bin/env python3
"""Recolours the water sprites into blood for Carcinoma's lakes and rivers.

A sprite colour only multiplies, and the water art is dark blue, so tinting it red lands near black.
This remaps each frame's brightness onto a red ramp instead and keeps its alpha.

Usage: python Tools/_WF/Planets/gen_blood_water.py
Writes Resources/Textures/_WF/Planets/BloodWater/blood-lake.rsi and blood-river.rsi
"""

import json
import os

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
TEXTURES = os.path.join(ROOT, "Resources", "Textures")

# (source rsi, output rsi, brightness range of the source art, dark end, light end)
JOBS = [
    ("_WF/Resprites/Wizden/Tiles/TileEntities/liquid-water.rsi", "_WF/Planets/BloodWater/blood-lake.rsi",
     (38, 84), (156, 18, 28), (232, 58, 62)),
    ("Tiles/Planet/water.rsi", "_WF/Planets/BloodWater/blood-river.rsi",
     (110, 195), (148, 16, 26), (232, 60, 64)),
]


def recolour(img, span, dark, light):
    lo, hi = span
    img = img.convert("RGBA")
    out = []
    for r, g, b, a in img.getdata():
        if a == 0:
            out.append((0, 0, 0, 0))
            continue
        t = (0.3 * r + 0.59 * g + 0.11 * b - lo) / (hi - lo)
        t = min(1.0, max(0.0, t))
        out.append(tuple(round(d + (l - d) * t) for d, l in zip(dark, light)) + (a,))
    res = Image.new("RGBA", img.size)
    res.putdata(out)
    return res


def main():
    for src, dst, span, dark, light in JOBS:
        src_dir = os.path.join(TEXTURES, *src.split("/"))
        dst_dir = os.path.join(TEXTURES, *dst.split("/"))
        os.makedirs(dst_dir, exist_ok=True)
        for name in os.listdir(src_dir):
            path = os.path.join(src_dir, name)
            if name.endswith(".png"):
                recolour(Image.open(path), span, dark, light).save(os.path.join(dst_dir, name))
            elif name == "meta.json":
                with open(path, encoding="utf-8") as f:
                    meta = json.load(f)
                meta["copyright"] = meta.get("copyright", "") + "; recoloured to blood for Wolfgate"
                with open(os.path.join(dst_dir, name), "w", encoding="utf-8", newline="\r\n") as f:
                    json.dump(meta, f, indent=2)
                    f.write("\n")


if __name__ == "__main__":
    main()
