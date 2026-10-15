import json
import struct
import tempfile
import unittest
from pathlib import Path
from validate_scanner_ui import validate


class ScannerUiEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write("client-completed.json", dict(realDesktopRenderer=True))
        self.write("server-completed.json", dict(cases=4))
        for index in range(4):
            geometry = dict(Target=index, Grid=1, Shape=3, Radius=7, Revision=1,
                            RoomStatus="Ok" if index < 2 else "ExteriorSector",
                            TileCount=9 if index == 0 else 1, DoorCount=4 if index == 0 else 0,
                            Cells=list(range(9)) if index == 0 else [1],
                            BoundaryCells=list(range(4)) if index == 0 else [], PreviewTruncated=False)
            self.write(f"server-case-{index}.json", dict(powered=True, position=index, geometry=geometry))
            self.write(f"client-case-{index}.json", dict(actualNetworkBui=True, actualWorldOverlay=True,
                       hiddenRangeEditor=True, readableRussianText=True, details="Покрытие: помещение.", geometry=geometry))
            (self.root / f"scanner-{index}.png").write_bytes(b"\x89PNG\r\n\x1a\n" + b"\0" * 8 + struct.pack(">II", 1600, 900))

    def write(self, name, value):
        (self.root / name).write_text(json.dumps(value), encoding="utf-8")

    def test_accepts_matching_actual_case_evidence(self):
        self.assertEqual(validate(self.root)["status"], "PASS")

    def test_rejects_different_client_cells(self):
        path = self.root / "client-case-2.json"
        value = json.loads(path.read_text()); value["geometry"]["Cells"] = [99]
        self.write(path.name, value)
        with self.assertRaises(AssertionError): validate(self.root)

    def test_rejects_mojibake_even_if_marked_readable(self):
        path = self.root / "client-case-0.json"
        value = json.loads(path.read_text()); value["details"] = "РџРѕРєСЂС‹С‚РёРµ"
        self.write(path.name, value)
        with self.assertRaises(AssertionError): validate(self.root)

    def test_rejects_missing_server_acknowledgement(self):
        (self.root / "server-completed.json").unlink()
        with self.assertRaises(FileNotFoundError): validate(self.root)

    def test_room_localization_is_readable_utf8(self):
        value = (Path(__file__).resolve().parents[2] / "Resources/Locale/ru-RU/_Forge/KIAS/rooms.ftl").read_text(encoding="utf-8")
        self.assertIn("kias-room-status-ok = замкнутое помещение", value)
        self.assertNotIn("Рџ", value)


if __name__ == "__main__": unittest.main()
