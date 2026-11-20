#!/usr/bin/env python3
"""Generates the Wolfmed part_degradation RSI: per-limb wound overlays for V3.

Every state is drawn through the matching human body-part sprite's own alpha as a
mask, so an overlay can never paint outside the limb it belongs to. The mask is
then eroded and the patch budget capped, which keeps each overlay to a small
central blob that still sits inside the limbs of species with a different
silhouette (the masks are human; no per-species art is generated).

Five variants per limb:
  muscle   organic stage 1, torn skin over red muscle
  bone     organic stage 2, the same wound opened to off-white bone
  strut    mechanical stage 1, a peeled panel over gunmetal structure
  wire     mechanical stage 2, structure plus copper wiring
  necrotic dead tissue, a dark mottled wash over the whole eroded limb

Deterministic: every blob comes from random.Random seeded with the state name and
direction, so re-running the script reproduces the RSI byte for byte.

Usage: python Tools/_WF/Wolfmed/gen_part_degradation_rsi.py
Reads  Resources/Textures/Mobs/Species/Human/parts.rsi/
Writes Resources/Textures/_WF/Wolfmed/Effects/part_degradation.rsi/
"""

import json
import os
import random

from PIL import Image

FRAME = 32
DIRECTIONS = 4

ROOT = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
SRC_DIR = os.path.join(ROOT, "Resources", "Textures", "Mobs", "Species", "Human", "parts.rsi")
OUT_DIR = os.path.join(ROOT, "Resources", "Textures", "_WF", "Wolfmed", "Effects", "part_degradation.rsi")

# Kept verbatim from the source RSI's meta.json; the overlays are traced from its alpha.
SRC_COPYRIGHT = ("https://github.com/tgstation/tgstation/blob/"
                 "8024397cc81c5f47f74cf4279e35728487d0a1a7/icons/mob/human_parts_greyscale.dmi "
                 "and modified by DrSmugleaf")
COPYRIGHT = ("Derived from Resources/Textures/Mobs/Species/Human/parts.rsi ("
             + SRC_COPYRIGHT
             + "), masked and redrawn for Wolfgate (Wolfmed) by Tools/_WF/Wolfmed/gen_part_degradation_rsi.py")

# HumanoidVisualLayers name -> the source sprites whose alpha is intersected for the mask.
LAYERS = {
    "Chest": ["torso_m", "torso_f"],
    "Head": ["head_m", "head_f"],
    "LArm": ["l_arm"],
    "RArm": ["r_arm"],
    "LHand": ["l_hand"],
    "RHand": ["r_hand"],
    "LLeg": ["l_leg"],
    "RLeg": ["r_leg"],
    "LFoot": ["l_foot"],
    "RFoot": ["r_foot"],
}

MUSCLE = [(143, 29, 29, 255), (184, 49, 43, 255), (92, 20, 20, 255)]
BONE = [(232, 227, 210, 255), (194, 187, 166, 255)]
STRUT = [(110, 117, 124, 255), (69, 75, 80, 255), (153, 161, 168, 255)]
WIRE = [(181, 101, 29, 255), (217, 140, 58, 255), (47, 58, 68, 255)]
NECROSIS = [(59, 42, 63, 178), (34, 24, 38, 194), (85, 56, 74, 165)]

# Share of the eroded mask a stage covers, and the hard pixel cap that keeps big
# parts (torso) from turning into a solid slab of red.
BUDGETS = {
    "muscle": (0.40, 14),
    "bone": (0.55, 18),
    "strut": (0.40, 14),
    "wire": (0.55, 18),
}
# Accent pixels (bone slivers, copper wire) drawn inside the stage-2 patch.
ACCENTS = {"bone": (0.35, 6), "wire": (0.35, 6)}


def load_frames(name):
    """The four direction frames of one source state, as alpha masks."""
    image = Image.open(os.path.join(SRC_DIR, name + ".png")).convert("RGBA")
    frames = []
    for top in range(0, image.height, FRAME):
        for left in range(0, image.width, FRAME):
            alpha = image.crop((left, top, left + FRAME, top + FRAME)).split()[3]
            frames.append({(x, y) for y in range(FRAME) for x in range(FRAME)
                           if alpha.getpixel((x, y)) > 0})
    return frames[:DIRECTIONS]


def layer_masks(sources):
    """One mask per direction: the intersection of every source sprite's alpha."""
    per_source = [load_frames(name) for name in sources]
    masks = []
    for direction in range(DIRECTIONS):
        mask = set(per_source[0][direction])
        for other in per_source[1:]:
            mask &= other[direction]
        masks.append(mask)
    return masks


