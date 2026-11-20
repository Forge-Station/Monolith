"""Makes Carcinoma's scab shore tile: the flesh floor recoloured to a dried, slightly lighter scab so the shore band
blends into the ground instead of reading as bright pink squares. Run from the repository root:
python Tools/_WF/Planets/gen_carcinoma_scab.py"""
import random

from PIL import Image

SRC = "Resources/Textures/_WF/Resprites/Wizden/Tiles/meat.png"
OUT = "Resources/Textures/_WF/Planets/BoneField/scab_shore.png"
# Flesh palette (dark, mid, light) mapped to a dried scab a step brighter and browner.
MAP = {
    (83, 20, 56): (96, 36, 52),
    (104, 22, 57): (116, 44, 58),
    (141, 34, 68): (140, 62, 70),
}
FLECK = (74, 26, 36)


def main() -> None:
    src = Image.open(SRC).convert("RGBA")
    out = src.copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = px[x, y]
            px[x, y] = (*MAP.get((r, g, b), (r, g, b)), a)
    rng = random.Random(7)
    for i in range(out.width // 32):
        for _ in range(9):
            x, y = 32 * i + rng.randrange(1, 31), rng.randrange(1, 31)
            px[x, y] = (*FLECK, 255)
            if rng.random() < 0.5:
                px[x + 1, y] = (*FLECK, 255)
    out.save(OUT)


if __name__ == "__main__":
    main()
