"""Builds the cavern mouth RSIs: one per world, the crumbling lip of a hole drawn over the hole's own tiles.

A hole in the ground is empty tiles, and the z-level renderer draws the cavern below through them; the lip tiles around
it are solid ground the cavern never shows through. So this art only ever paints the hole's own tiles, and leaves their
middle clear. It is drawn on the dual grid: each piece is centred on a corner of the hole's tiles and covers a quarter
of each of the four tiles around it, painting only the quarters that are hole.

Over those quarters the ground overhangs the opening by a few wandering pixels, in the world's ground texture so it
reads as the lip itself, and ends in a dark broken edge. A hole's corners are rounded off and a corner of ground poking
into the hole gets the lip wrapped round it, so no square corner shows. Inside the opening: a suggestion of shaft wall
under a north rim, a sliver of side wall inside east and west rims and a soft shadow along every rim, all fading out
before the middle of a tile, and the world's own touch hanging off the lip now and then.

States (WFCavernShadeVisualsSystem picks them; the names must match its own):
- `v<mask>_<labels>_<variant>`: `mask` says which of the four tiles are hole (1 NW, 2 NE, 4 SW, 8 SE), 1 to 14; a
  corner with hole all round has no rim and draws nothing. `labels` says, for each piece border the rim crosses (1 top,
  2 right, 4 bottom, 8 left), whether the lip overhangs deep (set) or shallow there. Both pieces on a border see the
  same tile edge, whose label is a stable hash of it, so the rim meets itself there; between borders it wanders freely.
  `variant` is one of `variants(mask)` drawings.
- `pit`: a lone hole tile over black, the placement icon.

Nothing a piece draws near its borders depends on the tiles beyond its own four, so pieces join seamlessly: the rim is
pinned to its label there, every shadow and wall fades out before the middle of a tile, a shaft wall gives way to the
side ledge above a piece's bottom border, and details keep clear of the borders.
Run from the repo root: python Tools/_WF/Caverns/gen_pits.py
"""
import json
import math
import os
import random

from PIL import Image

OUT = "Resources/Textures/_WF/Caverns/Mouths"
TILE = 32
HALF = TILE // 2

LABEL_BITES = (4.0, 7.0)  # how far the lip overhangs the hole where the rim crosses a piece border, shallow or deep
BITE_MIN, BITE_MAX = 3.0, 10.0  # the lip never overhangs less or more than this
WANDER = 3.0  # how far the rim wanders in and out between borders
WANDER_CELL = 8.0  # the wander's lattice spacing, so a bulge or bite is about this wide
PIN = 4.0  # within this of a border the rim sits exactly on its label
FADE = 5.0  # over this much farther the wander fades in
ROUND = 10.0  # a hole corner is rounded to this radius inside the overhang
CORNER_BITE = 5.0  # the overhang at a hole corner
FILLET = 1.5  # a ground corner poking into the hole is wrapped a little wider than the lip's own overhang
FILLET_BITE = 9.0  # the overhang round such a corner, a bulge that smooths the step
CORNER_PULL = (4.0, 11.0)  # within the first distance of a corner its own bite holds; past the second, the edges'
CRUMBLE = 0.9  # pixel jitter on the rim line
EDGE = 1.3  # the broken edge of the lip
FACE = 8  # how far the shaft wall shows under a north rim
LEDGE = 2.0  # a sliver of side wall inside the east and west rims
SEAM = 4.0  # over this much above a piece's bottom border the shaft wall gives way to the side ledge, as below it
SHADOW = {"north": 9.0, "side": 6.0, "south": 3.5}  # how far each rim's shadow reaches into the opening
SHADOW_ALPHA = 0.6
REACH = (12.0, 16.0)  # everything in the opening fades out between these distances from the lip tiles
MARGIN = 2  # details keep this far from a piece's borders

TG = ("CC-BY-SA-3.0", "tgstation")

