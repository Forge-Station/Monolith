"""Builds the cavern stairs RSI: CE's stone stairs redrawn in steel, so built stairs don't read as a world's climb point.

Each pixel keeps its shading and takes its colour from a steel ramp, with the tread edges picked out a little brighter.
Run from the repo root: python Tools/_WF/Caverns/gen_stairs.py
"""
import json
import os

from PIL import Image

SOURCE = "Resources/Textures/_CE/Structures/Architecture/Ladders/stone.rsi"
OUT = "Resources/Textures/_WF/Caverns/Stairs/steel.rsi"
STATE = "straight"

# Dark to light. The stone art sits in the lower half of the range, so the ramp is stretched over what it uses.
RAMP = ((24, 27, 33), (58, 64, 74), (104, 112, 124), (156, 164, 174), (206, 212, 220))


def steel(level):
    """The ramp colour at a brightness from 0 to 1."""
    scaled = min(max(level, 0.0), 1.0) * (len(RAMP) - 1)
    low = int(scaled)
    high = min(low + 1, len(RAMP) - 1)
    mix = scaled - low
    return tuple(round(RAMP[low][i] + (RAMP[high][i] - RAMP[low][i]) * mix) for i in range(3))


def main():
    source = Image.open(os.path.join(SOURCE, STATE + ".png")).convert("RGBA")
    pixels = source.load()
    lumas = [
        0.299 * r + 0.587 * g + 0.114 * b
        for x in range(source.width)
        for y in range(source.height)
        for r, g, b, a in [pixels[x, y]]
        if a > 0
    ]
    darkest, lightest = min(lumas), max(lumas)

    out = Image.new("RGBA", source.size, (0, 0, 0, 0))
    drawn = out.load()

    for x in range(source.width):
        for y in range(source.height):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue

            level = (0.299 * r + 0.587 * g + 0.114 * b - darkest) / max(lightest - darkest, 1.0)
            drawn[x, y] = (*steel(level ** 0.8), a)

    os.makedirs(OUT, exist_ok=True)
    out.save(os.path.join(OUT, STATE + ".png"))

    with open(os.path.join(SOURCE, "meta.json"), encoding="utf-8") as handle:
        size = json.load(handle)["size"]

    meta = {
        "version": 1,
        "license": "CC-BY-SA-4.0",
        "copyright": "Recoloured by Tools/_WF/Caverns/gen_stairs.py from the stone stairs by TheShuEd (discord) in "
                     "_CE/Structures/Architecture/Ladders/stone.rsi",
        "size": size,
        "states": [{"name": STATE, "directions": 4}],
    }

    with open(os.path.join(OUT, "meta.json"), "w", encoding="utf-8", newline="\n") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    main()
