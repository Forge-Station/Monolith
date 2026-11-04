"""Recolours of existing mob RSIs for Verda's wildlife (frost bear, mutated deer). Run from the repo root.

    python Tools/_WF/Planets/gen_verda_life_art.py
"""
import colorsys
import json
import os
import shutil

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
SRC = os.path.join(ROOT, "Resources", "Textures", "Mobs", "Animals")
OUT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Planets", "Life", "verda")


def frost_pixel(r, g, b):
    """Dark grey fur becomes pale blue-white; the saturated glow turns ice blue."""
    sat = max(r, g, b) - min(r, g, b)
    if sat < 45:
        lum = (r + g + b) / 3
        v = min(250, 95 + lum * 1.85)
        return int(v - 10), int(v - 3), int(min(255, v + 4))
    if r > b:
        return b, g, r
    return r, g, b


def mutant_pixel(r, g, b):
    """Brown hide goes a sickly grey-green; the pale parts stay pale."""
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if s < 0.12:
        # Grey antlers and hooves: a green cast.
        r2, g2, b2 = colorsys.hsv_to_rgb(0.26, 0.18, v)
    else:
        r2, g2, b2 = colorsys.hsv_to_rgb(0.24, min(1.0, s * 0.9 + 0.08), min(1.0, v * 0.95 + 0.03))
    return int(r2 * 255), int(g2 * 255), int(b2 * 255)


def recolour(img, fn):
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a:
                r, g, b = fn(r, g, b)
                px[x, y] = (r, g, b, a)
    return img


# Second head: the box (x0, y0, x1, y1) inside a 32x32 cell is copied and pasted behind the cell with the offset.
# Order of cells in a state's grid rows is South, North, East, West.
HEAD = {
    "S": ((5, 12, 27, 28), (5, 1)),
    "N": ((5, 0, 27, 9), (5, 0)),
    "E": ((14, 0, 32, 17), (-8, 3)),
    "W": ((0, 0, 18, 17), (8, 3)),
}
DIRS = ["S", "N", "E", "W"]


def two_head_cell(cell, direction):
    box, (dx, dy) = HEAD[direction]
    head = cell.crop(box)
    # The extra head sits a little darker, so it reads as a separate head.
    head = recolour(head, lambda r, g, b: (int(r * 0.85), int(g * 0.9), int(b * 0.85)))
    out = Image.new("RGBA", cell.size, (0, 0, 0, 0))
    out.alpha_composite(head, (box[0] + dx, box[1] + dy))
    out.alpha_composite(cell)
    return out


def add_heads(img, animated):
    """Apply the second head to every cell; an animated state has a row per direction, a still one a 2x2 grid."""
    img = img.convert("RGBA")
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    cell = 32
    for row in range(img.height // cell):
        for col in range(img.width // cell):
            direction = DIRS[row if animated else row * (img.width // cell) + col]
            c = img.crop((col * cell, row * cell, (col + 1) * cell, (row + 1) * cell))
            out.paste(two_head_cell(c, direction), (col * cell, row * cell))
    return out


def write_meta(src_rsi, dst_rsi, note, renames=None):
    with open(os.path.join(src_rsi, "meta.json"), encoding="utf-8") as f:
        meta = json.load(f)
    meta["copyright"] = f"{meta['copyright']}; {note}"
    if renames:
        for state in meta["states"]:
            state["name"] = renames.get(state["name"], state["name"])
    with open(os.path.join(dst_rsi, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


def frost_bear():
    src = os.path.join(SRC, "bear.rsi")
    dst = os.path.join(OUT, "frost_bear.rsi")
    os.makedirs(dst, exist_ok=True)
    for name in ("bear", "bear_dead", "glow"):
        recolour(Image.open(os.path.join(src, name + ".png")), frost_pixel).save(os.path.join(dst, name + ".png"))
    write_meta(src, dst, "recoloured for Wolfgate (frost bear)")


def mutant_deer(kind):
    src = os.path.join(SRC, f"reindeer_{kind}.rsi")
    dst = os.path.join(OUT, f"mutant_deer_{kind}.rsi")
    os.makedirs(dst, exist_ok=True)
    with open(os.path.join(src, "meta.json"), encoding="utf-8") as f:
        states = json.load(f)["states"]
    for state in states:
        name = state["name"]
        img = recolour(Image.open(os.path.join(src, name + ".png")), mutant_pixel)
        if state.get("directions") == 4:
            img = add_heads(img, "delays" in state)
        img.save(os.path.join(dst, name + ".png"))
    write_meta(src, dst, "recoloured and given a second head for Wolfgate (mutated deer)")


if __name__ == "__main__":
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)
    frost_bear()
    mutant_deer("buck")
