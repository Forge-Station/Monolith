import copy
import unittest

from validate_hulls import STAGES, verify


class HullEvidenceTests(unittest.TestCase):
    def fixture(self):
        devices = [dict(uid=str(index), online=True, needsPower=True, powered=True, nativeProvider=True) for index in range(4)]
        stages = [dict(stage=stage, active=index != 3, running=index not in (1, 3),
                       recorded=(1, 1, 2, 2, 3)[index], devices=copy.deepcopy(devices)) for index, stage in enumerate(STAGES)]
        stages[3]["devices"][0]["powered"] = False
        return dict(status="PASS", fixture="AUX_FIXTURE", originalFloorCount=100,
                    placements={name: [index, 0] for index, name in enumerate(("core", "rack", "recorder", "KiasRoomScanner", "APC"))},
                    checkpoints=stages, roomGeometry=dict(scanners=[dict(uid='3', x=3, y=0, tileCount=2, cells=[[3, 0], [3, -1]])]))

    def test_accepts_observed_native_recovery(self):
        verify(self.fixture())

    def test_rejects_bypassed_power_and_missing_recovery(self):
        for field in ("needsPower", "powered", "nativeProvider"):
            with self.subTest(field=field):
                item = self.fixture()
                item["checkpoints"][0]["devices"][0][field] = False
                with self.assertRaisesRegex(AssertionError, "bypassed"):
                    verify(item)
        for index in (2, 4):
            item = self.fixture()
            item["checkpoints"][index]["recorded"] = item["checkpoints"][index - 1]["recorded"]
            with self.assertRaisesRegex(AssertionError, "actual graph output"):
                verify(item)

    def test_rejects_overlapping_machines_and_ineffective_disruption(self):
        item = self.fixture()
        item["placements"]["rack"] = item["placements"]["core"]
        with self.assertRaisesRegex(AssertionError, "Overlapping"):
            verify(item)
        item = self.fixture()
        item["checkpoints"][1]["running"] = True
        with self.assertRaisesRegex(AssertionError, "ineffective"):
            verify(item)

    def test_rejects_empty_or_misbound_scanner_geometry(self):
        item = self.fixture()
        item["roomGeometry"]["scanners"][0]["cells"] = []
        with self.assertRaisesRegex(AssertionError, "coverage"):
            verify(item)
        item = self.fixture()
        item["roomGeometry"]["scanners"][0]["x"] = 4
        with self.assertRaisesRegex(AssertionError, "placement mismatch"):
            verify(item)


if __name__ == "__main__":
    unittest.main()
