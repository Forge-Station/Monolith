"""Offline hull inventory; this does not certify native fixture compatibility."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

import yaml
from lab import ROOT, MapLoader


def inspect(path):
    content = path.read_bytes()
    value = yaml.load(content.decode("utf-8-sig"), Loader=MapLoader)
    counts = Counter()
    for group in value.get("entities", []):
        counts[group.get("proto", "<grid>")] += len(group.get("entities", []))
    missing = [name for name in ("KiasCore", "KiasControllerRack", "KiasDataCable", "KiasProgrammableController")
               if not counts[name]]
    if not counts["KiasRoomScanner"] and not counts["KiasAdvancedRoomScanner"]:
        missing.append("KiasRoomScanner/KiasAdvancedRoomScanner")
    return dict(path=str(path.relative_to(ROOT)), sha256=hashlib.sha256(content).hexdigest(),
                entities=sum(counts.values()), kiasInventory={name: count for name, count in sorted(counts.items()) if name.startswith("Kias")},
                missingMappedKiasPrerequisites=missing,
                status="MISSING_MAPPED_KIAS_PREREQUISITES" if missing else "MAPPED_KIAS_PREREQUISITES_PRESENT",
                nativeProofLinked=False)


def report(directory):
    rows = [inspect(path) for path in sorted(directory.glob("*.yml"))]
    assert len(rows) >= 8, "At least eight distinct authored hull maps must be inspected"
    return dict(status="OFFLINE_INVENTORY_ONLY", distinctHullsInspected=len(rows), nativeCompatibleHullsProven=0,
                scope="NativeLab currently binds labBriar and fixed Briar coordinates. Missing mapped prerequisites require separate auxiliary fixtures; this inventory is not a waiver or PASS for eight-hull gameplay.",
                hulls=rows)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", type=Path, default=ROOT / "Resources/Maps/_Forge/Shuttles/Archive/Mercenary")
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    result = report(options.directory)
    options.output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("status", "distinctHullsInspected", "nativeCompatibleHullsProven")}))