# The ground a mouth cuts, which the lip continues, and its licence; then the look: the shaft wall's top and foot
# colours, rubble, the lip's lit edge (colour and strength) and the world's own touch.
WORLDS = {
    "asclepiu": {
        "ground": "Resources/Textures/_CE/Tiles/Grass/grass.png",
        "license": ("CC-BY-SA-3.0", "TheShuEd, CrystallPunk-14 #290"),
        "wall": (112, 84, 54), "deep": (40, 34, 28), "rubble": (98, 88, 72),
        "edge": ((255, 255, 230), 0.12), "touch": "roots",
    },
    "fervidus": {
        "ground": "Resources/Textures/Tiles/Planet/basalt.png",
        "license": TG,
        "wall": (104, 80, 68), "deep": (40, 22, 18), "rubble": (70, 60, 58),
        "edge": ((255, 200, 160), 0.14), "touch": "embers",
    },
    "merak": {
        "ground": "Resources/Textures/Tiles/Asteroid/asteroid.png",
        "license": TG,
        "wall": (182, 132, 82), "deep": (70, 46, 30), "rubble": (140, 104, 70),
        "edge": ((255, 240, 200), 0.18), "touch": "sand",
    },
    "aerumna": {
        "ground": "Resources/Textures/Tiles/chromite.png",
        "license": ("CC-BY-NC-SA-3.0", "Mojave-Sun, recoloured by TheShuEd"),
        "wall": (112, 98, 156), "deep": (16, 12, 26), "rubble": (74, 66, 100),
        "edge": ((214, 190, 255), 0.42), "touch": "glints",
    },
    "thrascias": {
        "ground": "Resources/Textures/Tiles/Planet/Snow/snow.png",
        "license": TG,
        "wall": (184, 214, 236), "deep": (40, 62, 96), "rubble": (150, 176, 200),
        "edge": ((255, 255, 255), 0.0), "touch": "ice",
    },
    "carcinoma": {
        "ground": "Resources/Textures/Tiles/meat.png",
        "license": ("CC0-1.0", "EmoGarbage404, space-station-14 #13766"),
        "wall": (156, 48, 64), "deep": (42, 8, 16), "rubble": (110, 40, 52),
        "edge": ((255, 170, 180), 0.16), "touch": "sinew",
    },
}

# The four tiles around a piece's corner: which way each lies (x, y up) and its mask bit.
QUADRANTS = {"nw": (-1, 1, 1), "ne": (1, 1, 2), "sw": (-1, -1, 4), "se": (1, -1, 8)}

# A piece's borders: the two tiles either side of it and its label bit.
BORDERS = {
    "top": (("nw", "ne"), 1),
    "right": (("ne", "se"), 2),
    "bottom": (("sw", "se"), 4),
    "left": (("nw", "sw"), 8),
}

# The masks a piece can have: a corner with hole all round draws nothing.
MASKS = range(1, 15)


def variants(mask):
    """How many drawings a mask has: a diagonal pair one, the rest two."""
    return 1 if mask in (6, 9) else 2


def pit(mask, quadrant):
    return bool(mask & QUADRANTS[quadrant][2])


def crossings(mask):
    """The borders the rim crosses: those whose two tiles differ."""
    return [name for name, ((a, b), _) in BORDERS.items() if pit(mask, a) != pit(mask, b)]


def lerp(a, b, t):
    return a + (b - a) * max(0.0, min(1.0, t))


def mix(c1, c2, t):
    return tuple(lerp(a, b, t) for a, b in zip(c1, c2))


def scale(colour, factor):
    return tuple(max(0, min(255, round(c * factor))) for c in colour[:3])


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def local(x, y):
    """A pixel centre relative to the piece's corner, y up."""
    return x + 0.5 - HALF, HALF - (y + 0.5)


def rounded_quadrant(px, py, sx, sy, cx, cy, radius):
    """Signed distance to the quadrant sx*x >= cx, sy*y >= cy, its corner rounded to the radius."""
    qx = cx + radius - sx * px
    qy = cy + radius - sy * py
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - radius


