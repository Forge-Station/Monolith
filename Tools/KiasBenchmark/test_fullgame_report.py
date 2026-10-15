import csv
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from fullgame_report import tick_statistics, wave_statistics, verify_scaling_recovery, smoke_report
from fullgame_scenario import capabilities, generate
from world_smoke_scenario import DURATIONS, GROUPS


class FullGameEvidenceTests(unittest.TestCase):
    def test_room_overlap_repeats_door_inputs_during_breach_and_repair(self):
        steps=GROUPS['room_overlap']
        self.assertEqual(sum(kind=='geometry.breach' for _,kind in steps),2)
        self.assertEqual(sum(kind=='geometry.repair' for _,kind in steps),2)
        for tick,kind in steps:
            if kind in ('geometry.breach','geometry.repair'):
                self.assertIn((tick+1,'door.open'),steps)
                self.assertIn((tick+121,'door.close'),steps)
        self.assertGreater(DURATIONS['room_overlap'],max(tick for tick,_ in steps)+600)

    def test_background_is_seeded_and_does_not_overlap_composite_restores(self):
        first = generate(40, 45000, 7)
        self.assertEqual(first, generate(40, 45000, 7))
        self.assertNotEqual(first, generate(40, 45000, 8))
        self.assertEqual(len(first), len({item['id'] for item in first}))
        self.assertTrue(all(0 <= item['tick'] < 45000 for item in first))
        background = [item for item in first if not item['synchronous']]
        self.assertEqual({item['ship'] for item in background}, {f'ship-{i:04d}' for i in range(1, 41)})
        self.assertTrue(all(1800 <= item['tick'] - int(item['wave'].split('-')[1]) < 3120 for item in background))

    def test_reopened_requirements_have_native_smoke_and_full_fleet_waves(self):
        required = {"radiation.start", "radiation.clear", "light.power_off", "light.power_on",
                    "light.command_off", "light.command_on", "hull.damage", "hull.repair",
                    "dock.dock", "dock.undock", "ammo.finite_cycle"}
        inputs = generate(40, 45000)
        self.assertEqual(capabilities()["deferredChecks"], [])
        self.assertTrue(required <= {kind for _, kind in GROUPS["deferred"]})
        for kind in required:
            wave = [item for item in inputs if item["type"] == kind]
            self.assertGreaterEqual(len(wave), 40, kind)
            for tick in {item['tick'] for item in wave}:
                self.assertEqual(len([item for item in wave if item['tick']==tick]),40,kind)
            self.assertEqual(len({item["tick"] for item in wave}), 2 if kind.startswith('dock.') else 1, kind)
            self.assertTrue(all(item["synchronous"] for item in wave), kind)

    def test_required_events_preserve_collision_and_geometry(self):
        inputs = generate(2, 45000)
        types = {item["type"] for item in inputs}
        deferred = {kind for item in capabilities()["deferredChecks"] for kind in item["events"]}
        self.assertFalse(types & deferred)
        self.assertTrue({"collision.damage_threshold", "geometry.breach", "pdc.empty", "crew.death"} <= types)

    def test_combined_systems_retains_every_event(self):
        expected = sum(len(GROUPS[name]) for name in ("fanout", "crew_power", "gun"))
        self.assertEqual(len(GROUPS["systems"]), expected)
        self.assertLess(max(tick for tick, _ in GROUPS["systems"]), DURATIONS["systems"])

    def test_wave_rejects_split_engine_ticks(self):
        inputs = [dict(id=str(i), ship=str(i), tick=120, type="crew.death", wave="death") for i in range(2)]
        events = [dict(id=str(i), actualTick=120, engineTick=500+i, nativeEffect={"asynchronous": True}) for i in range(2)]
        with self.assertRaisesRegex(AssertionError, "span engine ticks"):
            wave_statistics(inputs, events, [], 2)

    def test_wave_rejects_missing_ship(self):
        with self.assertRaisesRegex(AssertionError, "Incomplete fleet wave"):
            wave_statistics([dict(id="x", ship="1", tick=120, type="gun.fire", wave="gun")], [], [], 2)

    def test_tick_evidence_rejects_nan_and_missing_samples(self):
        columns = ("relative_tick", "whole_tick_ms", "physics_ms", "driver_ms", "observer_ms",
                   "whole_allocated_bytes", "thread_allocated_bytes", "gc_pause_ms")
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "ticks.csv"
            with path.open("w", newline="") as stream:
                writer = csv.DictWriter(stream, columns)
                writer.writeheader()
                writer.writerow({name: "nan" if name == "whole_tick_ms" else 0 for name in columns})
            with self.assertRaisesRegex(AssertionError, "Invalid tick metric"):
                tick_statistics(path, 1)
            with self.assertRaisesRegex(AssertionError, "Missing tick samples"):
                tick_statistics(path, 2)

    def test_scaling_recovery_rejects_lost_cards_faults_and_pending_work(self):
        valid = dict(activeGrids=40, runningCards=1040, faultedCards=0, expectedOfflineGrids=0,
                     queues=dict.fromkeys(("_events", "_work", "_commands", "_boots", "_finalEvents", "_finalCommands", "pendingDispatch"), 0))
        verify_scaling_recovery(valid, 40)
        for changed in (dict(runningCards=1039), dict(activeGrids=39), dict(faultedCards=1),
                        dict(expectedOfflineGrids=1), dict(queues={}), dict(queues=dict(pendingDispatch=1)),
                        dict(queues={key: value for key, value in valid["queues"].items() if key != "pendingDispatch"})):
            with self.subTest(changed=changed), self.assertRaises(AssertionError):
                verify_scaling_recovery(valid | changed, 40)

    def test_smoke_cannot_pass_with_empty_or_incomplete_final_telemetry(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = root / "B"
            run.mkdir()
            fingerprints = dict(assemblySha256="assembly", nativeLabSha256="lab", nativeGameDriversSha256="drivers")
            files = {root / "suite-status.json": dict(results=[dict(status="PASS", variant="B", group="systems", path=str(run))]),
                     root / "baseline-manifest.json": dict(builds=[dict(label="B", **fingerprints)]),
                     run / "started.json": dict(ships=40), run / "run-manifest.json": fingerprints}
            for path, value in files.items():
                path.write_text(json.dumps(value))
            (root / "systems.jsonl").write_text("")
            for telemetry in ({}, dict(activeGrids=40, runningCards=1040, faultedCards=0, expectedOfflineGrids=0, queues={})):
                with self.subTest(telemetry=telemetry), patch("fullgame_report.validate", return_value={}):
                    (run / "final-telemetry.json").write_text(json.dumps(telemetry))
                    with self.assertRaises(AssertionError):
                        smoke_report(root)


if __name__ == "__main__":
    unittest.main()
