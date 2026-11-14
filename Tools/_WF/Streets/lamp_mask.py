"""Masks a lit state down to its lamps: pixels that light up against the unlit state stay, the rest are cleared.

RMC and CMU draw a light's lit state as a full copy of the sprite on an unshaded layer, so the pole and housing glow in
the dark too. port_streets.py runs lamp_masks of port.yml through here after the PNGs are read.
"""
from __future__ import annotations

import io
import json

from PIL import Image

NOTE = "Lamp layer masked for Wolfgate."


def frames(image: Image.Image, cell: tuple, count: int) -> list:
    """Cuts an RSI sheet into its cells, in file order: cells run left to right, then top to bottom."""
    columns = image.width // cell[0]
    return [image.crop(((i % columns) * cell[0], (i // columns) * cell[1],
                        (i % columns + 1) * cell[0], (i // columns + 1) * cell[1])) for i in range(count)]


def mask(off_png: bytes, on_png: bytes, cell: tuple, directions: int, frame_count: int) -> bytes:
    """The lit sheet keeping only pixels brighter than the unlit state of the same direction; the rest are cleared."""
    off = Image.open(io.BytesIO(off_png)).convert("RGBA")
    lit = Image.open(io.BytesIO(on_png)).convert("RGBA")
    off_cells = frames(off, cell, directions)
    lit_cells = frames(lit, cell, directions * frame_count)
    result = Image.new("RGBA", lit.size, (0, 0, 0, 0))
    columns = lit.width // cell[0]
    for index, lit_cell in enumerate(lit_cells):
        base = off_cells[index // frame_count]
        out = Image.new("RGBA", cell, (0, 0, 0, 0))
        kept = out.load()
        a, b = lit_cell.load(), base.load()
        for y in range(cell[1]):
            for x in range(cell[0]):
                if a[x, y] != b[x, y] and (b[x, y][3] == 0 or sum(a[x, y][:3]) > sum(b[x, y][:3])):
                    kept[x, y] = a[x, y]
        result.paste(out, ((index % columns) * cell[0], (index // columns) * cell[1]))
    buffer = io.BytesIO()
    result.save(buffer, "PNG", optimize=True)
    return buffer.getvalue()


def same_pixels(a: bytes, b: bytes) -> bool:
    """True when two PNGs decode to the same RGBA image, so a Pillow version change doesn't read as drift."""
    one, two = Image.open(io.BytesIO(a)).convert("RGBA"), Image.open(io.BytesIO(b)).convert("RGBA")
    return one.size == two.size and one.tobytes() == two.tobytes()


def noted_meta(meta_bytes: bytes) -> bytes:
    """The meta.json with the lamp-layer note on its copyright, keeping the file's layout and line endings."""
    text = meta_bytes.decode("utf-8-sig")
    meta = json.loads(text)
    if NOTE not in meta.get("copyright", ""):
        meta["copyright"] = f"{meta.get('copyright', '')} {NOTE}".strip()
    newline = "\r\n" if "\r\n" in text else "\n"
    return (json.dumps(meta, indent=2, ensure_ascii=False).replace("\n", newline) + newline).encode("utf-8")
