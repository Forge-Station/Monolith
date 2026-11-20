#!/usr/bin/env python3
"""Copies the RMC-14 textures and audio the xenonids need into the Xenonids module and builds the audio attributions.

    python Tools/_WF/Xenonids/copy_assets.py [--rmc E:/GitHub/RMC-14] [--dry-run]

The reference set is every `_RMC14/...` texture or audio path named by the xeno prototypes, the xeno C# and XAML
(Content.*/_RMC14/Xenonids, Shields, Stun, Slow, Pulling) and the xeno locale, plus whole folders (Mobs/Xenonids,
Structures/Xenos, xeno-named Actions, Interface, Effects and Objects entries, Audio Xeno and Voice/Xeno).

    Resources/Textures/_RMC14/<sub>  ->  Resources/Textures/_WF/Xenonids/<sub>   (whole RSI folders, byte for byte)
    Resources/Audio/_RMC14/<sub>     ->  Resources/Audio/_WF/Xenonids/<sub>

A file Wolfgate already has with identical bytes at the same `_RMC14` path is not copied; those paths are written to
Tools/_WF/Xenonids/asset_reuse.txt so import_rmc.py leaves them pointing at `_RMC14`. The audio attributions are built
from the RMC attributions.yml next to each file. Rerunning is safe: unchanged files are left alone.
"""
from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
from collections import defaultdict
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("copy_assets.py needs PyYAML: pip install pyyaml")

sys.path.insert(0, str(Path(__file__).resolve().parent))
import xeno_sources as xs  # noqa: E402

REPO = xs.REPO
DEST = "_WF/Xenonids"
REUSE = Path(__file__).resolve().parent / "asset_reuse.txt"
ATTRIBUTIONS = "Resources/Audio/_WF/Xenonids/attributions.yml"
NEWLINE = "\r\n" if sys.platform == "win32" else "\n"

# Skipped even though their name says xeno: xeno figurines are not ported.
SKIP_NAMES = {"xeno_figurines.rsi"}

REF = re.compile(r"(?:(?<![\w])/?(Textures|Audio)/)?(?<![\w.])_RMC14/([^\s\"'<>,;{}()\[\]|`]+)")


STAGED_REF = re.compile(r"(?:(?<![\w])/?(Textures|Audio)/)?(?<![\w.])_WF/Xenonids/([^\s\"'<>,;{}()\[\]|`]+)")


def refs_in(text: str):
    """Yields (kind, sub path below _RMC14) for every resource path in a text."""
    for m in REF.finditer(text):
        kind = m.group(1) or "Textures"
        sub = m.group(2).rstrip(".:#")
        yield kind, sub


def collect(rmc: Path):
    """Returns {(kind, sub): {source files}} for the reference set."""
    found: dict[tuple[str, str], set[str]] = defaultdict(set)
    sources = xs.xeno_prototype_files(rmc) + xs.xeno_code_files(rmc) + xs.xeno_locale_files(rmc)
    for f in sources:
        rel = f.relative_to(rmc).as_posix()
        for kind, sub in refs_in(xs.read_text(f)):
            found[(kind, sub)].add(rel)
    # staged prototypes (dependencies included) already rewritten to _WF/Xenonids: map back to the RMC path
    staged = Path(__file__).resolve().parent / "staging/Prototypes"
    for f in sorted(staged.rglob("*.yml")) if staged.exists() else []:
        for m in STAGED_REF.finditer(xs.read_text(f)):
            found[(m.group(1) or "Textures", m.group(2).rstrip(".:#"))].add("staging/" + f.relative_to(staged).as_posix())
    return found


def normalise(root: Path, sub: str):
    """Maps a reference to the file or RSI folder it names. Returns (path-or-None, how)."""
    parts = sub.split("/")
    for i, p in enumerate(parts):
        if p.endswith(".rsi"):
            return "/".join(parts[: i + 1]), "rsi"
    target = root / sub
    if target.is_file():
        return sub, "file"
    if target.is_dir():
        return sub.rstrip("/"), "dir"
    return None, "missing"


OTHER = re.compile(r"(?<![\w])(/?(?:Textures|Audio)/)?((?:[\w\-]+/)*[\w\-.]+\.(?:rsi|png|ogg|svg|wav))")


