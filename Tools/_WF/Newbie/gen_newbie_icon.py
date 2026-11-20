"""Draws the 8x8 learner-plate mark for Resources/Textures/_WF/Newbie/newbie_mark.rsi."""
import json
import pathlib

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WF/Newbie/newbie_mark.rsi"

COLOURS = {
    ".": (0, 0, 0, 0),
    "s": (0, 0, 0, 90),
    "r": (150, 146, 138, 255),
    "w": (250, 247, 238, 255),
    "L": (214, 52, 48, 255),
}

# A rounded cream plate with a soft rim, a red L and a faint drop shadow, read top row first.
ROWS = [
    ".rrrrr..",
    "rwwwwwr.",
    "rwLwwwrs",
    "rwLwwwrs",
    "rwLLLwrs",
    "rwwwwwrs",
    ".rrrrrss",
    "..sssss.",
]


def main():
    img = Image.new("RGBA", (8, 8))
    for y, row in enumerate(ROWS):
        for x, cell in enumerate(row):
            img.putpixel((x, y), COLOURS[cell])

    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / "mark.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Made for Wolfgate by Tools/_WF/Newbie/gen_newbie_icon.py",
        "size": {"x": 8, "y": 8},
        "states": [{"name": "mark"}],
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", newline="\n")


if __name__ == "__main__":
    main()
