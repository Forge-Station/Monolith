"""Minimal DMI / RSI reader. DMI: PNG + zTXt 'Description' metadata. Frames are
laid out row-major; order inside a state is frame-major with directions inner."""
import json, os
from PIL import Image

def read_dmi_meta(path):
    im = Image.open(path)
    desc = im.info.get("Description")
    if desc is None:
        # zTXt might be under text dict
        desc = getattr(im, "text", {}).get("Description")
    return im, desc

def parse_desc(desc):
    lines = desc.splitlines()
    width = height = 32
    states = []
    cur = None
    for ln in lines:
        s = ln.strip()
        if s.startswith("#") or not s:
            continue
        k, _, v = s.partition("=")
        k = k.strip(); v = v.strip()
        if k == "state":
            cur = {"name": json.loads(v) if v.startswith('"') else v, "dirs": 1, "frames": 1}
            states.append(cur)
        elif cur is None:
            if k == "width": width = int(v)
            elif k == "height": height = int(v)
        else:
            if k in ("dirs", "frames", "loop", "rewind", "movement"):
                cur[k] = int(v)
            elif k == "delay":
                cur[k] = [float(x) for x in v.split(",")]
            elif k == "hotspot":
                cur[k] = v
            else:
                cur[k] = v
    return width, height, states

def load_dmi(path):
    """Returns dict: name -> {'dirs','frames','delay', 'img': [frame][dir] -> RGBA Image}; also ordered list."""
    im, desc = read_dmi_meta(path)
    w, h, states = parse_desc(desc)
    im = im.convert("RGBA")
    cols = im.width // w
    idx = 0
    out = []
    for st in states:
        frames = []
        for f in range(st["frames"]):
            dirs = []
            for d in range(st["dirs"]):
                x = (idx % cols) * w; y = (idx // cols) * h
                dirs.append(im.crop((x, y, x + w, y + h)))
                idx += 1
            frames.append(dirs)
        st = dict(st); st["img"] = frames
        out.append(st)
    return w, h, out

def load_rsi_state(rsi_dir, state):
    meta = json.load(open(os.path.join(rsi_dir, "meta.json"), encoding="utf-8-sig"))
    w, h = meta["size"]["x"], meta["size"]["y"]
    st = next(s for s in meta["states"] if s["name"] == state)
    nd = st.get("directions", 1)
    delays = st.get("delays")
    nf = len(delays[0]) if delays else 1
    im = Image.open(os.path.join(rsi_dir, state + ".png")).convert("RGBA")
    cols = im.width // w
    # RSI: direction-major, frames inner
    res = [[None] * nd for _ in range(nf)]
    idx = 0
    for d in range(nd):
        for f in range(nf):
            x = (idx % cols) * w; y = (idx // cols) * h
            res[f][d] = im.crop((x, y, x + w, y + h))
            idx += 1
    return res  # [frame][dir]