def noise(x, y, salt):
    """A stable value in [-1, 1] for a pixel and a salt."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFF) / 32767.5 - 1.0


class Piece:
    """One drawing: a mask, the labels of the borders the rim crosses and a variant, with its rim field.

    Each rim edge runs from the piece's corner to a border and has its own overhang along it: its label near the
    border, a wander in between, and near the corner the corner's own overhang, so a hole corner rounds widely and a
    ground corner poking in gets the lip wrapped round it.
    """

    def __init__(self, world, mask, labels, variant):
        self.mask = mask
        self.rng = random.Random(f"{world}:{mask}:{labels}:{variant}")
        self.labels = {name: LABEL_BITES[1 if labels & BORDERS[name][1] else 0] for name in crossings(mask)}
        self.pits = [q for q in QUADRANTS if pit(mask, q)]
        self.grounds = [q for q in QUADRANTS if not pit(mask, q)]
        if len(self.pits) == 1 or mask in (6, 9):
            self.corner = CORNER_BITE
        elif len(self.pits) == 3:
            self.corner = FILLET_BITE
        else:
            self.corner = None
        # Smooth wander along each edge and across the piece: random values on a lattice, blended.
        self.lines = {name: [self.rng.uniform(-1, 1) for _ in range(8)] for name in list(BORDERS) + ["across"]}
        self.lattice = {(i, j): self.rng.uniform(-1, 1) for i in range(8) for j in range(8)}

    def line(self, name, s):
        """The wander along one edge at a signed distance s from the corner."""
        g = (s + HALF) / WANDER_CELL + 1
        i = int(g)
        values = self.lines[name]
        return lerp(values[i], values[i + 1], smoothstep(g - i))

    def wander(self, px, py):
        gx, gy = (px + HALF) / WANDER_CELL + 1, (py + HALF) / WANDER_CELL + 1
        i, j = int(gx), int(gy)
        fx, fy = smoothstep(gx - i), smoothstep(gy - j)
        v = self.lattice
        return lerp(lerp(v[(i, j)], v[(i + 1, j)], fx), lerp(v[(i, j + 1)], v[(i + 1, j + 1)], fx), fy)

    def edge(self, name, t):
        """An edge's overhang at a distance t from the corner along it: its label within PIN of the border."""
        label = self.labels[name]
        fade = smoothstep((HALF - PIN - t) / FADE)
        bite = label + WANDER * self.line(name, t) * fade
        if self.corner is not None:
            near, far = CORNER_PULL
            bite = lerp(bite, self.corner, smoothstep((far - t) / (far - near)))
        return max(BITE_MIN, min(BITE_MAX, bite))

    def straight(self, s, low, high):
        """The overhang along a straight rim at signed distance s from the corner, between its two borders' labels."""
        fade = smoothstep((HALF - PIN - abs(s)) / FADE)
        bite = lerp(self.labels[low], self.labels[high], smoothstep((s + HALF - PIN) / (2 * (HALF - PIN))))
        return max(BITE_MIN, min(BITE_MAX, bite + WANDER * self.line("across", s) * fade))

    @staticmethod
    def quadrant(q):
        """A quadrant's directions and the borders its vertical and horizontal rims cross."""
        sx, sy, _ = QUADRANTS[q]
        return sx, sy, ("top" if sy > 0 else "bottom"), ("right" if sx > 0 else "left")

    def field(self, px, py):
        """Signed distance from the rim: positive on solid ground (the lip tiles and the overhang), negative in the opening."""
        n = len(self.pits)
        if n == 1 or self.mask in (6, 9):
            best = 99.0
            for q in self.pits:
                sx, sy, vertical, horizontal = self.quadrant(q)
                bv = self.edge(vertical, max(0.0, sy * py))
                bh = self.edge(horizontal, max(0.0, sx * px))
                best = min(best, rounded_quadrant(px, py, sx, sy, bv, bh, ROUND))
            return best
        if n == 3:
            sx, sy, vertical, horizontal = self.quadrant(self.grounds[0])
            bv = self.edge(vertical, max(0.0, sy * py))
            bh = self.edge(horizontal, max(0.0, sx * px))
            return -rounded_quadrant(px, py, sx, sy, -bv, -bh, (bv + bh) / 2 + FILLET)
        (ax, ay, _), (bx, by, _) = QUADRANTS[self.pits[0]], QUADRANTS[self.pits[1]]
        if ay == by:
            return self.straight(px, "left", "right") - ay * py
        return self.straight(py, "bottom", "top") - ax * px

    def inside_tiles(self, px, py):
        """Whether a point lies on one of the piece's hole tiles, the only ones it may paint."""
        return any((px > 0) == (QUADRANTS[q][0] > 0) and (py > 0) == (QUADRANTS[q][1] > 0) for q in self.pits)

    def reach(self, px, py):
        """How far a point lies from the nearest lip tile."""
        best = 99.0
        for q in self.grounds:
            sx, sy, _ = QUADRANTS[q]
            best = min(best, math.hypot(max(0.0, -sx * px), max(0.0, -sy * py)))
        return best


