"""Deterministic, centered SS14 grid authoring for planetary settlements."""

import base64
import struct
from collections import defaultdict

import yaml


class PoiMap:
    """One authored site, with explicit roof coverage and outward road sockets."""

    def __init__(self, slug, width, height, planet, title_key, connection="Through", inhabited=True, ground="FloorGrass", expeditions=None):
        self.slug, self.width, self.height = slug, width, height
        self.planet, self.title_key = planet, title_key
        self.connection, self.inhabited = connection, inhabited
        # An inhabited settlement is never an expedition target; anything else is unless it says otherwise.
        self.expeditions = not inhabited if expeditions is None else expeditions
        self.tiles = {(x, y): ground for y in range(height) for x in range(width)}
        self.entities, self.sockets, self.decals = [], [], []
        self.roofs = set()
        self.notes, self.landmarks = [], []

    def rect(self, x, y, w, h, tile):
        for ty in range(max(y, 0), min(y + h, self.height)):
            for tx in range(max(x, 0), min(x + w, self.width)):
                self.tiles[tx, ty] = tile

    def entity(self, proto, x, y, rotation=0, anchored=True, components=None):
        if (x, y) not in self.tiles:
            raise ValueError(f"{self.slug}: {proto} outside footprint at {x},{y}")
        self.entities.append((proto, x, y, rotation, anchored, components or []))

    def roof(self, x, y, w, h, covered=True):
        for ty in range(y, y + h):
            for tx in range(x, x + w):
                if (tx, ty) not in self.tiles:
                    raise ValueError(f"{self.slug}: roof outside footprint")
                if covered:
                    self.roofs.add((tx, ty))
                else:
                    self.roofs.discard((tx, ty))

    def road(self, x1, y1, x2, y2, width=5, surface="RMCPlanetHybrisaAsphalt", shoulder="RMCPlanetHybrisaSidewalk", shoulder_width=1, markings=True):
        if x1 != x2 and y1 != y2:
            raise ValueError("Road centerlines must be cardinal")
        half = width // 2
        horizontal = y1 == y2
        for radius, tile in ((half + shoulder_width, shoulder), (half, surface)):
            if horizontal:
                self.rect(min(x1, x2), y1 - radius, abs(x2 - x1) + 1, radius * 2 + 1, tile)
            else:
                self.rect(x1 - radius, min(y1, y2), radius * 2 + 1, abs(y2 - y1) + 1, tile)
        if markings:
            length = abs(x2 - x1) if horizontal else abs(y2 - y1)
            for offset in range(12, length - 5, 48):
                self.decals.append(("RoadSlow", min(x1, x2) + offset if horizontal else x1,
                                    y1 if horizontal else min(y1, y2) + offset, 270 if horizontal else 0))

    def socket(self, x, y, direction):
        dx, dy = {"North": (0, 1), "East": (1, 0), "South": (0, -1), "West": (-1, 0)}[direction]
        if (x, y) not in self.tiles or (x + dx, y + dy) in self.tiles:
            raise ValueError(f"{self.slug}: socket must point out of the footprint")
        self.sockets.append((x, y, direction))

    def building(self, x, y, w, h, kind="home", ruined=False, wall="WallConcrete", floor="FloorWood", door_side="south", windows=True):
        """Build furnished rooms around an unobstructed three-tile entrance corridor."""
        self.rect(x, y, w, h, floor)
        self.roof(x, y, w, h)
        center = x + w // 2
        door = (center, y if door_side == "south" else y + h - 1)
        occupied = {(e[1], e[2]) for e in self.entities if e[4]}
        manual = "WoodDoor" if wall == "WallWood" else "MetalDoor"
        # Caller-provided traders, water stock and loose supplies use these bays.
        reserved = {(x + dx, y + dy) for dx, dy in (
            (5, 10), (16, 12), (16, 15), (2, 17),
            (3, 4), (3, 5), (3, 6), (8, 4), (8, 5), (8, 6),
            (13, 4), (13, 6), (10, 4), (13, 8), (15, 5),
            (10, 7), (10, 11), (15, 9), (18, 5), (28, 5),
            (22, 9), (27, 5), (30, 11), (12, 7), (4, 9),
        )}

        def place(proto, px, py, anchored=True, item=False):
            if not (x < px < x + w - 1 and y < py < y + h - 1):
                return
            if (px, py) in reserved or (anchored and (px, py) in occupied):
                return
            if item or not (center - 1 <= px <= center + 1):
                self.entity(proto, px, py, anchored=anchored)
                if anchored:
                    occupied.add((px, py))

        for ty in range(y, y + h):
            for tx in range(x, x + w):
                if tx not in (x, x + w - 1) and ty not in (y, y + h - 1):
                    continue
                if (tx, ty) == door:
                    self.entity(manual, tx, ty)
                elif ruined and ((ty == y + h - 1 and x + 4 <= tx <= x + 6) or
                                 (tx == x + w - 1 and y + h // 2 <= ty <= y + h // 2 + 2)):
                    self.roofs.discard((tx, ty))
                else:
                    window = windows and tx in (x + 4, x + w - 5) and ty in (y, y + h - 1)
                    self.entity("Window" if window else wall, tx, ty)
                occupied.add((tx, ty))
        if ruined:
            self.roof(x + w // 2 + 1, y + h // 2, max(1, w // 4), max(1, h // 3), covered=False)
            self.roof(x + 2, y + 2, max(1, w // 5), max(1, h // 5), covered=False)
        else:
            self.entity("AlwaysPoweredLightLED", center, y + h - 3)
            occupied.add((center, y + h - 3))

        roomed = kind in ("home", "clinic", "office") and w >= 18 and h >= 13
        if roomed:
            # A central public hall reaches doors into bedrooms, wards or offices.
            for px in (center - 2, center + 2):
                for py in range(y + 1, y + h - 1):
                    if py in (y + 4, y + h - 5):
                        place(manual, px, py)
                    elif not ruined or (px + py) % 5:
                        place(wall, px, py)
            divider = y + h // 2
            for side in ((x + 1, center - 1), (center + 2, x + w - 1)):
                opening = (side[0] + side[1]) // 2
                for px in range(*side):
                    if px == opening:
                        place(manual, px, divider)
                    elif not ruined or px % 4:
                        place(wall, px, divider)
            if not ruined:
                for px in (x + 3, x + w - 4):
                    place("AlwaysPoweredLightLED", px, y + h - 3)

        left, right = x + 2, x + w - 3
        bottom, top = y + 3, y + h - 4
        if kind == "home":
            # Separate sleeping rooms, a dining side and rear washing/storage areas.
            for by in range(bottom, top, 5):
                place("Bed", left, by)
                place("Bed", left + 3, by)
                place("WardrobeGrey", center - 3, by + 2)
                place("TableWood", right - 2, by)
                place("Chair", right - 3, by)
                place("Chair", right - 1, by)
                place("BookshelfFilled", right, by + 2)
            place("Sink", right, top)
            place("ToiletEmpty", right - 3, top)
            place("WaterCooler", left, top)
        elif kind == "clinic":
            for by in range(bottom, top, 4):
                for px in (left, right):
                    place("MedicalBed", px, by)
                    place("TableWood", px + (1 if px == left else -1), by + 1)
                    place("MedkitFilled", px, by + 1, anchored=False, item=True)
            place("Sink", left + 3, top)
            place("Rack", right - 3, top)
            place("MedkitBruteFilled", right - 3, top, anchored=False, item=True)
            place("MedkitBurnFilled", left + 3, bottom, anchored=False, item=True)
        elif kind == "office":
            for by in range(bottom, top, 4):
                for px in (left, right - 1):
                    place("TableWood", px, by)
                    place("ChairOfficeDark", px + 1, by)
                    place("BookshelfFilled", px, by + 2)
            place("WaterCooler", right, top)
        elif kind in ("market", "cafe"):
            # Front public seating and a rear counter with two clear end passages.
            counter_y = y + h - 5
            for px in range(x + 2, x + w - 2):
                place("TableCounterWood", px, counter_y)
            for px in range(x + 2, x + w - 2, 3):
                place("Rack" if kind == "market" else "BookshelfFilled", px, y + h - 2)
            for py in range(bottom, counter_y - 2, 4):
                for px in (left, right - 2):
                    place("TableWood", px, py)
                    place("Chair", px + 1, py)
                    place("Chair", px, py + 1)
            place("WaterCooler", right, top)
        elif kind == "utility":
            for py in range(bottom, top, 4):
                place("WaterTankFull", left, py)
                place("Rack", right, py)
                place("CrateGenericSteel", right - 3, py)
            place("Sink", left, top)
            place("ToiletEmpty", right, top)
        else:
            # Accessible parallel benches and storage aisles rather than empty sheds.
            for py in range(bottom, top, 4):
                for px in (left, left + 3, right - 3, right):
                    place("TableFolding" if kind == "workshop" and px < center else "CrateGenericSteel", px, py)
                if kind == "workshop":
                    place("ToolboxMechanicalFilled", left, py, anchored=False, item=True)
                    place("Rack", right, py + 2)
                else:
                    place("Rack", left + 3, py + 2)
                    place("Rack", right - 3, py + 2)
            place("WaterCooler", right, top)
        if ruined:
            # Keep recognizable former uses, with broken furniture and uneven roofs.
            place("N14ChairOfficeBlueBroken", right - 1, bottom + 1)
            place("N14DecorFloorBrickrubble", left + 1, top)
            place("CrateGenericSteel", right, top)

    def entry(self):
        ox, oy = self.width // 2, self.height // 2
        entry = {"mapPath": f"/Maps/_WF/PlanetPois/{self.slug}.yml", "name": self.title_key}
        if not self.expeditions:
            entry["expeditions"] = False
        entry.update({
            "footprint": f"{-ox},{-oy},{self.width - ox},{self.height - oy}",
            "minCount": 1, "maxCount": 1, "connection": self.connection,
            "sockets": [{"offset": f"{x - self.width // 2},{y - self.height // 2}", "direction": d} for x, y, d in self.sockets]})
        return entry

    def serialize(self):
        """Encode v7 tile chunks and 8x8 roof masks using engine traversal order."""
        ox, oy = self.width // 2, self.height // 2
        palette = ["Space"] + sorted(set(self.tiles.values()) - {"Space"})
        tile_ids = {tile: index for index, tile in enumerate(palette)}
        chunks = {}
        for cy in range(-oy // 16, (self.height - oy - 1) // 16 + 1):
            for cx in range(-ox // 16, (self.width - ox - 1) // 16 + 1):
                raw = bytearray()
                for ty in range(16):
                    for tx in range(16):
                        tile = self.tiles.get((cx * 16 + tx + ox, cy * 16 + ty + oy), "Space")
                        raw.extend(struct.pack("<iBBB", tile_ids[tile], 0, 0, 0))
                key = f"{cx},{cy}"
                chunks[key] = {"ind": key, "tiles": base64.b64encode(raw).decode("ascii"), "version": 7}
        masks = defaultdict(int)
        for tx, ty in sorted(self.roofs):
            tx, ty = tx - ox, ty - oy
            masks[f"{tx // 8},{ty // 8}"] |= 1 << ((ty % 8) * 8 + tx % 8)
        components = [{"type": "Transform", "parent": "invalid"}, {"type": "MapGrid", "chunks": chunks},
                      {"type": "Roof", "data": dict(masks)}]
        if self.decals:
            nodes = defaultdict(dict)
            for index, (proto, x, y, rotation) in enumerate(self.decals):
                nodes[proto, rotation][index] = f"{x - ox},{y - oy}"
            components.append({"type": "DecalGrid", "chunkCollection": {"version": 2, "nodes": [
                {"node": {"id": proto, "angle": rotation}, "decals": positions}
                for (proto, rotation), positions in nodes.items()]}})
        groups = defaultdict(list)
        for uid, (proto, x, y, rotation, anchored, extra) in enumerate(self.entities, 2):
            transform = {"type": "Transform", "parent": 1, "pos": f"{x - ox + .5:g},{y - oy + .5:g}", "anchored": anchored}
            if rotation:
                transform["rot"] = rotation
            groups[proto].append({"uid": uid, "components": [transform] + extra})
        data = {"meta": {"format": 7, "category": "Grid", "engineVersion": "277.0.0", "forkId": "", "forkVersion": "", "entityCount": len(self.entities) + 1},
                "maps": [], "grids": [1], "orphans": [1], "nullspace": [], "tilemap": dict(enumerate(palette)),
                "entities": [{"proto": "", "entities": [{"uid": 1, "components": components}]}] +
                            [{"proto": proto, "entities": entities} for proto, entities in groups.items()]}
        return yaml.safe_dump(data, sort_keys=False, width=120, allow_unicode=True)
