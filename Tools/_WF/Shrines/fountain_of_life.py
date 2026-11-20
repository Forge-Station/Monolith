"""Draws the Fountain of Life sprite: a flesh basin, its glowing pool at three fill levels, and the ribbed spire.

Run from the repository root: python Tools/_WF/Shrines/fountain_of_life.py
"""

import json
import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WF/Shrines/fountain_of_life.rsi"
SIZE = 64
FRAMES = 4
DELAY = 0.18

# Carcinoma's palette (carcinoma.yml) for the flesh, and a pale gold for the ichor.
OUTLINE = (40, 16, 19, 255)
FLESH_DARK = (92, 29, 38, 255)
FLESH = (140, 51, 65, 255)
FLESH_LIGHT = (193, 96, 106, 255)
BONE = (216, 172, 160, 255)
BONE_DARK = (160, 112, 104, 255)
HOLLOW = (34, 12, 16, 255)
ICHOR = (255, 214, 120, 255)
ICHOR_LIGHT = (255, 240, 196, 255)
ICHOR_DEEP = (222, 160, 72, 255)

CX, CY = 32, 46
RIM_X, RIM_Y = 27, 12
POOL_X, POOL_Y = 21, 8


def ellipse(draw, cx, cy, rx, ry, fill):
    draw.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=fill)


def basin():
    img = Image.new("RGBA", (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    # Body of the basin: a fleshy bowl with a darker underside.
    ellipse(d, CX, CY + 4, RIM_X + 1, RIM_Y + 2, OUTLINE)
    ellipse(d, CX, CY + 3, RIM_X, RIM_Y + 1, FLESH_DARK)
    ellipse(d, CX, CY, RIM_X + 1, RIM_Y + 1, OUTLINE)
    ellipse(d, CX, CY, RIM_X, RIM_Y, FLESH)
    # Lit upper lip of the rim.
    for x in range(CX - RIM_X + 3, CX + RIM_X - 2):
        t = (x - CX) / RIM_X
        y = CY - int(round(RIM_Y * math.sqrt(max(0.0, 1 - t * t))))
        img.putpixel((x, y + 1), FLESH_LIGHT)
    # Veins down the outside of the bowl.
    for vx in (CX - 18, CX - 7, CX + 6, CX + 17):
        for i in range(5):
            img.putpixel((vx + (i % 2), CY + 8 + i), FLESH_LIGHT if i < 2 else FLESH_DARK)
    # The hollow the ichor fills.
    ellipse(d, CX, CY, POOL_X + 1, POOL_Y + 1, OUTLINE)
    ellipse(d, CX, CY, POOL_X, POOL_Y, HOLLOW)
    return img


def fill(level, frame):
    """The pool at level 1-3 of 3, rippling over the frames."""
    img = Image.new("RGBA", (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    rx, ry = POOL_X - (3 - level) * 3, POOL_Y - (3 - level) * 2
    ellipse(d, CX, CY + (3 - level), rx, ry, ICHOR_DEEP)
    ellipse(d, CX, CY + (3 - level) - 1, rx - 1, ry - 1, ICHOR)
    # Rings spreading from where the drops land, a little further each frame.
    for ring in range(2):
        r = 3 + ((frame + ring * 2) % FRAMES) * (rx - 4) / FRAMES
        if r >= rx - 1:
            continue
        d.ellipse((CX - r, CY - 1 - r * ry / rx, CX + r, CY - 1 + r * ry / rx), outline=ICHOR_LIGHT)
    # Glints that wander round the surface.
    for i in range(3):
        a = frame * 0.9 + i * 2.1
        gx = CX + int(math.cos(a) * (rx - 4))
        gy = CY - 1 + int(math.sin(a) * (ry - 3))
        img.putpixel((gx, gy), ICHOR_LIGHT)
    return img


def spire(frame, beating):
    """The ribs and the heart-bulb over the pool; a drop falls from the bulb while there is ichor."""
    img = Image.new("RGBA", (SIZE, SIZE))
    d = ImageDraw.Draw(img)
    # Ribs arching up from the rim toward the stalk.
    for side in (-1, 1):
        for i, (base_x, height) in enumerate(((24, 22), (17, 28), (10, 32))):
            points = []
            for s in range(13):
                t = s / 12
                x = CX + side * (base_x * (1 - t) + 3 * t + math.sin(t * math.pi) * (4 + i * 2))
                y = CY - 4 - height * math.sin(t * math.pi / 2)
                points.append((x, y))
            d.line(points, fill=OUTLINE, width=3)
            d.line(points, fill=BONE if i != 1 else BONE_DARK, width=1)
    # The stalk, twisted tendons tapering upward.
    for y in range(14, CY - 2):
        t = (y - 14) / (CY - 16)
        half = 1.5 + 2.5 * t
        twist = math.sin(y * 0.6 + frame * 0.4) * 0.8
        d.line((CX - half + twist, y, CX + half + twist, y), fill=OUTLINE)
        if half > 1.5:
            d.line((CX - half + 1 + twist, y, CX + half - 1 + twist, y), fill=FLESH if y % 3 else FLESH_DARK)
    # The heart at the top, swelling on alternate frames while it still bleeds ichor.
    r = 7 + (1 if beating and frame % 2 == 0 else 0)
    ellipse(d, CX, 13, r + 1, r, OUTLINE)
    ellipse(d, CX, 13, r, r - 1, FLESH)
    ellipse(d, CX - 2, 11, r - 4, r - 5, FLESH_LIGHT)
    if beating:
        ellipse(d, CX + 1, 14, 2, 2, ICHOR)
        img.putpixel((CX + 1, 13), ICHOR_LIGHT)
        # A drop on its way down to the pool.
        dy = 22 + frame * 5
        d.line((CX + 2, dy, CX + 2, dy + 2), fill=ICHOR)
        img.putpixel((CX + 2, dy + 2), ICHOR_LIGHT)
    else:
        ellipse(d, CX + 1, 14, 2, 2, HOLLOW)
    return img


def strip(frames):
    sheet = Image.new("RGBA", (SIZE * len(frames), SIZE))
    for i, frame in enumerate(frames):
        sheet.alpha_composite(frame, (i * SIZE, 0))
    return sheet


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    states = []

    basin().save(OUT / "basin.png")
    states.append({"name": "basin"})
    for level in (1, 2, 3):
        strip([fill(level, f) for f in range(FRAMES)]).save(OUT / f"fill-{level}.png")
        states.append({"name": f"fill-{level}", "delays": [[DELAY] * FRAMES]})
    strip([spire(f, True) for f in range(FRAMES)]).save(OUT / "spire.png")
    states.append({"name": "spire", "delays": [[DELAY] * FRAMES]})
    spire(0, False).save(OUT / "spire-dry.png")
    states.append({"name": "spire-dry"})

    meta = {
        "version": 1,
        "license": "CC0-1.0",
        "copyright": "Drawn by Tools/_WF/Shrines/fountain_of_life.py for Wolfgate; dedicated to the public domain",
        "size": {"x": SIZE, "y": SIZE},
        "states": states,
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(f"Wrote {len(states)} states to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
