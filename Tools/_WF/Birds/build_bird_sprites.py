"""Builds the planet bird RSIs under Resources/Textures/_WF/Birds/ by recolouring tgstation sprites.

Sources (tgstation, CC-BY-SA 3.0): the parrot in icons/mob/simple/animal.dmi (parrot_fly, parrot_sit, parrot_dead) and
the pAI hawk and crow holoforms in icons/mob/silicon/pai.dmi (hawk, hawk_rest, hawk_dead, crow). The pAI forms carry a
green holo outline, which is stripped; their greyscale bodies are gradient-mapped. The parrot is recoloured by hue band.
Each RSI also gets a "shadow" state: the flight frames as a translucent black silhouette.

Run from the repository root: python Tools/_WF/Birds/build_bird_sprites.py [path to a tgstation checkout]
"""
import colorsys
import json
import re
import struct
import sys
import zlib
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WF/Birds"
TG = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("E:/GitHub/tgstation")
TG_COMMIT = "74f7d24a354c8e82071af3d9d28313b930526a90"
ANIMAL = "icons/mob/simple/animal.dmi"
PAI = "icons/mob/silicon/pai.dmi"


def dmi_states(path: Path):
    """Parses a .dmi: returns (sheet, frame width, frame height, columns, states)."""
    data = path.read_bytes()
    i, text = 8, None
    while i < len(data):
        length = struct.unpack(">I", data[i:i + 4])[0]
        kind, body = data[i + 4:i + 8], data[i + 8:i + 8 + length]
        if kind in (b"zTXt", b"tEXt"):
            key, _, rest = body.partition(b"\0")
            if key == b"Description":
                text = zlib.decompress(rest[1:]).decode() if kind == b"zTXt" else rest.decode()
                break
        i += 12 + length
    width = int(re.search(r"width = (\d+)", text).group(1))
    height = int(re.search(r"height = (\d+)", text).group(1))
    states, current, index = [], None, 0
    for line in text.splitlines():
        line = line.strip()
        if match := re.match(r'state = "(.*)"', line):
            current = {"name": match.group(1), "dirs": 1, "frames": 1, "delay": None}
            states.append(current)
        elif current is not None and (match := re.match(r"(\w+) = (.*)", line)):
            key, value = match.groups()
            if key == "dirs":
                current["dirs"] = int(value)
            elif key == "frames":
                current["frames"] = int(value)
            elif key == "delay":
                current["delay"] = [float(v) for v in value.split(",")]
    for state in states:
        state["start"] = index
        index += state["dirs"] * state["frames"]
    sheet = Image.open(path).convert("RGBA")
    return sheet, width, height, sheet.width // width, states


def load_state(path: Path, name: str, occurrence: int = 0):
    """Frames of one state as frames[direction][frame], plus its BYOND delays in ticks."""
    sheet, width, height, columns, states = dmi_states(path)
    state = [s for s in states if s["name"] == name][occurrence]
    frames = []
    for direction in range(state["dirs"]):
        row = []
        for frame in range(state["frames"]):
            n = state["start"] + frame * state["dirs"] + direction
            x, y = (n % columns) * width, (n // columns) * height
            row.append(sheet.crop((x, y, x + width, y + height)))
        frames.append(row)
    return frames, state["delay"] or [1.0] * state["frames"]


def strip_outline(image: Image.Image) -> Image.Image:
    """Removes the pAI holo outline: bright green pixels reachable from the transparent outside."""
    image = image.copy()
    px = image.load()
    w, h = image.size

    def green(c):
        r, g, b, a = c
        return a and g > 150 and g > r + 60 and g > b + 60

    changed = True
    while changed:
        changed = False
        for y in range(h):
            for x in range(w):
                if not green(px[x, y]):
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = x + dx, y + dy
                    if not (0 <= nx < w and 0 <= ny < h) or px[nx, ny][3] == 0:
                        px[x, y] = (0, 0, 0, 0)
                        changed = True
                        break
    return image


def hexc(value: str):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def gradient(stops):
    """A gradient map from greyscale value (0-1) through the given colours."""
    colours = [hexc(s) for s in stops]

    def lookup(v):
        v = min(max(v, 0.0), 1.0) * (len(colours) - 1)
        i = min(int(v), len(colours) - 2)
        t = v - i
        return tuple(round(colours[i][k] * (1 - t) + colours[i + 1][k] * t) for k in range(3))
    return lookup


def map_grey(image, ramp, eyes, brightest):
    """Gradient-maps a greyscale pAI body so its brightest grey lands at 0.65 of the ramp; the remaining green (the
    eyes) becomes the eye colour and other coloured pixels (blood on the dead hawk) are kept."""
    image = strip_outline(image)
    px = image.load()
    eye = hexc(eyes)
    value_scale = 0.65 / brightest
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if not a:
                continue
            if g > r + 40 and g > b + 40:
                px[x, y] = (*eye, a)
                continue
            if max(r, g, b) - min(r, g, b) > 60:
                continue
            px[x, y] = (*ramp(max(r, g, b) / 255 * value_scale), a)
    return image


def map_hue(image, bands):
    """Recolours the parrot by hue band. bands: list of (predicate(h, s, v), (hue or None, sat, value scale, alpha))."""
    image = image.copy()
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if not a:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            hd = h * 360
            for test, (hue, sat, scale, alpha) in bands:
                if test(hd, s, v):
                    nh = h if hue is None else hue / 360
                    ns = s if sat is None else sat
                    nr, ng, nb = colorsys.hsv_to_rgb(nh, ns, min(1.0, v * scale))
                    px[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), round(a * alpha))
                    break
    return image


