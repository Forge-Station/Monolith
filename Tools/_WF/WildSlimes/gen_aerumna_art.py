"""Makes Aerumna's wild slime floors: see-through pink crystal (from the exoplanet crystal tile) and its solid grotto
version. Run from the repository root: python Tools/_WF/WildSlimes/gen_aerumna_art.py"""
import random

from PIL import Image, ImageDraw, ImageFilter

SOURCE = "Resources/Textures/_WF/DecorImport/Tiles/aurora/exoplanet_crystal.png"
OUT = "Resources/Textures/_WF/WildSlimes/Aerumna"
CRYSTAL = (214, 96, 178)
ALPHA = 0.58


def crystal_tile(src: Image.Image, seed: int, alpha: float, dark: float) -> Image.Image:
    rng = random.Random(seed)
    grey = src.convert("L").filter(ImageFilter.GaussianBlur(0.6))
    out = Image.new("RGBA", src.size)
    px = out.load()
    g = grey.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            v = g[x, y] / 255.0
            shade = (0.5 + 0.7 * v) * dark
            r, gg, b = (min(255, int(c * shade)) for c in CRYSTAL)
            px[x, y] = (r, gg, b, min(255, int(255 * alpha * (0.8 + 0.4 * v))))
    draw = ImageDraw.Draw(out)
    # Facet edges and a sparkle or two.
    for _ in range(2):
        cx, cy = rng.randrange(3, w - 6), rng.randrange(3, h - 6)
        draw.line((cx, cy, cx + rng.randrange(3, 6), cy + rng.randrange(3, 6)), fill=(255, 200, 240, int(255 * min(1, alpha + 0.25))))
    for _ in range(rng.choice([1, 2])):
        cx, cy = rng.randrange(2, w - 2), rng.randrange(2, h - 2)
        draw.point((cx, cy), fill=(255, 245, 255, 255))
    return out


def strip(alpha: float, dark: float, seed: int) -> Image.Image:
    src = Image.open(SOURCE).convert("RGBA")
    variants = [src, src.transpose(Image.FLIP_LEFT_RIGHT), src.transpose(Image.FLIP_TOP_BOTTOM), src.rotate(90)]
    out = Image.new("RGBA", (32 * len(variants), 32))
    for i, variant in enumerate(variants):
        out.paste(crystal_tile(variant, seed + i, alpha, dark), (32 * i, 0))
    return out


def main() -> None:
    strip(ALPHA, 1.0, 61).save(f"{OUT}/pink_crystal.png")
    strip(1.0, 0.7, 71).save(f"{OUT}/pink_crystal_cavern.png")


if __name__ == "__main__":
    main()
