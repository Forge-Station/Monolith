"""Paints the Fervidus obsidian glass: eight variants, an organic pattern kept off the tile borders so any two
variants sit side by side.

Run from the repo root: python Tools/_WF/WildSlimes/gen_slime_tiles.py [contact_sheet.png]
Needs numpy and Pillow.
"""
import math
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3] / "Resources" / "Textures" / "_WF" / "WildSlimes"
SIZE = 32
VARIANTS = 8

# The ground a patch is drawn over shows through, so the surface tiles are semi-transparent; caverns are solid.


def clamp(v, lo=0.0, hi=255.0):
    return np.minimum(np.maximum(v, lo), hi)


def periodic_noise(rng, cells, octaves=2):
    """Smooth noise that wraps at the tile size, so a tile joins itself and shares its base look with every variant."""
    total = np.zeros((SIZE, SIZE))
    amp = 1.0
    norm = 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        grid = rng.random((n, n))
        xs = np.arange(SIZE) / SIZE * n
        x0 = np.floor(xs).astype(int) % n
        x1 = (x0 + 1) % n
        f = xs - np.floor(xs)
        f = f * f * (3 - 2 * f)
        row = grid[:, x0] * (1 - f) + grid[:, x1] * f
        col = row[x0, :] * (1 - f)[:, None] + row[x1, :] * f[:, None]
        total += col * amp
        norm += amp
        amp *= 0.5
    return total / norm