def red(h, s, v): return (h >= 330 or h < 15) and s > 0.4
def blue(h, s, v): return 190 <= h < 260 and s > 0.3
def warm(h, s, v): return 15 <= h < 70 and s > 0.4
def head(h, s, v): return 80 <= h < 140 and s > 0.4
def dark(h, s, v): return v < 0.2
def pale(h, s, v): return s <= 0.3
def anything(h, s, v): return True


def shadow(image):
    """A translucent black silhouette of a frame."""
    out = Image.new("RGBA", image.size)
    src, dst = image.load(), out.load()
    for y in range(image.height):
        for x in range(image.width):
            if src[x, y][3]:
                dst[x, y] = (0, 0, 0, 90)
    return out


def save_state(folder: Path, name: str, frames, delays_ticks):
    """Writes one RSI state PNG (one row per direction) and returns its meta entry."""
    dirs, count = len(frames), len(frames[0])
    w, h = frames[0][0].size
    sheet = Image.new("RGBA", (w * count, h * dirs))
    for d, row in enumerate(frames):
        for f, image in enumerate(row):
            sheet.paste(image, (f * w, d * h))
    sheet.save(folder / f"{name}.png")
    entry = {"name": name}
    if dirs > 1:
        entry["directions"] = dirs
    if count > 1:
        entry["delays"] = [[round(t / 10, 2) for t in delays_ticks] for _ in range(dirs)]
    return entry


def apply(frames, fn):
    return [[fn(image) for image in row] for row in frames]


