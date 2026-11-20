"""Turn a hand-made grid map into a roadside site that faces a road on its south edge, or an off-road one on a trail.

    python Tools/_WF/PlanetPois/roadside_map.py <saved grid.yml> <name> [--trail] [--below <level under.yml> [--offset x,y]]

Writes Resources/Maps/_WF/PlanetPois/<name>.yml and prints the table entry for it. On the way it:
- swaps indestructible walls for their ordinary versions (sites are fully destructible),
- keeps only the grid components a site uses (no shuttle, gravity, atmosphere or implicit roof),
- roofs the enclosed rooms: every tile the open ground can't reach past walls, windows and doors, and their walls,
- lays a strip of the world's road (5 wide, a sidewalk each side) along the south edge, with an entrance at each end.
The site is a Through site that turns with the road (`rotate: true`).

With --trail there is no road strip: the site stands off the roads, and a footpath is worn from the outside of its first
door to the nearest road (an Isolated site with `trail: true`). Its footprint keeps a tile of open ground round it.

With --below the level under the site, such as a basement saved from the z-level below in mapping mode, is written too,
as <name>_below.yml in the site's frame, and the entry names it (`below`). The offset between the two is worked out
from where their grids stood and where the stairs meet an opening in the site's floor; --offset sets it by hand.
"""
import base64
import math
import re
import struct
import sys
from collections import deque
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[3]
ROAD, SHOULDER, WIDTH = "RMCPlanetHybrisaAsphalt", "RMCPlanetHybrisaSidewalk", 5
KEEP = {"MetaData", "Transform", "MapGrid", "DecalGrid"}
BLOCKS = re.compile(r"^((N14|CE|RMC|WF)?Wall(?!mount)|CMAncientTemple|Window|Grille|Airlock|.*Door|.*Window|.*Barricade|.*Girder)")
SIDES = (((0, -1), "South"), ((0, 1), "North"), ((-1, 0), "West"), ((1, 0), "East"))


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor('!', lambda l, s, n: None)


