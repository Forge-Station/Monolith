"""Recolours of existing mob RSIs for the cave life of the Underkarst, the Cinder Vaults and the Sandstone Galleries.

Each output is `Resources/Textures/_WF/Caverns/Life/<world>/<name>.rsi`: every state of the source RSI mapped through a
hue/saturation/value change, glow states included. Run from the repo root:

    python Tools/_WF/Caverns/gen_life_karst_cinder_sandstone.py
"""
import colorsys
import json
import os
import shutil

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
TEX = os.path.join(ROOT, "Resources", "Textures")
OUT = os.path.join(TEX, "_WF", "Caverns", "Life")

# world: name: (source rsi, body change, glow change or None, note)
# A change is a dict: hue (set, 0-1), shift (added to hue), s_mul, s_add, v_mul, v_add.
MOBS = {
    "asclepiu": {
        "pale_bat": ("Mobs/Animals/bat.rsi", dict(hue=0.56, s_mul=0.3, v_mul=1.15, v_add=0.12), None, "blind pale cave bat"),
        "cave_frog": ("Mobs/Animals/frog.rsi", dict(hue=0.07, s_mul=0.25, v_mul=0.9, v_add=0.4), None, "blind cave frog"),
        "cave_crab": ("Mobs/Animals/crab.rsi", dict(hue=0.1, s_mul=0.15, v_mul=1.15, v_add=0.15), None, "chalk-white cave crab"),
        "cave_spider": ("Mobs/Animals/spider.rsi", dict(hue=0.6, s_mul=0.2, v_mul=1.1, v_add=0.12), None, "pale cave spider"),
    },
    "fervidus": {
        "cinder_spider": ("Mobs/Animals/spacespider.rsi", dict(hue=0.03, s_mul=0.9, v_mul=0.55, v_add=0.04),
                          dict(hue=0.07, s_mul=1.0, v_mul=1.0), "cinder spider"),
        "ember_viper": ("Mobs/Animals/spacecobra.rsi", dict(hue=0.02, s_mul=0.85, v_mul=0.5, v_add=0.03),
                        dict(hue=0.08, s_mul=1.0, v_mul=1.0), "ember viper"),
        "slag_tick": ("Mobs/Aliens/Xenos/spacetick.rsi", dict(hue=0.04, s_mul=0.7, v_mul=0.55, v_add=0.05), None, "slag tick"),
        "sulphur_carp": ("Mobs/Aliens/Carps/space.rsi", dict(hue=0.15, s_mul=0.85, v_mul=0.95, v_add=0.04),
                         dict(hue=0.05, s_mul=1.0, v_mul=1.0), "sulphur carp"),
    },
    "merak": {
        "pallid_viper": ("Mobs/Animals/spacecobra.rsi", dict(hue=0.12, s_mul=0.3, v_mul=1.2, v_add=0.2),
                         dict(hue=0.12, s_mul=0.8, v_mul=1.0), "pallid sandviper"),
        "dust_bat": ("Mobs/Animals/bat.rsi", dict(hue=0.09, s_mul=0.6, v_mul=1.2, v_add=0.1), None, "dust bat"),
        "gallery_crab": ("Mobs/Animals/crab.rsi", dict(hue=0.11, s_mul=0.75, v_mul=1.0, v_add=0.08), None, "gallery crab"),
        "crypt_tick": ("Mobs/Aliens/Xenos/spacetick.rsi", dict(hue=0.13, s_mul=0.5, v_mul=1.15, v_add=0.2), None, "crypt tick"),
    },
}


def recolour(image, change):
    image = image.convert("RGBA")
    px = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = px[x, y]
            if not a:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if "hue" in change:
                h = change["hue"]
            h = (h + change.get("shift", 0)) % 1
            s = min(1.0, s * change.get("s_mul", 1) + change.get("s_add", 0))
            v = min(1.0, v * change.get("v_mul", 1) + change.get("v_add", 0))
            r, g, b = colorsys.hsv_to_rgb(h, s, v)
            px[x, y] = (round(r * 255), round(g * 255), round(b * 255), a)
    return image


def build(world, name, source, body, glow, note):
    src = os.path.join(TEX, source)
    dst = os.path.join(OUT, world, name + ".rsi")
    os.makedirs(dst, exist_ok=True)
    with open(os.path.join(src, "meta.json"), encoding="utf-8-sig") as f:
        meta = json.load(f)
    for state in meta["states"]:
        change = glow if glow is not None and ("glow" in state["name"] or "mouth" in state["name"]) else body
        recolour(Image.open(os.path.join(src, state["name"] + ".png")), change).save(os.path.join(dst, state["name"] + ".png"))
    meta["copyright"] = f"{meta['copyright']}; recoloured for Wolfgate ({note}) by Tools/_WF/Caverns/gen_life_karst_cinder_sandstone.py"
    with open(os.path.join(dst, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    for world, mobs in MOBS.items():
        shutil.rmtree(os.path.join(OUT, world), ignore_errors=True)
        for name, (source, body, glow, note) in mobs.items():
            build(world, name, source, body, glow, note)
