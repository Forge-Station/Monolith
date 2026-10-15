"""Reject incomplete or stale v4.1 preflight evidence before a final FullGame run."""
import argparse
import hashlib
import json
from pathlib import Path
from source_fingerprint import ROOT, capture


def check(evidence: Path | None):
    contract = json.loads((Path(__file__).with_name("fullgame_contract.json")).read_text(encoding="utf-8"))
    document = {}
    if evidence and evidence.is_file():
        document = json.loads(evidence.read_text(encoding="utf-8-sig"))
    fresh = document.get("sourceFingerprint") == capture()["sha256"]
    supplied = document.get("requirements", {})
    rows = []
    for requirement in contract["requirements"]:
        item = supplied.get(requirement["id"], {})
        status = "INCOMPLETE"
        reason = "No current native preflight proof"
        proofs = item.get("proofs", [])
        if item.get("status") == "PASS" and proofs and fresh:
            valid = True
            for proof in proofs:
                path = (evidence.parent / proof["path"]).resolve()
                if not path.is_relative_to(evidence.parent.resolve()) or not path.is_file():
                    valid = False
                    break
                if hashlib.sha256(path.read_bytes()).hexdigest() != proof.get("sha256"):
                    valid = False
                    break
            status = "PASS" if valid else "FAIL"
            reason = "Verified proof hashes" if valid else "Missing or changed proof file"
        elif item:
            declared = item.get("status", "INCOMPLETE")
            status = declared if fresh and declared in {"FAIL", "INCOMPLETE", "BLOCKED"} else "INCOMPLETE"
            reason = ("PASS declaration has no verifiable proof" if declared == "PASS"
                      else item.get("reason", reason)) if fresh else "Preflight source fingerprint is stale"
        rows.append({**requirement, "status": status, "reason": reason})
    return {"status": "READY" if all(row["status"] == "PASS" for row in rows) else "NOT_READY",
            "scope": contract["scope"], "deferredChecks": contract["deferredChecks"],
            "freshEvidence": fresh, "requirements": rows,
            "deferredMeasurements": contract.get("ownerDeferredMeasurements", [])}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--evidence", type=Path)
    parser.add_argument("--json", action="store_true")
    options = parser.parse_args()
    evidence = options.evidence
    pointer = ROOT / ".kias-benchmark/CURRENT_PREFLIGHT.txt"
    if evidence is None and pointer.is_file():
        evidence = Path(pointer.read_text(encoding="utf-8-sig").strip())
    result = check(evidence)
    if options.json:
        print(json.dumps(result, indent=2))
    else:
        print("KIAS v4.1 strict preflight: " + result["status"])
        for item in result["deferredChecks"]:
            print(f"  DEFERRED_REQUIRED {item['id']}: see DEFERRED_CHECKS.md")
        for item in result["deferredMeasurements"]:
            print(f"  DEFERRED_REQUIRED {item['id']}: {item['reason']}")
        for row in result["requirements"]:
            print(f"  {row['status']:10} {row['id']}: {row['reason']}")
    raise SystemExit(0 if result["status"] == "READY" else 1)
