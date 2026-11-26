"""Shared helpers for the Xenonids asset and prototype scripts: which RMC-14 files count as "xeno sources"."""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]

PROTO_EXCLUDE = (
    "Roles/Jobs/Survivor/",
    "Entities/Objects/xeno_figurines.yml",
    "Entities/Mobs/Xeno/fun.yml",
)
CODE_DIRS = (
    "Content.Shared/_RMC14/Xenonids",
    "Content.Server/_RMC14/Xenonids",
    "Content.Client/_RMC14/Xenonids",
    "Content.Shared/_RMC14/Shields",
    "Content.Client/_RMC14/Shields",
    "Content.Shared/_RMC14/Stun",
    "Content.Client/_RMC14/Stun",
    "Content.Shared/_RMC14/Slow",
    "Content.Shared/_RMC14/Pulling",
)
CODE_SUFFIXES = {".cs", ".xaml", ".yml"}


def find_rmc(arg: str | None) -> Path:
    if arg:
        return Path(arg)
    for base in [REPO.parent, *REPO.parents]:
        if (base / "RMC-14").exists():
            return base / "RMC-14"
    for guess in ("E:/GitHub/RMC-14", "C:/GitHub/RMC-14"):
        if Path(guess).exists():
            return Path(guess)
    sys.exit("RMC-14 checkout not found; pass --rmc")


def xeno_prototype_files(rmc: Path) -> list[Path]:
    """Every prototype .yml under Prototypes/_RMC14 whose path (below _RMC14) contains 'xeno'."""
    root = rmc / "Resources/Prototypes/_RMC14"
    out = []
    for f in sorted(root.rglob("*.yml")):
        rel = f.relative_to(root).as_posix()
        if "xeno" not in rel.lower():
            continue
        if any(rel.startswith(x) or rel == x for x in PROTO_EXCLUDE):
            continue
        out.append(f)
    return out


def xeno_locale_files(rmc: Path) -> list[Path]:
    root = rmc / "Resources/Locale/en-US/_RMC14"
    return sorted(f for f in root.rglob("*.ftl") if "xeno" in f.relative_to(root).as_posix().lower())


def xeno_code_files(rmc: Path) -> list[Path]:
    out = []
    for d in CODE_DIRS:
        base = rmc / d
        if base.exists():
            out += sorted(f for f in base.rglob("*") if f.is_file() and f.suffix in CODE_SUFFIXES)
    return out


def read_text(path: Path) -> str:
    return path.read_bytes().decode("utf-8-sig", errors="replace")


# --- prototype blocks -----------------------------------------------------------------------------------------

TOP_ITEM = re.compile(r"^- ", re.M)


def split_blocks(text: str) -> list[tuple[int, int]]:
    """Returns (start, end) character spans of the top-level `- type:` items of a prototype file.

    A span starts at its `- ` line and runs to the next top-level item; comment lines directly above the next item
    belong to that item, so they are trimmed off the end of this one.
    """
    starts = [m.start() for m in TOP_ITEM.finditer(text)]
    spans = []
    for i, s in enumerate(starts):
        e = starts[i + 1] if i + 1 < len(starts) else len(text)
        # pull leading comment lines of the next item out of this span
        seg = text[s:e]
        lines = seg.splitlines(keepends=True)
        while len(lines) > 1 and (lines[-1].strip() == "" or lines[-1].startswith("#")) and i + 1 < len(starts):
            lines.pop()
        spans.append((s, s + sum(len(x) for x in lines)))
    return spans
