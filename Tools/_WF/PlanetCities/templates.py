"""Writes the blank city plot and sewer templates mappers start from; --check fails when they are stale.

    python Tools/_WF/PlanetCities/templates.py            write the templates
    python Tools/_WF/PlanetCities/templates.py --check    fail when a written template differs
    python Tools/_WF/PlanetCities/templates.py --preview DIR   also draw each template as a PNG into DIR

Plots face south (y = 0 is the street edge) and come as one file per floor: ground, upper1, upper2 and basement. Sewer
pieces are 8x8 cells with openings marked on their edges. Content.Server/_WF/PlanetCities/README.md has the rules.
"""
from __future__ import annotations

import argparse
import sys
from collections import deque
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WF" / "PlanetPois"))
from map_author import PoiMap  # noqa: E402

OUT = ROOT / "Resources" / "Maps" / "_WF" / "PlanetCities" / "Templates"
PLOT_SIZES = [(8, 8), (8, 12), (12, 12), (12, 16), (16, 16), (16, 24)]
FLOORS = {"ground": "FloorConcreteSmooth", "upper1": "FloorWood", "upper2": "FloorWood", "basement": "FloorConcrete"}
WALL = "WallBrick"
SEWER_WALL = "WallConcrete"
SEWER = 8


def blank(slug: str, width: int, height: int, floor: str) -> PoiMap:
    return PoiMap(slug, width, height, "", "", ground=floor)


def stairs(width: int, height: int) -> dict[str, tuple[int, int]]:
    """Fixed stair tiles: ground to upper1, upper1 to upper2 beside it, basement to ground in the far corner."""
    return {"up1": (1, height - 2), "up2": (2, height - 2), "down": (width - 2, height - 2)}


