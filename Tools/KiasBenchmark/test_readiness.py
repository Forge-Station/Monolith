import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import readiness


class ReadinessEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="kias-readiness-")
        self.directory = Path(self.temporary.name)
        self.evidence = self.directory / "preflight.json"
        self.contract = json.loads(Path(__file__).with_name("fullgame_contract.json").read_text(encoding="utf-8"))
        self.fingerprint = patch("readiness.capture", return_value={"sha256": "current-source"})
        self.fingerprint.start()
        self.addCleanup(self.fingerprint.stop)
        self.addCleanup(self.temporary.cleanup)

    def write(self, proofs, fingerprint="current-source"):
        self.evidence.write_text(json.dumps({"sourceFingerprint": fingerprint,
            "requirements": {row["id"]: {"status": "PASS", "proofs": proofs}
                             for row in self.contract["requirements"]}}), encoding="utf-8")

    def test_unproven_pass_is_not_ready(self):
        self.write([])
        result = readiness.check(self.evidence)
        self.assertEqual(result["status"], "NOT_READY")
        self.assertTrue(all(row["status"] == "INCOMPLETE" for row in result["requirements"]))

    def test_owner_measurement_deferral_cannot_waive_missing_native_proof(self):
        proof = self.directory / "proof.json"
        proof.write_text("original", encoding="utf-8")
        self.write([{"path": proof.name, "sha256": hashlib.sha256(proof.read_bytes()).hexdigest()}])
        document = json.loads(self.evidence.read_text(encoding="utf-8"))
        del document["requirements"]["native_collision"]
        self.evidence.write_text(json.dumps(document), encoding="utf-8")
        result = readiness.check(self.evidence)
        self.assertEqual(result["status"], "NOT_READY")
        self.assertFalse(result["deferredMeasurements"])
        self.assertEqual(next(row for row in result["requirements"] if row["id"] == "native_collision")["status"], "INCOMPLETE")

    def test_changed_proof_fails(self):
        proof = self.directory / "native-proof.json"
        proof.write_text("original", encoding="utf-8")
        self.write([{"path": proof.name, "sha256": hashlib.sha256(proof.read_bytes()).hexdigest()}])
        self.assertEqual(readiness.check(self.evidence)["status"], "READY")
        proof.write_text("changed", encoding="utf-8")
        result = readiness.check(self.evidence)
        self.assertEqual(result["status"], "NOT_READY")
        self.assertTrue(all(row["status"] == "FAIL" for row in result["requirements"]))

    def test_stale_source_invalidates_evidence(self):
        proof = self.directory / "native-proof.json"
        proof.write_text("original", encoding="utf-8")
        self.write([{"path": proof.name, "sha256": hashlib.sha256(proof.read_bytes()).hexdigest()}], "old-source")
        self.assertEqual(readiness.check(self.evidence)["status"], "NOT_READY")


if __name__ == "__main__":
    unittest.main()
