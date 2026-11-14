"""Converts blood splatter states from NovaSector's icons/effects/blood.dmi into an RSI.

Usage: python Tools/_WF/Wolfmed/import_nova_blood.py <path to blood.dmi>
       python Tools/_WF/Wolfmed/import_nova_blood.py --free   (rebuild the free states from the RSI alone)

DMI sheets are frame-major (frame 0 of every direction, then frame 1, ...); RSI sheets are direction-major.

FIX1: each hitsplatter also gets a "<state>_free" single-direction copy of its East row. The spray is
rotated to the exact angle of the hit, so it must not answer to RSI directions at all; East is the row
whose art already points along +X, which is where a sprite rotation of zero puts it.
"""
import json
import os
import sys

from PIL import Image

STATES = ["hitsplatter1", "hitsplatter2", "hitsplatter3",
          "splatter1", "splatter2", "splatter3", "splatter4", "splatter5", "splatter6",
          "floor1", "floor2", "floor3", "floor4", "floor5", "floor6", "floor7"]
OUT = "Resources/Textures/_WF/Wolfmed/Effects/blood_splatter.rsi"
COMMIT = "7499f47bfc6b768a72c922be46971ebfccf794a1"
FREE_SUFFIX = "_free"
FREE_ROW = 2  # RSI direction order is South, North, East, West; East already points along +X.


def parse(description):
    states, current = [], None
    for line in description.split("\n"):
        line = line.strip()
        if line.startswith("state ="):
            current = {"state": line.split("=", 1)[1].strip().strip('"')}
            states.append(current)
        elif current is not None and "=" in line:
            key, value = [part.strip() for part in line.split("=", 1)]
            current[key] = value
    return states


def free_state(name, size=32):
    """Crops the East row out of an already written state sheet and saves it as a 1-direction state."""
    sheet = Image.open(os.path.join(OUT, name + ".png")).convert("RGBA")
    frames = sheet.width // size
    row = sheet.crop((0, FREE_ROW * size, sheet.width, (FREE_ROW + 1) * size))
    row.save(os.path.join(OUT, name + FREE_SUFFIX + ".png"))
    return frames


def add_free_states(meta_states, size=32):
    """Appends a free copy of every hitsplatter to the state list, in place."""
    for entry in list(meta_states):
        if not entry["name"].startswith("hitsplatter") or entry["name"].endswith(FREE_SUFFIX):
            continue

        frames = free_state(entry["name"], size)
        free = {"name": entry["name"] + FREE_SUFFIX}
        if "delays" in entry:
            free["delays"] = [list(entry["delays"][FREE_ROW])]
        else:
            free["delays"] = [[0.1] * frames]
        meta_states.append(free)


def rebuild_free():
    """Regenerates the free states from the RSI that is already on disk, with no DMI needed."""
    with open(os.path.join(OUT, "meta.json")) as handle:
        meta = json.load(handle)

    meta["states"] = [state for state in meta["states"] if not state["name"].endswith(FREE_SUFFIX)]
    add_free_states(meta["states"], meta["size"]["x"])
    with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=4)
        handle.write("\n")
    print("wrote", len(meta["states"]), "states")


def main(path):
    source = Image.open(path)
    states = parse(source.text["Description"])  # read before convert(), which drops PNG text chunks
    sheet = source.convert("RGBA")
    size = 32
    columns = sheet.width // size
    os.makedirs(OUT, exist_ok=True)
    meta_states, index = [], 0
    for state in states:
        dirs, frames = int(state.get("dirs", 1)), int(state.get("frames", 1))
        start, index = index, index + dirs * frames
        if state["state"] not in STATES:
            continue
        out = Image.new("RGBA", (frames * size, dirs * size))
        for frame in range(frames):
            for direction in range(dirs):
                cell = start + frame * dirs + direction
                box = ((cell % columns) * size, (cell // columns) * size)
                out.paste(sheet.crop((box[0], box[1], box[0] + size, box[1] + size)), (frame * size, direction * size))
        out.save(os.path.join(OUT, state["state"] + ".png"))
        entry = {"name": state["state"]}
        if dirs > 1:
            entry["directions"] = dirs
        if frames > 1:
            delays = [float(d) / 10 for d in state.get("delay", ",".join(["1"] * frames)).split(",")]
            entry["delays"] = [delays for _ in range(dirs)]
        meta_states.append(entry)
    add_free_states(meta_states, size)
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Taken from NovaSector at https://github.com/NovaSector/NovaSector/blob/" + COMMIT +
                     "/icons/effects/blood.dmi (originally tgstation). Converted to RSI for Wolfgate.",
        "size": {"x": size, "y": size},
        "states": meta_states,
    }
    with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=4)
        handle.write("\n")
    print("wrote", len(meta_states), "states")


if __name__ == "__main__":
    if sys.argv[1] == "--free":
        rebuild_free()
    else:
        main(sys.argv[1])
