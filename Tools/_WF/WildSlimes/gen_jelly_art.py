"""Makes the WildSlimes jelly art: Carcinoma's see-through jelly floor (from the flesh floor), its solid grotto
version and the jelly mound decor. Run from the repository root: python Tools/_WF/WildSlimes/gen_jelly_art.py"""
import json
import random

from PIL import Image, ImageDraw, ImageFilter

FLESH = "Resources/Textures/_WF/Resprites/Wizden/Tiles/meat.png"
OUT = "Resources/Textures/_WF/WildSlimes"
JELLY = (214, 78, 96)
ALPHA = 0.62


def jelly_tile(src: Image.Image, seed: int, alpha: float, dark: float, glints: bool = True) -> Image.Image:
    rng = random.Random(seed)
    grey = src.convert("L").filter(ImageFilter.GaussianBlur(1.2))
    out = Image.new("RGBA", src.size)
    px = out.load()
    g = grey.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            v = g[x, y] / 255.0
            shade = (0.55 + 0.6 * v) * dark
            r, gg, b = (min(255, int(c * shade)) for c in JELLY)
            px[x, y] = (r, gg, b, min(255, int(255 * alpha * (0.85 + 0.3 * (1 - v)))))
    if not glints:
        return out
    draw = ImageDraw.Draw(out)
    # Glossy highlights and a few bubbles.
    for _ in range(rng.choice([1, 2])):
        cx, cy = rng.randrange(3, w - 3), rng.randrange(3, h - 3)
        rad = rng.choice([0, 1, 1])
        draw.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), fill=(240, 170, 185, int(255 * min(1, alpha + 0.15))))
    for _ in range(2):
        cx, cy = rng.randrange(4, w - 4), rng.randrange(4, h - 4)
        draw.line((cx, cy, cx + rng.randrange(2, 5), cy - 1), fill=(255, 225, 230, int(255 * min(1, alpha + 0.3))))
    return out


def strip(alpha: float, dark: float, seed: int, glints: bool = True) -> Image.Image:
    flesh = Image.open(FLESH).convert("RGBA")
    n = flesh.width // 32
    out = Image.new("RGBA", (32 * n, 32))
    for i in range(n):
        out.paste(jelly_tile(flesh.crop((32 * i, 0, 32 * i + 32, 32)), seed + i, alpha, dark, glints), (32 * i, 0))
    return out


def blob(size: float, seed: int) -> Image.Image:
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
            r, g, b = (min(255, int(c * light)) for c in JELLY)
            px[x, y] = (r, g, b, int(255 * (0.55 + 0.35 * d)))
    draw = ImageDraw.Draw(img)
    hx, hy = cx - rx * 0.35, cy - ry * 0.45
    draw.ellipse((hx - 2, hy - 1, hx + 2, hy + 1), fill=(255, 220, 228, 230))
    for _ in range(int(3 * size)):
        bx, by = cx + rng.uniform(-rx / 2, rx / 2), cy + rng.uniform(-ry / 3, ry / 2)
        draw.ellipse((bx - 1, by - 1, bx + 1, by + 1), fill=(120, 20, 40, 200))
    return img


def main() -> None:
    # No glints on the ground tiles: bright dashes and plus signs tile into a visible grid.
    strip(ALPHA, 0.88, 11, False).save(f"{OUT}/Tiles/flesh_jelly.png")
    strip(1.0, 0.8, 21, False).save(f"{OUT}/Tiles/flesh_jelly_cavern.png")
    rsi = f"{OUT}/jelly_mound.rsi"
    blob(1.0, 3).save(f"{rsi}/mound.png")
    blob(0.6, 4).save(f"{rsi}/blob.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Drawn by Tools/_WF/WildSlimes/gen_jelly_art.py for Wolfgate",
        "size": {"x": 32, "y": 32},
        "states": [{"name": "mound"}, {"name": "blob"}],
    }
    with open(f"{rsi}/meta.json", "w", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
