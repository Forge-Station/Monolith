"""Builds the cave creature RSIs, each an existing mob's sprites mapped onto a new colour ramp by brightness.

Run from the repo root: python Tools/_WF/Caverns/gen_life.py
"""
import json
import os

from PIL import Image

OUT = "Resources/Textures/_WF/Caverns/Life"
TEXTURES = "Resources/Textures"

# output rsi: (source rsi, states kept (None for all), dark, mid, bright)
CREATURES = {
    "rime_bat": ("Mobs/Animals/bat.rsi", None, "#243a52", "#9fcdea", "#f4fcff"),
    "deep_bat": ("Mobs/Animals/bat.rsi", None, "#07030d", "#352052", "#a07be0"),
    "rime_slime": ("Mobs/Aliens/slimes.rsi", ["blue_adult_slime", "blue_adult_slime_dead"], "#5b86a8", "#cde8fa", "#ffffff"),
    "antibody": ("Mobs/Aliens/slimes.rsi", ["blue_adult_slime", "blue_adult_slime_dead"], "#8c5a66", "#f3d9db", "#ffffff"),
    "bile_slug": ("Mobs/Animals/slug.rsi", None, "#2f3d0b", "#97b127", "#e6f76c"),
}

COPYRIGHT = ("Recoloured for Wolfgate by Tools/_WF/Caverns/gen_life.py from {source} ({original})")


def hex_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def ramp(dark, mid, bright, v):
    if v < 0.5:
        return lerp(dark, mid, v * 2)
    return lerp(mid, bright, (v - 0.5) * 2)


def recolour(name, source, states, dark, mid, bright):
    src = f"{TEXTURES}/{source}"
    out = f"{OUT}/{name}.rsi"
    os.makedirs(out, exist_ok=True)
    meta = json.load(open(f"{src}/meta.json", encoding="utf-8-sig"))
    dark, mid, bright = hex_rgb(dark), hex_rgb(mid), hex_rgb(bright)
    kept = []

    for state in meta["states"]:
        if states is not None and state["name"] not in states:
            continue

        image = Image.open(f"{src}/{state['name']}.png").convert("RGBA")
        pixels = image.load()
        lo, hi = 1.0, 0.0
        for y in range(image.height):
            for x in range(image.width):
                r, g, b, a = pixels[x, y]
                if a:
                    v = (0.299 * r + 0.587 * g + 0.114 * b) / 255
                    lo, hi = min(lo, v), max(hi, v)

        span = max(hi - lo, 0.05)
        for y in range(image.height):
            for x in range(image.width):
                r, g, b, a = pixels[x, y]
                if not a:
                    continue
                v = ((0.299 * r + 0.587 * g + 0.114 * b) / 255 - lo) / span
                pixels[x, y] = ramp(dark, mid, bright, min(max(v, 0.0), 1.0)) + (a,)

        image.save(f"{out}/{state['name']}.png")
        kept.append(state)

    meta["states"] = kept
    meta["copyright"] = COPYRIGHT.format(source=source, original=meta.get("copyright", "see the source"))
    with open(f"{out}/meta.json", "w", encoding="utf-8", newline="\n") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")


def main():
    for name, (source, states, dark, mid, bright) in CREATURES.items():
        recolour(name, source, states, dark, mid, bright)


if __name__ == "__main__":
    main()
