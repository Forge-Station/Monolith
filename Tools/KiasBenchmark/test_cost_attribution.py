import csv
import json
from pathlib import Path
import tempfile
import unittest
from cost_attribution import read_run, group, block_interval
from instrument_system_profile import instrument


class CostTests(unittest.TestCase):
    def test_exclusive_accounting_keeps_nested_phases_out_of_parent(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            for name,value in {"run-manifest.json":{"performanceEligible":True},"completed.json":{"measuredTicks":2},"profile-config.json":{"phaseCounters":True,"systemCounters":True}}.items():
                (root/name).write_text(json.dumps(value),encoding="utf-8")
            (root/"ticks.csv").write_text("relative_tick,whole_tick_ms,whole_allocated_bytes,gc_pause_ms\n0,5,100,0\n1,5,100,0\n",encoding="utf-8")
            (root/"system-phases.csv").write_text("relative_tick,kind,name,calls,inclusive_ms,exclusive_ms,exclusive_allocated_bytes\n0,system,PhysicsSystem,1,4,3,20\n0,phase,IoEmit,1,1,1,10\n1,system,PhysicsSystem,1,4,3,20\n1,phase,IoEmit,1,1,1,10\n",encoding="utf-8")
            result=read_run(root)
            self.assertEqual(result["groups"]["physics"]["meanMs"],3)
            self.assertEqual(result["groups"]["runtime_io_actuation"]["meanMs"],1)
            self.assertEqual(result["groups"]["residual"]["meanMs"],1)
            with (root/"system-phases.csv").open("a",encoding="utf-8") as stream:
                stream.write("0,system,AnotherSystem,1,3,3,0\n")
            with self.assertRaisesRegex(AssertionError,"Overlapping"):
                read_run(root)

    def test_groups_and_block_interval(self):
        self.assertEqual(group("system","KiasCrewSystem"),"crew_rooms_integration")
        self.assertEqual(group("phase","IoCommand"),"runtime_io_actuation")
        self.assertEqual(block_interval([1]*180,[2]*180),[1,1])

    def test_engine_hooks_are_isolated_and_idempotent(self):
        source=Path(__file__).resolve().parents[2]/"RobustToolbox/Robust.Shared/GameObjects/EntitySystemManager.cs"
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);path=root/"Robust.Shared/GameObjects/EntitySystemManager.cs";path.parent.mkdir(parents=True)
            path.write_bytes(source.read_bytes())
            for relative in ("Robust.Server/BaseServer.cs","Robust.Shared/GameObjects/EntityManager.cs"):
                destination=root/relative;destination.parent.mkdir(parents=True,exist_ok=True)
                destination.write_bytes((source.parents[2]/relative).read_bytes())
            first=instrument(root)
            self.assertEqual(first["sha256"],instrument(root)["sha256"])
            text=path.read_text(encoding="utf-8")
            self.assertEqual(text.count("DiagnosticAfterSystem?.Invoke"),2)
            self.assertIn("DiagnosticSystemFailure?.Invoke(error)",text)
            self.assertEqual(len(first["loopScopes"]),2)
            self.assertEqual((root/"Robust.Server/BaseServer.cs").read_text(encoding="utf-8").count('DiagnosticLoopScope("Engine.'),4)
        with self.assertRaisesRegex(ValueError,"production"):
            instrument(source.parents[2])
