#!/usr/bin/env python3
"""Stages RMC-14's xeno prototypes outside Resources until the C# they need has landed; used once during the port.

    python Tools/_WF/Xenonids/stage_prototypes.py [--rmc E:/GitHub/RMC-14] [--verbose]

  * Every xeno prototype file (see xeno_sources.py) goes through import_rmc.py's rewrite (resource paths, type names)
    into Tools/_WF/Xenonids/staging/Prototypes/<path below Prototypes/_RMC14>.
  * Prototypes those files reference that neither Wolfgate nor the staged set defines are copied, one block each, from
    RMC's prototypes into staging/Prototypes/deps/<kind>.yml, until nothing is missing.
  * staging/README.md lists the files, the dependencies with their origin, and the ids that could not be resolved.

Rerunning rebuilds the staging folder. Staged files move into Resources/Prototypes/_WF/Xenonids with the lane that
brings their C#.
"""
from __future__ import annotations

import argparse
import collections
import re
import shutil
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("stage_prototypes.py needs PyYAML: pip install pyyaml")

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import import_rmc as ir  # noqa: E402
import xeno_sources as xs  # noqa: E402

REPO = xs.REPO
STAGING = HERE / "staging"
PROTOS = STAGING / "Prototypes"
NL = "\r\n"

BLOCK_TYPE = re.compile(r"^- type:[ \t]*([A-Za-z0-9_]+)", re.M)
BLOCK_ID = re.compile(r"^  id:[ \t]*['\"]?([^\s'\"#]+)", re.M)

# last key -> prototype kinds (lower case) that the value names
EXPLICIT = {
    "damagecontainer": ["damagecontainer"],
    "damagemodifierset": ["damagemodifierset"],
    "playtimetracker": ["playtimetracker"],
    "jobicon": ["jobicon"],
    "department": ["department"],
    "departments": ["department"],
    "accesslevel": ["accesslevel"],
    "accesslevels": ["accesslevel"],
    "access": ["accesslevel", "accessgroup"],
    "collection": ["soundcollection"],
    "soundcollection": ["soundcollection"],
    "alert": ["alert"],
    "alerts": ["alert"],
    "alerttype": ["alert"],
    "alertcategory": ["alertcategory"],
    "category": ["alertcategory"],
    "statuseffect": ["entity", "statuseffect"],
    "reagent": ["reagent"],
    "reagents": ["reagent"],
    "task": ["htncompound"],
    "emote": ["emote"],
    "emotes": ["emote"],
    "faction": ["npcfaction"],
    "factions": ["npcfaction"],
    "body": ["body"],
    "healthicon": ["healthicon"],
    "decals": ["decal"],
    "radiochannel": ["radiochannel"],
    "radiochannels": ["radiochannel"],
    "channel": ["radiochannel"],
    "channels": ["radiochannel"],
    "trackermode": ["trackermode"],
}
# kinds whose ids are plain words that appear in many places; only matched through an explicit key
NEEDS_KEY = {"tag"}
# kinds followed when an id turns up as a plain value in a staged file; dependency blocks are followed through the
# EXPLICIT fields and `parent` only, or a base wall would pull in all of RMC's construction
GENERIC_KINDS = {
    "entity", "damagecontainer", "damagemodifierset", "alert", "alertcategory", "soundcollection", "statuseffect",
    "reagent", "htncompound", "jobicon", "playtimetracker", "department", "emote", "emotesounds", "speechsounds",
    "speechverb", "body", "healthicon", "edgespreader", "damagetype", "damagegroup", "job", "trackermode", "explosion",
}
KEY_HINT = {"tag": ("tag",)}


class Loader(yaml.SafeLoader):
    pass