class Canvas:
    def __init__(self, base, base_noise, rng, tint_range, np_rng):
        self.rng = rng
        shade = 1.0 + (base_noise - 0.5) * tint_range
        self.px = np.array(base, dtype=float)[None, None, :] * shade[:, :, None]
        # per variant dither, kept tiny so seams between variants stay quiet
        self.px += (np_rng.random((SIZE, SIZE, 1)) - 0.5) * 3.0

    def put(self, x, y, col, a):
        """Blends one pixel; drawing wraps round the tile so a feature cut by a border carries on at the other side."""
        x = int(x) % SIZE
        y = int(y) % SIZE
        self.px[y, x] = self.px[y, x] * (1 - a) + np.array(col, dtype=float) * a

    def pos(self):
        return self.rng.uniform(0, SIZE), self.rng.uniform(0, SIZE)

    def lump(self, cx, cy, r, col, a, soft=0.35):
        """An irregular patch: the radius wobbles with the angle, the rim fades over `soft`."""
        rng = self.rng
        ph = [rng.uniform(0, math.tau) for _ in range(3)]
        am = [rng.uniform(0.1, 0.3), rng.uniform(0.05, 0.2), rng.uniform(0.03, 0.12)]
        for y in range(int(cy - r * 1.5) - 1, int(cy + r * 1.5) + 2):
            for x in range(int(cx - r * 1.5) - 1, int(cx + r * 1.5) + 2):
                ang = math.atan2(y - cy, x - cx)
                rr = r * (1 + sum(am[k] * math.sin(ang * (k + 2) + ph[k]) for k in range(3)))
                d = math.hypot(x - cx, y - cy) / rr
                if d >= 1:
                    continue
                k = 1.0 if d < 1 - soft else (1 - d) / soft
                self.put(x, y, col, a * k)

    def line(self, x0, y0, x1, y1, col, a, thick=1):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(n + 1):
            t = i / max(n, 1)
            x, y = int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t))
            self.put(x, y, col, a)
            if thick > 1:
                self.put(x + 1, y, col, a * 0.55)
                self.put(x, y + 1, col, a * 0.55)

    def walk(self, x, y, heading, length, col, a, wobble=0.5, branch=0.0, depth=0, fade=False, thick=1):
        """A wandering vein that can fork."""
        rng = self.rng
        for i in range(length):
            heading += rng.uniform(-wobble, wobble)
            nx, ny = x + math.cos(heading), y + math.sin(heading)
            self.line(x, y, nx, ny, col, a * (1 - i / length * 0.6) if fade else a, thick if depth == 0 else 1)
            x, y = nx, ny
            if depth < 2 and branch and rng.random() < branch:
                self.walk(x, y, heading + rng.choice([-1, 1]) * rng.uniform(0.6, 1.1),
                          max(2, (length - i) // 2), col, a, wobble, branch * 0.5, depth + 1, fade)

    def ring(self, cx, cy, r, col, a):
        for k in range(int(r * 8)):
            t = k / (r * 8) * math.tau
            self.put(round(cx + math.cos(t) * r), round(cy + math.sin(t) * r), col, a)

    def facet(self, cx, cy, r, col, a, edge_col=None, edge_a=0.0):
        """A flat-shaded shard: a random triangle or quad, never the same twice."""
        rng = self.rng
        n = rng.choice([3, 4, 4])
        angs = sorted(rng.uniform(0, math.tau) for _ in range(n))
        pts = [(cx + math.cos(t) * r * rng.uniform(0.5, 1.0), cy + math.sin(t) * r * rng.uniform(0.5, 1.0)) for t in angs]

        def inside(px, py):
            sign = 0
            for i in range(n):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % n]
                cr = (x1 - x0) * (py - y0) - (y1 - y0) * (px - x0)
                if cr != 0:
                    if sign == 0:
                        sign = 1 if cr > 0 else -1
                    elif (cr > 0) != (sign > 0):
                        return False
            return True

        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if inside(x, y):
                    self.put(x, y, col, a)
        if edge_col is not None:
            for i in range(n):
                x0, y0 = pts[i]
                x1, y1 = pts[(i + 1) % n]
                self.line(x0, y0, x1, y1, edge_col, edge_a)

    def rgb(self):
        return clamp(self.px)


def lumps(c, n, rmin, rmax, col, a, soft=0.35):
    for _ in range(n):
        c.lump(*c.pos(), c.rng.uniform(rmin, rmax), col, a, soft)


def paint_obsidian(c):
    rng = c.rng
    lumps(c, 4, 4, 9, (10, 5, 16), 0.4)
    lumps(c, 3, 3, 7, (82, 60, 100), 0.2)
    off = rng.uniform(-14, 40)
    width = rng.uniform(3, 6)
    for y in range(SIZE):
        for x in range(SIZE):
            d = abs((x + y) - off - 16)
            if d < width * 2:
                c.put(x, y, (120, 94, 142), 0.18 * (1 - d / (width * 2)))
    for _ in range(rng.randint(3, 4)):
        x, y = c.pos()
        c.facet(x, y, rng.uniform(4, 8), (96, 72, 120), 0.16, (140, 112, 166), 0.14)
    for _ in range(rng.randint(1, 3)):
        x, y = c.pos()
        c.walk(x, y, rng.uniform(0, math.tau), rng.randint(7, 13), (132, 104, 156), 0.5, wobble=0.7, branch=0.12, thick=2)
    for _ in range(rng.randint(1, 3)):
        x, y = c.pos()
        c.lump(x, y, 2.4, (255, 120, 40), 0.22, 0.9)
        c.put(x, y, (255, 176, 80), 0.95)
        c.put(x + 1, y, (230, 100, 30), 0.6)
    for _ in range(rng.randint(2, 4)):
        x, y = c.pos()
        c.line(x, y, x + 3, y - 1, (222, 204, 240), 0.55)


# surface rgb, cavern multiplier, surface alpha, painter, seed, shade range
TILES = {
    "Fervidus/obsidian_glass": ((42, 28, 48), 0.72, 137, paint_obsidian, 14, 0.12),
}


def build_variants(name, base, painter, seed, shade):
    base_noise = periodic_noise(np.random.default_rng(seed), 3, 3)
    out = []
    for v in range(VARIANTS):
        rng = random.Random(seed * 1000 + v)
        np_rng = np.random.default_rng(seed * 1000 + v)
        canvas = Canvas(base, base_noise, rng, shade, np_rng)
        painter(canvas)
        out.append(canvas.rgb())
    return out


def save_strip(path, variants, alpha, mult):
    strip = np.zeros((SIZE, SIZE * len(variants), 4), dtype=np.uint8)
    for i, rgb in enumerate(variants):
        strip[:, i * SIZE:(i + 1) * SIZE, :3] = clamp(rgb * mult).astype(np.uint8)
        strip[:, i * SIZE:(i + 1) * SIZE, 3] = alpha
    Image.fromarray(strip, "RGBA").save(path)


def sheet(path):
    """A 6x6 random tiling of each surface tile as a ragged patch on grey ground, for looking at."""
    rng = random.Random(5)
    S = 3
    names = list(TILES)
    P = SIZE * 6 * S + 10
    out = Image.new("RGBA", (3 * P, 2 * P), (60, 60, 60, 255))
    for i, name in enumerate(names):
        strip = Image.open(ROOT / f"{name}.png").convert("RGBA")
        cells = [strip.crop((k * SIZE, 0, k * SIZE + SIZE, SIZE)) for k in range(strip.width // SIZE)]
        tile = Image.new("RGBA", (SIZE * 6 * S, SIZE * 6 * S), (112, 110, 100, 255))
        for y in range(6):
            for x in range(6):
                if x in (0, 5) and y in (0, 5):
                    continue
                tile.alpha_composite(rng.choice(cells).resize((SIZE * S, SIZE * S), Image.NEAREST), (x * SIZE * S, y * SIZE * S))
        out.alpha_composite(tile, ((i % 3) * P, (i // 3) * P))
    out.convert("RGB").save(path)


def main():
    for name, (base, mult, alpha, painter, seed, shade) in TILES.items():
        variants = build_variants(name, base, painter, seed, shade)
        save_strip(ROOT / f"{name}.png", variants, alpha, 1.0)
        save_strip(ROOT / f"{name}_cavern.png", variants, 255, mult)
        print("wrote", name)
    if len(sys.argv) > 1:
        sheet(sys.argv[1])


if __name__ == "__main__":
    main()