def border_distance(px, py):
    return min(HALF - abs(px), HALF - abs(py))


def draw_piece(world, look, ground, mask, labels, variant):
    piece = Piece(world, mask, labels, variant)
    rng = piece.rng
    salt = rng.randrange(1 << 20)
    smooth, field, reach, inside = {}, {}, {}, {}
    for y in range(TILE):
        for x in range(TILE):
            px, py = local(x, y)
            f = piece.field(px, py)
            smooth[(x, y)] = f
            if abs(f) < 2:
                # The crumble dies down at the borders, where the next piece's rim has to meet this one's.
                f += CRUMBLE * noise(x, y, salt) * smoothstep((border_distance(px, py) - 1.0) / 3.0)
            field[(x, y)] = f
            reach[(x, y)] = piece.reach(px, py)
            inside[(x, y)] = piece.inside_tiles(px, py)

    image = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    kinds = {}
    under = {}

    for y in range(TILE):
        for x in range(TILE):
            if not inside[(x, y)]:
                kinds[(x, y)] = "lip"
                continue

            f = field[(x, y)]
            n = noise(x, y, salt + 1)
            if f > 0:
                image.putpixel((x, y), overhang_colour(look, ground, x, y, f, n) + (255,))
                kinds[(x, y)] = "edge" if f <= EDGE else "overhang"
                continue

            px, py = local(x, y)
            depth = -smooth[(x, y)]
            side = facing(smooth, x, y)
            u = wall_depth(smooth, x, y, FACE + 2)
            under[(x, y)] = u
            shadow = SHADOW_ALPHA * (1.0 - smoothstep(depth / SHADOW[side]))
            colour, alpha = (0, 0, 0), shadow
            kinds[(x, y)] = "shadow" if shadow > 0.02 else "clear"

            if u is not None and u < FACE:
                wall = wall_colour(look, x, u, n, piece)
                # Opaque at the top of the cut, fading into the dark as it goes down.
                wall_alpha = 1.0 - smoothstep((u - FACE * 0.35) / (FACE * 0.65))
                # The piece below can't see this rim, so the wall is gone by the bottom border.
                wall_alpha *= smoothstep((py + HALF - 0.5) / SEAM)
                colour, alpha = over(wall, wall_alpha, colour, alpha)
                kinds[(x, y)] = "wall"
            if (u is None or py + HALF < SEAM) and side == "side" and depth < LEDGE:
                ledge = scale(mix(look["wall"], look["deep"], 0.55), lerp(0.8, 0.5, depth / LEDGE) * (1.0 + 0.1 * n))
                colour, alpha = over(ledge, 0.85, colour, alpha)
                kinds[(x, y)] = "wall"

            alpha *= 1.0 - smoothstep((reach[(x, y)] - REACH[0]) / (REACH[1] - REACH[0]))
            if alpha > 0.004:
                image.putpixel((x, y), tuple(int(c) for c in colour) + (round(255 * alpha),))

    details(look, piece, rng, image, kinds, under, smooth, reach)
    return image


