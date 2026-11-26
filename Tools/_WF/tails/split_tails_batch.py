"""Moves the south-facing frame of every tail marking onto the TailBehind layer, in bulk.

Facing south a tail draws over the groin and the leg gap: the humanoid layers have no per-direction
order, so one layer sits either above the body for every facing or below it for every facing. The
split is therefore made in the art. For each marking with bodyPart: Tail this writes

    <state>_FRONT   the state with the south frame cleared (north, east and west unchanged),
    <state>_BEHIND  the south frame only,

into Resources/Textures/_WF/Mobs/Customization/TailSplit/<source rsi path>, and rewrites the marking
so FRONT keeps the layer it had while BEHIND is mapped to TailBehind with a colorLinks entry back to
FRONT. The picker then shows one colour box per half-pair and MarkingsSet.EnsureValid pads saved
colours onto the new sprites. Every animation frame and delay is kept; states whose south frame is
empty, and states with a single direction, are left alone.

Markings that already have TailBehind states keep their structure: the south frame of a top-layer
state is merged under the BEHIND state it shares a colour with, or, when that state already holds
the same pixels, only cleared from the front half.

Markings in the Special category are left alone: bodyPart: Tail is used there to put head ornaments
on the top-most layer, not to draw a tail, so their art cannot reach anatomy and moving it behind the
body would only hide it behind the head.

--mode zone is the second pass over the markings the first one split. Moving the whole south frame
behind the body is right for a tail that is behind the body anyway and wrong for art drawn to sit in
front of the mob - wings and ruffs wired to the tail layer, tails that wrap round the hips - which
would simply vanish facing south. Those markings get their south frame back on the front layer minus
the anatomy zone: the union of the south frame of every FRONT anatomy sprite, over every species the
marking is allowed on and each species' own offsets, so no configuration of anatomy can be covered.
The choice is made by measurement, not by name: a marking whose south view the body would largely
hide keeps its frame in front. Only the art and the WOLFGATE comments change; the sprite list,
layering and colorLinks the first pass wrote are already right for both variants.

Run from the repository root (requires Pillow and PyYAML):
    python Tools/_WF/tails/split_tails_batch.py --report
    python Tools/_WF/tails/split_tails_batch.py --apply
    python Tools/_WF/tails/split_tails_batch.py --mode zone --report --json zone.json
    python Tools/_WF/tails/split_tails_batch.py --mode zone --apply
    python Tools/_WF/tails/split_tails_batch.py --mode zone --verify
"""
import argparse
import json
import os
import re
import sys

import yaml
from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from split_tail_south import CLEAR, SOUTH, TEXTURES, load_meta, opaque_pixels  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
PROTOTYPES = os.path.join(ROOT, "Resources", "Prototypes")
OUT_ROOT = "_WF/Mobs/Customization/TailSplit"
DONE_ROOTS = (OUT_ROOT, "_WF/Mobs/Customization/protogen_tails.rsi")
SPLIT_NOTE = "South frame separated onto the TailBehind layer for Wolfgate."
BEHIND_LAYER = "TailBehind"
FRONT_SUFFIX = "_FRONT"
BEHIND_SUFFIX = "_BEHIND"

MARK_SPLIT_ART = "# WOLFGATE - split art"
MARK_BEHIND_ART = "# WOLFGATE - south frame on TailBehind"
MARK_LAYERING = "# WOLFGATE - the south frame draws behind the body, so the tail never covers anatomy"
MARK_LINKS = "# WOLFGATE - both halves of a tail share one colour"
MARK_PLAIN = "# WOLFGATE"
MARK_RENAME = "# WOLFGATE - renamed by the south split"

# The zone-limited variant, written by --mode zone: the south frame keeps its place and only the
# pixels that could land on anatomy are moved behind the body.
MARK_ZONE_ART = "# WOLFGATE - split art, anatomy zone only"
MARK_ZONE_BEHIND = "# WOLFGATE - anatomy zone of the south frame on TailBehind"
MARK_ZONE_LAYERING = ("# WOLFGATE - the anatomy zone of the south frame draws behind the body, "
                      "so the tail never covers anatomy")
COMMENTS_FULL = {MARK_ZONE_ART: MARK_SPLIT_ART, MARK_ZONE_BEHIND: MARK_BEHIND_ART,
                 MARK_ZONE_LAYERING: MARK_LAYERING}
COMMENTS_ZONE = {v: k for k, v in COMMENTS_FULL.items()}

# Layers of the body silhouette used to measure and draw what the split moves behind the mob.
BODY_LAYERS = ["Chest", "Head", "LArm", "RArm", "LHand", "RHand", "LLeg", "RLeg", "LFoot", "RFoot"]
BODY_TINT = (196, 164, 132, 255)
TAIL_TINT = (232, 106, 44, 255)
MOVED_TINT = (255, 32, 200, 255)
ZONE_TINT = (64, 216, 255, 255)

# Categories whose bodyPart: Tail is a layer hack rather than a tail. Special is the head ornament
# slot: the art sits on the skull and never reaches anatomy, so the split would only hide it.
NOT_TAIL_CATEGORIES = ("Special",)

# What a reviewer needs to look at by eye: the south view the body now hides. A marking is flagged
# when the split hides this much of it on every species the marking is allowed on, or leaves this
# little of it visible; the species that keeps the most visible is the one measured.
HIDDEN_FRACTION = 0.3
VISIBLE_LEFT_PIXELS = 2

# Which of the two splits a marking gets in --mode zone. Moving the whole south frame behind the
# body is right for art that is behind the body anyway - the common tail - and wrong for art drawn
# to sit in front of the mob: wings and ruffs on the tail layer, and tails that wrap round the hips,
# would simply vanish facing south. The measure is the share of the south view the body hides once
# the whole frame is behind it, on the species that hides the least; above the share, or with
# nothing left to see, the marking keeps its south frame in front and only the anatomy zone moves.
#
# 7% is the widest gap in the measured distribution below the wings: the tails run up to 6.4% and the
# next marking is a moth wing at 7.8%, so the line falls in empty space and no family of wings is cut
# in half by it. Below it sit 148 tails the body hides under 2.5% of, which the full split costs
# nothing.
ZONE_HIDDEN_FRACTION = 0.07
ZONE_VISIBLE_LEFT_PIXELS = 2

# The anatomy art is drawn on a 32x32 tile centred on the mob, whatever the tail's own frame size.
ZONE_FRAME = (32, 32)
CHEST, GROIN = "Chest", "Groin"


# ---------------------------------------------------------------------------- prototype loading


class LooseLoader(yaml.SafeLoader):
    """SafeLoader that reads the engine's !type: tags as plain data."""


