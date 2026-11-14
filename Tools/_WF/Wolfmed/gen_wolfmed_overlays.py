#!/usr/bin/env python3
"""Builds the Wolfmed pain HUD, wound and rot RSIs from the Bobstation septic icons.

Usage: python Tools/_WF/Wolfmed/gen_wolfmed_overlays.py [--bob <modular_septic/icons dir>] [--nevado <modular_septic/icons dir>]

pain.rsi    pain0..pain7 and paindd from hud/screen_nigga.dmi, each frame composited onto the dark scanline
            backing the Crescent health alerts draw inside their own sprites (read off bleed.rsi/bleed3, whose
            glyph leaves every row's background showing).
wounds.rsi  bulletwound.dmi is one 3x3 wound glyph with a drip under it, drawn in the corner of the frame and
            placed by pixel offset in Bob's code, one state per facing. Here it is recoloured to grey (luminance,
            normalised so the brightest pixel is white, so the client's blood-colour tint lands on the blood
            colour itself) and baked onto each human body part in each direction: <Part>_drip (bullet on the
            torso and head, the slower 1bullet on limbs), <Part>_stream (2bullet, a heavy bleed) and <Part>_old
            (obullet, a clotted or dressed wound). The glyph sits on the centre of the part's visible pixels;
            a direction where the part is hidden behind others gets an empty frame.
rot.rsi     rot_parts.dmi in its own colour, renamed <Part>_rot. Wolfgate has no groin part, so rot_chest and
            rot_groin are composited into Chest_rot.
artery.rsi  artery.dmi's <site>_artery0/1 as they are (they are drawn in place on the human frame), as a blood mask
            (the reddest pixel is white, so the blood-colour tint lands on the blood colour): the still artery (0) and
            the spray (1, three frames, played once per blood spurt). head is the head's own artery, neck the stump
            where a head was, r_arm .. l_foot the limb's artery, cut or a stump.
stumps.rsi  Escape From Nevado's stump.dmi (its modular_septic copy), one stump per site where a limb was: <site>_stump
            is the flesh as a blood mask, <site>_stump_bone the bone and the outline in their own colours, and
            <site>_stump_drip / _stump_stream the bulletwound drip and trickle glyphs hung from the bottom of the
            stump, for a stump that bleeds.
sepsis.rsi  septicshock, the sepsis alert's severity 1: the hand-made sepsis state with paindowned's red border,
            flashing at the same rate. The sepsis state is the source and is left as it is.

downed.rsi  the Downed alert: two arrows pointing down, lit on top and shaded below, on the same backing as pain.rsi.

Deterministic; rerunning rewrites the seven RSIs byte for byte. --hud-only builds the last two alone, which need no
Bobstation checkout.
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dmi_extract import parse, read_description  # noqa: E402

FRAME = 32
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
TEXTURES = os.path.join(ROOT, "Resources", "Textures")
OUT = os.path.join(TEXTURES, "_WF", "Wolfmed")
DEFAULT_BOB = os.environ.get("BOB_ICONS")  # or pass --bob <bobstation modular_septic/icons>
BOB_REPO = "https://gitgud.io/bobstation/bobstation-reignited"
DEFAULT_NEVADO = os.environ.get("NEVADO_ICONS")  # or pass --nevado <EscapeFromNevado modular_septic/icons>
NEVADO_REPO = "https://github.com/EscapeFromNevado/EscapeFromNevado"

BACKING = os.path.join(TEXTURES, "_Crescent", "Interface", "Alerts", "bleed.rsi", "bleed3.png")
HUMAN = os.path.join(TEXTURES, "Mobs", "Species", "Human", "parts.rsi")

# HumanoidVisualLayers -> the human part sprites whose alpha (intersected) is that layer's silhouette.
PARTS = {
    "Chest": ["torso_m", "torso_f"],
    "Head": ["head_m", "head_f"],
    "RArm": ["r_arm"],
    "LArm": ["l_arm"],
    "RLeg": ["r_leg"],
    "LLeg": ["l_leg"],
    "LFoot": ["l_foot"],
    "RFoot": ["r_foot"],
    "LHand": ["l_hand"],
    "RHand": ["r_hand"],
}
# The humanoid sprite's layer order (Entities/Mobs/Species/base.yml); a later part hides an earlier one.
DRAW_ORDER = ["Chest", "Head", "RArm", "LArm", "RLeg", "LLeg", "LFoot", "RFoot", "LHand", "RHand"]
ROT = {
    "Head": ["rot_head"],
    "Chest": ["rot_chest", "rot_groin"],
    "RArm": ["rot_r_arm"],
    "LArm": ["rot_l_arm"],
    "RHand": ["rot_r_hand"],
    "LHand": ["rot_l_hand"],
    "RLeg": ["rot_r_leg"],
    "LLeg": ["rot_l_leg"],
    "RFoot": ["rot_r_foot"],
    "LFoot": ["rot_l_foot"],
}
FACINGS = ["south", "north", "east", "west"]  # BYOND and RSI share this direction order.
GLYPH_CENTRE = (1, 25)  # The centre of the wound glyph in every bulletwound state.
PAIN_STATES = ["pain0", "pain1", "pain2", "pain3", "pain4", "pain5", "pain6", "pain7", "paindd"]


def load_dmi(path):
    """{state: {"dirs", "frames", "delay", "images": [frame][dir] -> Image}} of a .dmi."""
    image, desc = read_description(path)
    width, height, states = parse(desc)
    image = image.convert("RGBA")
    columns = image.width // width
    index = 0
    out = {}
    for state in states:
        images = []
        for frame in range(state["frames"]):
            row = []
            for direction in range(state["dirs"]):
                i = index + frame * state["dirs"] + direction
                x, y = (i % columns) * width, (i // columns) * height
                row.append(image.crop((x, y, x + width, y + height)))
            images.append(row)
        index += state["dirs"] * state["frames"]
        out[state["name"]] = dict(state, images=images)
    return out


def write_rsi(directory, copyright_text, states, keep=()):
    """
    states: [(name, dirs, frames[dir][frame] -> Image, delays[dir] -> [seconds] or None)]. A state named in keep
    goes into the meta but its png is left as it is.
    """
    os.makedirs(directory, exist_ok=True)
    meta_states = []
    for name, dirs, frames, delays in states:
        flat = [img for direction in range(dirs) for img in frames[direction]]
        columns = math.ceil(math.sqrt(len(flat)))
        rows = math.ceil(len(flat) / columns)
        sheet = Image.new("RGBA", (columns * FRAME, rows * FRAME), (0, 0, 0, 0))
        for i, img in enumerate(flat):
            sheet.paste(img, ((i % columns) * FRAME, (i // columns) * FRAME))
        if name not in keep:
            sheet.save(os.path.join(directory, name + ".png"), optimize=True)
        entry = {"name": name}
        if dirs != 1:
            entry["directions"] = dirs
        if delays is not None:
            # The RSI validator wants every direction to last the same; Bob's 2bullet south runs a tenth longer than
            # its other facings. The shorter directions hold their last frame for the difference.
            longest = max(sum(row) for row in delays)
            delays = [row[:-1] + [round(row[-1] + longest - sum(row), 3)] for row in delays]
            entry["delays"] = delays
        meta_states.append(entry)
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": copyright_text,
        "size": {"x": FRAME, "y": FRAME},
        "states": meta_states,
    }
    with open(os.path.join(directory, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")
    print(f"wrote {len(meta_states)} states to {directory}")


def seconds(delay, count):
    """BYOND tenths of a second to RSI seconds; a missing delay list is BYOND's default of one tenth."""
    return [round((delay[i] if delay and i < len(delay) else 1) / 10, 3) for i in range(count)]