def over(top, top_alpha, bottom, bottom_alpha):
    """A colour with alpha laid over another."""
    alpha = top_alpha + bottom_alpha * (1.0 - top_alpha)
    if alpha <= 0:
        return (0, 0, 0), 0.0
    colour = tuple((t * top_alpha + b * bottom_alpha * (1.0 - top_alpha)) / alpha for t, b in zip(top, bottom))
    return colour, alpha


def overhang_colour(look, ground, x, y, f, n):
    """The lip over the hole: the ground itself, darkening towards a broken edge, with a lit rim just behind it."""
    texture = ground.getpixel(((x + HALF) % TILE, (y + HALF) % TILE))[:3]
    if f <= EDGE:
        return scale(mix(texture, look["deep"], 0.35), 0.5 * (1.0 + 0.08 * n))
    colour, strength = look["edge"]
    if f <= EDGE + 1.0 and strength > 0:
        return tuple(round(c) for c in mix(texture, colour, strength))
    return scale(texture, lerp(0.8, 1.0, (f - EDGE - 1.0) / 3.0))


def facing(smooth, x, y):
    """Which way the rim nearest a pixel faces: "side" for an east or west rim, "north" or "south" otherwise."""
    def at(ix, iy):
        return smooth[(min(TILE - 1, max(0, ix)), min(TILE - 1, max(0, iy)))]
    gx = at(x + 1, y) - at(x - 1, y)
    gy = at(x, y + 1) - at(x, y - 1)
    if abs(gx) > abs(gy):
        return "side"
    return "south" if gy > 0 else "north"


def wall_depth(smooth, x, y, reach):
    """How far below the rim straight above a pixel in the opening lies, or None when no rim is that close above it."""
    for k in range(1, reach + 1):
        if y - k < 0:
            return None
        if smooth[(x, y - k)] > 0:
            return k - 1
    return None


