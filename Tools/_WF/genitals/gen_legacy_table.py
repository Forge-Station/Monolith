"""Generates the legacy genital marking decode table and its golden test fixture from genitals.yml.

Run from anywhere:  python Tools/_WF/genitals/gen_legacy_table.py [--ref <git ref>]
Reads Resources/Prototypes/Entities/Mobs/Customization/Markings/genitals.yml, or with --ref that file at a git ref, and
decodes every Genital-category marking from its sprite state, not its id. S12 deleted the markings, so the output is
frozen: reproduce it with --ref c09f236deb (any commit from before the deletion). Writes:
  Content.Shared/_WF/Genitals/Migration/LegacyGenitalMarkings.Table.g.cs  (id -> LegacyEntry)
  Content.IntegrationTests/Tests/_WF/Genitals/LegacyGenitalGolden.g.cs    (id -> sprite state -> expected entry)
Skin-tone states (_s / -s) get a BakedColor: the mean colour of their opaque pixels over the first frame of every
direction. Shape ids come from the legacyToken fields of Resources/Prototypes/_WF/Genitals/shapes_*.yml.
Output uses CRLF and a file is only rewritten when its content changes, so a second run is a no-op.
"""
import argparse
import json
import os
import re
import subprocess
import sys

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
MARKINGS_REL = "Resources/Prototypes/Entities/Mobs/Customization/Markings/genitals.yml"
SHAPES_DIR = os.path.join(ROOT, "Resources", "Prototypes", "_WF", "Genitals")
TEXTURES = os.path.join(ROOT, "Resources", "Textures")
TABLE_OUT = os.path.join(ROOT, "Content.Shared", "_WF", "Genitals", "Migration", "LegacyGenitalMarkings.Table.g.cs")
GOLDEN_OUT = os.path.join(ROOT, "Content.IntegrationTests", "Tests", "_WF", "Genitals", "LegacyGenitalGolden.g.cs")
NEWLINE = "\r\n"

BREAST_CUPS = "abcdefghijklmno"

# Off-scheme states: amputated removes the penis; the BRC art is a size-4 knotted penis.
AMPUTATED_STATE = "amputated_FRONT"
SPECIAL_STATES = {
    "BRC-Knotted": ("Penis", "knotted", 4),
}

# (regex, slot, shape token or None for a group, step group, skin-tone group, sheathed)
PATTERNS = [
    (re.compile(r"^testicles_single_(\d)(-s)?_[01]_FRONT$"), "Testicles", "single", 1, 2, False),
    (re.compile(r"^testicles_sheath_(\d)_[01]_FRONT$"), "Testicles", "single", 1, None, True),
    (re.compile(r"^penis_([a-z]+)_(\d)(_s)?_[01]_FRONT$"), "Penis", None, 2, 3, False),
    (re.compile(r"^vagina_([a-z]+)_1(_s)?_[01]_FRONT$"), "Vagina", None, None, 2, False),
    (re.compile(r"^breasts_([a-z]+)_([a-o])_0_FRONT$"), "Breasts", None, 2, None, False),
]


class Entry:
    def __init__(self, marking_id, state, slot, shape, step, baked, sheathed, removes_penis=False):
        self.marking_id = marking_id
        self.state = state
        self.slot = slot
        self.shape = shape
        self.step = step
        self.baked = baked
        self.sheathed = sheathed
        self.removes_penis = removes_penis


def read_markings_text(ref):
    if ref is None:
        with open(os.path.join(ROOT, *MARKINGS_REL.split("/")), encoding="utf-8-sig") as f:
            return f.read()
    result = subprocess.run(["git", "-C", ROOT, "show", f"{ref}:{MARKINGS_REL}"], capture_output=True)
    if result.returncode != 0:
        sys.exit(f"git show {ref}:{MARKINGS_REL} failed: {result.stderr.decode(errors='replace').strip()}")
    return result.stdout.decode("utf-8-sig")