def read_tiles(data):
    """The save's tilemap, its non-space tiles by position and its raw chunks."""
    tilemap = {int(k): v for k, v in data["tilemap"].items()}
    tiles, chunks = {}, {}
    for group in data["entities"]:
        for ent in group["entities"]:
            for comp in ent.get("components", []):
                if comp.get("type") == "MapGrid":
                    for key, chunk in comp["chunks"].items():
                        cx, cy = map(int, key.split(","))
                        raw = base64.b64decode(chunk["tiles"])
                        chunks[key] = bytearray(raw)
                        for i in range(256):
                            tid = struct.unpack_from("<i", raw, i * 7)[0]
                            if tilemap.get(tid, "Space") != "Space":
                                tiles[cx * 16 + i % 16, cy * 16 + i // 16] = tid
    return tilemap, tiles, chunks


def grid_position(path):
    """Where the save's biggest grid stood in its map, before flatten() moves it."""
    data = yaml.load(path.read_text(encoding="utf-8"), Loader=Loader)
    best, size = (0.0, 0.0), -1
    for ent in data["entities"][0]["entities"]:
        comps = {c["type"]: c for c in ent.get("components", [])}
        if "MapGrid" in comps and len(comps["MapGrid"]["chunks"]) > size:
            size = len(comps["MapGrid"]["chunks"])
            best = tuple(map(float, str(comps["Transform"].get("pos", "0,0")).split(",")))
    return best


def stairs(data):
    """The tiles of the z-level stairs in a save."""
    found = set()
    for group in data["entities"]:
        if "Ladder" not in (group["proto"] or "") and "Stairs" not in (group["proto"] or ""):
            continue
        for ent in group["entities"]:
            for comp in ent.get("components", []):
                if comp.get("type") == "Transform" and "pos" in comp:
                    x, y = map(float, str(comp["pos"]).split(","))
                    found.add((int(x // 1), int(y // 1)))
    return found


def below(source, ground_source, ground_tiles, name, offset):
    """
    Writes the level under the site as <name>_below.yml in the site's own frame, so the game lays it under the site with
    the same origin and turn. Its offset comes from where the two grids stood, settled on the nearby whole-tile offset
    that puts its stairs under an opening in the site's floor and the most of it under the site.
    """
    text = flatten(source.read_text(encoding="utf-8").replace("\r\n", "\n"))
    data = yaml.load(text, Loader=Loader)
    text = re.sub(r"(- proto: \w+?)Indestructible\n", r"\1\n", text)
    tilemap, tiles, _ = read_tiles(data)
    if offset is None:
        (gx, gy), (bx, by) = grid_position(ground_source), grid_position(source)
        guess = (round(bx - gx), round(by - gy))
        steps = stairs(data)

        def opening(x, y):
            # A gap in the floor: no tile there, floor all round it.
            return (x, y) not in ground_tiles and all((x + a, y + b) in ground_tiles for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)))

        def score(o):
            shafts = sum(1 for (x, y) in steps if opening(x + o[0], y + o[1]))
            under = sum(1 for (x, y) in tiles if (x + o[0], y + o[1]) in ground_tiles)
            return shafts * 1000 + under
        offset = max(((guess[0] + dx, guess[1] + dy) for dx in range(-2, 3) for dy in range(-2, 3)), key=score)
    dx, dy = offset

    # Everything on the grid moves by the offset: tiles, entity positions and decals.
    lines, parent = text.split("\n"), None
    for i, line in enumerate(lines):
        if m := re.fullmatch(r"(\s+)parent: (\d+)", line):
            parent = int(m.group(2))
        elif (m := re.fullmatch(r"(\s+)pos: (-?[\d.]+),(-?[\d.]+)", line)) and parent == 1:
            lines[i] = f"{m.group(1)}pos: {float(m.group(2)) + dx:g},{float(m.group(3)) + dy:g}"
        elif m := re.fullmatch(r"(            \d+): (-?[\d.]+),(-?[\d.]+)", line):
            lines[i] = f"{m.group(1)}: {float(m.group(2)) + dx:g},{float(m.group(3)) + dy:g}"
        if line.startswith("    - type: ") and line != "    - type: Transform":
            parent = None
    text = "\n".join(lines)
    moved = {(x + dx, y + dy): tid for (x, y), tid in tiles.items()}
    # Underground, all of it is under cover.
    write(text, tilemap, moved, {}, set(moved), f"{name}_below", None)
    shafts = sorted((x + dx, y + dy) for (x, y) in stairs(data) if (x + dx, y + dy) not in ground_tiles)
    print(f"Wrote Resources/Maps/_WF/PlanetPois/{name}_below.yml: {len(moved)} tiles, offset {dx},{dy}, stairwells at {shafts}.")
    return f"    below: /Maps/_WF/PlanetPois/{name}_below.yml\n"


def main():
    source, name = Path(sys.argv[1]), sys.argv[2]
    trail = "--trail" in sys.argv[3:]
    below_path = Path(sys.argv[sys.argv.index("--below") + 1]) if "--below" in sys.argv else None
    offset = tuple(map(int, sys.argv[sys.argv.index("--offset") + 1].split(","))) if "--offset" in sys.argv else None
    text = flatten(source.read_text(encoding="utf-8").replace("\r\n", "\n"))
    data = yaml.load(text, Loader=Loader)

    text = re.sub(r"(- proto: \w+?)Indestructible\n", r"\1\n", text)

    # Tiles and where blocking entities stand.
    tilemap, tiles, chunks = read_tiles(data)
    below_entry = below(below_path, source, tiles, name, offset) if below_path else ""
    blocked, doors = set(), set()
    for group in data["entities"]:
        if not group["proto"] or not BLOCKS.match(group["proto"]):
            continue
        for ent in group["entities"]:
            for comp in ent.get("components", []):
                if comp.get("type") == "Transform" and comp.get("parent") == 1:
                    x, y = map(float, str(comp["pos"]).split(","))
                    blocked.add((int(x // 1), int(y // 1)))
                    if "Door" in group["proto"] or "Airlock" in group["proto"]:
                        doors.add((int(x // 1), int(y // 1)))

    xs, ys = [p[0] for p in tiles], [p[1] for p in tiles]
    left, right, bottom, top = min(xs), max(xs), min(ys), max(ys)

    # Interior: what the outside can't reach past a wall, window or door.
    outside, frontier = set(), deque((x, y) for x in range(left - 1, right + 2) for y in (bottom - 1, top + 1))
    while frontier:
        p = frontier.popleft()
        if p in outside or p in blocked or not (left - 1 <= p[0] <= right + 1 and bottom - 1 <= p[1] <= top + 1):
            continue
        outside.add(p)
        frontier.extend(((p[0] + 1, p[1]), (p[0] - 1, p[1]), (p[0], p[1] + 1), (p[0], p[1] - 1)))
    roofed = {p for p in tiles if p not in outside}
    roofed |= {p for p in blocked if any((p[0] + dx, p[1] + dy) in roofed for dx in (-1, 0, 1) for dy in (-1, 0, 1))}

    if trail:
        # The trail starts on the open ground beside the first outside door.
        start = next(((door[0] + dx, door[1] + dy), facing) for door in sorted(doors, key=lambda p: (p[1], p[0]))
                     for (dx, dy), facing in SIDES if (door[0] + dx, door[1] + dy) in outside)
        write(text, tilemap, tiles, chunks, roofed, name, None)
        print(f"""Wrote Resources/Maps/_WF/PlanetPois/{name}.yml: {right - left + 1} x {top - bottom + 1}, {len(roofed)} roofed tiles.
  - mapPath: /Maps/_WF/PlanetPois/{name}.yml
    name: wf-poi-{name.replace('_', '-')}
    expeditions: false
    footprint: {left - 1},{bottom - 1},{right + 2},{top + 2}
    minCount: 1
    maxCount: 1
    connection: Isolated
    trail: true
    sockets:
    - offset: {start[0][0]},{start[0][1]}
      direction: {start[1]}
    rotate: true
{below_entry}""", end="")
        return

    # The road along the south edge.
    road_id, shoulder_id = max(tilemap) + 1, max(tilemap) + 2
    depth = WIDTH + 2
    for x in range(left, right + 1):
        for i in range(depth):
            y = bottom - 1 - i
            tiles[x, y] = shoulder_id if i in (0, depth - 1) else road_id
    write(text, tilemap, tiles, chunks, roofed, name, (road_id, shoulder_id))

    lane = bottom - 1 - depth // 2
    print(f"""Wrote Resources/Maps/_WF/PlanetPois/{name}.yml: {right - left + 1} x {top - bottom + 1 + depth}, {len(roofed)} roofed tiles.
  - mapPath: /Maps/_WF/PlanetPois/{name}.yml
    name: wf-poi-{name.replace('_', '-')}
    expeditions: false
    footprint: {left},{bottom - depth},{right + 1},{top + 1}
    minCount: 1
    maxCount: 1
    connection: Through
    sockets:
    - offset: {left},{lane}
      direction: West
    - offset: {right},{lane}
      direction: East
    rotate: true""")


def flatten(text):
    """
    Makes any save a single grid with uid 1: a whole map's save keeps its biggest grid, and stray grids (a light or a
    wall placed off the edge makes one) are folded into it where they stood.
    """
    data = yaml.load(text, Loader=Loader)
    tilemap = {int(k): v for k, v in data["tilemap"].items()}
    grids = {}
    for ent in data["entities"][0]["entities"]:
        comps = {c["type"]: c for c in ent.get("components", [])}
        if "MapGrid" not in comps:
            continue
        x, y = map(float, str(comps["Transform"].get("pos", "0,0")).split(","))
        tiles = {}
        for key, chunk in comps["MapGrid"]["chunks"].items():
            cx, cy = map(int, key.split(","))
            raw = base64.b64decode(chunk["tiles"])
            for i in range(256):
                if tilemap.get(struct.unpack_from("<i", raw, i * 7)[0], "Space") != "Space":
                    tiles[cx * 16 + i % 16, cy * 16 + i // 16] = raw[i * 7:i * 7 + 7]
        grids[ent["uid"]] = ((x, y), tiles)
    main = max(grids, key=lambda uid: len(grids[uid][1]))
    if len(grids) == 1 and main == 1:
        return text
    (mx, my), merged = grids[main]
    offsets = {}
    for uid, ((x, y), tiles) in grids.items():
        offsets[uid] = (x - mx, y - my)
        if uid != main:
            if (x - mx) % 1 or (y - my) % 1:
                sys.exit(f"Grid {uid} isn't on the main grid's tiles; move it into place first.")
            for (tx, ty), tile in tiles.items():
                merged.setdefault((tx + int(x - mx), ty + int(y - my)), tile)
    print(f"Folded {len(grids) - 1} stray grid(s) into grid {main}.", file=sys.stderr)

    # A free uid for whatever else holds 1, then everything on any grid moves onto the main one as uid 1.
    spare = max(int(u) for u in re.findall(r"^  - uid: (\d+)$", text, re.M)) + 1
    lines, out, parent = text.split("\n"), [], None
    for line in lines:
        if re.fullmatch(r"  - uid: 1", line) and 1 not in grids:
            line = f"  - uid: {spare}"
        if m := re.fullmatch(r"(\s+)parent: (\d+)", line):
            parent = int(m.group(2))
            line = f"{m.group(1)}parent: {1 if parent in grids else spare if parent == 1 else parent}"
        elif (m := re.fullmatch(r"(\s+)pos: (-?[\d.]+),(-?[\d.]+)", line)) and parent in offsets and parent != main:
            dx, dy = offsets[parent]
            line = f"{m.group(1)}pos: {float(m.group(2)) + dx:g},{float(m.group(3)) + dy:g}"
        elif m := re.fullmatch(r"(\s+)gridUid: (\d+)", line):
            line = f"{m.group(1)}gridUid: {1 if int(m.group(2)) in grids else m.group(2)}"
        elif 1 not in grids and (m := re.fullmatch(r"(\s+- |\s+ent: )1", line)):
            # Whatever held uid 1 is also named in containers, slots and links; they follow it to the spare uid.
            line = f"{m.group(1)}{spare}"
        elif 1 not in grids and (m := re.fullmatch(r"(\s+)1:", line)):
            line = f"{m.group(1)}{spare}:"
        if line.startswith("    - type: ") and line != "    - type: Transform":
            parent = None
        out.append(line)
    text = "\n".join(out)

    # The main grid alone, with the merged tiles; write() rebuilds the rest of it.
    chunks = {}
    for (x, y), tile in merged.items():
        key = f"{x // 16},{y // 16}"
        space = next(k for k, v in tilemap.items() if v == "Space")
        chunk = chunks.setdefault(key, bytearray(struct.pack("<iBBB", space, 0, 0, 0) * 256))
        i = (y - (y // 16) * 16) * 16 + (x - (x // 16) * 16)
        chunk[i * 7:i * 7 + 7] = tile
    start = text.index("- proto: \"\"\n")
    end = text.index("\n- proto: ", start + 1) + 1
    group = text[start:end]
    own = group[group.index(f"  - uid: {main}\n"):]
    following = re.search(r"\n  - uid: \d+\n", own[1:])
    own = own[:following.start() + 2] if following else own
    decals = re.search(r"    - type: DecalGrid\n(?:      .*\n|        .*\n)*", own)
    body = "".join(f"        {k}:\n          ind: {k}\n          tiles: {base64.b64encode(bytes(v)).decode()}\n          version: 7\n"
                   for k, v in sorted(chunks.items()))
    grid = ("- proto: \"\"\n  entities:\n  - uid: 1\n    components:\n    - type: MetaData\n    - type: Transform\n"
            "      parent: invalid\n    - type: MapGrid\n      chunks:\n" + body + (decals.group(0) if decals else ""))
    text = text[:start] + grid + text[end:]
    text = re.sub(r"category: Map\n", "category: Grid\n", text, count=1)
    text = re.sub(r"^maps:\n(?:- .*\n)*", "maps: []\n", text, count=1, flags=re.M)
    text = re.sub(r"^grids:\n(?:- .*\n)*", "grids:\n- 1\n", text, count=1, flags=re.M)
    text = re.sub(r"^orphans:.*\n(?:- .*\n)*", "orphans:\n- 1\n", text, count=1, flags=re.M)
    count = len(re.findall(r"^  - uid: \d+$", text, re.M))
    return re.sub(r"entityCount: \d+", f"entityCount: {count}", text, count=1)


def write(text, tilemap, tiles, chunks, roofed, name, road):
    """Writes the site: its tiles, a roof over the rooms and its decals, without what a shuttle carries."""
    for (x, y), tid in tiles.items():
        key = f"{x // 16},{y // 16}"
        new = key not in chunks
        chunk = chunks.setdefault(key, bytearray(struct.pack("<iBBB", 0, 0, 0, 0) * 256))
        if new and tilemap.get(0) != "Space":
            # A new chunk's empty tiles have to name Space.
            space = next(k for k, v in tilemap.items() if v == "Space")
            for i in range(256):
                if struct.unpack_from("<i", chunk, i * 7)[0] == 0:
                    struct.pack_into("<i", chunk, i * 7, space)
        i = (y - (y // 16) * 16) * 16 + (x - (x // 16) * 16)
        struct.pack_into("<i", chunk, i * 7, tid)

    masks = {}
    for x, y in roofed:
        key = f"{x // 8},{y // 8}"
        masks[key] = masks.get(key, 0) | (1 << ((y % 8) * 8 + x % 8))

    # Rebuild the grid entity: its chunks, roof and decals, and nothing a shuttle carries.
    grid_start = text.index("- proto: \"\"\n")
    grid_end = text.index("\n- proto: ", grid_start + 1) + 1
    grid = text[grid_start:grid_end]
    decals = re.search(r"    - type: DecalGrid\n(?:      .*\n|        .*\n)*", grid)
    header = "- proto: \"\"\n  entities:\n  - uid: 1\n    components:\n    - type: MetaData\n    - type: Transform\n      parent: invalid\n    - type: MapGrid\n      chunks:\n"
    body = "".join(f"        {k}:\n          ind: {k}\n          tiles: {base64.b64encode(bytes(v)).decode()}\n          version: 7\n"
                   for k, v in sorted(chunks.items()))
    roof = "    - type: Roof\n      data:\n" + "".join(f"        {k}: {v}\n" for k, v in sorted(masks.items()))
    decal_text = keep_decals(decals.group(0), tiles) if decals else ""
    text = text[:grid_start] + header + body + roof + decal_text + text[grid_end:]

    if road:
        text = re.sub(r"tilemap:\n((?:  .*\n)*)", lambda m: "tilemap:\n" + m.group(1) + f"  {road[0]}: {ROAD}\n  {road[1]}: {SHOULDER}\n", text, count=1)
    out = ROOT / "Resources/Maps/_WF/PlanetPois" / f"{name}.yml"
    out.write_text(text, encoding="utf-8", newline="\n")


def keep_decals(block, tiles):
    """Drops decals that hang off the site's own tiles: a turned site can't place them on ground that isn't there yet."""
    head, *nodes = re.split(r"(?=^        - node:\n)", block, flags=re.M)
    kept = []
    for node in nodes:
        lines = node.split("\n")
        decals = [line for line in lines if (m := re.fullmatch(r"            \d+: (-?[\d.]+),(-?[\d.]+)", line))
                  and all((tx, ty) in tiles for tx in {math.floor(float(m.group(1))), math.floor(float(m.group(1)) + 0.999)}
                          for ty in {math.floor(float(m.group(2))), math.floor(float(m.group(2)) + 0.999)})]
        if decals:
            kept.append("\n".join(line for line in lines if not re.fullmatch(r"            \d+: .*", line) or line in decals))
    return head + "".join(kept) if kept else head.replace("        nodes:\n", "        nodes: []\n")


if __name__ == "__main__":
    main()
