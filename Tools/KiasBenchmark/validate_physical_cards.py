"""Require native physical evidence for every current controller preset."""
import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

import yaml
from lab import ROOT


def validate(artifacts, trx):
    counts = ET.parse(trx).find(".//{*}Counters").attrib
    assert int(counts["total"]) > 0 and int(counts["total"]) == int(counts["passed"]), "Regression did not pass completely"
    assert int(counts["failed"]) == int(counts["notExecuted"]) == 0, "Regression contains failures or skipped tests"
    map_hash = hashlib.sha256((ROOT / "Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml").read_bytes()).hexdigest()
    presets = (ROOT / "Resources/Prototypes/_Forge/KIAS/controller_presets.yml").read_bytes()
    preset_hash = hashlib.sha256(presets).hexdigest()
    required = {item["id"] for item in yaml.safe_load(presets) if item["type"] == "kiasControllerProgram"}
    assert len(required) == 27, "Unexpected controller preset set"
    proofs = []
    for path in sorted(artifacts.glob("physical-*.json")):
        item = json.loads(path.read_text(encoding="utf-8-sig"))
        assert item["status"] == "PASS" and not item.get("error"), f"Physical program failed: {path.name}"
        assert item["mapSha256"] == map_hash and item["presetSha256"] == preset_hash, f"Stale physical fixture: {path.name}"
        program = item["actualCardProgramSha256"]
        assert len(program) == 64 and all(char in "0123456789abcdef" for char in program), "Missing actual card program hash"
        proofs.append(dict(preset=item["preset"], path=str(path), actualCardProgramSha256=program,
                           sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    assert {item["preset"] for item in proofs} == required, "Missing physical controller preset proofs"
    return dict(status="PASS", physicalPrograms=len(required), physicalScenarios=len(proofs),
                regressionPassed=int(counts["passed"]), mapSha256=map_hash, presetSha256=preset_hash, proofs=proofs)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("artifacts", type=Path)
    parser.add_argument("trx", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    options = parser.parse_args()
    result = validate(options.artifacts, options.trx)
    options.output.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: result[key] for key in ("status", "physicalPrograms", "physicalScenarios", "regressionPassed")}))
