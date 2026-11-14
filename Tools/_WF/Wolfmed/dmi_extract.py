"""Lists or extracts the states of a BYOND .dmi (a PNG with a zTXt "Description" chunk) into per-frame PNGs.

Usage: python dmi_extract.py <file.dmi> [out_dir] [--states a,b,c]
Without out_dir it only prints the states, their sizes, directions and frame counts. With one, it writes
<out_dir>/<state>[-<dir>][-<frame>].png (32-bit RGBA) and a states.json with the delays.
"""
import json
import sys
from pathlib import Path

from PIL import Image


def read_description(path):
    im = Image.open(path)
    desc = im.text.get("Description") if hasattr(im, "text") else None
    if desc is None:
        raise SystemExit(f"{path}: no Description chunk (not a .dmi?)")
    return im, desc


def parse(desc):
    width = height = 32
    states = []
    current = None
    for raw in desc.splitlines():
        line = raw.strip()
        if line.startswith("width ="):
            width = int(line.split("=")[1])
        elif line.startswith("height ="):
            height = int(line.split("=")[1])
        elif line.startswith("state ="):
            current = {"name": line.split("=", 1)[1].strip().strip('"'), "dirs": 1, "frames": 1, "delay": None}
            states.append(current)
        elif current is not None and line.startswith("dirs ="):
            current["dirs"] = int(line.split("=")[1])
        elif current is not None and line.startswith("frames ="):
            current["frames"] = int(line.split("=")[1])
        elif current is not None and line.startswith("delay ="):
            current["delay"] = [float(x) for x in line.split("=")[1].split(",")]
    return width, height, states


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    only = None
    for a in sys.argv[1:]:
        if a.startswith("--states="):
            only = set(a.split("=", 1)[1].split(","))
    if not args:
        raise SystemExit(__doc__)
    im, desc = read_description(args[0])
    width, height, states = parse(desc)
    im = im.convert("RGBA")
    columns = im.width // width
    index = 0
    out = Path(args[1]) if len(args) > 1 else None
    if out:
        out.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for state in states:
        count = state["dirs"] * state["frames"]
        print(f"{state['name']!r}: {width}x{height} dirs={state['dirs']} frames={state['frames']} delay={state['delay']}")
        if out and (only is None or state["name"] in only):
            for frame in range(state["frames"]):
                for d in range(state["dirs"]):
                    i = index + frame * state["dirs"] + d
                    x, y = (i % columns) * width, (i // columns) * height
                    tile = im.crop((x, y, x + width, y + height))
                    name = state["name"] or "blank"
                    suffix = (f"-d{d}" if state["dirs"] > 1 else "") + (f"-f{frame}" if state["frames"] > 1 else "")
                    tile.save(out / f"{name}{suffix}.png")
            manifest[state["name"]] = {"dirs": state["dirs"], "frames": state["frames"], "delay": state["delay"]}
        index += count
    if out:
        (out / "states.json").write_text(json.dumps(manifest, indent=2))
        print(f"wrote {out}")


if __name__ == "__main__":
    main()
