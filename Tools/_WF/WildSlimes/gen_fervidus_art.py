"""Makes Fervidus's wild slime floors: see-through obsidian glass (from the obsidian tile) and its solid grotto
version. Run from the repository root: python Tools/_WF/WildSlimes/gen_fervidus_art.py"""
import random

from PIL import Image, ImageDraw, ImageFilter

SOURCE = "Resources/Textures/_WF/DecorImport/Tiles/shiptest/obsidian.png"
OUT = "Resources/Textures/_WF/WildSlimes/Fervidus"
GLASS = (46, 30, 52)
EMBER = (255, 120, 40)
ALPHA = 0.6


def glass_tile(src: Image.Image, seed: int, alpha: float, dark: float) -> Image.Image:
    rng = random.Random(seed)
    grey = src.convert("L").filter(ImageFilter.GaussianBlur(0.8))
    out = Image.new("RGBA", src.size)
    px = out.load()
    g = grey.load()
    w, h = src.size
    for y in range(h):
        for x in range(w):
            v = g[x, y] / 255.0
            shade = (0.6 + 0.9 * v) * dark
            r, gg, b = (min(255, int(c * shade)) for c in GLASS)
            px[x, y] = (r, gg, b, min(255, int(255 * alpha * (0.8 + 0.4 * v))))
    draw = ImageDraw.Draw(out)
    # Broad facets, lighter and darker, so the glass doesn't read as a flat fill.
    for i in range(3):
        cx, cy = rng.randrange(0, w), rng.randrange(0, h)
        points = [(cx + rng.randrange(-10, 11), cy + rng.randrange(-10, 11)) for _ in range(3)]
        tone = (88, 64, 104) if i % 2 == 0 else (24, 14, 30)
        r, gg, b = (int(c * dark) for c in tone)
        draw.polygon(points, fill=(r, gg, b, int(255 * alpha * 0.9)))
    # Conchoidal glints and a few embers caught in the glass.
    for _ in range(2):
        cx, cy = rng.randrange(4, w - 6), rng.randrange(4, h - 4)
        draw.line((cx, cy, cx + rng.randrange(2, 6), cy - rng.randrange(0, 3)), fill=(190, 170, 220, int(255 * min(1, alpha + 0.3))))
    for _ in range(rng.choice([1, 2, 3])):
        cx, cy = rng.randrange(2, w - 2), rng.randrange(2, h - 2)
        draw.point((cx, cy), fill=EMBER + (int(255 * min(1, alpha + 0.35)),))
    return out


def strip(alpha: float, dark: float, seed: int) -> Image.Image:
    src = Image.open(SOURCE).convert("RGBA")
    variants = [src, src.transpose(Image.FLIP_LEFT_RIGHT), src.transpose(Image.FLIP_TOP_BOTTOM), src.rotate(90)]
    out = Image.new("RGBA", (32 * len(variants), 32))
    for i, variant in enumerate(variants):
        out.paste(glass_tile(variant, seed + i, alpha, dark), (32 * i, 0))
    return out


def main() -> None:
    strip(ALPHA, 1.0, 41).save(f"{OUT}/obsidian_glass.png")
    strip(1.0, 0.75, 51).save(f"{OUT}/obsidian_glass_cavern.png")


if __name__ == "__main__":
    main()