def other_refs_missing(rmc: Path):
    """Non-_RMC14 resource paths in the xeno sources that Wolfgate does not have."""
    miss: dict[str, set[str]] = defaultdict(set)
    sources = xs.xeno_prototype_files(rmc) + xs.xeno_code_files(rmc) + xs.xeno_locale_files(rmc)
    for f in sources:
        text = xs.read_text(f)
        for m in OTHER.finditer(text):
            full = m.group(0)
            if "_RMC14" in full or "_WF" in full:
                continue
            if m.group(1):
                kind = "Textures" if "Textures" in m.group(1) else "Audio"
                rel = m.group(2)
            elif m.group(2).endswith((".rsi", ".png", ".svg")) and "/" in m.group(2):
                kind, rel = "Textures", m.group(2)
            else:
                continue
            if kind == "Textures" and m.group(2).count(".rsi/"):
                rel = rel.split(".rsi/")[0] + ".rsi"
            if not (REPO / "Resources" / kind / rel).exists():
                miss[f"{kind}/{rel}"].add(f.relative_to(rmc).as_posix())
    return miss


def whole_folders(rmc: Path) -> list[str]:
    """Texture sub paths (below _RMC14) copied whole regardless of references."""
    tex = rmc / "Resources/Textures/_RMC14"
    out = ["Mobs/Xenonids", "Structures/Xenos", "Mobs/Effects/on_fire_xeno.rsi"]
    for top in ("Actions", "Interface", "Effects", "Objects"):
        base = tex / top
        if not base.exists():
            continue
        stack = [base]
        while stack:
            d = stack.pop()
            for c in sorted(d.iterdir()):
                if "xeno" in c.name.lower():
                    if c.name not in SKIP_NAMES:
                        out.append(c.relative_to(tex).as_posix())
                elif c.is_dir() and not c.name.endswith(".rsi"):
                    stack.append(c)
    return out


def same(a: Path, b: Path) -> bool:
    return b.is_file() and a.read_bytes() == b.read_bytes()


