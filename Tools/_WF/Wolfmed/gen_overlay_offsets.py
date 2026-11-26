#!/usr/bin/env python3
"""Measures where each round-start species draws its body parts against the human ones and writes the offset table
the wound and rot overlays read (WFWolfmedOverlayOffsets).

Usage: python Tools/_WF/Wolfmed/gen_overlay_offsets.py [--print]

The overlays are drawn on the human silhouette. For every round-start species and every part layer, the centre of
the species' own part sprite is compared with the human one in each direction, and the median vertical difference
becomes that part's offset, in pixels, up positive. Only y is measured: a layer offset applies in every direction,
and a sideways shift flips between south and north. A species' sprite scale (a dwarf's 1, 0.8) needs nothing here,
because the overlays are layers of the same sprite and scale with it.

Rerunning overwrites Resources/Prototypes/_WF/Wolfmed/Damage/overlay_offsets.yml, hand tuning included; --print only
shows the table.
"""
import os
import statistics
import sys

import yaml
from PIL import Image

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
PROTOTYPES = os.path.join(ROOT, "Resources", "Prototypes")
TEXTURES = os.path.join(ROOT, "Resources", "Textures")
OUT = os.path.join(PROTOTYPES, "_WF", "Wolfmed", "Damage", "overlay_offsets.yml")
FRAME = 32
LAYERS = ["Head", "Chest", "LArm", "RArm", "LHand", "RHand", "LLeg", "RLeg", "LFoot", "RFoot"]
WANTED = ("species", "speciesBaseSprites", "humanoidBaseSprite")


class Loader(yaml.SafeLoader):
    """Reads SS14 prototypes, whose custom !type tags are irrelevant here."""


def _any_tag(loader, _suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node)
    return loader.construct_scalar(node)


Loader.add_multi_constructor("!", _any_tag)


def load_prototypes():
    found = {kind: {} for kind in WANTED}
    for directory, _, files in os.walk(PROTOTYPES):
        for name in files:
            if not name.endswith(".yml"):
                continue
            path = os.path.join(directory, name)
            with open(path, encoding="utf-8") as handle:
                text = handle.read()
            if not any(f"type: {kind}" in text for kind in WANTED):
                continue
            for entry in yaml.load(text, Loader=Loader) or []:
                if isinstance(entry, dict) and entry.get("type") in WANTED:
                    found[entry["type"]].setdefault(entry["id"], entry)
    return found


def centres(sprite, state):
    """The vertical centre of the part's pixels in each of the four directions, or None where it has none."""
    path = os.path.join(TEXTURES, sprite, state + ".png")
    if not os.path.exists(path):
        return None
    image = Image.open(path).convert("RGBA")
    columns = image.width // FRAME
    out = []
    for direction in range(4):
        left, top = (direction % columns) * FRAME, (direction // columns) * FRAME
        alpha = image.crop((left, top, left + FRAME, top + FRAME)).split()[3]
        ys = [y for y in range(FRAME) for x in range(FRAME) if alpha.getpixel((x, y)) > 0]
        out.append(sum(ys) / len(ys) if ys else None)
    return out


def part_centres(protos, sprite_set):
    sprites = (protos["speciesBaseSprites"].get(sprite_set) or {}).get("sprites") or {}
    out = {}
    for layer in LAYERS:
        base = (protos["humanoidBaseSprite"].get(sprites.get(layer)) or {}).get("baseSprite") or {}
        if "sprite" in base and "state" in base:
            out[layer] = centres(base["sprite"], base["state"])
    return out


def main():
    protos = load_prototypes()
    human = part_centres(protos, protos["species"]["Human"]["sprites"])
    table = {}
    for species_id, species in sorted(protos["species"].items()):
        if str(species.get("roundStart")).lower() != "true":
            continue
        measured = part_centres(protos, species.get("sprites"))
        offsets = {}
        for layer in LAYERS:
            # A left and a right part share one measurement, so a sub-pixel difference between the two sides
            # does not round them apart.
            pair = [layer, ("R" if layer[0] == "L" else "L") + layer[1:]] if layer[0] in "LR" else [layer]
            diffs = []
            for side in pair:
                own, base = measured.get(side), human.get(side)
                if own and base:
                    diffs += [b - o for o, b in zip(own, base) if o is not None and b is not None]
            shift = round(statistics.median(diffs)) if diffs else 0
            if shift:
                offsets[layer] = shift
        table[species_id] = offsets

    lines = []
    for species_id, offsets in table.items():
        if not offsets:
            lines.append(f"    {species_id}: {{}}")
            continue
        lines.append(f"    {species_id}:")
        lines.extend(f"      {layer}: 0, {offsets[layer]}" for layer in LAYERS if layer in offsets)

    if "--print" in sys.argv:
        print("\n".join(lines))
        return

    header = """\
# VISUALS: where the wound and rot overlays sit on each species, in pixels, up and right positive. Generated by
# Tools/_WF/Wolfmed/gen_overlay_offsets.py from the species' own part sprites against the human ones (the overlay
# art is drawn on the human silhouette); a species or part missing here gets no shift. Tune by hand freely, but a
# rerun of the script overwrites this file. A species' sprite scale (a dwarf's 1, 0.8) needs no entry: the overlays
# are layers of the same sprite and scale with it. An x offset applies in every direction, so a sideways shift
# that differs between facings cannot be expressed here.

- type: wolfmedOverlayOffsets
  id: WFWolfmedOverlayOffsets
  species:
"""
    with open(OUT, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(header + "\n".join(lines) + "\n")
    print(f"wrote {len(table)} species to {OUT}")


if __name__ == "__main__":
    main()
