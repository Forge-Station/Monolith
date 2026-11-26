#!/usr/bin/env python3
"""Draws contact sheets of the Streets module, one PNG per prototype folder, so a mapper can pick pieces by eye.

    python Tools/_WF/Streets/catalogue.py OUT_DIR

Each entity is drawn from its sprite layers (first frame, south-facing), each tile from its first variant and each decal
from its state, with the prototype id underneath. Needs PyYAML and Pillow.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import port_streets  # noqa: E402

REPO = port_streets.REPO
PROTOTYPES = REPO / port_streets.PROTOTYPES
TEXTURES = REPO / "Resources/Textures"
CELL = (148, 132)
COLUMNS = 10
BACKGROUND = (38, 42, 48)
PANEL = (58, 64, 72)


def frame(rsi: Path, state: str):
    """The first frame of a state, facing south."""
    meta = json.loads((rsi / "meta.json").read_text(encoding="utf-8-sig"))
    width, height = meta["size"]["x"], meta["size"]["y"]
    png = rsi / f"{state}.png"
    if not png.exists():
        return None
    return Image.open(png).convert("RGBA").crop((0, 0, width, height))


def entity_image(uid: str, docs: dict):
    """The visible layers of an entity stacked, its RSI taken from the nearest parent that names one."""
    chain = []
    doc = docs.get(uid)
    while doc is not None:
        chain.append(doc)
        parent = doc.get("parent")
        doc = docs.get(parent if isinstance(parent, str) else (parent or [None])[0])
    sprite = {}
    for doc in reversed(chain):
        own = next((comp for comp in doc.get("components") or [] if comp.get("type") == "Sprite"), None)
        if own:
            sprite.update(own)
    if not sprite.get("sprite"):
        return None
    layers = sprite.get("layers") or ([{"state": sprite["state"]}] if "state" in sprite else [])
    image = None
    for layer in layers:
        if not isinstance(layer, dict) or "state" not in layer or layer.get("visible") in ("false", "False"):
            continue
        piece = frame(TEXTURES / (layer.get("sprite") or sprite["sprite"]), layer["state"])
        if piece is None:
            continue
        if image is None:
            image = piece
        elif piece.size == image.size:
            image = Image.alpha_composite(image, piece)
    return image


def sheet(title: str, items: list, out: Path, font) -> None:
    rows = (len(items) + COLUMNS - 1) // COLUMNS
    page = Image.new("RGB", (COLUMNS * CELL[0] + 12, rows * CELL[1] + 44), BACKGROUND)
    draw = ImageDraw.Draw(page)
    draw.text((8, 8), f"{title}  ({len(items)})", font=font, fill=(240, 236, 224))
    for index, (uid, image) in enumerate(items):
        left = 6 + index % COLUMNS * CELL[0]
        top = 36 + index // COLUMNS * CELL[1]
        draw.rectangle((left + 2, top + 2, left + CELL[0] - 3, top + CELL[1] - 3), fill=PANEL)
        if image is not None:
            scale = min(2, (CELL[0] - 12) // image.width or 1, (CELL[1] - 34) // image.height or 1)
            if scale < 1:
                image.thumbnail((CELL[0] - 12, CELL[1] - 34))
            elif scale > 1:
                image = image.resize((image.width * scale, image.height * scale), Image.NEAREST)
            page.paste(image, (left + (CELL[0] - image.width) // 2, top + 4 + (CELL[1] - 34 - image.height) // 2), image)
        label = uid if len(uid) <= 24 else uid[:11] + ".." + uid[-11:]
        draw.text((left + 6, top + CELL[1] - 24), label, font=font, fill=(200, 208, 214))
    page.save(out)
    print(f"{out.name}: {len(items)}")


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 11)
    except OSError:
        font = ImageFont.load_default()

    files = sorted(PROTOTYPES.rglob("*.yml"))
    loaded = {path: port_streets.load(path) for path in files}
    docs = {doc["id"]: doc for found in loaded.values() for doc in found if doc.get("type") == "entity"}
    folders: dict[str, list] = {}
    for path, found in loaded.items():
        folder = path.parent.name if path.parent != PROTOTYPES else "Base"
        items = folders.setdefault(folder, [])
        for doc in found:
            if doc.get("abstract") == "true":
                continue
            if doc.get("type") == "entity":
                items.append((doc["id"], entity_image(doc["id"], docs)))
            elif doc.get("type") == "tile":
                png = REPO / "Resources" / doc["sprite"].lstrip("/")
                items.append((doc["id"], Image.open(png).convert("RGBA").crop((0, 0, 32, 32))))
            elif doc.get("type") == "decal":
                items.append((doc["id"], frame(TEXTURES / doc["sprite"]["sprite"], doc["sprite"]["state"])))
    for folder, items in sorted(folders.items()):
        if items:
            sheet(folder, items, out / f"{folder.lower()}.png", font)
    return 0


if __name__ == "__main__":
    sys.exit(main())