# --- pain HUD ---

def backing():
    """
    The health alerts' backing: each row's commonest colour in the side margins, where bleed3's drop never
    reaches, keeping the source's transparent corners.
    """
    source = Image.open(BACKING).convert("RGBA").crop((0, 0, FRAME, FRAME))
    out = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    for y in range(FRAME):
        row = [source.getpixel((x, y)) for x in range(FRAME)]
        margin = [row[x] for x in (*range(1, 5), *range(FRAME - 5, FRAME - 1)) if row[x][3] > 0]
        mode = max(sorted(set(margin)), key=margin.count)
        for x in range(FRAME):
            if row[x][3] > 0:
                out.putpixel((x, y), mode)
    return out


def bordered(tile, colour=(220, 30, 30, 255)):
    """The tile with a two-pixel border in the colour along the backing's outer opaque edge."""
    out = tile.copy()
    box = out.getbbox()
    if box is None:
        return out
    draw = ImageDraw.Draw(out)
    x0, y0, x1, y1 = box[0], box[1], box[2] - 1, box[3] - 1
    draw.rectangle((x0, y0, x1, y1), outline=colour, width=2)
    return out


def build_pain(bob):
    dmi = load_dmi(os.path.join(bob, "hud", "screen_nigga.dmi"))
    base = backing()
    states = []
    for name in PAIN_STATES:
        state = dmi[name]
        frames = []
        for frame in range(state["frames"]):
            tile = base.copy()
            tile.alpha_composite(state["images"][frame][0])
            frames.append(tile)
        if name == "paindd":
            # Pain crit (the faint): the border flashes red fast, on every frame of the glyph's own blink.
            frames = [img for frame in frames for img in (bordered(frame), frame)]
            states.append((name, 1, [frames], [[0.12] * len(frames)]))
            continue
        states.append((name, 1, [frames], [seconds(state["delay"], state["frames"])]))
    # Downed by pain: the top glyph with the border flashing red, slower than the faint's.
    downed = states[PAIN_STATES.index("pain7")][2][0][0]
    states.insert(PAIN_STATES.index("paindd"), ("paindowned", 1, [[bordered(downed), downed]], [[0.4, 0.4]]))
    write_rsi(os.path.join(OUT, "Interface", "Alerts", "pain.rsi"),
              f"Pain glyphs from modular_septic/icons/hud/screen_nigga.dmi, {BOB_REPO}, composited onto the "
              "backing of Resources/Textures/_Crescent/Interface/Alerts/bleed.rsi (sprites provided by @_miket) "
              "by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py for Wolfgate (Wolfmed)",
              states)


