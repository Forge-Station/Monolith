"""Recolours the vanilla animal RSIs into Thrascias's snow and crystal wildlife.

Run from the repo root: python Tools/_WF/Planets/recolor_thrascias_life.py
Each source RSI is copied under Resources/Textures/_WF/Planets/Life/Thrascias with its shading kept (luminance is
remapped and tinted), its meta.json copyright kept, and a "recoloured for Wolfgate" note added.
"""
import json
import os
import shutil

from PIL import Image

TEXTURES = os.path.join("Resources", "Textures")
OUT = os.path.join(TEXTURES, "_WF", "Planets", "Life", "Thrascias")

# name, source rsi, states to recolour (the rest are copied as they are), recolour settings
JOBS = [
    ("snow_fox.rsi", "Mobs/Animals/fox.rsi", ["fox", "fox-moving", "fox_dead"], dict(lo=80, hi=255, tint=(0.96, 0.99, 1.04))),
    ("frost_bear.rsi", "Mobs/Animals/bear.rsi", ["bear", "bear_dead"], dict(lo=90, hi=255, tint=(0.95, 0.98, 1.05))),
    ("yeti.rsi", "Mobs/Animals/gorilla.rsi", ["crawling", "standing", "dead", "icon"], dict(lo=75, hi=255, tint=(0.97, 0.99, 1.03))),
    ("snow_spider.rsi", "Mobs/Animals/spacespider.rsi", ["spacespider", "spacespider_dead"], dict(lo=85, hi=235, tint=(0.95, 0.98, 1.0))),
]

# The crab keeps only its one state, renamed.
CRAB = ("crystal_crab.rsi", "Mobs/Elemental/orecrab.rsi", "silver_crab", "crystal_crab", dict(lo=50, hi=255, tint=(0.72, 0.93, 1.18)))

NOTE = " Recoloured for Wolfgate."


def luminance(r, g, b):
    return 0.299 * r + 0.587 * g + 0.114 * b


def lum_range(paths):
    """The 2nd and 98th percentile luminance of every opaque pixel, so a dark source still ends up pale."""
    values = []
    for path in paths:
        im = Image.open(path).convert("RGBA")
        values += [luminance(r, g, b) for r, g, b, a in im.getdata() if a > 0]
    values.sort()
    return values[int(len(values) * 0.02)], values[int(len(values) * 0.98)]


def recolour(path_in, path_out, lo, hi, tint, span):
    im = Image.open(path_in).convert("RGBA")
    px = im.load()
    low, high = span
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            t = (luminance(r, g, b) - low) / max(1.0, high - low)
            v = lo + (hi - lo) * min(1.0, max(0.0, t))
            px[x, y] = (
                min(255, int(v * tint[0])),
                min(255, int(v * tint[1])),
                min(255, int(v * tint[2])),
                a,
            )
    im.save(path_out)


def write_meta(meta, path_out):
    meta["copyright"] = meta.get("copyright", "").rstrip() + NOTE
    with open(os.path.join(path_out, "meta.json"), "w", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


def main():
    for name, src, states, settings in JOBS:
        src_dir = os.path.join(TEXTURES, *src.split("/"))
        out_dir = os.path.join(OUT, name)
        shutil.rmtree(out_dir, ignore_errors=True)
        os.makedirs(out_dir)
        span = lum_range([os.path.join(src_dir, state + ".png") for state in states])
        for file in os.listdir(src_dir):
            if file == "meta.json":
                continue
            if os.path.splitext(file)[0] in states:
                recolour(os.path.join(src_dir, file), os.path.join(out_dir, file), span=span, **settings)
            else:
                shutil.copy(os.path.join(src_dir, file), out_dir)
        with open(os.path.join(src_dir, "meta.json")) as f:
            write_meta(json.load(f), out_dir)

    name, src, state, new_state, settings = CRAB
    src_dir = os.path.join(TEXTURES, *src.split("/"))
    out_dir = os.path.join(OUT, name)
    shutil.rmtree(out_dir, ignore_errors=True)
    os.makedirs(out_dir)
    source = os.path.join(src_dir, state + ".png")
    recolour(source, os.path.join(out_dir, new_state + ".png"), span=lum_range([source]), **settings)
    with open(os.path.join(src_dir, "meta.json")) as f:
        meta = json.load(f)
    meta["states"] = [{"name": new_state, "directions": 4}]
    write_meta(meta, out_dir)


if __name__ == "__main__":
    main()