def wall_colour(look, x, u, n, piece):
    """The shaft wall under a rim: strata that wobble along the tile, lit at the top and dark lower down."""
    wx = (x + HALF) % TILE
    depth = u / (FACE - 1)
    wobble = 0.6 * math.sin(2 * math.pi * wx / TILE + 1.3)
    # Away from the borders each drawing bends the strata its own way.
    middle = smoothstep(min(x, TILE - 1 - x) / 8.0)
    wobble += 1.6 * middle * piece.wander(x - HALF, 3.0)
    band = int((u + 0.5 + wobble) // 2.2)
    base = mix(look["wall"], look["deep"], depth * 0.85)
    light = lerp(1.1, 0.4, depth) * (1.0 + (0.14 if band % 2 else -0.1)) * (1.0 + 0.08 * n)
    if u == 0:
        light *= 1.15  # the lit top of the cut
    return scale(base, light)


# Surfaces a detail may be painted on: never a lip tile, and in the opening only near its rim.
SOLID = {"overhang", "edge"}
OPENING = {"wall", "shadow", "clear"}


def put(image, kinds, reach, x, y, colour, allowed, alpha=255):
    """Paints a detail pixel on an allowed surface, clear of the piece's borders and the middle of the hole."""
    if not (MARGIN <= x < TILE - MARGIN and MARGIN <= y < TILE - MARGIN):
        return False
    if kinds.get((x, y)) not in allowed or kinds[(x, y)] in OPENING and reach[(x, y)] > REACH[0]:
        return False
    image.putpixel((x, y), tuple(int(c) for c in colour[:3]) + (alpha,))
    return True


def pebble(image, kinds, reach, x, y, colour):
    """A pebble on the lip: a lit pixel, a mid one beside it and its shadow below."""
    if put(image, kinds, reach, x, y, scale(colour, 1.25), SOLID):
        put(image, kinds, reach, x + 1, y, scale(colour, 0.95), SOLID)
        put(image, kinds, reach, x, y + 1, scale(colour, 0.45), SOLID)


def inner(spots):
    return [(x, y) for x, y in spots if MARGIN + 1 <= x < TILE - MARGIN - 1 and MARGIN + 1 <= y < TILE - MARGIN - 1]


def details(look, piece, rng, image, kinds, under, smooth, reach):
    """Rubble, crumbs, wall cracks and the world's own touch, each only now and then so edges don't repeat."""
    spots = inner(kinds.keys())
    lip = [s for s in spots if kinds[s] == "overhang" and smooth[s] < 4]
    edge = [s for s in spots if kinds[s] == "edge"]
    rim = [s for s in spots if kinds[s] in ("shadow", "wall") and -smooth[s] < 2.5]
    wall = [s for s in spots if kinds[s] == "wall"]
    columns = sorted({x for x, y in wall if under.get((x, y)) == 0})
    rubble = look["rubble"]

    if lip and rng.random() < 0.4:
        pebble(image, kinds, reach, *rng.choice(lip), rubble)
    if rim and rng.random() < 0.35:
        # A clod about to break off the lip.
        x, y = rng.choice(rim)
        put(image, kinds, reach, x, y, scale(rubble, 0.8), OPENING)
        put(image, kinds, reach, x, y + 1, scale(rubble, 0.4), OPENING)
    if columns and rng.random() < 0.3:
        crack(image, kinds, reach, under, rng, rng.choice(columns), scale(look["deep"], 0.6),
              (255, 150, 60) if look["touch"] == "embers" else None)

    touch = look["touch"]
    if touch == "roots":
        if columns and rng.random() < 0.4:
            root(image, kinds, reach, under, rng, rng.choice(columns))
    elif touch == "embers":
        if wall and rng.random() < 0.3:
            x, y = rng.choice(wall)
            put(image, kinds, reach, x, y, (255, 120, 40), {"wall"})
            put(image, kinds, reach, x + 1, y, (150, 50, 20), {"wall"})
    elif touch == "sand":
        if columns and rng.random() < 0.35:
            spill(image, kinds, reach, under, rng, rng.choice(columns), look["wall"])
        if lip and rng.random() < 0.3:
            for _ in range(rng.randint(1, 2)):
                put(image, kinds, reach, *rng.choice(lip), scale(look["wall"], 0.7 + 0.2 * rng.random()), SOLID)
    elif touch == "glints":
        if rng.random() < 0.4:
            pool = wall + edge
            if pool:
                x, y = rng.choice(pool)
                put(image, kinds, reach, x, y, (230, 206, 255), {"wall", "edge"})
                put(image, kinds, reach, x + 1, y, (140, 110, 200), {"wall", "edge"})
    elif touch == "ice":
        if columns and rng.random() < 0.4:
            for _ in range(rng.randint(1, 2)):
                icicle(image, kinds, reach, under, rng, rng.choice(columns))
        if wall and rng.random() < 0.3:
            put(image, kinds, reach, *rng.choice(wall), (250, 252, 255), {"wall"})
    elif touch == "sinew":
        if columns and rng.random() < 0.35:
            dangle(image, kinds, reach, under, rng, rng.choice(columns), (196, 104, 110), rng.randint(4, FACE + 2),
                   wiggle=rng.random() < 0.6)
        if (wall or rim) and rng.random() < 0.3:
            put(image, kinds, reach, *rng.choice(wall + rim), (255, 196, 206), OPENING)


def column_top(kinds, under, x):
    """The first wall pixel of a column, at the top of the wall."""
    for y in range(TILE):
        if kinds.get((x, y)) == "wall" and under.get((x, y)) == 0:
            return y
    return None


def crack(image, kinds, reach, under, rng, x, colour, glow):
    """A dark crack zigzagging down the wall from its top; glowing at its core on Fervidus."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    for _ in range(rng.randint(3, FACE - 2)):
        y += 1
        x += rng.choice((-1, 0, 0, 1))
        if not put(image, kinds, reach, x, y, glow or colour, {"wall"}):
            return
        if glow:
            put(image, kinds, reach, x + 1, y, (150, 40, 20), {"wall"})


def dangle(image, kinds, reach, under, rng, x, colour, length, wiggle):
    """A strand hanging off the lip into the opening."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    for i in range(length):
        if not put(image, kinds, reach, x, y + i, scale(colour, 1.0 + 0.1 * (i % 2)), OPENING):
            return
        if wiggle and rng.random() < 0.35:
            x += rng.choice((-1, 1))


def root(image, kinds, reach, under, rng, x):
    """A root hanging off the lip: thick where it leaves the ground, wandering and thinning as it hangs."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    length = rng.randint(4, FACE + 3)
    for i in range(length):
        if not put(image, kinds, reach, x, y + i, (60, 40, 24), OPENING):
            return
        if i < length // 3:
            put(image, kinds, reach, x + 1, y + i, (104, 74, 44), OPENING)
        if rng.random() < 0.4:
            step = rng.choice((-1, 1))
            put(image, kinds, reach, x + step, y + i, (60, 40, 24), OPENING)
            x += step


def spill(image, kinds, reach, under, rng, x, sand):
    """Sand pouring off the lip, fanning out and thinning as it falls away into the dark."""
    top = column_top(kinds, under, x)
    if top is None:
        return
    spread = rng.randint(3, 5)
    length = rng.randint(FACE - 1, FACE + 4)
    for i in range(length):
        half = i // spread
        alpha = round(255 * lerp(1.0, 0.35, i / length))
        for dx in range(-half, half + 1):
            put(image, kinds, reach, x + dx, top + i, scale(sand, 0.85 + 0.25 * rng.random()), OPENING, alpha)


def icicle(image, kinds, reach, under, rng, x):
    """An icicle hanging off the lip, two pixels wide at the root and tapering."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    length = rng.randint(2, 6)
    for i in range(length):
        put(image, kinds, reach, x, y + i, (236, 248, 255), OPENING)
        if i < length // 2:
            put(image, kinds, reach, x + 1, y + i, (170, 214, 238), OPENING)


def draw_icon(world, look, ground):
    """A lone hole tile over black: each quarter from the corner piece whose only hole tile it is."""
    icon = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 255))
    # Quarter of the tile (left, top) and the corner piece that holds it, whose own quarter sits opposite.
    quarters = {(HALF, 0): 4, (0, 0): 8, (HALF, HALF): 1, (0, HALF): 2}
    for (left, top), mask in quarters.items():
        piece = draw_piece(world, look, ground, mask, 0, 0)
        region = piece.crop((HALF - left, HALF - top, TILE - left, TILE - top))
        icon.alpha_composite(region, (left, top))
    return icon