def parse_markings(text):
    """Top-level id, markingCategory and sprites of every marking. The file's layout is regular, so no YAML library."""
    markings, cur, in_sprites = [], None, False
    for raw in text.splitlines():
        line = raw.rstrip()
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith("- type:"):
            cur = {"sprites": []}
            markings.append(cur)
            in_sprites = False
            continue
        if cur is None:
            continue
        top = re.match(r"^  ([A-Za-z]\w*):\s*(.*)$", line)
        if top:
            key, value = top.groups()
            in_sprites = key == "sprites"
            if key in ("id", "markingCategory"):
                cur[key] = value.strip().strip("'\"")
            continue
        if in_sprites:
            sub = re.match(r"^\s*(-\s+)?(sprite|state):\s*(.+)$", line)
            if sub:
                _, key, value = sub.groups()
                value = value.strip().strip("'\"")
                if key == "sprite":
                    cur["sprites"].append({"sprite": value})
                elif cur["sprites"]:
                    cur["sprites"][-1]["state"] = value
    return markings


def parse_shape_tokens():
    """(slot, legacyToken) -> shape id, from the generated and hand-written shape prototypes."""
    tokens = {}
    for name in sorted(os.listdir(SHAPES_DIR)):
        if not (name.startswith("shapes_") and name.endswith(".yml")):
            continue
        with open(os.path.join(SHAPES_DIR, name), encoding="utf-8-sig") as f:
            blocks = re.split(r"(?m)^- type:", f.read())
        for block in blocks:
            fields = dict(re.findall(r"(?m)^  (id|slot|legacyToken):\s*(\S+)", block))
            if "legacyToken" in fields:
                tokens[(fields["slot"], fields["legacyToken"])] = fields["id"]
    return tokens


