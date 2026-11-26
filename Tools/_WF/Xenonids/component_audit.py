#!/usr/bin/env python3
"""Writes Docs/_WF/Xenonids/component-audit.md: where every component the staged xeno prototypes use is defined.

    python Tools/_WF/Xenonids/component_audit.py [--rmc E:/GitHub/RMC-14] [--engine E:/GitHub/RobustToolbox-291]

Run stage_prototypes.py first. A component name `X` in a prototype is the class `XComponent` (or the class whose
[ComponentProtoName("X")] says so). Searched: Wolfgate's Content.*, the engine checkout, RMC-14's Content.*.
"""
from __future__ import annotations

import argparse
import collections
import re
import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    sys.exit("component_audit.py needs PyYAML: pip install pyyaml")

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import stage_prototypes as sp  # noqa: E402
import xeno_sources as xs  # noqa: E402

REPO = xs.REPO
OUT = REPO / "Docs/_WF/Xenonids/component-audit.md"
SKIP_DIRS = {"bin", "obj", "RobustToolbox", ".git", "node_modules"}

CLASS = re.compile(r"\bclass\s+(\w+?)Component\b")
PROTO_NAME = re.compile(r"ComponentProtoName\(\s*\"(\w+)\"\s*\)\][^{;]*?\bclass\s+(\w+)", re.S)

# PORT_BRIEF.md cut list: component name (or regex) -> what the brief says
CUT_EXACT = {
    "Marine": "IsHostCandidate in Xenonids replaces it",
    "XenoMapTracked": "dropped (tactical map)",
    "XenoStructureMapTracked": "dropped (tactical map)",
    "AreaInfo": "dropped",
    "TacMapXenoAlert": "dropped (tactical map)",
    "Spottable": "dropped",
    "MotionDetectorTracked": "dropped",
    "RMCCanBeFultoned": "dropped",
    "JobPrefix": "dropped",
    "Skills": "dropped",
    "Language": "dropped",
    "LanguageLearning": "dropped",
    "RMCSurvivor": "dropped",
    "EquipSurvivorPreset": "dropped",
    "EntityPreset": "dropped",
    "FiremanCarriable": "dropped",
    "MultiHandedHolder": "dropped",
    "Undisposable": "dropped",
    "RMCPointing": "dropped",
    "RMCInnateRadioTextIncrease": "dropped",
    "BuckleWhitelist": "dropped",
    "CrashLandable": "dropped",
    "InnateCommandSpeech": "dropped",
    "RoofingEntity": "dropped",
    "Synth": "dropped",
    "BlockIdExamine": "dropped",
    "ShortExamine": "dropped",
    "DropshipHijacker": "dropped (hijack)",
    "HijackBurrowedSurge": "dropped (hijack)",
    "CMDistressSignalRule": "dropped",
    "OnFire": "upstream FlammableComponent",
    "RMCFireColor": "dropped (RMC fire)",
    "RMCImmuneToFireTileDamage": "dropped (RMC fire)",
    "RMCImmuneToIgnition": "marker checked in a Xenonids handler, else dropped",
    "DeletedByWeedKiller": "marker component in Xenonids",
    "RMCMedicalExamine": "dropped or marker (Wolfmed)",
    "RMCBlockMedicalExamine": "dropped or marker (Wolfmed)",
    "CMSurgeryTarget": "dropped or marker (Wolfmed)",
    "RMCSurgeryXenoHeart": "dropped or marker (Wolfmed)",
    "RMCDefibrillatorBlocked": "dropped or marker (Wolfmed)",
    "RMCHealthExaminable": "dropped or marker (Wolfmed)",
    "RMCNightVisionVisible": "empty marker in Xenonids",
    "RMCMesonsNonviewable": "empty marker in Xenonids",
    "NightVision": "Wolfgate's own component; RMC's is not ported",
}
CUT_PATTERNS = [
    (re.compile(r"^TacticalMap"), "dropped (tactical map)"),
    (re.compile(r"^Intel"), "dropped"),
    (re.compile(r"^Commendation"), "dropped"),
    (re.compile(r"^RMCBuckle"), "dropped"),
    (re.compile(r"^Hijack"), "dropped (hijack)"),
    (re.compile(r"^Dropship"), "dropped"),
]


def cut_note(name: str) -> str | None:
    if name in CUT_EXACT:
        return CUT_EXACT[name]
    for rx, note in CUT_PATTERNS:
        if rx.search(name):
            return note
    return None


def scan(root: Path, dirs: list[str]) -> dict[str, list[str]]:
    """component name -> [relative paths of files defining a class for it]."""
    out: dict[str, list[str]] = collections.defaultdict(list)
    for d in dirs:
        base = root / d
        if not base.exists():
            continue
        for f in base.rglob("*.cs"):
            if SKIP_DIRS & set(f.relative_to(root).parts[:-1]):
                continue
            try:
                text = f.read_bytes().decode("utf-8-sig", errors="replace")
            except OSError:
                continue
            if "Component" not in text:
                continue
            rel = f.relative_to(root).as_posix()
            for m in CLASS.finditer(text):
                if rel not in out[m.group(1)]:
                    out[m.group(1)].append(rel)
            for m in PROTO_NAME.finditer(text):
                if rel not in out[m.group(1)]:
                    out[m.group(1)].append(rel)
    return out