def build(world, look):
    source = look["ground"]
    license_id, author = look["license"]
    ground = Image.open(source).convert("RGBA").crop((0, 0, TILE, TILE))
    rsi = f"{OUT}/{world}_pit.rsi"
    os.makedirs(rsi, exist_ok=True)
    for name in os.listdir(rsi):
        if name.endswith(".png"):
            os.remove(f"{rsi}/{name}")

    states = []
    draw_icon(world, look, ground).save(f"{rsi}/pit.png")
    states.append({"name": "pit"})

    for mask in MASKS:
        crossed = crossings(mask)
        bits = [BORDERS[name][1] for name in crossed]
        for combo in range(1 << len(bits)):
            labels = sum(bit for i, bit in enumerate(bits) if combo & (1 << i))
            for variant in range(variants(mask)):
                name = f"v{mask}_{labels}_{variant}"
                draw_piece(world, look, ground, mask, labels, variant).save(f"{rsi}/{name}.png")
                states.append({"name": name})

    meta = {
        "version": 1,
        "license": license_id,
        "copyright": f"Lip cut from {source.removeprefix('Resources/')} ({author}), with its broken edge, shaft wall, "
                     f"shadows and rubble drawn over it, for Wolfgate cavern mouths by Tools/_WF/Caverns/gen_pits.py.",
        "size": {"x": TILE, "y": TILE},
        "states": states,
    }
    with open(f"{rsi}/meta.json", "w", newline="\n", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")
    return len(states)


if __name__ == "__main__":
    for world, look in WORLDS.items():
        count = build(world, look)
        print(f"{world}: {count} states, {look['license'][0]}")