# --- wound overlays ---

def load_masks(name):
    """The four direction masks of one human part sprite (a 2x2 sheet)."""
    image = Image.open(os.path.join(HUMAN, name + ".png")).convert("RGBA")
    masks = []
    for direction in range(4):
        left, top = (direction % 2) * FRAME, (direction // 2) * FRAME
        alpha = image.crop((left, top, left + FRAME, top + FRAME)).split()[3]
        masks.append({(x, y) for y in range(FRAME) for x in range(FRAME) if alpha.getpixel((x, y)) > 0})
    return masks


def part_masks():
    """Each part's own silhouette per direction, then the share of it no later part draws over."""
    own = {}
    for part, sources in PARTS.items():
        per_source = [load_masks(name) for name in sources]
        own[part] = [set.intersection(*(masks[d] for masks in per_source)) for d in range(4)]
    visible = {}
    for i, part in enumerate(DRAW_ORDER):
        visible[part] = []
        for d in range(4):
            covered = set().union(*(own[later][d] for later in DRAW_ORDER[i + 1:]))
            visible[part].append(own[part][d] - covered)
    return visible


def erode(mask):
    return {(x, y) for (x, y) in mask if {(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)} <= mask}


def anchor(mask):
    """The pixel of the part the glyph centres on: nearest the centroid of its eroded core, or None."""
    if len(mask) < 3:
        return None
    region = erode(mask) or mask
    cx = sum(x for x, _ in region) / len(region)
    cy = sum(y for _, y in region) / len(region)
    return min(region, key=lambda p: ((p[0] - cx) ** 2 + (p[1] - cy) ** 2, p[1], p[0]))


def grey(images):
    """Luminance to grey with alpha kept, normalised so the brightest pixel across the set is white."""
    def lum(c):
        return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]

    def pixels(img):
        return [img.getpixel((x, y)) for y in range(img.height) for x in range(img.width)]

    peak = max((lum(c) for img in images for c in pixels(img) if c[3] > 0), default=1) or 1
    out = []
    for img in images:
        g = Image.new("RGBA", img.size, (0, 0, 0, 0))
        for i, c in enumerate(pixels(img)):
            if c[3] > 0:
                v = min(255, round(lum(c) / peak * 255))
                g.putpixel((i % img.width, i // img.width), (v, v, v, c[3]))
        out.append(g)
    return out


def blood_mask(images):
    """A mask for art that is all blood: the strongest channel to grey, normalised so the reddest pixel across the
    set is white. Luminance is wrong here: pure reds sit near 30% and one orange highlight drags every red to black."""
    def pixels(img):
        return [img.getpixel((x, y)) for y in range(img.height) for x in range(img.width)]

    peak = max((max(c[:3]) for img in images for c in pixels(img) if c[3] > 0), default=1) or 1
    out = []
    for img in images:
        g = Image.new("RGBA", img.size, (0, 0, 0, 0))
        for i, c in enumerate(pixels(img)):
            if c[3] > 0:
                v = min(255, round(max(c[:3]) / peak * 255))
                g.putpixel((i % img.width, i // img.width), (v, v, v, c[3]))
        out.append(g)
    return out


def wound_glyphs(bob):
    """The bulletwound glyph frames, greyed in one pass so every variant shares one normalisation: (dmi, {(variant, dir, frame): image})."""
    dmi = load_dmi(os.path.join(bob, "mob", "human", "overlays", "bulletwound.dmi"))
    variants = ["bullet", "1bullet", "2bullet", "obullet"]
    keys = [(v, f, fr) for v in variants for f in range(4) for fr in range(dmi[f"{v}_{FACINGS[f]}"]["frames"])]
    return dmi, dict(zip(keys, grey([dmi[f"{v}_{FACINGS[f]}"]["images"][fr][f] for v, f, fr in keys])))


def build_wounds(bob):
    dmi, greyed = wound_glyphs(bob)

    visible = part_masks()
    uses = {  # state suffix -> source variant, per part
        "drip": lambda part: "bullet" if part in ("Chest", "Head") else "1bullet",
        "stream": lambda part: "2bullet",
        "old": lambda part: "obullet",
    }
    states = []
    for part in PARTS:
        anchors = [anchor(visible[part][d]) for d in range(4)]
        print(f"  {part}: glyph at {anchors}")
        for suffix, pick in uses.items():
            variant = pick(part)
            count = max(dmi[f"{variant}_{facing}"]["frames"] for facing in FACINGS)
            frames, delays = [], []
            for d, facing in enumerate(FACINGS):
                source = dmi[f"{variant}_{facing}"]
                row = []
                for fr in range(count):
                    tile = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
                    if anchors[d] is not None:
                        glyph = greyed[(variant, d, min(fr, source["frames"] - 1))]
                        tile.alpha_composite(glyph, (anchors[d][0] - GLYPH_CENTRE[0], anchors[d][1] - GLYPH_CENTRE[1]))
                    row.append(tile)
                frames.append(row)
                delays.append(seconds(source["delay"], count))
            states.append((f"{part}_{suffix}", 4, frames, delays if count > 1 else None))
    write_rsi(os.path.join(OUT, "Damage", "wounds.rsi"),
              f"Wound glyph from modular_septic/icons/mob/human/overlays/bulletwound.dmi, {BOB_REPO}, recoloured "
              "to grey and placed on the body parts of Resources/Textures/Mobs/Species/Human/parts.rsi "
              "(tgstation human_parts_greyscale.dmi, modified by DrSmugleaf) by "
              "Tools/_WF/Wolfmed/gen_wolfmed_overlays.py for Wolfgate (Wolfmed)",
              states)


# --- artery overlays ---

ARTERY_SITES = ["head", "neck", "r_arm", "l_arm", "r_hand", "l_hand", "r_leg", "l_leg", "r_foot", "l_foot"]
ARTERY_STATES = [f"{site}_artery{look}" for site in ARTERY_SITES for look in (0, 1)]


MIRROR = {"r_arm": "l_arm", "l_arm": "r_arm", "r_hand": "l_hand", "l_hand": "r_hand",
          "r_leg": "l_leg", "l_leg": "r_leg", "r_foot": "l_foot", "l_foot": "r_foot"}


def on_limb(tile, mask):
    """Whether any of the tile's pixels sits on the mask."""
    return any(tile.getpixel((x, y))[3] > 0 for (x, y) in mask)


def fix_limb_frames(site, frames):
    """
    Bob's sheets copy a frame or two onto the wrong side (l_foot north is r_foot's, r_arm west is its own east).
    frames: [dir] -> [frame images] of one limb site. A direction whose pixels sit on the mirror limb, or on no limb
    at all, is replaced by the mirror site's frames flipped, or blanked when those are empty on this limb too.
    """
    if site not in MIRROR:
        return frames
    own = load_masks(site)
    other = load_masks(MIRROR[site])
    fixed = []
    for d in range(4):
        row = frames[d]
        first = row[0]
        if first.getbbox() is None or on_limb(first, own[d]):
            fixed.append(row)
            continue
        flipped = [img.transpose(Image.FLIP_LEFT_RIGHT) for img in row]
        if on_limb(first, other[d]) and on_limb(flipped[0], own[d]):
            print(f"  {site} dir {d}: on the {MIRROR[site]}, mirrored")
            fixed.append(flipped)
        else:
            print(f"  {site} dir {d}: on no limb, blanked")
            fixed.append([Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0)) for _ in row])
    return fixed


def build_artery(bob):
    dmi = load_dmi(os.path.join(bob, "mob", "human", "overlays", "artery.dmi"))
    for name in ARTERY_STATES:
        site = name[:-len("_artery0")]
        by_dir = [[dmi[name]["images"][fr][d] for fr in range(dmi[name]["frames"])] for d in range(4)]
        by_dir = fix_limb_frames(site, by_dir)
        for fr in range(dmi[name]["frames"]):
            for d in range(4):
                dmi[name]["images"][fr][d] = by_dir[d][fr]
    keys = [(name, fr, d) for name in ARTERY_STATES for fr in range(dmi[name]["frames"]) for d in range(4)]
    greyed = dict(zip(keys, blood_mask([dmi[name]["images"][fr][d] for name, fr, d in keys])))
    states = []
    for name in ARTERY_STATES:
        source = dmi[name]
        frames = [[greyed[(name, fr, d)] for fr in range(source["frames"])] for d in range(4)]
        delays = [seconds(source["delay"], source["frames"]) for _ in range(4)] if source["frames"] > 1 else None
        states.append((name, 4, frames, delays))
    write_rsi(os.path.join(OUT, "Damage", "artery.rsi"),
              f"<site>_artery0/1 from modular_septic/icons/mob/human/overlays/artery.dmi, {BOB_REPO}, "
              "recoloured to grey by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py for Wolfgate (Wolfmed)",
              states)


# --- stump overlays ---

STUMP_SITES = ["neck", "r_arm", "l_arm", "r_hand", "l_hand", "r_leg", "l_leg", "r_foot", "l_foot"]


def is_flesh(c):
    """The reds are flesh; the greys (the bone) and the near-black outline are not."""
    r, g, b = c[:3]
    return r > 60 and r > g * 2 and r > b * 2


def split_stump(tile):
    """(flesh pixels as an image, everything else as an image) of one stump frame."""
    flesh = Image.new("RGBA", tile.size, (0, 0, 0, 0))
    bone = Image.new("RGBA", tile.size, (0, 0, 0, 0))
    for y in range(tile.height):
        for x in range(tile.width):
            c = tile.getpixel((x, y))
            if c[3] == 0:
                continue
            (flesh if is_flesh(c) else bone).putpixel((x, y), c)
    return flesh, bone


def stump_anchor(flesh):
    """Where a stump's drip hangs from: the middle of its lowest flesh row, or None for an empty frame."""
    pixels = [(x, y) for y in range(flesh.height) for x in range(flesh.width) if flesh.getpixel((x, y))[3] > 0]
    if not pixels:
        return None
    bottom = max(y for _, y in pixels)
    row = [x for x, y in pixels if y == bottom]
    return (round(sum(row) / len(row)), bottom)


def build_stumps(nevado, bob):
    dmi = load_dmi(os.path.join(nevado, "mob", "human", "overlays", "stump.dmi"))
    glyph_dmi, glyphs = wound_glyphs(bob)
    tiles = {site: fix_limb_frames(site, [[dmi[f"stump_{site}"]["images"][0][d]] for d in range(4)]) for site in STUMP_SITES}
    split = {(site, d): split_stump(tiles[site][d][0]) for site in STUMP_SITES for d in range(4)}
    masks = dict(zip(split.keys(), blood_mask([flesh for flesh, _ in split.values()])))
    states = []
    for site in STUMP_SITES:
        states.append((f"{site}_stump", 4, [[masks[(site, d)]] for d in range(4)], None))
        states.append((f"{site}_stump_bone", 4, [[split[(site, d)][1]] for d in range(4)], None))
        anchors = [stump_anchor(split[(site, d)][0]) for d in range(4)]
        print(f"  {site}: drip at {anchors}")
        for suffix, variant in (("drip", "bullet"), ("stream", "2bullet")):
            count = max(glyph_dmi[f"{variant}_{facing}"]["frames"] for facing in FACINGS)
            frames, delays = [], []
            for d, facing in enumerate(FACINGS):
                source = glyph_dmi[f"{variant}_{facing}"]
                row = []
                for fr in range(count):
                    tile = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
                    if anchors[d] is not None:
                        glyph = glyphs[(variant, d, min(fr, source["frames"] - 1))]
                        tile.alpha_composite(glyph, (anchors[d][0] - GLYPH_CENTRE[0], anchors[d][1] - GLYPH_CENTRE[1]))
                    row.append(tile)
                frames.append(row)
                delays.append(seconds(source["delay"], count))
            states.append((f"{site}_stump_{suffix}", 4, frames, delays if count > 1 else None))
    write_rsi(os.path.join(OUT, "Damage", "stumps.rsi"),
              f"Stumps from modular_septic/icons/mob/human/overlays/stump.dmi, {NEVADO_REPO}, split into a blood mask "
              f"and the bone; the drip and trickle glyphs from modular_septic/icons/mob/human/overlays/bulletwound.dmi, "
              f"{BOB_REPO}, hung from each stump, by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py for Wolfgate (Wolfmed)",
              states)


# --- rot overlays ---

def rot_frames(dmi, sources, d, count):
    rows = []
    for fr in range(count):
        tile = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
        for source in sources:
            tile.alpha_composite(dmi[source]["images"][fr][d])
        rows.append(tile)
    return rows


def on_part(tile, part, d):
    """(pixels of the frame on the human part, pixels in the frame)."""
    art = {(x, y) for y in range(FRAME) for x in range(FRAME) if tile.getpixel((x, y))[3] > 0}
    own = set.intersection(*(load_masks(n)[d] for n in PARTS[part]))
    return len(art & own), len(art)


def build_rot(bob):
    dmi = load_dmi(os.path.join(bob, "mob", "human", "species", "dead", "rot_parts.dmi"))
    states = []
    for part, sources in ROT.items():
        count = dmi[sources[0]]["frames"]
        frames = []
        for d in range(4):
            row = rot_frames(dmi, sources, d, count)
            hit, total = on_part(row[0], part, d)
            if total and not hit and part[0] in "LR":
                # rot_r_hand's north frame is a copy of its south one, on the wrong side of the body: use the
                # other hand's north frame mirrored instead.
                other = ROT[("R" if part[0] == "L" else "L") + part[1:]]
                row = [tile.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for tile in rot_frames(dmi, other, d, count)]
                hit, total = on_part(row[0], part, d)
                print(f"  {part}_rot dir {d}: off the part in the source, mirrored from the other side")
            if total:
                print(f"  {part}_rot dir {d}: {hit}/{total} pixels on the human {part}")
            frames.append(row)
        states.append((f"{part}_rot", 4, frames, [seconds(dmi[sources[0]]["delay"], count)] * 4))
    write_rsi(os.path.join(OUT, "Damage", "rot.rsi"),
              f"modular_septic/icons/mob/human/species/dead/rot_parts.dmi, {BOB_REPO}; rot_chest and rot_groin "
              "composited into Chest_rot by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py for Wolfgate (Wolfmed)",
              states)


# --- sepsis alert ---

def build_sepsis():
    """INFECTION: septic shock, the sepsis icon with paindowned's border flashing red at paindowned's 0.4 s."""
    directory = os.path.join(OUT, "Interface", "Alerts", "sepsis.rsi")
    sepsis = Image.open(os.path.join(directory, "sepsis.png")).convert("RGBA").crop((0, 0, FRAME, FRAME))
    write_rsi(directory,
              "Made for Wolfgate (Wolfmed); septicshock bordered by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py",
              [("sepsis", 1, [[sepsis]], None),
               ("septicshock", 1, [[bordered(sepsis), sepsis]], [[0.4, 0.4]])],
              keep=("sepsis",))


DOWNED_COLOURS = {
    "light": (246, 246, 246, 255),
    "main": (214, 214, 214, 255),
    "shade": (138, 138, 138, 255),
    "edge": (58, 58, 58, 255),
}


def chevron(image, top, colours):
    """One downward chevron, three pixels thick with a lit top row, a shaded bottom row and a dark edge under it."""
    for dx in range(9):
        y = top + round(dx * 0.75)
        for x in (7 + dx, 25 - dx):
            image.putpixel((x, y), colours["light"])
            image.putpixel((x, y + 1), colours["main"])
            image.putpixel((x, y + 2), colours["shade"])
            image.putpixel((x, y + 3), colours["edge"])


def build_downed():
    """Playtest 5: the Downed alert, two arrows pointing down on the health alerts' backing."""
    tile = backing()
    chevron(tile, 5, DOWNED_COLOURS)
    chevron(tile, 15, DOWNED_COLOURS)
    write_rsi(os.path.join(OUT, "Interface", "Alerts", "downed.rsi"),
              "Made for Wolfgate (Wolfmed) by Tools/_WF/Wolfmed/gen_wolfmed_overlays.py: two arrows down on the "
              "Crescent health alerts' backing",
              [("downed", 1, [[tile]], None)])


def main():
    if "--sepsis-only" in sys.argv or "--hud-only" in sys.argv:
        build_sepsis()
        build_downed()
        return
    bob = DEFAULT_BOB
    if "--bob" in sys.argv:
        bob = sys.argv[sys.argv.index("--bob") + 1]
    nevado = DEFAULT_NEVADO
    if "--nevado" in sys.argv:
        nevado = sys.argv[sys.argv.index("--nevado") + 1]
    if not bob or not nevado:
        sys.exit("pass --bob <bobstation modular_septic/icons> and --nevado <EscapeFromNevado modular_septic/icons>, or set BOB_ICONS and NEVADO_ICONS")
    build_pain(bob)
    build_wounds(bob)
    build_rot(bob)
    build_artery(bob)
    build_stumps(nevado, bob)
    build_sepsis()
    build_downed()


if __name__ == "__main__":
    main()