def _construct_unknown(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


LooseLoader.add_multi_constructor("", _construct_unknown)
LooseLoader.add_multi_constructor("!", _construct_unknown)


def read_text(path):
    """File contents with line endings untouched."""
    with open(path, encoding="utf-8-sig", newline="") as f:
        return f.read()


def split_lines(text):
    """Lines with their endings kept, plus the ending the file mostly uses."""
    lines = text.splitlines(keepends=True)
    eol = "\r\n" if text.count("\r\n") >= text.count("\n") - text.count("\r\n") else "\n"
    return lines, eol


def yaml_docs(text):
    return [doc for doc in yaml.load_all(text.replace("\r\n", "\n"), Loader=LooseLoader) if doc]


def prototype_files():
    for dirpath, _, filenames in os.walk(PROTOTYPES):
        for name in sorted(filenames):
            if name.endswith((".yml", ".yaml")):
                yield os.path.join(dirpath, name)


def rel(path):
    return os.path.relpath(path, ROOT).replace("\\", "/")


class Prototypes:
    """The marking, species and base-sprite prototypes this tool needs, read straight from YAML."""

    TAGS = ("type: marking", "type: species", "type: humanoidBaseSprite",
            "type: genitalShape", "type: genitalSheath", "type: genitalSettings")

    def __init__(self):
        self.tail_markings = []  # (file, marking data)
        self.species = {}
        self.base_sprites = {}
        self.humanoid_base = {}
        self.shapes = []
        self.sheaths = []
        self.settings = {}
        for path in prototype_files():
            text = read_text(path)
            if not any(tag in text for tag in self.TAGS):
                continue
            try:
                docs = yaml_docs(text)
            except yaml.YAMLError as e:
                print("warning: cannot parse %s (%s)" % (rel(path), e))
                continue
            for doc in docs:
                if not isinstance(doc, list):
                    continue
                for item in doc:
                    if not isinstance(item, dict):
                        continue
                    kind, pid = item.get("type"), item.get("id")
                    if kind == "marking" and item.get("bodyPart") == "Tail":
                        self.tail_markings.append((path, item))
                    elif kind == "species" and pid:
                        self.species[pid] = item
                    elif kind == "speciesBaseSprites" and pid:
                        self.base_sprites[pid] = item
                    elif kind == "humanoidBaseSprite" and pid:
                        self.humanoid_base[pid] = item
                    elif kind == "genitalShape":
                        self.shapes.append(item)
                    elif kind == "genitalSheath":
                        self.sheaths.append(item)
                    elif kind == "genitalSettings" and pid == "Default":
                        self.settings = item


# ---------------------------------------------------------------------------- RSI access


class Rsi:
    """One source RSI: its meta, and the sheets of the states asked for."""

    _cache = {}

    def __init__(self, path):
        self.path = path
        self.dir = os.path.join(TEXTURES, path)
        self.meta = load_meta(self.dir)
        self.size = (self.meta["size"]["x"], self.meta["size"]["y"])
        self._states = {s["name"]: s for s in self.meta["states"]}
        self._images = {}

    @classmethod
    def get(cls, path):
        if path not in cls._cache:
            cls._cache[path] = cls(path)
        return cls._cache[path]

    def state(self, name):
        return self._states.get(name)

    def image(self, name):
        if name not in self._images:
            with Image.open(os.path.join(self.dir, name + ".png")) as im:
                self._images[name] = im.convert("RGBA")
        return self._images[name]

    def frames(self, state):
        """Frame count per direction; delays may differ per direction, so they are read one by one."""
        directions = state.get("directions", 1)
        delays = state.get("delays")
        if not delays:
            return [1] * directions
        return [len(d) for d in delays[:directions]] + [1] * max(0, directions - len(delays))

    def boxes(self, state, direction):
        """Pixel boxes of one direction's frames. Sheets are direction-major with frames inner."""
        frames = self.frames(state)
        if direction >= len(frames):
            return []
        width, height = self.size
        sheet = self.image(state["name"])
        columns = max(1, sheet.width // width)
        start = sum(frames[:direction])
        boxes = []
        for f in range(frames[direction]):
            index = start + f
            x, y = (index % columns) * width, (index // columns) * height
            boxes.append((x, y, x + width, y + height))
        return boxes

    def south_pixels(self, state):
        sheet = self.image(state["name"])
        return sum(opaque_pixels(sheet.crop(box)) for box in self.boxes(state, SOUTH))


def mirror_rsi(path):
    """Destination RSI for a source RSI: the same path under the Wolfgate TailSplit folder."""
    return "%s/%s" % (OUT_ROOT, path)


# ---------------------------------------------------------------------------- the anatomy zone


class AnatomyZone:
    """
    Every south-facing pixel exposed anatomy can occupy, per species.

    The union of the south frame of every FRONT anatomy state - each shape at every size and arousal
    step, plus the sheath and slit art - kept per region, then shifted by the species' own chest and
    groin offsets. Tail art outside that union cannot cover anatomy on that species whatever the
    player picks, so only the part inside it has to move behind the body.

    excludedSpecies is deliberately ignored: a species that carries no anatomy today may carry it
    tomorrow, and covering it costs a handful of pixels of tail.
    """

    def __init__(self, protos, regions=(CHEST, GROIN)):
        self.protos = protos
        self.regions = {CHEST: set(), GROIN: set()}
        self.kept = tuple(regions)
        self.missing = []
        self._pixels = {}
        self._masks = {}
        self._collect()

    # -------------------------------------------------------------- building

    def _add(self, region, rsi_path, state_name):
        try:
            rsi = Rsi.get(rsi_path)
        except (OSError, KeyError):
            self.missing.append("%s/%s" % (rsi_path, state_name))
            return
        state = rsi.state(state_name)
        if state is None:
            self.missing.append("%s/%s" % (rsi_path, state_name))
            return
        if rsi.size != ZONE_FRAME:
            raise SystemExit("%s is %dx%d; the anatomy art must be %dx%d"
                             % (rsi_path, rsi.size[0], rsi.size[1], ZONE_FRAME[0], ZONE_FRAME[1]))
        sheet = rsi.image(state_name)
        pixels = self.regions[region]
        for box in rsi.boxes(state, SOUTH):
            icon = sheet.crop(box)
            width = icon.width
            for index, alpha in enumerate(icon.getchannel("A").getdata()):
                if alpha:
                    pixels.add((index % width, index // width))

    def _collect(self):
        for shape in self.protos.shapes:
            rsi_path, template = shape.get("sprite"), shape.get("state")
            if not rsi_path or not template:
                continue
            region = shape.get("region", GROIN)
            if region not in self.regions:
                region = GROIN
            overrides = shape.get("stateOverrides") or {}
            tokens = shape.get("sizeTokens") or []
            for key, aroused in (("sizes", "0"), ("arousedSizes", "1")):
                for step in shape.get(key) or []:
                    size = tokens[step - 1] if 1 <= step <= len(tokens) else str(step)
                    name = (template.replace("{size}", str(size))
                            .replace("{aroused}", aroused)
                            .replace("{layer}", "FRONT"))
                    self._add(region, rsi_path, overrides.get(name, name))
        # The sheath and slit sit at the groin and are drawn from their own prototype, not a shape.
        for sheath in self.protos.sheaths:
            rsi_path = sheath.get("sprite")
            if not rsi_path:
                continue
            for key in ("retractedOuter", "retractedInner", "emergingOuter", "emergingInner", "erectOuter"):
                name = sheath.get(key)
                if name:
                    self._add(GROIN, rsi_path, name)
        if not self.regions[GROIN]:
            raise SystemExit("no anatomy art was found; the zone would be empty")

    # -------------------------------------------------------------- per species

    def offsets(self, species):
        """Region shifts of one species in image pixels; the prototype is in sprite pixels, +y up."""
        table = (self.protos.settings.get("speciesOffsets") or {}).get(species) or {}
        shifts = {}
        for region in self.regions:
            raw = table.get(region)
            dx = dy = 0
            if isinstance(raw, str) and "," in raw:
                x, y = raw.split(",", 1)
                dx, dy = int(float(x)), int(float(y))
            shifts[region] = (dx, -dy)
        return shifts

    def species_names(self, allowed):
        """The species a marking may appear on; an unrestricted marking is measured against them all."""
        names = sorted(set(allowed or self.protos.species))
        return tuple(names or sorted(self.protos.species))

    def pixels(self, allowed):
        """The zone over every species a marking is allowed on, as coordinates in the 32x32 tile."""
        key = self.species_names(allowed)
        if key not in self._pixels:
            zone = set()
            for species in key:
                shifts = self.offsets(species)
                for region in self.kept:
                    base, (dx, dy) = self.regions[region], shifts[region]
                    for x, y in base:
                        nx, ny = x + dx, y + dy
                        if 0 <= nx < ZONE_FRAME[0] and 0 <= ny < ZONE_FRAME[1]:
                            zone.add((nx, ny))
            self._pixels[key] = zone
        return self._pixels[key]

    def mask(self, allowed, size):
        """
        The zone as an 'L' mask of one RSI's frame size, placed where the 32x32 tile is drawn. A frame
        whose margin is a half pixel wide is covered at both roundings, so nothing slips out either way.
        """
        key = (self.species_names(allowed), size)
        if key in self._masks:
            return self._masks[key]
        mask = Image.new("L", size, 0)
        pixels = mask.load()
        left, top = size[0] - ZONE_FRAME[0], size[1] - ZONE_FRAME[1]
        origins = {(left // 2, top // 2), ((left + 1) // 2, (top + 1) // 2)}
        for ox, oy in origins:
            for x, y in self.pixels(allowed):
                px, py = x + ox, y + oy
                if 0 <= px < size[0] and 0 <= py < size[1]:
                    pixels[px, py] = 255
        self._masks[key] = mask
        return mask


# ---------------------------------------------------------------------------- planning

# What a plan does to a marking's sprite list:
#   front   - an entry keeps its place but points at the cleared <state>_FRONT
#   behind  - an entry appended at the end, drawing the south frame on TailBehind
#   merge   - an existing TailBehind entry repointed at a copy that also holds the front's south frame
# Entries with an empty south frame, and states with one direction, are left as they are.


class Plan:
    """What one marking needs: entry rewrites, appended entries, map edits and the pixel counts."""

    def __init__(self, path, marking):
        self.file = rel(path)
        self.path = path
        self.id = marking.get("id")
        self.marking = marking
        self.category = marking.get("markingCategory")
        self.species = marking.get("speciesRestriction") or []
        self.layering = dict(marking.get("layering") or {})
        self.links = dict(marking.get("colorLinks") or {})
        self.sprites = [(s["sprite"], s["state"]) for s in marking.get("sprites") or []]
        self.kind = "single"
        self.entry_ops = {}     # entry index -> (op, rsi, state)
        self.appended = []      # (rsi, state, front state it is linked to)
        self.renames = {}       # old state name -> new state name (layering/colorLinks/coloring keys)
        self.skipped = []       # (state, reason)
        self.generate = []      # art jobs, see generate_art
        self.states_split = 0
        self.animated_states = 0
        self.south_pixels = 0
        self.moved_pixels = 0
        self.inner_pixels = 0
        self.shown_pixels = 0     # opaque pixels of the south view that moves behind the body
        self.hidden_pixels = 0    # of those, the ones the body hides on its most forgiving species
        self.species_shown = None
        self.species_measured = None
        self.mostly_hidden = False
        self.error = None

    @property
    def changes(self):
        return bool(self.entry_ops or self.appended)

    def layer_of(self, state):
        return self.layering.get(state, "Tail")

    def color_root(self, state):
        """The state a colour ultimately comes from, following colorLinks."""
        seen = set()
        while state in self.links and state not in seen:
            seen.add(state)
            state = self.links[state]
        return state


def plan_for(path, marking):
    plan = Plan(path, marking)
    if not plan.sprites:
        plan.error = "no sprites"
        return plan

    if plan.category in NOT_TAIL_CATEGORIES:
        # A head ornament wired to the tail layer for its draw order; behind the body it would only
        # end up behind the head.
        plan.kind = "not a tail"
        plan.skipped = [(state, "%s category, so not a tail" % plan.category) for _, state in plan.sprites]
        return plan

    if any(rsi.startswith(DONE_ROOTS) for rsi, _ in plan.sprites):
        plan.kind = "already"
        return plan

    behind_states = {s for s, layer in plan.layering.items() if layer == BEHIND_LAYER}
    plan.kind = "split" if behind_states else "single"

    for index, (rsi_path, state_name) in enumerate(plan.sprites):
        try:
            rsi = Rsi.get(rsi_path)
        except (OSError, KeyError) as e:
            plan.skipped.append((state_name, "rsi unreadable: %s" % e))
            continue
        state = rsi.state(state_name)
        if state is None:
            plan.skipped.append((state_name, "state missing from %s" % rsi_path))
            continue
        if state.get("directions", 1) < 4:
            plan.skipped.append((state_name, "one direction, so no separate south frame"))
            continue
        if state_name in behind_states:
            continue  # already drawn behind the mob
        south = rsi.south_pixels(state)
        if south == 0:
            plan.skipped.append((state_name, "empty south frame"))
            continue

        plan.south_pixels += south
        plan.states_split += 1
        if max(rsi.frames(state)) > 1:
            plan.animated_states += 1

        pair = behind_pair(plan, rsi, state_name, behind_states)
        front_state = state_name + FRONT_SUFFIX
        plan.entry_ops[index] = ("front", mirror_rsi(rsi_path), front_state)
        plan.renames[state_name] = front_state
        plan.generate.append({"kind": "front", "rsi": rsi_path, "state": state_name, "out": front_state})

        if pair is None:
            behind_state = state_name + BEHIND_SUFFIX
            plan.appended.append((mirror_rsi(rsi_path), behind_state, front_state))
            plan.generate.append({"kind": "behind", "rsi": rsi_path, "state": state_name, "out": behind_state})
        elif pair[1] == "covered":
            pass  # the paired BEHIND state already draws these pixels
        else:
            behind_index, _ = pair
            behind_name = plan.sprites[behind_index][1]
            plan.entry_ops[behind_index] = ("merge", mirror_rsi(rsi_path), behind_name)
            plan.generate.append({
                "kind": "merge", "rsi": rsi_path, "state": behind_name,
                "under": state_name, "out": behind_name,
            })

    return plan


def behind_pair(plan, rsi, front_state, behind_states):
    """
    The marking's own TailBehind state that already carries this front state's colour, as
    (entry index, "merge"/"covered"), or None when the south frame needs a new state.
    """
    root = plan.color_root(front_state)
    candidates = [
        index for index, (path, name) in enumerate(plan.sprites)
        if name in behind_states and path == rsi.path and plan.color_root(name) == root
    ]
    if not candidates:
        # Fall back to the FRONT/BEHIND naming convention when no colour link pairs the two.
        named = front_state.replace("FRONT", "BEHIND")
        candidates = [
            index for index, (path, name) in enumerate(plan.sprites)
            if name == named and name in behind_states and path == rsi.path
        ]
    for index in candidates:
        behind_name = plan.sprites[index][1]
        behind = rsi.state(behind_name)
        if behind is None:
            continue
        front_boxes = rsi.boxes(rsi.state(front_state), SOUTH)
        behind_boxes = rsi.boxes(behind, SOUTH)
        if len(front_boxes) != len(behind_boxes):
            continue  # different frame counts, so the frames cannot be merged one to one
        sheet = rsi.image(front_state)
        behind_sheet = rsi.image(behind_name)
        covered = True
        for fbox, bbox in zip(front_boxes, behind_boxes):
            front_icon = sheet.crop(fbox)
            behind_icon = behind_sheet.crop(bbox)
            merged = Image.alpha_composite(front_icon, behind_icon)
            if merged.tobytes() != behind_icon.tobytes():
                covered = False
                break
        return index, "covered" if covered else "merge"
    return None


def wagging_pairs(plans):
    """Static/animated marking pairs, as the wagging action swaps one id for the other."""
    by_id = {p.id: p for p in plans}
    pairs = []
    for pid, plan in by_id.items():
        animated = by_id.get(pid + "Animated")
        if animated is not None:
            pairs.append((plan, animated))
    return pairs


# ---------------------------------------------------------------------------- art generation


def build_front(rsi, state_name):
    """The state's sheet with every south frame cleared."""
    state = rsi.state(state_name)
    sheet = rsi.image(state_name).copy()
    for box in rsi.boxes(state, SOUTH):
        sheet.paste(CLEAR, box)
    return sheet


def build_behind(rsi, state_name):
    """An empty sheet holding only the state's south frames."""
    state = rsi.state(state_name)
    source = rsi.image(state_name)
    sheet = Image.new("RGBA", source.size, CLEAR)
    for box in rsi.boxes(state, SOUTH):
        sheet.paste(source.crop(box), box[:2])
    return sheet


def build_merged(rsi, behind_state, front_state):
    """The BEHIND state's sheet with the front state's south frames composited underneath it."""
    sheet = rsi.image(behind_state).copy()
    front = rsi.image(front_state)
    front_boxes = rsi.boxes(rsi.state(front_state), SOUTH)
    behind_boxes = rsi.boxes(rsi.state(behind_state), SOUTH)
    for fbox, bbox in zip(front_boxes, behind_boxes):
        merged = Image.alpha_composite(front.crop(fbox), sheet.crop(bbox))
        sheet.paste(merged, bbox[:2])
    return sheet


def generate_art(plans, apply=False):
    """Writes (or counts) one split per source state, shared by every marking that uses it."""
    jobs = {}
    sources = {}
    for plan in plans:
        for job in plan.generate:
            key = (job["rsi"], job["out"])
            existing = jobs.get(key)
            if existing is None:
                jobs[key] = job
                sources.setdefault(key, set()).add(plan.id)
                continue
            if existing["kind"] != job["kind"] or existing.get("under") != job.get("under"):
                raise SystemExit(
                    "state name clash: %s/%s wanted by %s and %s"
                    % (job["rsi"], job["out"], sorted(sources[key])[0], plan.id))
            sources[key].add(plan.id)

    written = {}
    for (rsi_path, out_state), job in sorted(jobs.items()):
        rsi = Rsi.get(rsi_path)
        source_state = rsi.state(job["state"])
        if job["kind"] == "front":
            sheet = build_front(rsi, job["state"])
        elif job["kind"] == "behind":
            sheet = build_behind(rsi, job["state"])
        else:
            sheet = build_merged(rsi, job["state"], job["under"])
        entry = dict(source_state, name=out_state)
        written.setdefault(rsi_path, []).append((entry, sheet))

    if not apply:
        return {"rsis": len(written), "states": len(jobs)}

    for rsi_path, states in sorted(written.items()):
        out_rel = mirror_rsi(rsi_path)
        out_dir = os.path.join(TEXTURES, out_rel)
        os.makedirs(out_dir, exist_ok=True)
        source = Rsi.get(rsi_path)
        meta = split_meta(out_dir, source)
        for entry, sheet in states:
            sheet.save(os.path.join(out_dir, entry["name"] + ".png"), optimize=True)
            index = next((i for i, s in enumerate(meta["states"]) if s["name"] == entry["name"]), None)
            if index is None:
                meta["states"].append(entry)
            else:
                meta["states"][index] = entry
        meta["states"].sort(key=lambda s: s["name"])
        with open(os.path.join(out_dir, "meta.json"), "w", encoding="utf-8", newline="\r\n") as f:
            json.dump(meta, f, indent=2)
            f.write("\n")
    return {"rsis": len(written), "states": len(jobs)}


# ---------------------------------------------------------------------------- the zone-limited split

# A zone-limited state keeps its whole south frame on the front layer except for the pixels that can
# reach anatomy, which go to the BEHIND half instead of the entire frame. The two halves still hold
# every source pixel exactly once, so the marking's sprite list, layering and colorLinks are the ones
# the full split already wrote and only the art and the WOLFGATE comments change.


class ZonePair:
    """One split state of a marking: the source state and the two halves written for it."""

    __slots__ = ("source", "base", "rsi", "front", "behind", "front_index", "behind_index")

    def __init__(self, source, base, rsi, front, behind, front_index, behind_index):
        self.source = source            # source RSI path
        self.base = base                # source state name
        self.rsi = rsi                  # written RSI path, under OUT_ROOT
        self.front = front
        self.behind = behind
        self.front_index = front_index
        self.behind_index = behind_index

    @property
    def key(self):
        return self.source, self.base


class ZonePlan:
    """
    One already-split marking, measured for the second pass.

    The pairs are read back out of the marking itself rather than replanned, so a re-run sees what is
    in the file. A marking whose BEHIND states came from upstream, or one this tool never
    touched, has no pair to rewrite and is reported as it is.
    """

    def __init__(self, path, marking):
        plan = Plan(path, marking)
        self.plan = plan
        self.file = plan.file
        self.path = path
        self.id = plan.id
        self.category = plan.category
        self.species = plan.species
        self.pairs = []
        self.foreign = []
        self.kind = "unchanged"
        self.treatment = "full"
        self.south_pixels = 0
        self.moved_pixels = 0
        self.visible_before = 0
        self.hidden_full = 0
        self.hidden_zone = 0
        self.zone_pixels = 0
        self.species_measured = None
        self.flags = []


def zone_plan_for(path, marking):
    """The FRONT/BEHIND pairs of one marking as the split left them."""
    zp = ZonePlan(path, marking)
    plan = zp.plan
    if plan.category in NOT_TAIL_CATEGORIES:
        zp.kind = "not a tail"
        return zp

    behind_states = {state for state, layer in plan.layering.items() if layer == BEHIND_LAYER}
    index_of = {}
    for index, entry in enumerate(plan.sprites):
        index_of.setdefault(entry, index)

    for index, (rsi_path, state) in enumerate(plan.sprites):
        if state not in behind_states:
            continue
        base = state[:-len(BEHIND_SUFFIX)] if state.endswith(BEHIND_SUFFIX) else None
        front = base + FRONT_SUFFIX if base else None
        front_index = index_of.get((rsi_path, front)) if front else None
        if (base is None
                or not rsi_path.startswith(OUT_ROOT + "/")
                or front_index is None
                or plan.links.get(state) != front):
            zp.foreign.append(state)
            continue
        zp.pairs.append(ZonePair(rsi_path[len(OUT_ROOT) + 1:], base, rsi_path,
                                 front, state, front_index, index))

    zp.kind = "split" if zp.pairs else ("upstream split" if zp.foreign else "unchanged")
    return zp


def masked(icon, mask, keep_inside):
    """A copy of an icon holding only the pixels inside (or outside) a mask, others fully clear."""
    blank = Image.new("RGBA", icon.size, CLEAR)
    return Image.composite(icon, blank, mask if keep_inside else mask.point(lambda v: 255 - v))


def flatten(icon):
    """
    A copy with the colour of every fully transparent pixel zeroed.

    Source art often leaves colour under transparent pixels, and the halves are cut from it in
    different ways, so two images can only be compared once that invisible colour is dropped.
    """
    return masked(icon, icon.getchannel("A").point(lambda a: 255 if a else 0), True)


def build_zone_front(rsi, state_name, mask):
    """The state's sheet with only the anatomy zone taken out of each south frame."""
    state = rsi.state(state_name)
    sheet = rsi.image(state_name).copy()
    for box in rsi.boxes(state, SOUTH):
        sheet.paste(masked(sheet.crop(box), mask, False), box[:2])
    return sheet


def build_zone_behind(rsi, state_name, mask):
    """An empty sheet holding the anatomy zone of each south frame."""
    state = rsi.state(state_name)
    source = rsi.image(state_name)
    sheet = Image.new("RGBA", source.size, CLEAR)
    for box in rsi.boxes(state, SOUTH):
        sheet.paste(masked(source.crop(box), mask, True), box[:2])
    return sheet


def zone_measure(zp, zone, bodies):
    """
    The south view of a whole marking: what the player saw, what the body would hide with the whole
    frame behind it, and how much of it lands on anatomy.

    Every south frame of every state is laid over one canvas, so a wag that swings out of the first
    frame still counts and a marking is measured by everything it ever draws facing south.
    """
    canvas = bodies.canvas
    whole = Image.new("RGBA", (canvas, canvas), CLEAR)
    inside = Image.new("RGBA", (canvas, canvas), CLEAR)
    for pair in zp.pairs:
        rsi = Rsi.get(pair.source)
        state = rsi.state(pair.base)
        if state is None:
            continue
        zp.south_pixels += rsi.south_pixels(state)
        mask = zone.mask(zp.species, rsi.size)
        for box in rsi.boxes(state, SOUTH):
            icon = rsi.image(pair.base).crop(box)
            zp.moved_pixels += opaque_pixels(masked(icon, mask, True))
            at = centre(canvas, icon.size)
            whole.alpha_composite(icon, at)
            inside.alpha_composite(masked(icon, mask, True), at)

    zp.visible_before = opaque_pixels(whole)
    zp.zone_pixels = opaque_pixels(inside)
    zp.species_measured, zp.hidden_full = bodies.least_hidden(zp.plan, whole)
    zp.hidden_zone = bodies.hides(zp.species_measured, inside)
    return whole, inside


def zone_decide(zp, fraction, visible_left):
    """Zone-limited when the full split would hide art the player was meant to see."""
    if not zp.pairs or not zp.visible_before:
        return "full"
    left = zp.visible_before - zp.hidden_full
    if left <= visible_left:
        zp.flags.append("only %d px would be left facing south" % left)
    if zp.hidden_full >= zp.visible_before * fraction:
        zp.flags.append("the body would hide %d%% of it" % round(100.0 * zp.hidden_full / zp.visible_before))
    return "zone" if zp.flags else "full"


def zone_settle(plans):
    """
    Spreads the zone-limited choice until the set is consistent, and says what it spread to.

    Two markings can share a source state, and the wagging action swaps a marking for its animated
    twin; either way the two have to draw the same, so the zone-limited half wins. It keeps more of
    the art in front and covers anatomy just as well, so following it never uncovers anything.
    """
    by_state = {}
    for zp in plans:
        for pair in zp.pairs:
            by_state.setdefault(pair.key, []).append(zp)
    partners = {}
    for static, animated in wagging_pairs(plans):
        partners.setdefault(static.id, []).append(animated)
        partners.setdefault(animated.id, []).append(static)

    shared, wagged, apart = [], [], []
    queue = [zp for zp in plans if zp.treatment == "zone"]
    while queue:
        zp = queue.pop()
        neighbours = [(other, shared) for pair in zp.pairs for other in by_state[pair.key]]
        neighbours += [(other, wagged) for other in partners.get(zp.id, [])]
        for other, where in neighbours:
            if other is zp or other.treatment == "zone":
                continue
            if not other.pairs:
                if [zp.id, other.id] not in apart:
                    apart.append([zp.id, other.id])
                continue
            other.treatment = "zone"
            other.flags.append("draws the same art as %s" % zp.id)
            where.append([zp.id, other.id])
            queue.append(other)
    return shared, wagged, apart


def zone_states(zone_plans):
    """
    Treatment and allowed species of every split state.

    Two markings can share a source state, and zone_settle has already made them agree, so the state
    simply follows its markings; its species are theirs put together, or every species when one of
    them has no restriction.
    """
    states = {}
    for zp in zone_plans:
        for pair in zp.pairs:
            entry = states.setdefault(pair.key, {
                "source": pair.source, "base": pair.base, "rsi": pair.rsi,
                "treatment": "full", "species": set(), "markings": [],
            })
            entry["markings"].append(zp.id)
            if zp.treatment != "zone":
                continue
            entry["treatment"] = "zone"
            # A marking with no speciesRestriction is allowed everywhere, and so is the state.
            if entry["species"] is not None:
                if zp.species:
                    entry["species"].update(zp.species)
                else:
                    entry["species"] = None
    return states


def zone_conflicts(zone_plans, states):
    """States a zone-limited marking and a full-split marking both use; zone_settle leaves none."""
    treatment = {zp.id: zp.treatment for zp in zone_plans}
    out = []
    for key, entry in sorted(states.items()):
        if entry["treatment"] != "zone":
            continue
        others = sorted({m for m in entry["markings"] if treatment.get(m) == "full"})
        if others:
            out.append({"state": "%s/%s" % key, "kept_full": others,
                        "zone": sorted({m for m in entry["markings"] if treatment.get(m) == "zone"})})
    return out


def generate_zone_art(states, zone, apply=False):
    """Rewrites every split state from its source: zone-limited where chosen, the full split elsewhere."""
    written = 0
    touched = set()
    for key in sorted(states):
        entry = states[key]
        rsi = Rsi.get(entry["source"])
        if entry["treatment"] == "zone":
            mask = zone.mask(sorted(entry["species"]) if entry["species"] else [], rsi.size)
            halves = {entry["base"] + FRONT_SUFFIX: build_zone_front(rsi, entry["base"], mask),
                      entry["base"] + BEHIND_SUFFIX: build_zone_behind(rsi, entry["base"], mask)}
        else:
            halves = {entry["base"] + FRONT_SUFFIX: build_front(rsi, entry["base"]),
                      entry["base"] + BEHIND_SUFFIX: build_behind(rsi, entry["base"])}
        out_dir = os.path.join(TEXTURES, entry["rsi"])
        for name, sheet in halves.items():
            path = os.path.join(out_dir, name + ".png")
            if os.path.isfile(path):
                with Image.open(path) as im:
                    if im.convert("RGBA").tobytes() == sheet.tobytes():
                        continue
            written += 1
            touched.add(entry["rsi"])
            if apply:
                sheet.save(path, optimize=True)
    return {"sheets": written, "rsis": len(touched)}


def retag_file(path, treatments):
    """Swaps the WOLFGATE comments of the markings in one file to the split they now carry."""
    text = read_text(path)
    lines, _ = split_lines(text)
    starts = [i for i, line in enumerate(lines)
              if ITEM.match(strip_eol(line)[0]) and not line.startswith((" ", "\t"))]
    starts.append(len(lines))
    out = list(lines)
    changed = 0
    for start, end in zip(starts, starts[1:]):
        head = "".join(lines[start:end])
        if "type: marking" not in head:
            continue
        try:
            item = yaml_docs(head)[0][0]
        except (yaml.YAMLError, IndexError):
            continue
        if not isinstance(item, dict) or item.get("id") not in treatments:
            continue
        table = COMMENTS_ZONE if treatments[item["id"]] == "zone" else COMMENTS_FULL
        hit = False
        for i in range(start, end):
            body, eol = strip_eol(lines[i])
            for old, new in table.items():
                if body.endswith(" " + old):
                    out[i] = body[:-len(old)] + new + eol
                    hit = True
                    break
        changed += 1 if hit else 0
    if changed:
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write("".join(out))
    return changed


def verify_zone(protos, zone_plans, states, zone):
    """
    Every written half must be the source state cut the way its marking is tagged, the two halves must
    hold every source pixel exactly once, no front half may keep a south pixel inside the zone, the
    destination meta must keep the source's frames and licence, and nothing may be left unused.
    """
    problems = []
    checked = 0
    for key in sorted(states):
        entry = states[key]
        rsi = Rsi.get(entry["source"])
        state = rsi.state(entry["base"])
        out_dir = os.path.join(TEXTURES, entry["rsi"])
        try:
            out_meta = load_meta(out_dir)
        except OSError as e:
            problems.append("%s has no readable meta.json (%s)" % (entry["rsi"], e))
            continue
        if out_meta.get("license") != rsi.meta.get("license"):
            problems.append("%s does not carry the licence of %s" % (entry["rsi"], entry["source"]))
        if not (out_meta.get("copyright") or "").strip():
            problems.append("%s has no copyright line" % entry["rsi"])
        for suffix in (FRONT_SUFFIX, BEHIND_SUFFIX):
            written = next((e for e in out_meta["states"] if e["name"] == entry["base"] + suffix), None)
            if written is None:
                problems.append("%s has no %s%s entry in meta.json" % (entry["rsi"], entry["base"], suffix))
            elif (written.get("directions") != state.get("directions")
                  or written.get("delays") != state.get("delays")):
                problems.append("%s/%s%s changed directions or delays"
                                % (entry["rsi"], entry["base"], suffix))
        if entry["treatment"] == "zone":
            mask = zone.mask(sorted(entry["species"]) if entry["species"] else [], rsi.size)
            expected = {FRONT_SUFFIX: build_zone_front(rsi, entry["base"], mask),
                        BEHIND_SUFFIX: build_zone_behind(rsi, entry["base"], mask)}
        else:
            mask = None
            expected = {FRONT_SUFFIX: build_front(rsi, entry["base"]),
                        BEHIND_SUFFIX: build_behind(rsi, entry["base"])}
        halves = {}
        for suffix, sheet in expected.items():
            name = entry["base"] + suffix
            path = os.path.join(out_dir, name + ".png")
            if not os.path.isfile(path):
                problems.append("%s/%s.png missing" % (entry["rsi"], name))
                continue
            with Image.open(path) as im:
                halves[suffix] = im.convert("RGBA")
            if halves[suffix].tobytes() != sheet.tobytes():
                problems.append("%s/%s.png is not the %s %s half of %s"
                                % (entry["rsi"], name, entry["treatment"], suffix.strip("_").lower(), entry["base"]))
            checked += 1
        if len(halves) != 2:
            continue
        source = rsi.image(entry["base"])
        for direction in range(len(rsi.frames(state))):
            for index, box in enumerate(rsi.boxes(state, direction)):
                where = "%s/%s dir %d frame %d" % (entry["rsi"], entry["base"], direction, index)
                front, behind = halves[FRONT_SUFFIX].crop(box), halves[BEHIND_SUFFIX].crop(box)
                icon = source.crop(box)
                # Each source pixel must sit in exactly one half, with its colour untouched: pick the
                # front where the front draws and the behind everywhere else, and count the halves.
                taken = front.getchannel("A").point(lambda a: 255 if a else 0)
                if (flatten(Image.composite(front, behind, taken)).tobytes() != flatten(icon).tobytes()
                        or opaque_pixels(front) + opaque_pixels(behind) != opaque_pixels(icon)):
                    problems.append("%s: the two halves do not add back up to the source" % where)
                if direction != SOUTH and (opaque_pixels(behind) or front.tobytes() != icon.tobytes()):
                    problems.append("%s: a facing other than south was changed" % where)
                if direction == SOUTH and mask is not None and opaque_pixels(masked(front, mask, True)):
                    problems.append("%s: the front half still draws inside the anatomy zone" % where)
                if direction == SOUTH and mask is None and opaque_pixels(front):
                    problems.append("%s: the front half of a full split still draws facing south" % where)

    # The marking itself, not the state: a front half must stay clear of the zone of every species
    # its own marking is allowed on, whichever other markings share the state.
    for zp in zone_plans:
        for pair in zp.pairs:
            entry = states.get(pair.key)
            if entry is None or entry["treatment"] != "zone":
                continue
            rsi = Rsi.get(pair.source)
            path = os.path.join(TEXTURES, pair.rsi, pair.front + ".png")
            if not os.path.isfile(path):
                continue
            with Image.open(path) as im:
                front = im.convert("RGBA")
            mask = zone.mask(zp.species, rsi.size)
            for box in rsi.boxes(rsi.state(pair.base), SOUTH):
                if opaque_pixels(masked(front.crop(box), mask, True)):
                    problems.append("%s: %s draws inside the anatomy zone of its own species"
                                    % (zp.id, pair.front))
                    break

    referenced = {(path, name) for _, marking in protos.tail_markings
                  for path, name in ((s["sprite"], s["state"]) for s in marking.get("sprites") or [])}
    for dirpath, _, filenames in os.walk(os.path.join(TEXTURES, OUT_ROOT)):
        if "meta.json" not in filenames:
            continue
        rsi_path = os.path.relpath(dirpath, TEXTURES).replace("\\", "/")
        for written in load_meta(dirpath)["states"]:
            if (rsi_path, written["name"]) not in referenced:
                problems.append("orphan: %s/%s is not used by any marking" % (rsi_path, written["name"]))
    return checked, problems


def zone_outline(zone, species, size):
    """A 1 px outline of the zone, as an alpha mask of one frame."""
    mask = zone.mask(species, size)
    edge = Image.new("L", size, 0)
    pixels, source = edge.load(), mask.load()
    for y in range(size[1]):
        for x in range(size[0]):
            if not source[x, y]:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < size[0] and 0 <= ny < size[1]) or not source[nx, ny]:
                    pixels[x, y] = 255
                    break
    return edge


def zone_review_images(zp, zone, bodies, whole, inside):
    """The south view upstream, with the full split, and with the zone-limited split."""
    canvas = bodies.canvas
    body = tint(bodies.get(zp.species_measured or bodies.species_for(zp.plan)), BODY_TINT)
    outside = Image.new("RGBA", (canvas, canvas), CLEAR)
    for pair in zp.pairs:
        rsi = Rsi.get(pair.source)
        state = rsi.state(pair.base)
        if state is None:
            continue
        boxes = rsi.boxes(state, SOUTH)
        if not boxes:
            continue
        icon = rsi.image(pair.base).crop(boxes[0])
        outside.alpha_composite(masked(icon, zone.mask(zp.species, rsi.size), False), centre(canvas, icon.size))

    def plate():
        return Image.new("RGBA", (canvas, canvas), (38, 38, 44, 255))

    upstream, full, limited = plate(), plate(), plate()
    upstream.alpha_composite(body)
    upstream.alpha_composite(whole)
    full.alpha_composite(whole)
    full.alpha_composite(body)
    limited.alpha_composite(inside)
    limited.alpha_composite(body)
    limited.alpha_composite(outside)

    edge = Image.new("RGBA", (canvas, canvas), ZONE_TINT)
    edge.putalpha(zone_outline(zone, zp.species, (canvas, canvas)))
    tiles = []
    for image in (upstream, full, limited):
        image.alpha_composite(edge)
        tiles.append(image)
    return tiles


def zone_sheets(entries, out_pattern, scale=3, columns=4, rows=8):
    """UPSTREAM | FULL SPLIT | ZONE SPLIT tiles for every marking that changes treatment."""
    if not entries:
        return []
    canvas = entries[0]["tiles"][0].width
    tile = canvas * scale
    gap = 6
    cell_w = tile * 3 + gap * 2
    cell_h = tile + 32
    margin = 12
    per_page = columns * rows
    label, small = font(13), font(11)
    paths = []
    pages = (len(entries) + per_page - 1) // per_page
    for page in range(pages):
        chunk = entries[page * per_page:(page + 1) * per_page]
        used_rows = (len(chunk) + columns - 1) // columns
        width = margin * 2 + columns * cell_w + (columns - 1) * gap * 3
        height = margin * 2 + 26 + used_rows * (cell_h + gap * 3)
        sheet = Image.new("RGBA", (width, height), (24, 24, 28, 255))
        draw = ImageDraw.Draw(sheet)
        draw.text((margin, margin),
                  "Tails moved to the zone-limited split - south view: upstream | whole frame behind "
                  "| anatomy zone behind.  The cyan outline is the anatomy zone (page %d/%d)."
                  % (page + 1, pages), font=label, fill=(235, 235, 235))
        for i, entry in enumerate(chunk):
            col, row = i % columns, i // columns
            x = margin + col * (cell_w + gap * 3)
            y = margin + 26 + row * (cell_h + gap * 3)
            for j, image in enumerate(entry["tiles"]):
                sheet.alpha_composite(image.resize((tile, tile), Image.NEAREST), (x + j * (tile + gap), y))
                draw.rectangle([x + j * (tile + gap), y, x + j * (tile + gap) + tile - 1, y + tile - 1],
                               outline=(70, 70, 78))
            draw.text((x, y + tile + 3), entry["id"][:58], font=small, fill=(255, 190, 110))
            draw.text((x, y + tile + 16),
                      "%s: the body hid %d of %d px (%d%%); the zone split hides %d, moves %d"
                      % (entry["species"], entry["hidden_full"], entry["visible_before"],
                         entry["percent"], entry["hidden_zone"], entry["moved"]),
                      font=small, fill=(160, 160, 170))
        path = out_pattern % (page + 1)
        sheet.convert("RGB").save(path)
        paths.append(path)
    return paths


def split_meta(out_dir, source):
    """The destination meta, keeping the source licence and credit and noting the split."""
    copyright_line = ("%s %s" % (source.meta.get("copyright", "").strip(), SPLIT_NOTE)).strip()
    licence = source.meta.get("license")
    if not licence:
        raise SystemExit("%s has no licence in meta.json" % source.path)
    if not os.path.isfile(os.path.join(out_dir, "meta.json")):
        return {
            "version": source.meta.get("version", 1),
            "license": licence,
            "copyright": copyright_line,
            "size": source.meta["size"],
            "states": [],
        }
    meta = load_meta(out_dir)
    meta["license"] = licence
    meta["copyright"] = copyright_line
    meta["size"] = source.meta["size"]
    return meta


# ---------------------------------------------------------------------------- YAML rewriting

LINE_ENDS = ("\n", "\r")
FIELD = re.compile(r"^  ([A-Za-z0-9_]+):(.*)$")
ITEM = re.compile(r"^(\s*)-(\s|$)")
VALUE = re.compile(r"^(\s*(?:-\s+)?[A-Za-z0-9_]+:\s*)(\S+)(.*)$")
MAP_ENTRY = re.compile(r"^(\s*)([^\s:#][^:]*):(\s*)(\S+)?(.*)$")


def strip_eol(line):
    return line.rstrip("\r\n"), line[len(line.rstrip("\r\n")):]


def with_comment(line, comment):
    """Adds a trailing comment to a line that has none."""
    body, eol = strip_eol(line)
    if "#" in body:
        return line
    return "%s %s%s" % (body, comment, eol)


def set_value(line, value, comment=None):
    body, eol = strip_eol(line)
    match = VALUE.match(body)
    if not match:
        raise ValueError("cannot rewrite %r" % body)
    body = "%s%s%s" % (match.group(1), value, match.group(3))
    line = body + eol
    return with_comment(line, comment) if comment else line


class MarkingBlock:
    """One `- type: marking` item in a prototype file, edited as text so comments survive."""

    def __init__(self, lines, start, end):
        self.lines = lines
        self.start = start
        self.end = end  # exclusive

    def field(self, name):
        """(first line index, end index) of a field of this marking, or None."""
        found = None
        for i in range(self.start, self.end):
            body = strip_eol(self.lines[i])[0]
            match = FIELD.match(body)
            if match and match.group(1) == name:
                found = i
                break
        if found is None:
            return None
        i = found + 1
        while i < self.end:
            body = strip_eol(self.lines[i])[0]
            if body.strip() and FIELD.match(body):
                break
            i += 1
        return found, i

    def sprite_items(self):
        """(start, end) line spans of each entry under `sprites:`, and their indent."""
        span = self.field("sprites")
        if span is None:
            raise ValueError("no sprites field")
        head, end = span
        if strip_eol(self.lines[head])[0].split(":", 1)[1].strip():
            raise ValueError("inline sprites list")
        items = []
        indent = None
        for i in range(head + 1, end):
            body = strip_eol(self.lines[i])[0]
            match = ITEM.match(body)
            if match and (indent is None or len(match.group(1)) == indent):
                indent = len(match.group(1))
                items.append([i, i + 1])
            elif items and body.strip():
                items[-1][1] = i + 1
        return [(a, b) for a, b in items], (indent if indent is not None else 2), end


def rewrite_file(path, plans):
    """Applies every plan for one file; returns the number of markings changed."""
    text = read_text(path)
    lines, eol = split_lines(text)
    # A file whose last line has no ending would swallow anything appended to it, so it is given one
    # while the edits are worked out and the file's own style is put back at the end.
    unterminated = bool(lines) and not lines[-1].endswith(LINE_ENDS)
    if unterminated:
        lines[-1] += eol
    blocks = []
    for i, line in enumerate(lines):
        if ITEM.match(strip_eol(line)[0]) and not line.startswith((" ", "\t")):
            blocks.append(i)
    blocks.append(len(lines))
    spans = [(blocks[i], blocks[i + 1]) for i in range(len(blocks) - 1)]

    by_id = {p.id: p for p in plans if p.changes}
    edits = []  # (line index, "replace"/"insert", lines)
    changed = 0
    for start, end in spans:
        head = "".join(lines[start:end])
        if "type: marking" not in head:
            continue
        try:
            item = yaml_docs(head)[0][0]
        except (yaml.YAMLError, IndexError):
            continue
        if not isinstance(item, dict) or item.get("id") not in by_id:
            continue
        plan = by_id[item["id"]]
        edits.extend(block_edits(MarkingBlock(lines, start, end), plan, eol))
        changed += 1

    if not edits:
        return 0

    if changed != len(by_id):
        raise ValueError("%s: matched %d of %d planned markings" % (rel(path), changed, len(by_id)))

    # Back to front, and a line's own rewrite before anything inserted after it.
    out = list(lines)
    for index, kind, payload in sorted(edits, key=lambda e: (-e[0], e[1] != "replace")):
        if kind == "replace":
            out[index:index + 1] = payload
        else:
            out[index:index] = payload
    result = "".join(out)
    if unterminated and result.endswith(eol):
        result = result[:-len(eol)]  # the file ended without a newline and still does
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(result)
    return changed


def block_edits(block, plan, eol):
    """Line edits for one marking: entry rewrites, appended entries and map updates."""
    edits = []
    items, indent, _ = block.sprite_items()
    if len(items) != len(plan.sprites):
        raise ValueError("%s: %d sprite entries in text, %d in YAML" % (plan.id, len(items), len(plan.sprites)))
    for name in ("layering", "colorLinks", "coloring"):
        if plan.marking.get(name) and block.field(name) is None:
            raise ValueError("%s: cannot find the %s field in the file text" % (plan.id, name))

    pad = " " * indent
    inner = " " * (indent + 2)
    for index, (op, rsi_path, state) in sorted(plan.entry_ops.items()):
        start, end = items[index]
        comment = MARK_SPLIT_ART if op == "front" else MARK_BEHIND_ART
        for i in range(start, end):
            body = strip_eol(block.lines[i])[0]
            if re.match(r"^\s*-?\s*sprite:", body):
                edits.append((i, "replace", [set_value(block.lines[i], rsi_path, comment)]))
            elif re.match(r"^\s*state:", body):
                edits.append((i, "replace", [set_value(block.lines[i], state)]))

    if plan.appended:
        new = []
        for rsi_path, state, _ in plan.appended:
            new.append("%s- sprite: %s %s%s" % (pad, rsi_path, MARK_BEHIND_ART, eol))
            new.append("%sstate: %s%s" % (inner, state, eol))
        edits.append((items[-1][1], "insert", new))

    # layering before colorLinks, as in the markings split by hand.
    fields = [
        ("layering", [(state, BEHIND_LAYER) for _, state, _ in plan.appended], MARK_LAYERING),
        ("colorLinks", [(state, front) for _, state, front in plan.appended], MARK_LINKS),
    ]
    created = []
    for name, additions, comment in fields:
        if not additions:
            continue
        if block.field(name) is not None:
            edits.extend(map_edits(block, name, additions, eol))
        else:
            created.append("  %s: %s%s" % (name, comment, eol))
            created += ["    %s: %s%s" % (key, value, eol) for key, value in additions]
    if created:
        edits.append((block.field("sprites")[0], "insert", created))
    edits.extend(rename_edits(block, plan, "layering", keys=True, values=False))
    edits.extend(rename_edits(block, plan, "colorLinks", keys=True, values=True))
    edits.extend(rename_edits(block, plan, "coloring", keys=True, values=False, depth=3))
    return edits


def map_edits(block, field, additions, eol):
    """Adds entries to the end of an existing `layering:`/`colorLinks:` map."""
    head, end = block.field(field)
    last = head
    for i in range(head + 1, end):
        if strip_eol(block.lines[i])[0].strip():
            last = i
    new = ["    %s: %s %s%s" % (key, value, MARK_PLAIN, eol) for key, value in additions]
    return [(last + 1, "insert", new)]


def rename_edits(block, plan, field, keys, values, depth=2):
    """Renames states in a map after the split renamed them in the sprites list."""
    if not plan.renames:
        return []
    span = block.field(field)
    if span is None:
        return []
    head, end = span
    edits = []
    for i in range(head + 1, end):
        body, _ = strip_eol(block.lines[i])
        match = MAP_ENTRY.match(body)
        if not match:
            continue
        key_indent, key, gap, value = len(match.group(1)), match.group(2).strip(), match.group(3), match.group(4)
        if key_indent > depth * 2:
            continue
        new_key = plan.renames.get(key, key)
        new_value = plan.renames.get(value, value) if (values and value) else value
        if new_key == key and new_value == value:
            continue
        body_eol = strip_eol(block.lines[i])[1]
        rebuilt = "%s%s:%s%s%s" % (match.group(1), new_key, gap, new_value or "", match.group(5))
        edits.append((i, "replace", [with_comment(rebuilt + body_eol, MARK_RENAME)]))
    return edits


# ---------------------------------------------------------------------------- review rendering


class Bodies:
    """South-facing body silhouettes per species, for measuring and drawing what moves behind."""

    def __init__(self, protos, canvas=48):
        self.protos = protos
        self.canvas = canvas
        self._cache = {}

    def species_for(self, plan):
        names = plan.species or []
        if "Human" in names or not names:
            return "Human"
        for name in names:
            if name in self.protos.species:
                return name
            head = name.split()[0]
            if head in self.protos.species:
                return head
        return "Human"

    def get(self, species):
        if species in self._cache:
            return self._cache[species]
        canvas = self.canvas
        body = Image.new("RGBA", (canvas, canvas), CLEAR)
        proto = self.protos.species.get(species)
        sprites = {}
        if proto is not None:
            sprites = (self.protos.base_sprites.get(proto.get("sprites"), {}) or {}).get("sprites") or {}
        for layer in BODY_LAYERS:
            base_id = sprites.get(layer)
            if not base_id:
                continue
            entry = self.protos.humanoid_base.get(base_id + "Male") or self.protos.humanoid_base.get(base_id)
            base = (entry or {}).get("baseSprite")
            if not isinstance(base, dict) or "sprite" not in base:
                continue
            try:
                rsi = Rsi.get(base["sprite"])
                state = rsi.state(base.get("state", ""))
                if state is None:
                    continue
                icon = rsi.image(state["name"]).crop(rsi.boxes(state, SOUTH)[0])
            except (OSError, KeyError, IndexError):
                continue
            body.alpha_composite(icon, centre(canvas, icon.size))
        self._cache[species] = body
        return body

    def hides(self, species, icons):
        """Pixels of an icon composite that this species' body silhouette covers."""
        alpha = self.get(species).getchannel("A").point(lambda a: 255 if a else 0)
        blank = Image.new("L", icons.size, 0)
        covered = icons.copy()
        covered.putalpha(Image.composite(icons.getchannel("A"), blank, alpha))
        return opaque_pixels(covered)

    def least_hidden(self, plan, icons):
        """The species a marking is allowed on that keeps the most of the composite visible."""
        names = [n for n in (plan.species or list(self.protos.species)) if n in self.protos.species]
        best = None
        for name in names:
            if not opaque_pixels(self.get(name)):
                continue  # no silhouette read for this species, so it would measure as hiding nothing
            hidden = self.hides(name, icons)
            if best is None or hidden < best[1]:
                best = (name, hidden)
        if best is not None:
            return best
        shown = self.species_for(plan)
        return shown, self.hides(shown, icons)


def centre(canvas, size):
    return ((canvas - size[0]) // 2, (canvas - size[1]) // 2)


def tint(image, colour):
    """Flat colour with the image's alpha, so silhouettes read against each other."""
    flat = Image.new("RGBA", image.size, colour)
    flat.putalpha(image.getchannel("A"))
    return flat


def south_icon(plan, rsi_path, state_name):
    rsi = Rsi.get(rsi_path)
    state = rsi.state(state_name)
    boxes = rsi.boxes(state, SOUTH)
    if not boxes:
        return None
    return rsi.image(state_name).crop(boxes[0])


def review_images(plan, bodies):
    """South views before and after with the moved pixels marked, and the counts behind the body."""
    canvas = bodies.canvas
    species = bodies.species_for(plan)
    body = bodies.get(species)
    body_alpha = body.getchannel("A").point(lambda a: 255 if a else 0)
    inner_alpha = body_alpha.filter(ImageFilter.MinFilter(3))

    # Each south frame, with where it is drawn now and where it lands after the split.
    icons = []
    for index, (rsi_path, state) in enumerate(plan.sprites):
        icon = south_icon(plan, rsi_path, state)
        if icon is None:
            continue
        behind_now = plan.layer_of(state) == BEHIND_LAYER
        moves = plan.entry_ops.get(index, (None,))[0] == "front"
        icons.append((icon, behind_now, behind_now or moves))

    under_before = [i for i, before, _ in icons if before]
    over_before = [i for i, before, _ in icons if not before]
    under_after = [i for i, _, after in icons if after]
    over_after = [i for i, _, after in icons if not after]

    blank = Image.new("L", (canvas, canvas), 0)
    moving = Image.new("RGBA", (canvas, canvas), CLEAR)
    for icon, before, after in icons:
        if before or not after:
            continue  # only frames that go from over the body to behind it
        moving.alpha_composite(icon, centre(canvas, icon.size))
    moved = moving.copy()
    moved.putalpha(Image.composite(moving.getchannel("A"), blank, body_alpha))
    inner = moving.copy()
    inner.putalpha(Image.composite(moving.getchannel("A"), blank, inner_alpha))
    moved_pixels = opaque_pixels(moved)
    inner_pixels = opaque_pixels(inner)
    shown_pixels = opaque_pixels(moving)
    measured, hidden_pixels = bodies.least_hidden(plan, moving)

    def compose(under, over):
        out = Image.new("RGBA", (canvas, canvas), CLEAR)
        for icon in under:
            out.alpha_composite(tint(icon, TAIL_TINT), centre(canvas, icon.size))
        out.alpha_composite(tint(body, BODY_TINT))
        for icon in over:
            out.alpha_composite(tint(icon, TAIL_TINT), centre(canvas, icon.size))
        return out

    before = compose(under_before, over_before)
    after = compose(under_after, over_after)
    diff = compose(under_after, [])
    diff.alpha_composite(tint(moved, MOVED_TINT))
    return {
        "before": before, "after": after, "diff": diff,
        "moved": moved_pixels, "inner": inner_pixels, "species": species,
        "shown": shown_pixels, "hidden": hidden_pixels, "measured": measured,
    }


def font(size):
    try:
        return ImageFont.load_default(size)
    except TypeError:
        return ImageFont.load_default()


def contact_sheets(entries, out_pattern, scale=2, columns=5, rows=12):
    """BEFORE/AFTER/MOVED tiles for every changed tail, largest change first, paged into PNGs."""
    if not entries:
        return []
    canvas = entries[0]["before"].width
    tile = canvas * scale
    gap = 6
    cell_w = tile * 3 + gap * 2
    cell_h = tile + 30
    margin = 12
    per_page = columns * rows
    label = font(12)
    small = font(11)
    paths = []
    pages = (len(entries) + per_page - 1) // per_page
    for page in range(pages):
        chunk = entries[page * per_page:(page + 1) * per_page]
        used_rows = (len(chunk) + columns - 1) // columns
        width = margin * 2 + columns * cell_w + (columns - 1) * gap * 3
        height = margin * 2 + 24 + used_rows * (cell_h + gap * 3)
        sheet = Image.new("RGBA", (width, height), (24, 24, 28, 255))
        draw = ImageDraw.Draw(sheet)
        draw.text((margin, margin), "Tail south frame moved behind the body - before | after | moved pixels"
                  "   (page %d/%d, the flagged ones first, then by how much of the south view the body hides)"
                  % (page + 1, pages), font=label, fill=(235, 235, 235))
        for i, entry in enumerate(chunk):
            col, row = i % columns, i // columns
            x = margin + col * (cell_w + gap * 3)
            y = margin + 24 + row * (cell_h + gap * 3)
            for j, key in enumerate(("before", "after", "diff")):
                img = entry[key].resize((tile, tile), Image.NEAREST)
                sheet.alpha_composite(img, (x + j * (tile + gap), y))
                draw.rectangle([x + j * (tile + gap), y, x + j * (tile + gap) + tile - 1, y + tile - 1],
                               outline=(70, 70, 78))
            note = "%s%s" % (entry["id"], "  MOSTLY-HIDDEN" if entry["mostly_hidden"] else "")
            draw.text((x, y + tile + 3), note[:46], font=small,
                      fill=(255, 170, 80) if entry["mostly_hidden"] else (235, 235, 235))
            draw.text((x, y + tile + 15),
                      "%s  moved %d of %d px; %s hides %d of %d"
                      % (entry["species"], entry["moved"], entry["south"],
                         entry["measured"], entry["hidden"], entry["shown"]),
                      font=small, fill=(160, 160, 170))
        path = out_pattern % (page + 1)
        sheet.convert("RGB").save(path)
        paths.append(path)
    return paths


# ---------------------------------------------------------------------------- main


def zone_main(args, protos):
    """The second pass: the tails whose art belongs in front keep it, minus the anatomy zone."""
    zone = AnatomyZone(protos, args.zone_regions)
    plans = []
    for path, marking in protos.tail_markings:
        if args.ids and marking.get("id") not in args.ids:
            continue
        if args.files and not any(f.replace("\\", "/") in rel(path) for f in args.files):
            continue
        plans.append(zone_plan_for(path, marking))

    bodies = Bodies(protos)
    views = {}
    for zp in plans:
        if not zp.pairs:
            continue
        views[zp.id] = zone_measure(zp, zone, bodies)
        zp.treatment = zone_decide(zp, args.hidden_fraction, args.visible_left)
    measured = sum(1 for zp in plans if zp.treatment == "zone")
    shared, wagged, apart = zone_settle(plans)

    states = zone_states(plans)
    conflicts = zone_conflicts(plans, states)
    art = generate_zone_art(states, zone, apply=False)

    if args.verify:
        checked, problems = verify_zone(protos, plans, states, zone)
        print("verified %d split sheets over %d states" % (checked, len(states)))
        for problem in problems:
            print("  %s" % problem)
        print("%d problem(s)" % len(problems))
        raise SystemExit(1 if problems else 0)

    review = []
    for zp in sorted(plans, key=lambda p: -p.hidden_full):
        if zp.treatment != "zone":
            continue
        whole, inside = views[zp.id]
        review.append({
            "id": zp.id, "species": zp.species_measured, "moved": zp.moved_pixels,
            "visible_before": zp.visible_before, "hidden_full": zp.hidden_full,
            "hidden_zone": zp.hidden_zone,
            "percent": round(100.0 * zp.hidden_full / max(1, zp.visible_before)),
            "tiles": zone_review_images(zp, zone, bodies, whole, inside),
        })

    counts = {
        "tail_markings": len(plans),
        "split_states_found": sum(len(zp.pairs) for zp in plans),
        "split_markings": sum(1 for zp in plans if zp.kind == "split"),
        "upstream_split_left_alone": sum(1 for zp in plans if zp.kind == "upstream split"),
        "not_a_tail_left_alone": sum(1 for zp in plans if zp.kind == "not a tail"),
        "never_split": sum(1 for zp in plans if zp.kind == "unchanged"),
        "zone_limited": sum(1 for zp in plans if zp.treatment == "zone"),
        "zone_limited_by_measurement": measured,
        "zone_limited_by_a_shared_state": len(shared),
        "zone_limited_by_a_wagging_partner": len(wagged),
        "kept_full": sum(1 for zp in plans if zp.pairs and zp.treatment == "full"),
        "states_zone_limited": sum(1 for e in states.values() if e["treatment"] == "zone"),
        "states_kept_full": sum(1 for e in states.values() if e["treatment"] == "full"),
        "sheets_to_rewrite": art["sheets"],
        "rsis_to_touch": art["rsis"],
        "wagging_pairs_left_apart": len(apart),
        "states_shared_with_a_full_marking": len(conflicts),
        "anatomy_zone_pixels": len(zone.pixels([])),
        # What the second pass buys back: south pixels the body hid under the full split and no longer
        # hides. The chest half of the zone spans the torso, so chest-worn art gains little by it.
        "pixels_put_back_in_front": sum(zp.hidden_full - zp.hidden_zone
                                        for zp in plans if zp.treatment == "zone"),
        "zone_markings_that_gain_nothing": sum(1 for zp in plans if zp.treatment == "zone"
                                               and zp.hidden_full == zp.hidden_zone),
        # Markings whose whole south view sits inside the zone: nothing is left for the front half, so
        # the zone-limited cut writes exactly what the full split wrote. Art that rides the chest lands
        # here, because the chest half of the zone covers the torso.
        "zone_markings_cut_whole": sum(1 for zp in plans if zp.treatment == "zone"
                                       and zp.south_pixels and zp.moved_pixels == zp.south_pixels),
    }

    report = {
        "threshold": {
            "hidden_fraction": args.hidden_fraction,
            "visible_left_pixels": args.visible_left,
            "rule": "zone-limited when the body would hide at least the fraction of the south view, "
                    "or leave no more than the pixels visible, on the species that hides the least",
        },
        "anatomy_zone": {
            "regions": list(zone.kept),
            "pixels_unrestricted": len(zone.pixels([])),
            "pixels_no_offsets": len(set().union(*(zone.regions[r] for r in zone.kept))),
            "chest_pixels": len(zone.regions[CHEST]),
            "groin_pixels": len(zone.regions[GROIN]),
            "missing_anatomy_states": sorted(set(zone.missing)),
        },
        "counts": counts,
        "followed_a_shared_state": shared,
        "followed_a_wagging_partner": wagged,
        "wagging_pairs_left_apart": apart,
        "states_shared_with_a_full_marking": conflicts,
        "markings": [
            {
                "id": zp.id, "file": zp.file, "kind": zp.kind, "category": zp.category,
                "treatment": zp.treatment, "why": zp.flags, "species": zp.species,
                "species_measured": zp.species_measured,
                "states": [pair.base for pair in zp.pairs],
                "south_pixels": zp.south_pixels,
                "visible_before": zp.visible_before,
                "hidden_by_full_split": zp.hidden_full,
                "hidden_fraction": round(zp.hidden_full / zp.visible_before, 4) if zp.visible_before else None,
                "hidden_by_zone_split": zp.hidden_zone,
                "put_back_in_front": zp.hidden_full - zp.hidden_zone,
                "zone_pixels_in_view": zp.zone_pixels,
                "moved_pixels": zp.moved_pixels if zp.treatment == "zone" else zp.south_pixels,
                "upstream_behind_states": zp.foreign,
            }
            for zp in sorted(plans, key=lambda p: (p.treatment != "zone", -p.hidden_full))
            if zp.pairs or zp.foreign
        ],
    }

    if args.json:
        with open(args.json, "w", encoding="utf-8", newline="\n") as f:
            json.dump(report, f, indent=1)
            f.write("\n")
        print("wrote %s" % args.json)
    if args.sheet and not args.no_sheet:
        for path in zone_sheets(review, args.sheet):
            print("wrote %s" % path)

    for key, value in counts.items():
        print("%-42s %s" % (key, value))

    if args.apply:
        done = generate_zone_art(states, zone, apply=True)
        treatments = {zp.id: zp.treatment for zp in plans if zp.pairs}
        by_file = {}
        for zp in plans:
            if zp.pairs:
                by_file.setdefault(zp.path, []).append(zp)
        retagged = 0
        for path, file_plans in sorted(by_file.items()):
            retagged += retag_file(path, {zp.id: treatments[zp.id] for zp in file_plans})
        print("applied: %d sheets in %d RSIs, %d markings retagged in %d files"
              % (done["sheets"], done["rsis"], retagged, len(by_file)))


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--report", action="store_true", help="measure only, write the JSON report and contact sheets")
    mode.add_argument("--apply", action="store_true", help="write the split art and rewrite the markings")
    mode.add_argument("--verify", action="store_true", help="check the written art against the source states")
    parser.add_argument("--mode", choices=("full", "zone"), default="full",
                        help="full: move the whole south frame behind the body (the first pass); "
                             "zone: keep the south frame in front where the art belongs there and move "
                             "only the pixels that can reach anatomy")
    parser.add_argument("--hidden-fraction", type=float, default=ZONE_HIDDEN_FRACTION, dest="hidden_fraction",
                        help="--mode zone: the share of the south view the body may hide before the "
                             "marking keeps its frame in front (default %(default)s)")
    parser.add_argument("--visible-left", type=int, default=ZONE_VISIBLE_LEFT_PIXELS, dest="visible_left",
                        help="--mode zone: pixels that may be left visible facing south (default %(default)s)")
    parser.add_argument("--zone-regions", nargs="+", choices=(CHEST, GROIN), default=[CHEST, GROIN],
                        dest="zone_regions",
                        help="--mode zone: the anatomy regions the zone covers. Both by default, so no "
                             "configuration of anatomy can be covered; the chest half is the widest "
                             "breast art and spans most of the torso, so Groin alone is the lever for "
                             "putting chest-worn wings back in front")
    parser.add_argument("--ids", nargs="*", help="limit to these marking ids")
    parser.add_argument("--files", nargs="*", help="limit to markings from these prototype files (substring match)")
    parser.add_argument("--json", help="path of the JSON report")
    parser.add_argument("--sheet", help="pattern of the contact sheets, e.g. review_%%d.png")
    parser.add_argument("--no-sheet", action="store_true", help="skip the contact sheets")
    args = parser.parse_args()

    protos = Prototypes()
    # --verify checks the written art whichever pass wrote it, so it reads each state's treatment
    # from the second pass rather than assuming the whole south frame moved.
    if args.mode == "zone" or args.verify:
        zone_main(args, protos)
        return

    plans = []
    for path, marking in protos.tail_markings:
        if args.ids and marking.get("id") not in args.ids:
            continue
        if args.files and not any(f.replace("\\", "/") in rel(path) for f in args.files):
            continue
        plans.append(plan_for(path, marking))

    counts = {
        "tail_markings": len(plans),
        "already_split_by_wolfgate": sum(1 for p in plans if p.kind == "already"),
        "not_a_tail_left_alone": sum(1 for p in plans if p.kind == "not a tail"),
        "unchanged": sum(1 for p in plans if p.kind not in ("already", "not a tail") and not p.changes),
        "single_layer_split": sum(1 for p in plans if p.kind == "single" and p.changes),
        "multi_layer_adjusted": sum(1 for p in plans if p.kind == "split" and p.changes),
        "states_split": sum(p.states_split for p in plans),
        "animated_states": sum(p.animated_states for p in plans),
        "sprites_added": sum(len(p.appended) for p in plans),
        "states_merged": sum(1 for p in plans for op, _, _ in p.entry_ops.values() if op == "merge"),
        "skipped_states": sum(len(p.skipped) for p in plans),
    }
    art = generate_art(plans, apply=False)
    counts["rsis_generated"] = art["rsis"]
    counts["generated_states"] = art["states"]

    bodies = Bodies(protos)
    review = []
    for plan in plans:
        if not plan.changes:
            continue
        shot = review_images(plan, bodies)
        plan.moved_pixels = shot["moved"]
        plan.inner_pixels = shot["inner"]
        plan.shown_pixels = shot["shown"]
        plan.hidden_pixels = shot["hidden"]
        plan.species_shown = shot["species"]
        plan.species_measured = shot["measured"]
        plan.mostly_hidden = bool(shot["hidden"]) and (
            shot["hidden"] >= shot["shown"] * HIDDEN_FRACTION
            or shot["shown"] - shot["hidden"] <= VISIBLE_LEFT_PIXELS)
        if plan.moved_pixels:
            review.append({
                "id": plan.id, "species": shot["species"], "moved": shot["moved"],
                "inner": shot["inner"], "south": plan.south_pixels,
                "shown": shot["shown"], "hidden": shot["hidden"], "measured": shot["measured"],
                "mostly_hidden": plan.mostly_hidden,
                "before": shot["before"], "after": shot["after"], "diff": shot["diff"],
            })
    review.sort(key=lambda e: (not e["mostly_hidden"], -e["hidden"], -e["moved"]))
    counts["visibly_changed"] = len(review)
    counts["mostly_hidden"] = sum(1 for e in review if e["mostly_hidden"])

    mismatched = []
    for static, animated in wagging_pairs(plans):
        if len(static.sprites) + len(static.appended) != len(animated.sprites) + len(animated.appended):
            mismatched.append([static.id, animated.id])
    counts["wagging_pairs"] = len(wagging_pairs(plans))
    counts["wagging_pairs_with_different_sprite_counts"] = len(mismatched)

    report = {
        "counts": counts,
        "mostly_hidden": [
            {"id": e["id"], "species": e["measured"], "hidden": e["hidden"], "shown": e["shown"],
             "south": e["south"], "inner": e["inner"]}
            for e in review if e["mostly_hidden"]
        ],
        "wagging_sprite_count_mismatches": mismatched,
        "markings": [
            {
                "id": p.id, "file": p.file, "kind": p.kind, "category": p.category,
                "species": p.species, "sprites_before": len(p.sprites),
                "sprites_after": len(p.sprites) + len(p.appended),
                "states_split": p.states_split, "animated_states": p.animated_states,
                "south_pixels": p.south_pixels, "moved_pixels": p.moved_pixels,
                "inner_pixels": p.inner_pixels, "species_shown": p.species_shown,
                "shown_pixels": p.shown_pixels, "hidden_pixels": p.hidden_pixels,
                "species_measured": p.species_measured, "mostly_hidden": p.mostly_hidden,
                "merged": [s for op, _, s in p.entry_ops.values() if op == "merge"],
                "added": [s for _, s, _ in p.appended],
                "skipped": [{"state": s, "reason": r} for s, r in p.skipped],
                "error": p.error,
            }
            for p in sorted(plans, key=lambda p: -p.moved_pixels)
        ],
    }

    if args.json:
        with open(args.json, "w", encoding="utf-8", newline="\n") as f:
            json.dump(report, f, indent=1)
            f.write("\n")
        print("wrote %s" % args.json)
    if args.sheet and not args.no_sheet:
        for path in contact_sheets(review, args.sheet):
            print("wrote %s" % path)

    for key, value in counts.items():
        print("%-42s %s" % (key, value))

    if args.apply:
        art = generate_art(plans, apply=True)
        by_file = {}
        for plan in plans:
            if plan.changes:
                by_file.setdefault(plan.path, []).append(plan)
        markings = 0
        for path, file_plans in sorted(by_file.items()):
            markings += rewrite_file(path, file_plans)
        print("applied: %d markings in %d files, %d states in %d RSIs"
              % (markings, len(by_file), art["states"], art["rsis"]))


if __name__ == "__main__":
    main()