def _any(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


Loader.add_multi_constructor("!", _any)
Loader.add_multi_constructor("tag:", _any)


# ids that PORT_BRIEF.md's cut list drops; the dependency is listed but the port removes the reference
CUT = re.compile(r"TacticalMap|AreaInfo|Spottable|Intel|Commendation|Hijack|Dropship|MotionDetector|Fulton", re.I)


def blocks_of(text: str):
    """Yields (kind, id, start, end) for every top-level prototype item of a file."""
    for s, e in xs.split_blocks(text):
        blk = text[s:e]
        mt = BLOCK_TYPE.match(blk)
        if not mt:
            continue
        mi = BLOCK_ID.search(blk)
        yield mt.group(1), (mi.group(1) if mi else None), s, e


def index(roots: list[Path]):
    """kind (lower) -> id -> (file, kind as written, block text)."""
    idx: dict[str, dict[str, tuple[Path, str, str]]] = collections.defaultdict(dict)
    for root in roots:
        if not root.exists():
            continue
        for f in sorted(root.rglob("*.yml")):
            text = xs.read_text(f)
            for kind, ident, s, e in blocks_of(text):
                if ident is not None:
                    idx[kind.lower()].setdefault(ident, (f, kind, text[s:e]))
    return idx


def walk(node, chain, out):
    if isinstance(node, dict):
        for k, v in node.items():
            ks = str(k)
            if isinstance(v, (dict, list)):
                walk(v, chain + [ks], out)
            else:
                out.append((chain + [ks], v, False))
            out.append((chain + [ks], ks, True))
    elif isinstance(node, list):
        for item in node:
            if isinstance(item, (dict, list)):
                walk(item, chain, out)
            else:
                out.append((chain, item, False))


def load_blocks(text: str):
    """Parses a file (whole, so anchors work); falls back to block by block. Returns a list of top-level mappings."""
    try:
        docs = yaml.load(text, Loader=Loader)
        return [d for d in (docs or []) if isinstance(d, dict)], 0
    except yaml.YAMLError:
        out, failed = [], 0
        for s, e in xs.split_blocks(text):
            try:
                d = yaml.load(text[s:e], Loader=Loader)
                out += [x for x in d if isinstance(x, dict)]
            except yaml.YAMLError:
                failed += 1
        return out, failed


def references(doc: dict):
    """Yields (kinds-or-None, value, explicit) for each id-looking scalar in a prototype."""
    kind = str(doc.get("type", "")).lower()
    out: list = []
    walk({k: v for k, v in doc.items()}, [], out)
    for chain, value, is_key in out:
        if not isinstance(value, str) or not value or len(value) > 120 or " " in value:
            continue
        last = chain[-1].lower() if chain else ""
        if len(chain) == 1 and last == "parent" and not is_key:
            yield [kind], value, True, chain
            continue
        hints = EXPLICIT.get(last)
        if last == "category" and kind != "alert":
            hints = None
        tagish = any("tag" in c.lower() for c in chain) and kind != "decal"
        if kind == "job" and last == "icon" and not is_key:
            yield ["jobicon"], value, True, chain
            continue
        if kind == "accessgroup" and last == "tags" and not is_key:
            yield ["accesslevel"], value, True, chain
            continue
        if tagish and not is_key:
            yield ["tag"], value, True, chain
        elif hints and not is_key:
            yield hints, value, True, chain
        else:
            yield None, value, False, chain


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rmc", default=None)
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()
    rmc = xs.find_rmc(args.rmc)
    pairs = ir.load_map("Xenonids")

    def convert(text: str) -> str:
        return ir.transform(Path("x.yml"), text, "Xenonids", pairs)

    print("indexing prototypes ...", flush=True)
    wf = index([REPO / "Resources/Prototypes", REPO / "Resources/EnginePrototypes"])
    rm = index([rmc / "Resources/Prototypes", rmc / "Resources/EnginePrototypes"])

    rm_by_id: dict[str, list[str]] = collections.defaultdict(list)
    for k, ids in rm.items():
        for i in ids:
            rm_by_id[i].append(k)
    wf_any = {i for ids in wf.values() for i in ids}

    # --- stage the xeno files ---------------------------------------------------------------------------------
    if STAGING.exists():
        shutil.rmtree(STAGING)
    root = rmc / "Resources/Prototypes/_RMC14"
    staged_files: list[str] = []
    staged_ids: dict[tuple[str, str], str] = {}
    staged_text: dict[str, str] = {}
    for f in xs.xeno_prototype_files(rmc):
        rel = f.relative_to(root).as_posix()
        # same destination layout as import_rmc: `_RMC14/Xenonids/x` collapses to `x`
        sub = rel[len("Xenonids/"):] if rel.startswith("Xenonids/") else rel
        raw = f.read_bytes().decode("utf-8-sig", errors="surrogateescape")
        text = convert(raw)
        dest = PROTOS / sub
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(text.encode("utf-8", errors="surrogateescape"))
        staged_files.append(sub)
        staged_text[sub] = raw
        for kind, ident, _, _ in blocks_of(raw):
            if ident:
                key = (kind.lower(), ident)
                staged_ids.setdefault(key, sub)

    collisions = sorted((k, v) for k, v in staged_ids.items() if k[1] in wf.get(k[0], {}))

    # --- dependency closure ---------------------------------------------------------------------------------------
    have = set(staged_ids)
    deps: dict[str, list[tuple[str, str, Path]]] = collections.defaultdict(list)  # kind written -> (id, text, source)
    dep_origin: dict[tuple[str, str], Path] = {}
    notes: list[str] = []

    def defined(kind: str, ident: str) -> bool:
        return (kind, ident) in have or ident in wf.get(kind, {})

    unresolved: dict[tuple[str, str], set[str]] = collections.defaultdict(set)
    not_followed: dict[tuple[str, str], set[str]] = collections.defaultdict(set)
    queue: list[tuple[str, dict]] = []
    parse_failures = 0
    for sub, raw in staged_text.items():
        docs, failed = load_blocks(raw)
        parse_failures += failed
        for d in docs:
            queue.append((sub, d))

    reasons: dict[tuple[str, str], str] = {}
    while queue:
        origin, doc = queue.pop()
        for kinds, value, explicit, chain in references(doc):
            targets: list[str] = []
            if kinds:
                # explicit field: the first listed kind that exists anywhere wins; none -> unresolved
                found = [k for k in kinds if defined(k, value) or value in rm.get(k, {})]
                if not found:
                    unresolved[(kinds[0], value)].add(origin)
                    continue
                targets = [k for k in found if not defined(k, value)]
                if not targets:
                    continue
            else:
                # any scalar equal to an RMC-only id of some kind; an id Wolfgate defines under another kind is a
                # coincidence unless the RMC kind is an entity
                from_dep = origin.startswith("deps:")
                for k in rm_by_id.get(value, ()):
                    if k in NEEDS_KEY or defined(k, value):
                        continue
                    if k not in GENERIC_KINDS or from_dep:
                        if k in ("entity", "guideentry"):
                            not_followed[(k, value)].add(origin)
                        continue
                    if k != "entity" and value in wf_any:
                        continue
                    targets.append(k)
            for k in targets:
                if (k, value) in have or value not in rm[k]:
                    continue
                f, written, blk = rm[k][value]
                have.add((k, value))
                dep_origin[(k, value)] = f
                reasons[(k, value)] = f"{'field ' + '.'.join(chain[-2:]) if explicit else 'value'} in {origin}"
                deps[written].append((value, blk, f))
                docs, failed = load_blocks(blk)
                parse_failures += failed
                for d in docs:
                    queue.append((f"deps:{k}/{value}", d))

    # --- write deps -------------------------------------------------------------------------------------------------
    (PROTOS / "deps").mkdir(parents=True, exist_ok=True)
    n_deps = 0
    for written, items in sorted(deps.items()):
        lines = []
        for ident, blk, f in sorted(items, key=lambda t: t[0].lower()):
            src = f.relative_to(rmc).as_posix()
            body = convert(blk).replace("\r\n", "\n").rstrip("\n")
            lines.append(f"# RMC-14: {src}\n{body}\n")
            n_deps += 1
        (PROTOS / "deps" / f"{written}.yml").write_bytes((NL.join(("\n".join(lines)).split("\n")) + NL).encode("utf-8"))

    # --- README -----------------------------------------------------------------------------------------------------
    out = []
    out.append("# Xenonids prototype staging")
    out.append("")
    out.append("RMC-14's xeno prototypes, run through `Tools/_WF/Xenonids/import_rmc.py` (resource paths point at "
               "`_WF/Xenonids`, type names follow `namespaces.yml`) and parked here, outside `Resources`, so the YAML "
               "linter and the server do not load them while the components they use do not exist. A file moves to "
               "`Resources/Prototypes/_WF/Xenonids/<same sub path>` with the lane that brings its C#; the `deps` files "
               "move with the first file that needs them (split a block out into the module when it is used elsewhere).")
    out.append("")
    out.append("Rebuilt by `python Tools/_WF/Xenonids/stage_prototypes.py` from RMC-14 `7fca80389a`. Do not edit the "
               "files here for gameplay changes you want to keep; edit them after they have moved.")
    out.append("")
    out.append(f"## Staged files ({len(staged_files)})")
    out.append("")
    group = collections.defaultdict(list)
    for s in staged_files:
        group[s.split("/")[0] if "/" in s else "."].append(s)
    for g in sorted(group):
        out.append(f"- `{g}/`" if g != "." else "- (root)")
        for s in sorted(group[g]):
            out.append(f"  - `{s}`")
    out.append("")
    out.append(f"## Dependencies ({n_deps} blocks in `Prototypes/deps/<kind>.yml`)")
    out.append("")
    out.append("Prototypes the staged files reference that Wolfgate and the staged set do not define, copied from RMC-14 "
               "(path-rewritten). Each block is preceded by a `# RMC-14:` comment naming its source file.")
    out.append("")
    for written, items in sorted(deps.items()):
        out.append(f"### {written} ({len(items)})")
        out.append("")
        for ident, blk, f in sorted(items, key=lambda t: t[0].lower()):
            cut = " (cut list: the reference is removed when the file moves in)" if CUT.search(ident) else ""
            out.append(f"- `{ident}` from `{f.relative_to(rmc).as_posix()}`{cut}")
        out.append("")
    out.append("## Unresolved ids")
    out.append("")
    if unresolved:
        out.append("Named by an id field of a staged or dependency prototype, defined neither in Wolfgate nor in RMC-14:")
        out.append("")
        for (k, ident), srcs in sorted(unresolved.items()):
            out.append(f"- {k} `{ident}` (used in {', '.join(sorted(srcs)[:3])})")
    else:
        out.append("None.")
    out.append("")
    out.append("## Referenced but not copied")
    out.append("")
    guides = sorted(i for (k, i) in not_followed if k == "guideentry")
    ents = sorted((i, sorted(o)[:2]) for (k, i), o in not_followed.items() if k == "entity")
    out.append("Left out on purpose; a dependency block that names them needs the reference cut or the prototype ported "
               "when its file moves in.")
    out.append("")
    if guides:
        out.append(f"- Guidebook entries ({len(guides)}), named by `GuideHelp` components of the staged mobs and structures; "
                   "their pages are RMC's `Resources/ServerInfo/Guidebook/_RMC14/Guides/Xenonid`, not imported here: " + ", ".join(f"`{g}`" for g in guides))
    if ents:
        out.append(f"- Entities named by values inside dependency blocks ({len(ents)}). Dependency blocks are followed through "
                   "`parent` and the id fields only, so RMC's building, weapon and medal trees stay out: " + ", ".join(f"`{i}`" for i, _ in ents))
    out.append("")
    out.append("## Ids the staged set defines that Wolfgate already defines")
    out.append("")
    if collisions:
        out.append("Loading both would fail on the duplicate; resolve each when its file moves in.")
        out.append("")
        for (k, ident), src in collisions:
            out.append(f"- {k} `{ident}` (staged in `{src}`, Wolfgate: `{wf[k][ident][0].relative_to(REPO).as_posix()}`)")
    else:
        out.append("None.")
    out.append("")
    (STAGING / "README.md").write_bytes(NL.join(out).encode("utf-8") + NL.encode())

    print(f"staged {len(staged_files)} files, {n_deps} dependency blocks in {len(deps)} kind files, "
          f"{len(unresolved)} unresolved, {len(collisions)} id collisions with Wolfgate, {parse_failures} blocks failed to parse")
    if args.verbose:
        for (k, ident), why in sorted(reasons.items()):
            print(f"  dep {k} {ident}  <- {why}")
    for (k, ident), srcs in sorted(unresolved.items()):
        print(f"  unresolved {k} {ident}  <- {sorted(srcs)[:2]}")
    for (k, ident), src in collisions:
        print(f"  collision {k} {ident}  ({src})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
