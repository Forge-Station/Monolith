"""Builds the cavern decor RSIs recoloured from existing art: the Gut's digestive acid and each world's glow flora.

digestive_acid.rsi is lava.rsi hue-shifted to a yellow-green, with every state renamed lava -> acid for IconSmooth.
glow_flora.rsi holds one plant per world: `<plant>` is the source sprite mapped onto the world's colour ramp by
brightness, drawn shaded, and `<plant>_glow` is its brightest pixels in the glow colour, drawn unshaded on top.
Run from the repo root: python Tools/_WF/Caverns/gen_flavour.py
"""
import colorsys
import json
import os

from PIL import Image

OUT = "Resources/Textures/_WF/Caverns"
TILE = 32

ACID_SOURCE = "Resources/Textures/Tiles/Planet/lava.rsi"
ACID_HUE = 58  # degrees added to the lava's hue
ACID_SATURATION = 0.9
ACID_VALUE = 0.85

HYDRO = "Resources/Textures/Objects/Specific/Hydroponics"

# plant: (source rsi, state, dark, mid, bright, glow colour, share of opaque pixels that glow). A dark of None keeps
# the source colours.
PLANTS = {
    "glowcaps": (f"{HYDRO}/chanterelle.rsi", "harvest", "#0c3431", "#2aa88e", "#c4fff2", "#86ffe0", 0.45),
    "ember_lichen": (f"{HYDRO}/lingzhi.rsi", "harvest", None, None, None, "#ffb347", 0.4),
    "lamp_agave": (f"{HYDRO}/aloe.rsi", "harvest", "#3a260c", "#b98224", "#ffe7a6", "#ffd06a", 0.35),
    "shadow_bloom": (f"{HYDRO}/spacemans_trumpet.rsi", "harvest", "#1a0d29", "#7434b0", "#f4b8ff", "#d77bff", 0.4),
    "rime_thistle": (f"{HYDRO}/glasstle.rsi", "harvest", "#15314a", "#62ace0", "#f2fbff", "#c4ecff", 0.4),
    "nerve_cluster": ("Resources/Textures/Structures/Specific/Anomalies/flora_anom.rsi", "bulb", "#300812", "#a8283f",
                      "#ffb8c6", "#ff5d7c", 0.45),
}

COPYRIGHT_ACID = ("lava.rsi from https://github.com/tgstation/tgstation/tree/f116442e34fe3e941a1df474bb57bb410dd177a3/"
                  "icons/turf, hue-shifted for Wolfgate by Tools/_WF/Caverns/gen_flavour.py")
COPYRIGHT_FLORA = ("Recoloured for Wolfgate by Tools/_WF/Caverns/gen_flavour.py from chanterelle (vgstation13 commit "
                   "1dbcf389b0ec6b2c51b002df5fef8dd1519f8068), "
                   "lingzhi, aloe, spacemans_trumpet and glasstle (tgstation commit 40d89d11ea4a5cb81d61dc1018b46f4e7d32c62a) "
                   "and the flora anomaly bulb by TheShuEd (CC0-1.0)")


def hex_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def luminance(r, g, b):
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255


def acid():
    rsi = f"{OUT}/digestive_acid.rsi"
    os.makedirs(rsi, exist_ok=True)
    meta = json.load(open(f"{ACID_SOURCE}/meta.json", encoding="utf-8-sig"))

    for state in meta["states"]:
        image = Image.open(f"{ACID_SOURCE}/{state['name']}.png").convert("RGBA")
        pixels = image.load()
        for y in range(image.height):
            for x in range(image.width):
                r, g, b, a = pixels[x, y]
                h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
                r, g, b = colorsys.hsv_to_rgb((h + ACID_HUE / 360) % 1, s * ACID_SATURATION, v * ACID_VALUE)
                pixels[x, y] = (round(r * 255), round(g * 255), round(b * 255), a)
        state["name"] = state["name"].replace("lava", "acid")
        image.save(f"{rsi}/{state['name']}.png")

    meta["license"] = "CC-BY-SA-3.0"
    meta["copyright"] = COPYRIGHT_ACID
    write_meta(rsi, meta)


def flora():
    rsi = f"{OUT}/glow_flora.rsi"
    os.makedirs(rsi, exist_ok=True)
    states = []

    for plant, (source, state, dark, mid, bright, glow, share) in PLANTS.items():
        meta = json.load(open(f"{source}/meta.json", encoding="utf-8-sig"))
        entry = next(s for s in meta["states"] if s["name"] == state)
        image = Image.open(f"{source}/{state}.png").convert("RGBA")
        base = image.copy()
        lit = Image.new("RGBA", image.size, (0, 0, 0, 0))
        src = image.load()
        out = base.load()
        light = lit.load()

        # Glow threshold per frame, so an animated source keeps its pulse.
        for fx in range(0, image.width, TILE):
            for fy in range(0, image.height, TILE):
                lums = sorted(luminance(*src[x, y][:3]) for x in range(fx, fx + TILE) for y in range(fy, fy + TILE)
                              if src[x, y][3] > 0)
                if not lums:
                    continue
                cut = lums[int(len(lums) * (1 - share))]
                low, high = lums[0], lums[-1]

                for x in range(fx, fx + TILE):
                    for y in range(fy, fy + TILE):
                        r, g, b, a = src[x, y]
                        if a == 0:
                            continue
                        lum = luminance(r, g, b)
                        if dark is not None:
                            t = (lum - low) / (high - low) if high > low else 0.5
                            colour = lerp(hex_rgb(dark), hex_rgb(mid), t * 2) if t < 0.5 else \
                                lerp(hex_rgb(mid), hex_rgb(bright), t * 2 - 1)
                            out[x, y] = colour + (a,)
                        if lum >= cut:
                            t = (lum - cut) / (high - cut) if high > cut else 1
                            light[x, y] = lerp(hex_rgb(glow), (255, 255, 255), t * 0.35) + (round(a * (0.75 + 0.25 * t)),)

        base.save(f"{rsi}/{plant}.png")
        lit.save(f"{rsi}/{plant}_glow.png")
        for name in (plant, f"{plant}_glow"):
            copy = {"name": name}
            if "delays" in entry:
                copy["delays"] = entry["delays"]
            states.append(copy)

    write_meta(rsi, {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT_FLORA,
        "size": {"x": TILE, "y": TILE},
        "states": states,
    })


def write_meta(rsi, meta):
    with open(f"{rsi}/meta.json", "w", encoding="utf-8", newline="\n") as file:
        json.dump(meta, file, indent=2)
        file.write("\n")


if __name__ == "__main__":
    acid()
    flora()
