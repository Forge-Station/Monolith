"""Verify eight real hull compatibility fixtures without certifying full gameplay."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from source_fingerprint import ROOT

HULLS = ("Striks", "SPunisher", "seeker", "Incinerator", "dreadnought", "Corvet", "briar", "achilles")
STAGES = ("powered-physical-graph", "data-cut", "data-restored", "core-power-cut", "core-power-restored")


def verify(item):
    assert item["status"] == "PASS" and item["fixture"] == "AUX_FIXTURE", "Missing native hull proof"
    assert item["originalFloorCount"] > 0, "Missing original hull floor"
    placements = item["placements"]
    assert len(placements) == len({tuple(value) for value in placements.values()}) == 5, "Overlapping fixture machines"
    stages = item["checkpoints"]
    assert [stage["stage"] for stage in stages] == list(STAGES), "Missing hull recovery stage"
    for index in (0, 2, 4):
        stage = stages[index]
        assert stage["active"] and stage["running"] and stage["recorded"] > 0, "Physical graph unavailable"
        assert len(stage["devices"]) == len({device["uid"] for device in stage["devices"]}) == 4
        assert all(device["online"] and device["needsPower"] and device["powered"] and device["nativeProvider"]
                   for device in stage["devices"]), "Native power or DATA bypassed"
    assert not stages[1]["running"] and not stages[3]["running"] and not stages[3]["active"], "Disruption ineffective"
    assert stages[1]["active"] and all(device["powered"] and device["needsPower"] and device["nativeProvider"]
                                     for device in stages[1]["devices"]), "DATA cut also bypassed native power"
    assert not stages[3]["devices"][0]["powered"], "Core voltage loss not observed"
    assert stages[2]["recorded"] > stages[1]["recorded"], "No actual graph output after DATA recovery"
    assert stages[4]["recorded"] > stages[3]["recorded"], "No actual graph output after power recovery"
    scanner = stages[4]["devices"][3]["uid"]
    covered = [row for row in item["roomGeometry"]["scanners"] if row["uid"] == scanner]
    assert len(covered) == 1 and covered[0]["tileCount"] > 0 and covered[0]["cells"], "Missing real scanner coverage"
    assert [covered[0]["x"], covered[0]["y"]] == placements["KiasRoomScanner"], "Scanner placement mismatch"


def validate(directory, trx):
    tree = ET.parse(trx)
    counts = tree.find(".//{*}Counters").attrib
    assert int(counts["total"]) >= 8 and int(counts["total"]) == int(counts["passed"]), "Hull regression incomplete"
    assert int(counts["failed"]) == int(counts["notExecuted"]) == 0, "Hull regression failed or skipped"
    tests = [row.attrib for row in tree.findall(".//{*}UnitTestResult")]
    proofs = []
    for hull in HULLS:
        name = f'RealHullPowersPhysicalGraphAndRecoversData("{hull}")'
        assert any(row["testName"] == name and row["outcome"] == "Passed" for row in tests), f"Missing native test: {hull}"
        path = directory / f"hull-{hull}.json"
        item = json.loads(path.read_text(encoding="utf-8-sig"))
        assert item["hull"] == hull, "Hull binding mismatch"
        expected = f"/SharedMaps/_Forge/Shuttles/Mercenary/{hull}.yml"
        assert item["sourceMap"] == expected, "Different source hull"
        assert item["sourceHash"] == hashlib.sha256((ROOT / ("Resources" + expected)).read_bytes()).hexdigest(), "Stale hull map"
        verify(item)
        proofs.append(dict(hull=hull, file=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                           sourceHash=item["sourceHash"], nativeStages=len(item["checkpoints"])))
    return dict(status="PASS", compatibleHullClasses=8, fullGameplayCoverage=False, performanceEligible=False,
                scope="Native power, physical graph/recorder, scanner geometry, DATA and core-power recovery only; full combat/FTL coverage remains separate",
                proofs=proofs)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    parser.add_argument("trx", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = validate(args.directory, args.trx)
    args.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("status", "compatibleHullClasses", "fullGameplayCoverage")}))