def baked_color(sprite, state):
    """Mean colour of the opaque pixels over the first frame of every direction, as #RRGGBB."""
    rsi = os.path.join(TEXTURES, *sprite.split("/"))
    with open(os.path.join(rsi, "meta.json"), encoding="utf-8-sig") as f:
        meta = json.load(f)
    info = next(s for s in meta["states"] if s["name"] == state)
    width, height = meta["size"]["x"], meta["size"]["y"]
    directions = info.get("directions", 1)
    delays = info.get("delays") or [[1.0]] * directions
    image = Image.open(os.path.join(rsi, state + ".png")).convert("RGBA")
    columns = image.width // width
    total, count, index = [0, 0, 0], 0, 0
    for direction in range(directions):
        # RSI frames are direction-major: every frame of direction 0, then direction 1, ...
        x, y = (index % columns) * width, (index // columns) * height
        pixels = image.crop((x, y, x + width, y + height)).tobytes()
        for i in range(0, len(pixels), 4):
            if pixels[i + 3] == 0:
                continue
            total[0] += pixels[i]
            total[1] += pixels[i + 1]
            total[2] += pixels[i + 2]
            count += 1
        index += len(delays[direction]) if direction < len(delays) else 1
    if count == 0:
        sys.exit(f"{state} has no opaque pixels")
    return "#" + "".join(f"{round(c / count):02X}" for c in total)


def shape_for(tokens, slot, token, marking_id):
    shape = tokens.get((slot, token))
    if shape is None:
        sys.exit(f"{marking_id}: no {slot} shape has legacyToken '{token}'")
    return shape


def decode(marking, tokens):
    marking_id = marking["id"]
    if not marking["sprites"] or "state" not in marking["sprites"][0]:
        sys.exit(f"{marking_id}: no sprite state")
    sprite = marking["sprites"][0]["sprite"]
    state = marking["sprites"][0]["state"]

    if state == AMPUTATED_STATE:
        return Entry(marking_id, state, "Penis", "", 0, None, False, removes_penis=True)
    if state in SPECIAL_STATES:
        slot, token, step = SPECIAL_STATES[state]
        return Entry(marking_id, state, slot, shape_for(tokens, slot, token, marking_id), step, None, False)

    for regex, slot, fixed_token, step_group, skin_group, sheathed in PATTERNS:
        match = regex.match(state)
        if not match:
            continue
        token = fixed_token or match.group(1)
        if slot == "Breasts":
            step = BREAST_CUPS.index(match.group(step_group)) + 1
        elif step_group is None:
            step = 1
        else:
            step = int(match.group(step_group))
        skin = skin_group is not None and match.group(skin_group) is not None
        baked = baked_color(sprite, state) if skin else None
        return Entry(marking_id, state, slot, shape_for(tokens, slot, token, marking_id), step, baked, sheathed)

    sys.exit(f"{marking_id}: unrecognised state '{state}'; extend PATTERNS or SPECIAL_STATES")


def cs_bool(value):
    return "true" if value else "false"


def table_file(entries):
    lines = [
        f"// Generated by Tools/_WF/genitals/gen_legacy_table.py from genitals.yml ({len(entries)} entries). Do not edit.",
        "// Frozen: the genital markings have been deleted from genitals.yml, so the generator only reproduces this file with",
        "// --ref at a commit from before their deletion (e.g. c09f236deb). LegacyGenitalMarkingsTest checks it against LegacyGenitalGolden.g.cs.",
        "",
        "namespace Content.Shared._WF.Genitals.Migration;",
        "",
        "public static partial class LegacyGenitalMarkings",
        "{",
        "    private static readonly Dictionary<string, LegacyEntry> Table = new()",
        "    {",
    ]
    for e in entries:
        if e.removes_penis:
            value = "LegacyEntry.PenisRemoved"
        else:
            baked = f"Color.FromHex(\"{e.baked}\")" if e.baked else "null"
            value = (f"new(GenitalSlot.{e.slot}, \"{e.shape}\", {e.step}, "
                     f"BakedColor: {baked}, Sheathed: {cs_bool(e.sheathed)})")
        lines.append(f"        [\"{e.marking_id}\"] = {value},")
    lines += ["    };", "}", ""]
    return NEWLINE.join(lines)


def golden_file(entries):
    lines = [
        f"// Generated by Tools/_WF/genitals/gen_legacy_table.py from genitals.yml ({len(entries)} entries). Do not edit.",
        "#nullable enable",
        "using Content.Shared._WF.Genitals;",
        "",
        "namespace Content.IntegrationTests.Tests._WF.Genitals;",
        "",
        "/// <summary>Golden fixture for the legacy migration table: marking id, its sprite state when generated, and the expected entry.</summary>",
        "public static class LegacyGenitalGolden",
        "{",
        "    public static readonly (string Id, string State, GenitalSlot Slot, string Shape, int Step, string? BakedColor, bool Sheathed, bool RemovesPenis)[] Entries =",
        "    {",
    ]
    for e in entries:
        baked = f"\"{e.baked}\"" if e.baked else "null"
        lines.append(f"        (\"{e.marking_id}\", \"{e.state}\", GenitalSlot.{e.slot}, \"{e.shape}\", {e.step}, {baked}, "
                     f"{cs_bool(e.sheathed)}, {cs_bool(e.removes_penis)}),")
    lines += ["    };", "}", ""]
    return NEWLINE.join(lines)


def write_if_changed(path, content):
    if os.path.exists(path):
        with open(path, encoding="utf-8", newline="") as f:
            if f.read() == content:
                return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(content)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--ref", help="read genitals.yml at this git ref instead of the working tree")
    args = parser.parse_args()

    tokens = parse_shape_tokens()
    markings = [m for m in parse_markings(read_markings_text(args.ref)) if m.get("markingCategory") == "Genital"]
    if not markings:
        sys.exit("no Genital-category markings found; they have been deleted, so run with --ref c09f236deb")

    entries, seen = [], set()
    for marking in markings:
        if marking["id"] in seen:
            sys.exit(f"duplicate marking id {marking['id']}")
        seen.add(marking["id"])
        entries.append(decode(marking, tokens))

    for path, content in ((TABLE_OUT, table_file(entries)), (GOLDEN_OUT, golden_file(entries))):
        changed = write_if_changed(path, content)
        print(f"{'wrote' if changed else 'unchanged'} {os.path.relpath(path, ROOT)}")

    counts = {}
    for e in entries:
        key = "amputated" if e.removes_penis else e.slot
        counts[key] = counts.get(key, 0) + 1
    baked = sum(1 for e in entries if e.baked)
    sheathed = sum(1 for e in entries if e.sheathed)
    print(f"{len(entries)} entries: {counts}; {baked} skin-tone (baked colour); {sheathed} fused sheath")


if __name__ == "__main__":
    main()
