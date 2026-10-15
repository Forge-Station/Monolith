import csv
import json
import tempfile
import unittest
from pathlib import Path

from instrument_physics_queries import SIGNATURES, instrument
from native_proof import lines
from physics_experiment import plan, report, write_plan
from physics_report import read_run


class QueryCalibrationInstrumentationTests(unittest.TestCase):
    def test_production_engine_is_rejected_before_any_write(self):
        engine = Path(__file__).resolve().parents[2] / 'RobustToolbox'
        source = engine / 'Robust.Shared/Physics/B2DynamicTree.cs'
        before = source.read_bytes()
        with self.assertRaisesRegex(ValueError, 'production physics'):
            instrument(engine)
        self.assertEqual(source.read_bytes(), before)

    def test_short_performance_keeps_stable_idle_and_real_collision(self):
        document=plan(2,short=True)
        self.assertFalse(document['functionalProbe'])
        self.assertEqual(document['measuredTicks'],7200)
        self.assertEqual(document['collisionTick'],1800)
        self.assertGreater(document['measuredTicks']-document['clearTick']-document['settleTicks'],1800)
        with self.assertRaises(ValueError):plan(2,probe=True,short=True)

    def test_upgrade_old_instrumentation_and_repeat(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / "Robust.Shared/Physics/B2DynamicTree.cs"
            path.parent.mkdir(parents=True)
            path.write_text("        #region Queries\n" + "\n".join(
                "        " + signature + "\n        {\n        }" for signature in SIGNATURES), encoding="utf-8")
            first = instrument(root)
            current = path.read_text(encoding="utf-8")
            self.assertEqual(current.count("if (DiagnosticQueriesEnabled)"), 5)
            self.assertEqual(instrument(root)["sha256"], first["sha256"])
            legacy = current.replace('        public static bool DiagnosticQueriesEnabled { get; } = '
                                     'System.Environment.GetEnvironmentVariable("NATIVE_LAB_PHYSICS_QUERY_COUNTERS") != "0";\n', '')
            legacy = legacy.replace("if (DiagnosticQueriesEnabled) ", "")
            path.write_text(legacy, encoding="utf-8")
            self.assertEqual(instrument(root)["sha256"], first["sha256"])


class PhysicsExperimentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "experiment"
        self.document = write_plan(self.root, 2, True)

    def write(self, path, value):
        path.write_text(json.dumps(value), encoding="utf-8")

    def populate(self):
        inputs = sorted(lines(self.root / "scenario.jsonl"), key=lambda row: (row["tick"], row["id"]))
        for entry in self.document["runs"]:
            path = self.root / entry["id"]
            path.mkdir()
            self.write(path / "run-manifest.json", dict(physicsMode=entry["physicsMode"], variant=entry["variant"],
                physicsDeviceGroup="KiasPrototypes", physicsQueryCountersEnabled=entry["queryCounters"],
                diagnosticTimeScale=1, adminLogProbe=0, networkClients=0, nativeNpcPauseWhenNoPlayers=False,
                nativeLabSha256="lab", nativeGameDriversSha256="drivers", telemetrySha256="telemetry",
                assemblySha256="assembly-" + entry["variant"], mapSha256="map-" + entry["variant"],
                performanceEligible=not self.document["functionalProbe"]))
            self.write(path / "started.json", dict(scenarioSha256=self.document["scenarioSha256"],
                plannedEvents=80, ships=40, warmupTicks=1800, measuredTicks=self.document["measuredTicks"]))
            self.write(path / "completed.json", dict(status="PARTIAL_NATIVE_DRY_RUN_COMPLETE",
                ships=40, measuredTicks=self.document["measuredTicks"], nativeCompleted=80, deliveredEvents=80))
            events, outcomes = [], []
            for item in inputs:
                events.append(dict(id=item["id"], ship=item["ship"], type=item["type"],
                    scheduledTick=item["tick"], actualTick=item["tick"], engineTick=1800 + item["tick"],
                    nativeEffect=dict(asynchronous=True, deadlineTicks=1800)))
                outcomes.append(dict(id=item["id"], ship=item["ship"], type=item["type"],
                    scheduledTick=item["tick"], nativeStartedTick=1800 + item["tick"],
                    nativeCompletedTick=1980 + item["tick"], relativeCompletedTick=180 + item["tick"], latencyTicks=180,
                    postconditionTrue=True, proof={"realContactOrRecovery": True}))
            for name, rows in (("events.jsonl", events), ("native-outcomes.jsonl", outcomes)):
                (path / name).write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
            (path / "server.log").write_text("[INFO] complete\n", encoding="utf-8")
            snapshot = dict(rows=[dict(category="fixture", Bodies=40, Fixtures=40, Collidable=40, Proxies=40, Awake=0, Contacts=0)],
                            queryCountersEnabled=entry["queryCounters"], fixtureTreeTraversals=0,
                            runtimePaused=False, instrumentationMs=1, instrumentationBytes=100)
            self.write(path / "physics-before.json", snapshot)
            self.write(path / "physics-final.json", dict(snapshot, runtimePaused=entry["physicsMode"] == "C",
                fixtureTreeTraversals=500 if entry["queryCounters"] else 0))
            if entry["physicsMode"] == "D":
                self.write(path / "physics-D-affected.json", [dict(prototype="KiasCore", previouslyCollidable=True)])
            fields = ["relative_tick", "physics_ms", "physics_allocated_bytes", "whole_tick_ms", "content_engine_ms",
                      "driver_ms", "observer_ms", "whole_allocated_bytes", "gc_pause_ms"]
            with (path / "ticks.csv").open("w", newline="", encoding="utf-8") as stream:
                writer = csv.DictWriter(stream, fieldnames=fields)
                writer.writeheader()
                for tick in range(self.document["measuredTicks"]):
                    writer.writerow(dict.fromkeys(fields, 1) | {"relative_tick": tick})

    def test_plan_counterbalances_and_keeps_native_synchronous_waves(self):
        self.assertEqual([row["label"] for row in self.document["runs"][:8]], ["A", "B", "C", "D2", "D2", "C", "B", "A"])
        inputs = lines(self.root / "scenario.jsonl")
        self.assertEqual(len(inputs), 80)
        self.assertEqual({row["tick"] for row in inputs}, {120, 780})
        self.assertFalse(self.document["finalABBA"])
        with self.assertRaises(ValueError):
            plan(1)

    def test_completed_probe_preserves_open_overhead_and_performance_limit(self):
        self.populate()
        result = report(self.root)
        self.assertEqual(result["status"], "REPLICATED_PHYSICS_DIAGNOSTIC_COMPLETE")
        self.assertFalse(result["performanceEligible"])
        self.assertFalse(result["fullV3Ready"])
        self.assertEqual(result["runs"][0]["settledTick"], 1260)
        self.assertEqual(result["varianceOfRunMedians"]["B"]["idle_before"]["physics_ms"]["samples"], 2)

    def test_completed_short_performance_report_uses_its_own_plan(self):
        self.root = Path(self.temporary.name) / "short-experiment"
        self.document = write_plan(self.root, 2, False, short=True)
        self.populate()
        result = report(self.root)
        self.assertTrue(result["performanceEligible"])
        self.assertEqual(result["runs"][0]["settledTick"], 2940)
        self.assertEqual(result["runs"][0]["summary"]["measuredTicks"], 7200)
        self.assertEqual(len(result["runs"]), 12)
        self.document["shortPerformanceStudy"] = False
        self.write(self.root / "experiment-plan.json", self.document)
        with self.assertRaisesRegex(AssertionError, "Changed experiment plan"):
            report(self.root)

    def test_missing_counter_toggle_is_rejected(self):
        self.populate()
        path = self.root / "r1-A/physics-before.json"
        snapshot = json.loads(path.read_text())
        snapshot["queryCountersEnabled"] = None
        self.write(path, snapshot)
        with self.assertRaisesRegex(AssertionError, "toggle unavailable"):
            report(self.root)

    def test_disabled_counter_must_really_stop_counting(self):
        self.populate()
        path = self.root / "r1-counter-off/physics-final.json"
        snapshot = json.loads(path.read_text())
        snapshot["fixtureTreeTraversals"] = 2
        self.write(path, snapshot)
        with self.assertRaisesRegex(AssertionError, "still count"):
            report(self.root)

    def test_recovery_not_included_in_idle_after(self):
        self.populate()
        summary = read_run(self.root / "r1-B", (120, 780, 1260))
        self.assertIn("recovery", summary["windows"])
        self.assertEqual(summary["windows"]["idle_after"]["ticksOverBudget"], 0)

    def test_inventory_mismatch_is_not_a_causal_comparison(self):
        self.populate()
        path = self.root / "r1-C/physics-before.json"
        snapshot = json.loads(path.read_text())
        snapshot["rows"][0]["Bodies"] += 1
        self.write(path, snapshot)
        with self.assertRaisesRegex(AssertionError, "inventory mismatch"):
            report(self.root)

    def test_corrupt_tick_evidence_is_rejected_before_phase_statistics(self):
        self.populate()
        path = self.root / "r1-B/ticks.csv"
        original = path.read_text(encoding="utf-8")
        for old, new, message in (("1,1,1,1,1,1,1,1,1", "0,1,1,1,1,1,1,1,1", "Tick sequence"),
                                  ("1,1,1,1,1,1,1,1,1", "1.5,1,1,1,1,1,1,1,1", "Tick sequence"),
                                  ("1,1,1,1,1,1,1,1,1", "1,1,inf,1,1,1,1,1,1", "Invalid physics metric")):
            with self.subTest(corruption=new):
                path.write_text(original.replace(old, new, 1), encoding="utf-8")
                with self.assertRaisesRegex(AssertionError, message):
                    read_run(path.parent, (120, 780, 1260))

    def test_unpaused_runtime_c_is_rejected(self):
        self.populate()
        path = self.root / "r1-C/physics-final.json"
        snapshot = json.loads(path.read_text())
        snapshot["runtimePaused"] = False
        self.write(path, snapshot)
        with self.assertRaisesRegex(AssertionError, "mode ineffective"):
            report(self.root)


if __name__ == "__main__":
    unittest.main()
