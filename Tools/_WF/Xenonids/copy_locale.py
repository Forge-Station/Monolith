#!/usr/bin/env python3
"""Copies the RMC-14 xeno locale into Resources/Locale/en-US/_WF/Xenonids and collects the shared keys they lean on.

    python Tools/_WF/Xenonids/copy_locale.py [--rmc E:/GitHub/RMC-14] [--dry-run]

  1. Every .ftl under RMC's Locale/en-US/_RMC14 whose path contains `xeno` is copied (same names and sub-folders).
  2. Every Fluent key the xeno sources use (Loc.GetString and other string literals that equal an RMC key, plain
     scalars in the xeno prototypes, `ent-<Id>` names of the entities they define, and messages or terms referenced from
     a copied entry) that RMC defines in a non-xeno ftl is appended, entry for entry, to rmc-shared.ftl.
  3. A key Wolfgate already defines anywhere under Resources/Locale/en-US is dropped from the copies; the report lists
     those whose text differs (Wolfgate's wins).

Bytes are read and written as they are, so line endings and a byte order mark survive. Rerunning rebuilds the folder.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import xeno_sources as xs  # noqa: E402

REPO = xs.REPO
BOM = b"\xef\xbb\xbf"
OUT = REPO /"Resources/Locale/en-US/_WF/Xenonids"
KEY_LINE = re.compile(r"^(-?[A-Za-z][A-Za-z0-9_-]*)[ \t]*=")
FLUENT_REF = re.compile(r"\{\s*(-?[A-Za-z][A-Za-z0-9_-]*)(?:\.[A-Za-z][A-Za-z0-9_-]*)?\s*[\)\},-]|\{\s*(-?[A-Za-z][A-Za-z0-9_-]*)(?:\.[A-Za-z][A-Za-z0-9_-]*)?\s*$")
WORD = re.compile(r"[A-Za-z][A-Za-z0-9_.-]*")


class Entry:
    def __init__(self, key: str, first: int, last: int, lines: list[bytes]):
        self.key = key
        self.first = first  # inclusive line index
        self.last = last    # inclusive line index
        self.lines = lines
        self.attached = 0   # comment lines directly above that belong to the entry

    def text(self) -> str:
        return "\n".join(x.decode("utf-8-sig", errors="replace").rstrip() for x in self.lines)

    def norm(self) -> str:
        return re.sub(r"[ \t]+", " ", self.text()).strip()


def split_lines(data: bytes) -> list[bytes]:
    return data.splitlines(keepends=True)


def parse(data: bytes) -> tuple[list[bytes], list[Entry]]:
    lines = split_lines(data)
    entries: list[Entry] = []
    cur: Entry | None = None
    for i, raw in enumerate(lines):
        line = raw.decode("utf-8-sig", errors="replace") if i == 0 else raw.decode("utf-8", errors="replace")
        m = KEY_LINE.match(line)
        if m:
            if cur:
                entries.append(cur)
            cur = Entry(m.group(1), i, i, [raw])
        elif line.startswith("#"):
            if cur:
                entries.append(cur)
                cur = None
        elif line.strip() == "":
            continue
        elif cur:  # indented, or a closing brace at column 0 that Fluent still accepts
            # include blank lines that sit between this continuation and the entry
            while cur.last + 1 < i:
                cur.last += 1
                cur.lines.append(lines[cur.last])
            cur.last = i
            cur.lines.append(raw)
    if cur:
        entries.append(cur)
    for e in entries:
        j = e.first - 1
        n = 0
        while j >= 0:
            t = lines[j].decode("utf-8", errors="replace")
            if t.startswith("#") and not t.startswith("##"):
                n += 1
                j -= 1
            else:
                break
        e.attached = n
    return lines, entries


def ftl_files(root: Path) -> list[Path]:
    return sorted(root.rglob("*.ftl"))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--rmc", default=None)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--list-loose", action="store_true")
    ap.add_argument("--list-shared", action="store_true")
    args = ap.parse_args()
    rmc = xs.find_rmc(args.rmc)
    rmc_loc = rmc / "Resources/Locale/en-US/_RMC14"

    # --- Wolfgate's keys (everything except our own output) -------------------------------------------------
    ours: dict[str, tuple[str, str]] = {}
    for f in ftl_files(REPO / "Resources/Locale/en-US"):
        if OUT in f.parents:
            continue
        _, ents = parse(f.read_bytes())
        for e in ents:
            ours.setdefault(e.key, (f.relative_to(REPO).as_posix(), e.norm()))

    # --- RMC's keys ------------------------------------------------------------------------------------------
    xeno_files = xs.xeno_locale_files(rmc)
    xeno_set = set(xeno_files)
    rmc_keys: dict[str, tuple[Path, Entry]] = {}
    parsed: dict[Path, tuple[list[bytes], list[Entry]]] = {}
    for f in ftl_files(rmc_loc):
        parsed[f] = parse(f.read_bytes())
        for e in parsed[f][1]:
            rmc_keys.setdefault(e.key, (f, e))
    rmc_other: set[str] = set()
    for f in ftl_files(rmc / "Resources/Locale/en-US"):
        if rmc_loc in f.parents:
            continue
        rmc_other |= {e.key for e in parse(f.read_bytes())[1]}

    # --- keys the xeno sources use ----------------------------------------------------------------------------
    strict: set[str] = set()
    loose: set[str] = set()
    prefixes: set[str] = set()
    for f in xs.xeno_code_files(rmc):
        text = xs.read_text(f)
        if f.suffix == ".yml":
            continue
        for m in re.finditer(r'"([A-Za-z][A-Za-z0-9_-]*(?:-[A-Za-z0-9_-]+)+)"', text):
            if m.group(1) in rmc_keys:
                (strict if re.search(r"(GetString|Loc|Popup|popup|Message)\b[^;]*" + re.escape(m.group(0)), text) else loose).add(m.group(1))
        for m in re.finditer(r'(?:\$|@)?"([A-Za-z][A-Za-z0-9_-]*-)(?:\{|"\s*\+)', text):
            if len(m.group(1)) >= 8:
                prefixes.add(m.group(1))
    protos = xs.xeno_prototype_files(rmc)
    ent_ids: set[str] = set()
    for f in protos:
        text = xs.read_text(f)
        for m in re.finditer(r"^\s*-?\s*(?:name|description|desc|suffix|popup\w*|message\w*|loc\w*|text|label)\s*:\s*['\"]?([A-Za-z][A-Za-z0-9_.-]*)['\"]?\s*$", text, re.M):
            if m.group(1) in rmc_keys:
                strict.add(m.group(1))
        for m in WORD.finditer(text):
            if m.group(0) in rmc_keys:
                loose.add(m.group(0))
        for m in re.finditer(r"^\s*(?:- )?id:\s*([A-Za-z0-9_]+)\s*$", text, re.M):
            ent_ids.add(m.group(1))
    for i in ent_ids:
        for k in (f"ent-{i}", f"alert-name-{i}", f"alert-description-{i}", f"reagent-name-{i}", f"job-name-{i}", f"job-description-{i}"):
            if k in rmc_keys:
                strict.add(k)
    for p in prefixes:
        for k in rmc_keys:
            if k.startswith(p):
                loose.add(k)

    # --- closure over references inside entries ---------------------------------------------------------------
    used = set(strict) | set(loose)
    # everything in the copied xeno files also counts for reference scanning
    queue = list(used) + [e.key for f in xeno_files for e in parsed[f][1]]
    seen = set(queue)
    undefined: set[str] = set()
    while queue:
        k = queue.pop()
        if k not in rmc_keys:
            continue
        entry = rmc_keys[k][1]
        for m in re.finditer(r"\{\s*(?:NUMBER\(|DATETIME\()?\s*(-?[A-Za-z][A-Za-z0-9_-]*)(?:\.[A-Za-z][A-Za-z0-9_-]*)?", entry.text()):
            ref = m.group(1)
            if ref.isupper() or ref.startswith("-") is False and ref.isupper():
                continue
            if ref not in rmc_keys and ref not in ours and ref not in rmc_other:
                undefined.add(f"{ref} (in {k})")
            if ref in rmc_keys and ref not in seen:
                seen.add(ref)
                used.add(ref)
                queue.append(ref)

    # --- build outputs ----------------------------------------------------------------------------------------
    dropped_same: list[str] = []
    dropped_diff: list[tuple[str, str]] = []
    dup_inside: list[str] = []
    claimed: dict[str, str] = {}
    outputs: dict[Path, bytes] = {}
    kept_keys = 0
    empty_files: list[str] = []

    def accept(key: str, entry: Entry, where: str) -> bool:
        if key in ours:
            if ours[key][1] == entry.norm():
                dropped_same.append(key)
            else:
                dropped_diff.append((key, ours[key][0]))
            return False
        if key in claimed:
            dup_inside.append(f"{key} ({claimed[key]} and {where})")
            return False
        claimed[key] = where
        return True

    for f in xeno_files:
        lines, ents = parsed[f]
        drop = [False] * len(lines)
        for e in ents:
            if not accept(e.key, e, f.relative_to(rmc_loc).as_posix()):
                for j in range(e.first - e.attached, e.last + 1):
                    drop[j] = True
            else:
                kept_keys += 1
        data = b"".join(l for l, d in zip(lines, drop) if not d)
        if not any(e for e in ents if not drop[e.first]):
            empty_files.append(f.relative_to(rmc_loc).as_posix())
            continue
        outputs[OUT / f.relative_to(rmc_loc)] = data

    # shared: keys defined in a non-xeno RMC ftl
    nl = b"\r\n"
    shared: list[bytes] = []
    shared_keys = []
    for k in sorted(used):
        f, e = rmc_keys[k]
        if f in xeno_set:
            continue
        if not accept(k, e, "rmc-shared"):
            continue
        shared_keys.append((k, f.relative_to(rmc_loc).as_posix()))
        body = b"".join(e.lines).removeprefix(BOM)
        if not body.endswith(b"\n"):
            body += nl
        shared.append(body)
    if shared:
        head = b"# Keys the xeno code uses that RMC-14 defines outside its xeno locale files." + nl + nl
        outputs[OUT / "rmc-shared.ftl"] = head + nl.join(b.rstrip(b"\r\n") + nl for b in shared)
    kept_keys += len(shared_keys)

    upstream_only = sorted(k for k in (strict | loose) if k in rmc_other and k not in ours)

    if not args.dry_run:
        if OUT.exists():
            for f in OUT.rglob("*.ftl"):
                f.unlink()
        for path, data in outputs.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)

    print(f"ftl files written: {len(outputs)} ({len(outputs) - (1 if shared else 0)} copied from xeno locale + "
          f"{'rmc-shared.ftl' if shared else 'no shared file'})")
    print(f"keys kept: {kept_keys} ({len(shared_keys)} in rmc-shared.ftl)")
    print(f"strict uses: {len(strict)}, loose-only uses: {len(loose - strict)}, closure additions: {len(used) - len(strict | loose)}")
    print(f"dropped, identical in Wolfgate: {len(dropped_same)}")
    print(f"dropped, Wolfgate text differs: {len(dropped_diff)}")
    for k, w in dropped_diff:
        print(f"  {k}  (Wolfgate: {w})")
    if empty_files:
        print("copied files left empty after dropping (not written): " + ", ".join(empty_files))
    if dup_inside:
        print("keys defined twice inside the RMC set (first kept):")
        for d in dup_inside:
            print("  " + d)
    if undefined:
        print("Fluent references to keys defined nowhere:")
        for u in sorted(undefined):
            print("  " + u)
    if upstream_only:
        print("keys used that only RMC's upstream locale defines and Wolfgate lacks:")
        for k in upstream_only:
            print("  " + k)
    if args.list_loose:
        for k in sorted(loose - strict):
            print(f"  loose {k}  <- {rmc_keys[k][0].relative_to(rmc_loc).as_posix()}")
    if args.list_shared:
        for k, w in shared_keys:
            print(f"  shared {k}  <- {w}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
