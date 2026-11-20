"""Makes the jelly art of the Verda, Merak and Thrascias wild slimes: each world's see-through patch floor, its solid
grotto version and its mound decor, tinted from a grain tile. Run from the repository root:
python Tools/_WF/WildSlimes/gen_world_jelly_art.py"""
import json
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = "Resources/Textures/_WF/WildSlimes"

# World folder: grain tile strip, jelly colour, surface alpha, file stem, decor rsi.
WORLDS = {
    "Verda": ("Resources/Textures/_WF/Resprites/Wizden/Tiles/Ice/ice.png", (84, 196, 176), 0.5, "water_jelly", "dew_mound"),
    "Merak": ("Resources/Textures/_WF/DecorImport/Tiles/nebula/rock_sand.png", (218, 178, 112), 0.58, "glass_sand", "glass_lump"),
    "Thrascias": ("Resources/Textures/_WF/DecorImport/Tiles/goonstation/ice_rough.png", (128, 184, 236), 0.5, "rime_ice", "rime_mound"),
}


def lighten(colour, amount):
    return tuple(min(255, int(c + (255 - c) * amount)) for c in colour)


def jelly_tile(src, colour, seed, alpha, dark):
    """The grain tile's own detail, tinted: no drawn glints, which read as marks on the floor."""
    grey = src.convert("L").filter(ImageFilter.GaussianBlur(0.4))
    out = Image.new("RGBA", src.size)
    px = out.load()
    g = grey.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            v = g[x, y] / 255.0
            shade = (0.55 + 0.75 * v) * dark
            r, gg, b = (min(255, int(c * shade)) for c in colour)
            px[x, y] = (r, gg, b, min(255, int(255 * alpha * (0.85 + 0.3 * (1 - v)))))
    return out


def strip(grain_path, colour, alpha, dark, seed):
    grain = Image.open(grain_path).convert("RGBA")
    n = min(4, grain.width // 32)
    out = Image.new("RGBA", (32 * 4, 32))
    for i in range(4):
        j = i % n
        tile = jelly_tile(grain.crop((32 * j, 0, 32 * j + 32, 32)), colour, seed + i, alpha, dark)
        if i >= n:
            tile = tile.transpose(Image.Transpose.ROTATE_90)
        out.paste(tile, (32 * i, 0))
    return out


def blob(colour, size, seed):
    rng = random.Random(seed)
    img = Image.new("RGBA", (32, 32))
    px = img.load()
    cx, cy = 16 + rng.uniform(-1, 1), 18
    rx, ry = 11 * size, 8 * size
    for y in range(32):
        for x in range(32):
            d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
            if d > 1:
                continue
            light = 1.15 - 0.5 * ((y - (cy - ry)) / (2 * ry)) - 0.2 * d
            r, g, b = (min(255, int(c * light)) for c in colour)
            px[x, y] = (r, g, b, int(255 * (0.55 + 0.35 * d)))
    draw = ImageDraw.Draw(img)
    hx, hy = cx - rx * 0.35, cy - ry * 0.45
    draw.ellipse((hx - 2, hy - 1, hx + 2, hy + 1), fill=lighten(colour, 0.8) + (230,))
    speck = tuple(int(c * 0.45) for c in colour)
    for _ in range(int(3 * size)):
        bx, by = cx + rng.uniform(-rx / 2, rx / 2), cy + rng.uniform(-ry / 3, ry / 2)
        draw.ellipse((bx - 1, by - 1, bx + 1, by + 1), fill=speck + (200,))
    return img


def main():
    for seed, (world, (grain, colour, alpha, stem, decor)) in enumerate(WORLDS.items()):
        folder = f"{OUT}/{world}"
        os.makedirs(f"{folder}/Tiles", exist_ok=True)
        strip(grain, colour, alpha, 1.0, 31 + seed * 10).save(f"{folder}/Tiles/{stem}.png")
        strip(grain, colour, 1.0, 0.9, 41 + seed * 10).save(f"{folder}/Tiles/{stem}_cavern.png")
        rsi = f"{folder}/{decor}.rsi"
        os.makedirs(rsi, exist_ok=True)
        blob(colour, 1.0, 5 + seed).save(f"{rsi}/mound.png")
        blob(colour, 0.6, 6 + seed).save(f"{rsi}/blob.png")
        meta = {
            "version": 1,
            "license": "CC-BY-SA-3.0",
            "copyright": "Drawn by Tools/_WF/WildSlimes/gen_world_jelly_art.py for Wolfgate",
            "size": {"x": 32, "y": 32},
            "states": [{"name": "mound"}, {"name": "blob"}],
        }
        with open(f"{rsi}/meta.json", "w", newline="\n") as f:
            json.dump(meta, f, indent=2)
            f.write("\n")


if __name__ == "__main__":
    main()
