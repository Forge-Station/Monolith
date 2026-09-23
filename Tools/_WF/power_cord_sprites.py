"""Generates the power cord coil and clamp RSIs.

The coil states are recolours of the hand drawn 32x32 cord sprite; the clamps are
drawn here in the same flat cassette-futurism style as the tow eye plate.

    python Tools/_WF/power_cord_sprites.py [path/to/cable_rope.png]

Writes Resources/Textures/_WF/Tether/power_cord_{coils,clamps}.rsi. The source PNG is
only read, never written.
"""

import json
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Tether")
DEFAULT_SOURCE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tether_source", "cable_rope.png")

# Voltage tints, matching the stock HV / MV / APC cable colours.
VOLTAGES = {
    "hv": (255, 146, 38),
    "mv": (255, 225, 77),
    "lv": (62, 204, 95),
}

META = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Made by the Wolfgate team",
    "size": {"x": 32, "y": 32},
}


def tint(image, colour, strength=0.78, floor=48):
    """Recolours the lit part of the sprite towards a voltage colour, keeping its shading."""
    out = Image.new("RGBA", image.size)
    src = image.convert("RGBA").load()
    dst = out.load()
    for y in range(image.size[1]):
        for x in range(image.size[0]):
            r, g, b, a = src[x, y]
            if a == 0:
                continue
            lum = (r * 299 + g * 587 + b * 114) // 1000
            if lum <= floor:
                # Outline and deep shadow stay neutral so the silhouette survives.
                dst[x, y] = (r, g, b, a)
                continue

            scale = lum / 255.0
            target = tuple(int(c * scale) for c in colour)
            dst[x, y] = tuple(
                min(255, int(o * (1.0 - strength) + t * strength))
                for o, t in zip((r, g, b), target)
            ) + (a,)

    return out


def clamp(colour):
    """A bolted plate with a lit terminal block in the middle."""
    image = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    outline = (28, 30, 34, 255)
    plate = (104, 110, 118, 255)
    shade = (72, 77, 84, 255)
    light = (148, 155, 164, 255)

    draw.rectangle((10, 10, 21, 21), fill=plate, outline=outline)
    draw.line((11, 11, 20, 11), fill=light)
    draw.line((11, 11, 11, 20), fill=light)
    draw.line((11, 20, 20, 20), fill=shade)
    draw.line((20, 11, 20, 20), fill=shade)

    for bolt in ((12, 12), (19, 12), (12, 19), (19, 19)):
        draw.point(bolt, fill=outline)

    draw.rectangle((14, 14, 17, 17), fill=colour, outline=outline)
    draw.point((15, 15), fill=(255, 255, 255, 255))
    return image


def write_rsi(name, states):
    path = os.path.join(OUT, name + ".rsi")
    os.makedirs(path, exist_ok=True)
    for state, image in states.items():
        image.save(os.path.join(path, state + ".png"))

    meta = dict(META)
    meta["states"] = [{"name": state} for state in states]
    with open(os.path.join(path, "meta.json"), "w", encoding="utf-8", newline="\n") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")


def main():
    source = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SOURCE
    cord = Image.open(source).convert("RGBA")
    if cord.size != (32, 32):
        cord = cord.resize((32, 32), Image.NEAREST)

    write_rsi("power_cord_coils", {key: tint(cord, rgb) for key, rgb in VOLTAGES.items()})
    write_rsi("power_cord_clamps", {key: clamp(rgb + (255,)) for key, rgb in VOLTAGES.items()})
    print("wrote", OUT)


if __name__ == "__main__":
    main()
