"""Builds the Wolfmed gib decal masks from Escape From Nevado's icons/effects/blood.dmi.

Each gib state is split into layers a decal can colour separately: <name> is the blood (the reds, a greyscale
mask tinted with the body's blood colour), <name>_flesh the skin-coloured bits (the peach and orange in gib2 and
gib6, a greyscale mask tinted with the body's skin colour), and <name>_meat the pink innards (kept in their own
colours). Four-direction states become four states, <name>_0 .. _3, so a decal can pick one and take its own
rotation. The decal prototypes for every state go to Resources/Prototypes/_WF/Wolfmed/Decals/gibs.yml.

Usage: python gen_wolfmed_gibs.py [--dmi <blood.dmi>]
"""
import colorsys
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(REPO, "Resources", "Textures", "_WF", "Wolfmed", "Effects", "gibs.rsi")
DEFAULT_DMI = os.environ.get("NEVADO_BLOOD_DMI")  # or pass --dmi <path to icons/effects/blood.dmi>
NEVADO_REPO = "https://github.com/EscapeFromNevado/EscapeFromNevado (icons/effects/blood.dmi)"
STATES = ["gibmid1", "gib1", "gib2", "gib3", "gib4", "gib5", "gib6"]
FRAME = 32
# Below this the mask keeps no shading: the darkest blood is still a third of the tint.
FLOOR = 0.35

sys.path.insert(0, HERE)
from dmi_extract import parse, read_description  # noqa: E402


def layer(r, g, b):
    """Which layer a pixel belongs to: the peach and orange are flesh, the magenta pinks are meat, the rest blood."""
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    hue = h * 360
    if 15 <= hue <= 50:
        return "flesh"
    if 270 <= hue < 340 and s < 0.7:
        return "meat"
    return "blood"


def grey_mask(tile, chosen):
    """A greyscale mask of the chosen pixels, the darkest at FLOOR of the tint and the lightest at all of it."""
    out = Image.new("RGBA", tile.size, (0, 0, 0, 0))
    lum = {(x, y): 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2] for x, y, p in chosen}
    low, high = min(lum.values()), max(lum.values())
    for x, y, p in chosen:
        t = 1.0 if high == low else (lum[(x, y)] - low) / (high - low)
        grey = int(255 * (FLOOR + (1 - FLOOR) * t))
        out.putpixel((x, y), (grey, grey, grey, p[3]))
    return out


def layers(tile):
    """{"blood": mask, "flesh": mask or absent, "meat": coloured image or absent}."""
    pixels = [(x, y, tile.getpixel((x, y))) for x in range(tile.width) for y in range(tile.height)]
    opaque = [(x, y, p) for x, y, p in pixels if p[3] > 0]
    groups = {"blood": [], "flesh": [], "meat": []}
    for x, y, p in opaque:
        groups[layer(*p[:3])].append((x, y, p))
    out = {}
    if groups["blood"]:
        out["blood"] = grey_mask(tile, groups["blood"])
    if groups["flesh"]:
        out["flesh"] = grey_mask(tile, groups["flesh"])
    if groups["meat"]:
        meat = Image.new("RGBA", tile.size, (0, 0, 0, 0))
        for x, y, p in groups["meat"]:
            meat.putpixel((x, y), p)
        out["meat"] = meat
    return out


def main():
    dmi = DEFAULT_DMI
    if "--dmi" in sys.argv:
        dmi = sys.argv[sys.argv.index("--dmi") + 1]
    if not dmi:
        sys.exit("pass --dmi <path to EscapeFromNevado icons/effects/blood.dmi> or set NEVADO_BLOOD_DMI")
    im, desc = read_description(dmi)
    width, height, states = parse(desc)
    im = im.convert("RGBA")
    columns = im.width // width
    index = 0
    out_states = []
    decals = []
    os.makedirs(OUT, exist_ok=True)
    for state in states:
        count = state["dirs"] * state["frames"]
        if state["name"] in STATES:
            for d in range(state["dirs"]):
                i = index + d
                x, y = (i % columns) * width, (i // columns) * height
                tile = im.crop((x, y, x + width, y + height))
                name = state["name"] if state["dirs"] == 1 else f"{state['name']}_{d}"
                split = layers(tile)
                for kind, image in split.items():
                    suffix = "" if kind == "blood" else "_" + kind
                    image.save(os.path.join(OUT, name + suffix + ".png"), optimize=True)
                    out_states.append({"name": name + suffix})
                    decals.append((name + suffix, kind))
        index += count
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": f"Gib art from {NEVADO_REPO}, split into blood and flesh greyscale masks by "
                     "Tools/_WF/Wolfmed/gen_wolfmed_gibs.py for Wolfgate (Wolfmed)",
        "size": {"x": FRAME, "y": FRAME},
        "states": out_states,
    }
    with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")
    print(f"wrote {len(out_states)} states to {OUT}")
    print(" ".join(s["name"] for s in out_states))
    write_decals(decals)


def write_decals(decals):
    """One decal prototype per state: the blood and flesh layers take a custom colour, the meat keeps its own."""
    path = os.path.join(REPO, "Resources", "Prototypes", "_WF", "Wolfmed", "Decals", "gibs.yml")
    lines = [
        "# Generated by Tools/_WF/Wolfmed/gen_wolfmed_gibs.py from Escape From Nevado's gib art. Trauma gibs (GORE):",
        "# decals like the floor splats, cleanable, never painted by a mapper. WFWolfmedGib<state> is the blood layer,",
        "# tinted with the blood colour; _flesh the skin-coloured bits, tinted with the body's skin colour; _meat the",
        "# innards in their own colours.",
        "",
    ]
    for name, kind in decals:
        lines += [
            "- type: decal",
            f"  id: WFWolfmedGib_{name}",
            "  tags: [ \"station\", \"dirty\" ]",
            "  defaultCleanable: true",
            f"  defaultCustomColor: {'false' if kind == 'meat' else 'true'}",
            "  showMenu: false",
            "  sprite:",
            "    sprite: _WF/Wolfmed/Effects/gibs.rsi",
            f"    state: {name}",
            "",
        ]
    with open(path, "w", newline="\n") as handle:
        handle.write("\n".join(lines))
    print(f"wrote {len(decals)} decals to {path}")


if __name__ == "__main__":
    main()