def erode(mask):
    """Drops every pixel missing a four-neighbour, so patches pull away from the silhouette."""
    return {(x, y) for (x, y) in mask
            if {(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)} <= mask}


def core_of(mask):
    """The drawable region: eroded where that leaves enough pixels, the raw mask on tiny limbs."""
    inner = erode(mask)
    if len(inner) >= 4:
        deeper = erode(inner)
        return deeper if len(deeper) >= 6 else inner
    return mask


def centre(pixels):
    return (sum(x for x, _ in pixels) / len(pixels), sum(y for _, y in pixels) / len(pixels))


def grow(rng, region, budget):
    """A jagged blob inside region: flood out from the most central pixel, skipping at random."""
    if not region or budget < 1:
        return set()

    cx, cy = centre(region)
    ordered = sorted(region, key=lambda p: ((p[0] - cx) ** 2 + (p[1] - cy) ** 2, p))
    blob = {ordered[0]}
    frontier = [ordered[0]]
    while frontier and len(blob) < budget:
        x, y = frontier.pop(rng.randrange(len(frontier)))
        neighbours = [(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1),
                      (x - 1, y - 1), (x + 1, y + 1)]
        rng.shuffle(neighbours)
        for pixel in neighbours:
            if len(blob) >= budget:
                break
            if pixel in region and pixel not in blob and rng.random() < 0.72:
                blob.add(pixel)
                frontier.append(pixel)

    # A blob that stalled before spending its budget restarts from the nearest unused pixel.
    if len(blob) < budget:
        for pixel in ordered:
            if len(blob) >= budget:
                break
            if pixel not in blob and any(n in blob for n in
                                         [(pixel[0] - 1, pixel[1]), (pixel[0] + 1, pixel[1]),
                                          (pixel[0], pixel[1] - 1), (pixel[0], pixel[1] + 1)]):
                blob.add(pixel)
    return blob


def budget_for(variant, region):
    share, cap = BUDGETS[variant]
    return max(1, min(cap, int(round(len(region) * share))))


def paint(pixels, palette, rng, weights):
    """Assigns each pixel a palette entry; the weights are what makes the patch read as jagged."""
    return {pixel: palette[rng.choices(range(len(palette)), weights=weights)[0]]
            for pixel in sorted(pixels)}


def draw_state(variant, mask, direction, seed_name):
    """The pixel dictionary for one state and one direction."""
    if not mask:
        return {}

    rng = random.Random(f"{seed_name}:{variant}:{direction}")
    region = core_of(mask)
    if not region:
        return {}

    if variant == "necrotic":
        # Discolouration, not a hole: the whole eroded limb goes dark and mottled.
        return paint(region, NECROSIS, rng, [5, 3, 2])

    if variant in ("muscle", "bone"):
        base, palette, weights = "muscle", MUSCLE, [4, 2, 3]
    else:
        base, palette, weights = "strut", STRUT, [4, 3, 2]

    blob = grow(rng, region, budget_for(variant, region))
    pixels = paint(blob, palette, rng, weights)

    if variant in ACCENTS:
        share, cap = ACCENTS[variant]
        inner = sorted(blob, key=lambda p: (p[1], p[0]))
        count = max(1, min(cap, int(round(len(blob) * share))))
        accent_palette = BONE if variant == "bone" else WIRE
        for pixel in rng.sample(inner, min(count, len(inner))):
            pixels[pixel] = accent_palette[rng.randrange(len(accent_palette))]

    return pixels


def write_state(name, masks):
    """Writes one state's PNG: four 32x32 direction frames in a 2x2 grid, as the source RSI uses."""
    variant = name.split("_")[1]
    sheet = Image.new("RGBA", (FRAME * 2, FRAME * 2), (0, 0, 0, 0))
    for direction, mask in enumerate(masks):
        frame = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
        for (x, y), colour in draw_state(variant, mask, direction, name).items():
            frame.putpixel((x, y), colour)
        sheet.paste(frame, ((direction % 2) * FRAME, (direction // 2) * FRAME))
    sheet.save(os.path.join(OUT_DIR, name + ".png"), optimize=True)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    states = []
    for layer, sources in LAYERS.items():
        masks = layer_masks(sources)
        for variant in ("muscle", "bone", "strut", "wire", "necrotic"):
            name = f"{layer}_{variant}"
            write_state(name, masks)
            states.append({"name": name, "directions": DIRECTIONS})

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": COPYRIGHT,
        "size": {"x": FRAME, "y": FRAME},
        "states": states,
    }
    with open(os.path.join(OUT_DIR, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")
    print(f"wrote {len(states)} states to {OUT_DIR}")


if __name__ == "__main__":
    main()
