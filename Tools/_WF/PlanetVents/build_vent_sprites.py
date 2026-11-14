"""Builds the breathing vent RSIs under Resources/Textures/_WF/PlanetVents/ from tgstation sprites.

Sources (tgstation, CC-BY-SA 3.0): the lavaland tumor in icons/obj/mining_zones/tumor.dmi (tumor) becomes the
ventricle; its grey rubble is recoloured to Carcinoma's flesh, and the inhale and exhale are the mound squashed and
swollen frame by frame. The puff is the steam and steam clouds of icons/effects/particles/smoke.dmi, tinted warm,
growing, rising and fading.

Run from the repository root: python Tools/_WF/PlanetVents/build_vent_sprites.py [path to a tgstation checkout]
"""
import colorsys
import importlib.util
import json
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WF/PlanetVents"
TG = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("E:/GitHub/tgstation")
TG_COMMIT = "fb1012102257b7b0a08d861fd2b8ba963c416e93"
TUMOR = "icons/obj/mining_zones/tumor.dmi"
SMOKE = "icons/effects/particles/smoke.dmi"

_spec = importlib.util.spec_from_file_location("build_bird_sprites", ROOT / "Tools/_WF/Birds/build_bird_sprites.py")
birds = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(birds)

# Carcinoma's palette (planets/carcinoma.yml), dark to light.
FLESH_DARK = (0x28, 0x10, 0x13)
FLESH_MID = (0x77, 0x2B, 0x36)
PUFF_TINT = (0xE8, 0xB8, 0xAC)

# How long the vent holds the last frame of a one-off state, in seconds; the server changes state long before.
HOLD = 30.0


def lerp(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def flesh(image: Image.Image) -> Image.Image:
    """Recolours the grey rock round the tumor's base into dark flesh, keeping its shading."""
    image = image.copy()
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if not a:
                continue
            h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
            if s < 0.2:
                px[x, y] = lerp(FLESH_DARK, FLESH_MID, min(1.0, l * 1.6)) + (a,)
    return image


def reshape(image: Image.Image, sx: float, sy: float, brightness: float = 1.0) -> Image.Image:
    """Scales the drawn part about its bottom centre, so the mound heaves without leaving the ground."""
    box = image.getbbox()
    part = image.crop(box)
    w = max(1, round(part.width * sx))
    h = max(1, round(part.height * sy))
    part = part.resize((w, h), Image.NEAREST)
    if brightness != 1.0:
        px = part.load()
        for y in range(part.height):
            for x in range(part.width):
                r, g, b, a = px[x, y]
                px[x, y] = (min(255, round(r * brightness)), min(255, round(g * brightness)),
                            min(255, round(b * brightness)), a)
    out = Image.new("RGBA", image.size)
    cx = (box[0] + box[2]) / 2
    out.alpha_composite(part, (round(cx - w / 2), box[3] - h))
    return out


def save_rsi(folder: Path, size, states, copyright_text):
    """Writes each state as a horizontal strip and the meta.json."""
    folder.mkdir(parents=True, exist_ok=True)
    meta_states = []
    for name, frames, delays in states:
        strip = Image.new("RGBA", (size[0] * len(frames), size[1]))
        for i, frame in enumerate(frames):
            strip.alpha_composite(frame, (i * size[0], 0))
        strip.save(folder / f"{name}.png")
        state = {"name": name}
        if len(frames) > 1:
            state["delays"] = [delays]
        meta_states.append(state)
    meta = {
        "version": 1,
        "size": {"x": size[0], "y": size[1]},
        "license": "CC-BY-SA-3.0",
        "copyright": copyright_text,
        "states": meta_states,
    }
    (folder / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def build_vent():
    frames, _ = birds.load_state(TG / TUMOR, "tumor")
    base = [flesh(f) for f in frames[0]]
    rest = base[0]

    # Breathing in: the mound sinks and darkens as it draws the air down.
    inhale_steps = [(1.0, 1.0, 1.0), (1.01, 0.96, 0.97), (1.02, 0.92, 0.94), (1.03, 0.88, 0.91), (1.04, 0.85, 0.88),
                    (1.05, 0.82, 0.86)]
    inhale = [reshape(rest, sx, sy, br) for sx, sy, br in inhale_steps]
    inhale_delays = [0.7] * (len(inhale) - 1) + [HOLD]

    # Breathing out: it swells past its rest size, flushes, and settles.
    exhale_steps = [(1.02, 0.9, 0.95), (0.98, 1.06, 1.08), (0.97, 1.1, 1.12), (0.99, 1.04, 1.05), (1.0, 1.0, 1.0)]
    exhale = [reshape(rest, sx, sy, br) for sx, sy, br in exhale_steps]
    exhale_delays = [0.08, 0.1, 0.35, 0.4, HOLD]

    save_rsi(OUT / "ventricle.rsi", rest.size, [
        ("idle", base, [1.4, 1.4]),
        ("inhale", inhale, inhale_delays),
        ("exhale", exhale, exhale_delays),
    ], f"Taken from tgstation at https://github.com/tgstation/tgstation/commit/{TG_COMMIT} ({TUMOR}: tumor); "
       "recoloured and animated for Wolfgate by Tools/_WF/PlanetVents/build_vent_sprites.py.")


def tint(image: Image.Image, color, alpha: float) -> Image.Image:
    """Multiplies a grey sprite by a colour and scales its opacity."""
    image = image.copy()
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if a:
                px[x, y] = (r * color[0] // 255, g * color[1] // 255, b * color[2] // 255, round(a * alpha))
    return image


def build_puff():
    clouds = [birds.load_state(TG / SMOKE, name)[0][0][0]
              for name in ("steam_2", "steam_3", "steam_cloud_1", "steam_cloud_2", "steam_cloud_3", "steam_cloud_4",
                           "steam_cloud_5")]
    width, height = 32, 64
    frames = []
    count = len(clouds)
    for i, cloud in enumerate(clouds):
        t = i / (count - 1)
        frame = Image.new("RGBA", (width, height))
        sprite = tint(cloud, PUFF_TINT, 0.95 * (1.0 - t) ** 0.5 + 0.05)
        # Rises from the vent's mouth near the bottom of the frame, leaning a little downwind.
        frame.alpha_composite(sprite, (round(2 * t), round(30 - 26 * t)))
        frames.append(frame)
    frames.append(Image.new("RGBA", (width, height)))
    delays = [0.2] * count + [HOLD]
    save_rsi(OUT / "puff.rsi", (width, height), [("puff", frames, delays)],
             f"Taken from tgstation at https://github.com/tgstation/tgstation/commit/{TG_COMMIT} ({SMOKE}: steam_2, "
             "steam_3, steam_cloud_1-5); tinted and animated for Wolfgate by Tools/_WF/PlanetVents/build_vent_sprites.py.")


if __name__ == "__main__":
    build_vent()
    build_puff()