def write_rsi(name, states, copyright_note):
    folder = OUT / f"{name}.rsi"
    folder.mkdir(parents=True, exist_ok=True)
    entries = [save_state(folder, state, frames, delays) for state, (frames, delays) in states.items()]
    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC-BY-SA-3.0",
        "copyright": f"Taken from tgstation at https://github.com/tgstation/tgstation/commit/{TG_COMMIT} "
                     f"({copyright_note}); recoloured for Wolfgate by Tools/_WF/Birds/build_bird_sprites.py.",
        "states": entries,
    }
    (folder / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")


def parrot_bird(name, bands, slow=1.0):
    fly, fly_delay = load_state(TG / ANIMAL, "parrot_fly")
    sit, sit_delay = load_state(TG / ANIMAL, "parrot_sit")
    dead, dead_delay = load_state(TG / ANIMAL, "parrot_dead")
    recolour = lambda image: map_hue(image, bands)
    fly = apply(fly, recolour)
    write_rsi(name, {
        "fly": (fly, [d * slow for d in fly_delay]),
        "sit": (apply(sit, recolour), sit_delay),
        "dead": (apply(dead, recolour), dead_delay),
        "shadow": (apply(fly, shadow), [d * slow for d in fly_delay]),
    }, f"{ANIMAL}: parrot_fly, parrot_sit, parrot_dead")


def brightest_grey(frames):
    """The brightest grey (0-1) in a state, ignoring coloured pixels."""
    top = 0
    for row in frames:
        for image in row:
            for r, g, b, a in image.getdata():
                if a and max(r, g, b) - min(r, g, b) <= 60:
                    top = max(top, max(r, g, b))
    return top / 255


def hawk_bird(name, ramp, eyes, ground="hawk_rest", slow=1.0):
    def grey(frames):
        top = brightest_grey(frames)
        return apply(frames, lambda image: map_grey(image, ramp, eyes, top))

    fly, fly_delay = load_state(TG / PAI, "hawk")
    fly = grey(fly)
    if ground == "crow":
        # The second "crow" state is the animated standing crow; its 0.5-tick delays are slowed to a head bob.
        sit, sit_delay = load_state(TG / PAI, "crow", 1)
        sit_delay = [d * 8 for d in sit_delay]
        note = f"{PAI}: hawk, crow, hawk_dead"
    else:
        sit, sit_delay = load_state(TG / PAI, "hawk_rest")
        note = f"{PAI}: hawk, hawk_rest, hawk_dead"
    dead, dead_delay = load_state(TG / PAI, "hawk_dead")
    write_rsi(name, {
        "fly": (fly, [d * slow for d in fly_delay]),
        "sit": (grey(sit), sit_delay),
        "dead": (grey(dead), dead_delay),
        "shadow": (apply(fly, shadow), [d * slow for d in fly_delay]),
    }, note)


def main():
    # Verda songbird: olive back, warm brown wings, yellow breast and crown.
    parrot_bird("songbird", [
        (dark, (None, None, 1.0, 1.0)),
        (red, (80, 0.55, 1.0, 1.0)),
        (blue, (30, 0.55, 0.9, 1.0)),
        (warm, (52, 0.85, 1.05, 1.0)),
        (head, (55, 0.8, 1.2, 1.0)),
    ], slow=0.6)
    # Merak glass-winged swift: sandy body, pale see-through wings.
    parrot_bird("glass_swift", [
        (dark, (None, 0.2, 1.0, 1.0)),
        (red, (35, 0.35, 1.15, 1.0)),
        (blue, (185, 0.25, 1.6, 0.55)),
        (warm, (180, 0.15, 1.3, 0.6)),
        (head, (30, 0.3, 1.2, 1.0)),
    ], slow=0.5)
    # Thrascias snow grouse: white with grey-barred wings and a dark tail.
    parrot_bird("snow_grouse", [
        (dark, (None, 0.1, 1.0, 1.0)),
        (red, (210, 0.05, 1.45, 1.0)),
        (blue, (215, 0.15, 0.9, 1.0)),
        (warm, (25, 0.5, 0.9, 1.0)),
        (head, (210, 0.05, 1.5, 1.0)),
    ], slow=0.8)
    # Asclepiu lake gull: white body, grey wings, yellow bill.
    parrot_bird("lake_gull", [
        (dark, (None, 0.1, 1.0, 1.0)),
        (red, (200, 0.04, 1.45, 1.0)),
        (blue, (205, 0.12, 1.1, 1.0)),
        (warm, (48, 0.8, 1.05, 1.0)),
        (head, (200, 0.03, 1.6, 1.0)),
    ], slow=1.2)
    # Fervidus ash hawk: charcoal plumage glowing ember at the edges.
    hawk_bird("ash_hawk", gradient(["#120c0a", "#3d2b23", "#8a3d1a", "#e8661c", "#ffc35a"]), "#ffcf4a")
    # Aerumna carrion bird: soot black, dull red eyes, crow-like on the ground.
    hawk_bird("carrion_bird", gradient(["#050506", "#141418", "#25242b", "#3b3842", "#5c5866"]), "#a3202a", ground="crow", slow=1.3)
    # Carcinoma flesh kite: raw pink skin over dark red sinew.
    hawk_bird("flesh_kite", gradient(["#2a0508", "#6e1420", "#b5384a", "#e07a86", "#ffc2b8"]), "#f2e24a")
    print(f"Wrote birds to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
