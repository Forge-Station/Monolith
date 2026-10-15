import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from partial_archive import package


class PartialArchiveTests(unittest.TestCase):
    def test_failed_run_keeps_evidence_and_verifiable_hashes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "launcher-status.json").write_text('{"status":"FAILED_INCOMPLETE"}')
            (root / "server.log").write_text("failure evidence")
            for folder in ("builds", "bin", "data", "obj"):
                (root / folder).mkdir()
                (root / folder / "large-file.log").write_text("excluded")
            (root / "assembly.dll").write_bytes(b"excluded")
            for _ in range(2):
                with zipfile.ZipFile(package(root)) as archive:
                    self.assertIsNone(archive.testzip())
                    hashes = json.loads(archive.read("ARCHIVE_SHA256.json"))
                    self.assertEqual(set(hashes), {"launcher-status.json", "server.log"})
                    for name, expected in hashes.items():
                        self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), expected)
                    self.assertEqual(json.loads(archive.read("launcher-status.json"))["status"], "FAILED_INCOMPLETE")
                    self.assertEqual(set(archive.namelist()), set(hashes) | {"ARCHIVE_SHA256.json", "ANALYSIS_README.txt"})