def used_components(rmc_staging: Path) -> dict[str, set[str]]:
    used: dict[str, set[str]] = collections.defaultdict(set)
    for f in sorted(rmc_staging.rglob("*.yml")):
        docs, _ = sp.load_blocks(xs.read_text(f))
        rel = f.relative_to(rmc_staging).as_posix()
        for d in docs:
            if str(d.get("type", "")).lower() != "entity":
                continue
            for c in d.get("components") or []:
                if isinstance(c, dict) and "type" in c:
                    used[str(c["type"])].add(rel)
    return used


def show(paths: list[str], limit: int = 2) -> str:
    if not paths:
        return ""
    shown = ", ".join(f"`{p}`" for p in paths[:limit])
    return shown + (f" (+{len(paths) - limit})" if len(paths) > limit else "")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rmc", default=None)
    ap.add_argument("--engine", default="E:/GitHub/RobustToolbox-291")
    args = ap.parse_args()
    rmc = xs.find_rmc(args.rmc)
    engine = Path(args.engine)
    staging = HERE / "staging/Prototypes"
    if not staging.exists():
        sys.exit("run stage_prototypes.py first")

    used = used_components(staging)
    wf = scan(REPO, ["Content.Shared", "Content.Server", "Content.Client"])
    rm = scan(rmc, ["Content.Shared", "Content.Server", "Content.Client"])
    en = scan(engine, ["Robust.Shared", "Robust.Server", "Robust.Client"]) if engine.exists() else {}

    rows = []
    totals = collections.Counter()
    for name in sorted(used):
        w = wf.get(name, [])
        e = en.get(name, [])
        rx = [p for p in rm.get(name, []) if "/_RMC14/Xenonids/" in p]
        re_ = [p for p in rm.get(name, []) if "/_RMC14/Xenonids/" not in p]
        cut = cut_note(name)
        if e and not w:
            status = "engine"
        elif w:
            status = "wolfgate"
        elif cut:
            status = "cut"
        elif rx:
            status = "rmc-xeno"
        elif re_:
            status = "rmc-other"
        else:
            status = "missing"
        totals[status] += 1
        if cut and status != "cut":
            totals["cut-also"] += 1
        rows.append((name, status, w, e, rx, re_, cut, used[name]))

    order = {"engine": 0, "wolfgate": 1, "rmc-xeno": 2, "rmc-other": 3, "cut": 4, "missing": 5}
    rows.sort(key=lambda r: (order[r[1]], r[0].lower()))
    label = {
        "engine": "engine", "wolfgate": "Wolfgate", "rmc-xeno": "RMC Xenonids", "rmc-other": "RMC elsewhere",
        "cut": "cut list", "missing": "defined nowhere",
    }

    lines = [
        "# Xenonids component audit",
        "",
        "Every component named by the staged xeno prototypes (`Tools/_WF/Xenonids/staging/Prototypes`, dependencies "
        "included) and where its class is defined. Generated by `Tools/_WF/Xenonids/component_audit.py` from "
        f"RMC-14 `7fca80389a`, this checkout and the engine v291 source; rerun it after restaging.",
        "",
        "Status is the first that applies: **engine** (Robust itself), **Wolfgate** (a class of that name exists here "
        "already; check it is the same thing before reusing it), **cut list** (`PORT_BRIEF.md` drops or replaces it), "
        "**RMC Xenonids** (`Content.*/_RMC14/Xenonids`, ported with the module), **RMC elsewhere** (needs the RMCCore "
        "module or a trimmed copy), **defined nowhere** (not a class in either checkout).",
        "",
        "## Totals",
        "",
        f"{len(rows)} components.",
        "",
        "| Status | Count |",
        "|---|---|",
    ]
    for k in ("engine", "wolfgate", "rmc-xeno", "rmc-other", "cut", "missing"):
        lines.append(f"| {label[k]} | {totals[k]} |")
    lines += [
        "",
        f"{totals['cut-also']} further components are on the cut list but also have a class here (counted under their "
        "first status above; the note column says what the brief wants).",
        "",
        "## Components",
        "",
        "| Component | Status | Wolfgate / engine | RMC Xenonids | RMC elsewhere | Cut list | Used in |",
        "|---|---|---|---|---|---|---|",
    ]
    for name, status, w, e, rx, re_, cut, files in rows:
        here = show(w) if w else (show(e, 1) + " (engine)" if e else "")
        used_in = f"{len(files)} files" if len(files) > 3 else ", ".join(f"`{f}`" for f in sorted(files))
        lines.append(f"| `{name}` | {label[status]} | {here} | {show(rx, 1)} | {show(re_, 2)} | {cut or ''} | {used_in} |")
    lines.append("")
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(("\r\n".join(lines)).encode("utf-8"))
    print(f"{len(rows)} components: " + ", ".join(f"{label[k]} {totals[k]}" for k in order) + f"; cut-also {totals['cut-also']}")
    for name, status, *_ in rows:
        if status == "missing":
            print("  defined nowhere:", name)
    return 0


if __name__ == "__main__":
    sys.exit(main())
