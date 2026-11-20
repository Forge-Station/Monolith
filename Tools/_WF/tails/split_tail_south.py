"""Splits tail states into a FRONT state without the south frame and a BEHIND state with only the south frame.

A single-layer tail draws every facing on one layer, so facing south it covers the groin and the leg gap.
The marking then maps <state>_FRONT to TailOversuit and <state>_BEHIND to TailBehind, with a colorLinks entry
from BEHIND to FRONT: facing south the tail draws under the body, other facings stay above clothing.

Run from the repository root (requires Pillow):
    python Tools/_WF/tails/split_tail_south.py _Mono/Mobs/Customization/protogen_parts.rsi tail_protogen bushy_tail_protogen shark_tail_protogen

<rsi> and --out are relative to Resources/Textures. Split states are merged into --out (default
_WF/Mobs/Customization/protogen_tails.rsi); the source RSI is never modified.
"""
import argparse
import json
import os
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
TEXTURES = os.path.join(ROOT, "Resources", "Textures")
DEFAULT_OUT = "_WF/Mobs/Customization/protogen_tails.rsi"
SPLIT_NOTE = "Split into FRONT/BEHIND states for Wolfgate."
SOUTH = 0  # RSI direction order: S, N, E, W, then SE, SW, NE, NW
CLEAR = (0, 0, 0, 0)


def load_meta(rsi_dir):
    with open(os.path.join(rsi_dir, "meta.json"), encoding="utf-8-sig") as f:
        return json.load(f)


def icon_boxes(sheet, size, directions, frames):
    """Pixel box of every icon as [direction][frame]; RSI sheets are direction-major with frames inner."""
    w, h = size
    cols = sheet.width // w
    needed = directions * frames
    if sheet.width % w or sheet.height % h or cols * (sheet.height // h) < needed:
        raise ValueError("a %dx%d sheet cannot hold %d icons of %dx%d" % (sheet.width, sheet.height, needed, w, h))
    boxes = []
    for d in range(directions):
        row = []
        for f in range(frames):
            i = d * frames + f
            x, y = (i % cols) * w, (i // cols) * h
            row.append((x, y, x + w, y + h))
        boxes.append(row)
    return boxes


def opaque_pixels(image):
    return sum(image.getchannel("A").histogram()[1:])


def split_state(src_dir, meta, name):
    """Returns (front, behind, state meta, south pixel count) for one source state."""
    state = next((s for s in meta["states"] if s["name"] == name), None)
    if state is None:
        raise ValueError("state %r not found" % name)
    directions = state.get("directions", 1)
    if directions not in (4, 8):
        raise ValueError("state %r has %d direction(s) and so no separate south frame" % (name, directions))
    delays = state.get("delays")
    frames = len(delays[0]) if delays else 1
    size = (meta["size"]["x"], meta["size"]["y"])

    with Image.open(os.path.join(src_dir, name + ".png")) as im:
        source = im.convert("RGBA")

    front = source.copy()
    behind = Image.new("RGBA", source.size, CLEAR)
    south_pixels = 0
    for box in icon_boxes(source, size, directions, frames)[SOUTH]:
        icon = source.crop(box)
        south_pixels += opaque_pixels(icon)
        behind.paste(icon, box[:2])
        front.paste(CLEAR, box)
    return front, behind, state, south_pixels


def target_meta(out_dir, src_meta, out_rel):
    """Existing destination meta, or a new one crediting the source art."""
    if not os.path.isfile(os.path.join(out_dir, "meta.json")):
        return {
            "version": 1,
            "license": src_meta["license"],
            "copyright": "%s %s" % (src_meta["copyright"].strip(), SPLIT_NOTE),
            "size": src_meta["size"],
            "states": [],
        }

    meta = load_meta(out_dir)
    # One RSI carries one licence and one credit line, so art from another source needs its own --out.
    if meta["license"] != src_meta["license"] or meta["size"] != src_meta["size"]:
        sys.exit("%s is %s at %s; the source is %s at %s. Use another --out." % (
            out_rel, meta["license"], meta["size"], src_meta["license"], src_meta["size"]))
    if src_meta["copyright"].strip() not in meta["copyright"]:
        sys.exit("%s credits other art. Use another --out." % out_rel)
    return meta


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("rsi", help="source RSI, relative to Resources/Textures")
    parser.add_argument("states", nargs="+", help="states to split")
    parser.add_argument("--out", default=DEFAULT_OUT, help="destination RSI, relative to Resources/Textures")
    args = parser.parse_args()

    src_dir = os.path.join(TEXTURES, args.rsi)
    out_dir = os.path.join(TEXTURES, args.out)
    src_meta = load_meta(src_dir)
    meta = target_meta(out_dir, src_meta, args.out)
    os.makedirs(out_dir, exist_ok=True)

    for name in args.states:
        front, behind, state, south_pixels = split_state(src_dir, src_meta, name)
        if south_pixels == 0:
            print("warning: %s has an empty south frame; the split changes nothing" % name)
        for suffix, image in (("_FRONT", front), ("_BEHIND", behind)):
            entry = dict(state, name=name + suffix)
            image.save(os.path.join(out_dir, entry["name"] + ".png"), optimize=True)
            index = next((i for i, s in enumerate(meta["states"]) if s["name"] == entry["name"]), None)
            if index is None:
                meta["states"].append(entry)
            else:
                meta["states"][index] = entry
        print("%-24s %3d south pixels -> %s_BEHIND" % (name, south_pixels, name))

    with open(os.path.join(out_dir, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")
    print("wrote %s (%d states)" % (args.out, len(meta["states"])))


if __name__ == "__main__":
    main()
