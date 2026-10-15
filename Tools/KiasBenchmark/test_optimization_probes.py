from pathlib import Path
import tempfile
import unittest
from instrument_optimization_probes import instrument


class OptimizationProbeTests(unittest.TestCase):
    def test_old_paths_only_exist_in_isolated_sources(self):
        production=Path(__file__).resolve().parents[2]
        paths=("Content.Server/_Forge/KIAS/KiasCrewSystem.cs","Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.cs")
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary)
            for relative in paths:
                path=root/relative;path.parent.mkdir(parents=True,exist_ok=True)
                path.write_bytes((production/relative).read_bytes())
            instrument(root)
            crew=(root/paths[0]).read_text(encoding="utf-8")
            runtime=(root/paths[1]).read_text(encoding="utf-8")
            self.assertIn("DisableCrewTypeFilter",crew)
            self.assertIn("DisableCoverageReuse",crew)
            self.assertIn("DisableRuntimeIdleFastPath",runtime)
            for relative in paths:
                self.assertNotIn("Content.Server.Benchmark",(production/relative).read_text(encoding="utf-8-sig"))
        with self.assertRaisesRegex(ValueError,"production"):
            instrument(production)