def plot(width: int, height: int, floor: str) -> PoiMap:
    m = blank(f"plot_{width}x{height}_{floor}", width, height, FLOORS[floor])
    m.roof(0, 0, width, height)
    door = (width // 2, 0)
    windows = {(x, 0) for x in range(2, width - 2, 3) if abs(x - door[0]) > 1}
    for y in range(height):
        for x in range(width):
            if x not in (0, width - 1) and y not in (0, height - 1):
                continue
            if floor == "ground" and (x, y) == door:
                m.entity("MetalDoor", x, y)
                m.entity("WFCityFrontDoor", x, y)
            elif floor in ("ground", "upper1", "upper2") and (x, y) in windows:
                m.entity("Window", x, y)
            else:
                m.entity(WALL, x, y)
    s = stairs(width, height)
    marks = {"ground": [("WFCityStairs", s["up1"]), ("WFCityStairwell", s["down"])],
             "upper1": [("WFCityStairwell", s["up1"]), ("WFCityStairs", s["up2"])],
             "upper2": [("WFCityStairwell", s["up2"])],
             "basement": [("WFCityStairs", s["down"])]}[floor]
    for proto, (x, y) in marks:
        m.entity(proto, x, y)
    m.entity("AlwaysPoweredLightLED", width // 2, height - 2)
    return m


def sewer(slug: str, openings: set[str], manhole: bool = False) -> PoiMap:
    """An 8x8 cell: walls round a 6-wide tunnel, a two-tile channel down the middle and catwalks where it turns."""
    m = blank(f"sewer_{slug}", SEWER, SEWER, "FloorConcrete")
    m.roof(0, 0, SEWER, SEWER)
    edges = {"North": [(x, SEWER - 1) for x in range(1, 7)], "South": [(x, 0) for x in range(1, 7)],
             "East": [(SEWER - 1, y) for y in range(1, 7)], "West": [(0, y) for y in range(1, 7)]}
    open_tiles = {tile for side in openings for tile in edges[side]}
    for y in range(SEWER):
        for x in range(SEWER):
            if (x in (0, SEWER - 1) or y in (0, SEWER - 1)) and (x, y) not in open_tiles:
                m.entity(SEWER_WALL, x, y)
    channel = {(3, 3), (4, 3), (3, 4), (4, 4)}
    for side in openings:
        span = {"North": range(3, SEWER), "South": range(0, 5), "East": range(3, SEWER), "West": range(0, 5)}[side]
        channel |= {(a, b) if side in ("East", "West") else (b, a) for a in span for b in (3, 4)}
    turns = openings not in ({"North", "South"}, {"East", "West"})
    for x, y in sorted(channel):
        # Catwalks bridge the channel where it turns or branches, so the walkways on either side meet.
        if turns and len(openings) > 1 and 3 <= x <= 4 and 3 <= y <= 4:
            m.entity("CMCatwalk", x, y)
        m.entity("CEWaterMoving", x, y)
    for side in sorted(openings):
        x, y = {"North": (3, SEWER - 1), "South": (3, 0), "East": (SEWER - 1, 3), "West": (0, 3)}[side]
        m.entity("WFCitySewerLink", x, y)
    if manhole:
        m.entity("WFCityManhole", 1, 3)
    m.entity("AlwaysPoweredLightLED", 1 if manhole else 2, 5)
    return m


def chamber() -> PoiMap:
    """A 16x16 junction room with all four openings and pillars, for where main lines cross."""
    size = SEWER * 2
    m = blank("sewer_chamber", size, size, "FloorConcrete")
    m.roof(0, 0, size, size)
    mouths = {(x, y) for x in range(5, 11) for y in (0, size - 1)} | {(x, y) for y in range(5, 11) for x in (0, size - 1)}
    for y in range(size):
        for x in range(size):
            if (x in (0, size - 1) or y in (0, size - 1)) and (x, y) not in mouths:
                m.entity(SEWER_WALL, x, y)
    for x, y in ((4, 4), (11, 4), (4, 11), (11, 11)):
        m.entity(SEWER_WALL, x, y)
    channel = {(a, b) for a in range(size) for b in (7, 8)} | {(b, a) for a in range(size) for b in (7, 8)}
    for x, y in sorted(channel):
        if 6 <= x <= 9 and 6 <= y <= 9:
            m.entity("CMCatwalk", x, y)
        m.entity("CEWaterMoving", x, y)
    for x, y in ((7, 0), (7, size - 1), (0, 7), (size - 1, 7)):
        m.entity("WFCitySewerLink", x, y)
    m.entity("WFCityManhole", 2, 2)
    for x, y in ((2, 13), (13, 2), (13, 13)):
        m.entity("AlwaysPoweredLightLED", x, y)
    return m


def reachable(m: PoiMap, start: tuple[int, int], targets: set[tuple[int, int]]) -> bool:
    """Every target can be walked to from start without crossing walls."""
    walls = {(x, y) for proto, x, y, *_ in m.entities if proto in (WALL, SEWER_WALL, "Window")}
    seen, frontier = set(), deque([start])
    while frontier:
        tile = frontier.popleft()
        if tile in seen or tile in walls or tile not in m.tiles:
            continue
        seen.add(tile)
        x, y = tile
        frontier.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return targets <= seen


def build() -> dict[str, PoiMap]:
    maps = {}
    for width, height in PLOT_SIZES:
        for floor in FLOORS:
            m = plot(width, height, floor)
            marks = {(x, y) for proto, x, y, *_ in m.entities if proto in ("WFCityStairs", "WFCityStairwell")}
            if not reachable(m, (width // 2, 1), marks):
                raise SystemExit(f"{m.slug}: a stair tile is walled off")
            maps[f"Plots/{m.slug}.yml"] = m
    pieces = {"straight": {"North", "South"}, "corner": {"South", "East"}, "tee": {"South", "East", "West"},
              "cross": {"North", "South", "East", "West"}, "end": {"South"}}
    for slug, openings in pieces.items():
        m = sewer(slug, openings)
        maps[f"Sewers/{m.slug}.yml"] = m
    m = sewer("manhole", {"North", "South"}, manhole=True)
    maps[f"Sewers/{m.slug}.yml"] = m
    m = chamber()
    maps[f"Sewers/{m.slug}.yml"] = m
    return maps


def preview(m: PoiMap, path: Path) -> None:
    from PIL import Image, ImageDraw
    scale = 24
    colors = {"FloorConcreteSmooth": "#8d918a", "FloorWood": "#9c805e", "FloorConcrete": "#6f7470"}
    marks = {WALL: "#7a3b2e", SEWER_WALL: "#4a4f55", "Window": "#7fb7d9", "MetalDoor": "#c9a43a",
             "CEWaterMoving": "#2f5d6b", "CMCatwalk": "#b0b0a0", "WFCityFrontDoor": "#3ad14a",
             "WFCityStairs": "#e06ad8", "WFCityStairwell": "#e04040", "WFCitySewerLink": "#f0f040",
             "WFCityManhole": "#40e0e0", "AlwaysPoweredLightLED": "#fff7c0"}
    img = Image.new("RGB", (m.width * scale, m.height * scale), "#15202a")
    draw = ImageDraw.Draw(img)
    for (x, y), tile in m.tiles.items():
        top = (m.height - 1 - y) * scale
        draw.rectangle([x * scale, top, x * scale + scale - 1, top + scale - 1], fill=colors.get(tile, "#555"))
    for order in (0, 1, 2):
        for proto, x, y, *_ in m.entities:
            layer = 2 if proto.startswith("WFCity") or proto.startswith("AlwaysPowered") else 1 if proto == "CMCatwalk" else 0
            if layer != order:
                continue
            top = (m.height - 1 - y) * scale
            inset = 7 if layer == 2 else 0 if layer == 0 else 4
            draw.rectangle([x * scale + inset, top + inset, x * scale + scale - 1 - inset, top + scale - 1 - inset],
                           fill=marks.get(proto, "#ff00ff"))
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true", help="fail when a written template is stale")
    parser.add_argument("--preview", type=Path, help="draw each template as a PNG into this folder")
    args = parser.parse_args()
    stale = []
    for relative, m in build().items():
        path = OUT / relative
        content = m.serialize().replace("\n", "\r\n")
        if args.check:
            if not path.exists() or path.read_bytes().decode("utf-8") != content:
                stale.append(relative)
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content.encode("utf-8"))
        if args.preview:
            preview(m, args.preview / relative.replace(".yml", ".png"))
    if stale:
        raise SystemExit("Stale templates: " + ", ".join(stale))
    print("Templates checked." if args.check else f"Wrote {len(build())} templates.")


if __name__ == "__main__":
    main()