def files_of(path: Path):
    return [path] if path.is_file() else sorted(f for f in path.rglob("*") if f.is_file())


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rmc", default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    rmc = xs.find_rmc(args.rmc)

    roots = {"Textures": rmc / "Resources/Textures/_RMC14", "Audio": rmc / "Resources/Audio/_RMC14"}
    ours = {"Textures": REPO / "Resources/Textures/_RMC14", "Audio": REPO / "Resources/Audio/_RMC14"}
    dests = {k: REPO / f"Resources/{k}/{DEST}" for k in roots}

    wanted: dict[tuple[str, str], set[str]] = defaultdict(set)  # (kind, sub) -> why
    missing: dict[tuple[str, str], set[str]] = defaultdict(set)
    dir_refs: dict[tuple[str, str], set[str]] = defaultdict(set)

    for (kind, sub), srcs in sorted(collect(rmc).items()):
        path, how = normalise(roots[kind], sub)
        if how == "missing":
            missing[(kind, sub)] |= srcs
        elif how == "dir":
            dir_refs[(kind, sub)] |= srcs
        else:
            wanted[(kind, path)] |= srcs
    for sub in whole_folders(rmc):
        wanted[("Textures", sub)].add("(whole folder)")
    audio_tree = roots["Audio"]
    for sub in ("Xeno", "Voice/Xeno"):
        if (audio_tree / sub).exists():
            for f in files_of(audio_tree / sub):
                if f.suffix == ".ogg":
                    wanted[("Audio", f.relative_to(audio_tree).as_posix())].add("(whole folder)")

    # drop entries inside another wanted folder
    keys = sorted(wanted)
    dedup = set(keys)
    for kind, sub in keys:
        parts = sub.split("/")
        for i in range(1, len(parts)):
            if (kind, "/".join(parts[:i])) in dedup:
                dedup.discard((kind, sub))
                break

    copied = defaultdict(int)
    reused = []
    changed_files = defaultdict(int)
    audio_copied: list[str] = []
    rsis = 0
    for kind, sub in sorted(dedup):
        src = roots[kind] / sub
        if not src.exists():
            missing[(kind, sub)] |= wanted[(kind, sub)]
            continue
        files = files_of(src)
        mine = ours[kind] / sub
        if all(same(f, ours[kind] / f.relative_to(roots[kind])) for f in files):
            reused.append(f"{kind}/_RMC14/{sub}")
            continue
        for f in files:
            rel = f.relative_to(roots[kind])
            out = dests[kind] / rel
            if f.name == "attributions.yml":
                continue
            copied[kind] += 1
            if f.name == "meta.json":
                rsis += 1
            if out.is_file() and out.read_bytes() == f.read_bytes():
                continue
            changed_files[kind] += 1
            if kind == "Audio":
                audio_copied.append(rel.as_posix())
            if not args.dry_run:
                out.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(f, out)
    # audio list for attributions includes already-present copies
    audio_all = sorted({f.relative_to(dests["Audio"]).as_posix() for f in dests["Audio"].rglob("*.ogg")}) if dests["Audio"].exists() else []
    if args.dry_run:
        audio_all = sorted(set(audio_all) | set(audio_copied))

    if not args.dry_run:
        REUSE.write_text(NEWLINE.join(sorted(reused)) + (NEWLINE if reused else ""), encoding="utf-8", newline="")
        build_attributions(rmc, audio_all)

    print(f"textures: {rsis} RSI folders, {copied['Textures']} files ({changed_files['Textures']} written)")
    print(f"audio: {copied['Audio']} files, {changed_files['Audio']} written")
    if reused:
        print("\nidentical in Wolfgate already, not copied (kept at _RMC14):")
        for r in reused:
            print("  " + r)
    if dir_refs:
        print("\nreferences that name a folder, not a file (dynamic paths; check they are covered by a copied folder):")
        for (kind, sub), srcs in sorted(dir_refs.items()):
            print(f"  {kind}/_RMC14/{sub}  <- {', '.join(sorted(srcs)[:3])}")
    if missing:
        print("\nreferenced paths RMC itself lacks:")
        for (kind, sub), srcs in sorted(missing.items()):
            print(f"  {kind}/_RMC14/{sub}  <- {', '.join(sorted(srcs)[:3])}")
    other = other_refs_missing(rmc)
    if other:
        print("\nnon-_RMC14 resource paths the xeno sources use that Wolfgate lacks:")
        for path, srcs in sorted(other.items()):
            print(f"  {path}  <- {', '.join(sorted(srcs)[:2])}")
    return 0


def build_attributions(rmc: Path, audio_all: list[str]) -> None:
    """Writes Resources/Audio/_WF/Xenonids/attributions.yml from the RMC attributions next to each file."""
    tree = rmc / "Resources/Audio/_RMC14"
    cache: dict[Path, dict[str, dict]] = {}

    def lookup(rel: str):
        full = tree / rel
        p = full.parent
        while True:
            if p not in cache:
                att = p / "attributions.yml"
                table = {}
                if att.is_file():
                    for e in yaml.safe_load(xs.read_text(att)) or []:
                        for fn in e["files"]:
                            table[fn] = e
                cache[p] = table
            entry = cache[p].get(full.relative_to(p).as_posix())
            if entry is not None:
                return entry, p
            if p == tree:
                return None, p
            p = p.parent

    entries: dict[int, tuple[dict, list[str]]] = {}
    lacking = []
    for rel in audio_all:
        entry, _ = lookup(rel)
        if entry is None:
            lacking.append(rel)
            continue
        entries.setdefault(id(entry), (entry, []))[1].append(rel)
    out = []
    for entry, files in entries.values():
        fl = ", ".join(json.dumps(f) for f in sorted(files))
        out.append(f"- files: [{fl}]")
        for k, v in entry.items():
            if k != "files":
                out.append(f"  {k}: {json.dumps(v, ensure_ascii=False)}")
        out.append("")
    text = NEWLINE.join(out)
    dest = REPO / ATTRIBUTIONS
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(text, encoding="utf-8", newline="")
    print(f"attributions: {len(entries)} entries covering {len(audio_all) - len(lacking)} files -> {ATTRIBUTIONS}")
    if lacking:
        print("audio files with no RMC attribution entry:")
        for f in lacking:
            print("  " + f)


if __name__ == "__main__":
    sys.exit(main())
