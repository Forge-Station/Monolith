import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from diagnostic_report import report


class DiagnosticTests(unittest.TestCase):
    def test_partial_data_never_certified(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); series=root/"series"; series.mkdir(); run=root/"runs"/"1-A"; run.mkdir(parents=True)
            (series/"status.json").write_text(json.dumps(dict(status="FAILED_INCOMPLETE",currentRun=1,currentVariant="A")),encoding="utf-8")
            (run/"progress.json").write_text('{"currentTick":8100,"completedNative":920}',encoding="utf-8")
            (run/"ticks.csv").write_text("whole_tick_ms\n2\n20\nbroken\n",encoding="utf-8")
            (run/"KiasBench0001.log").write_text("[ERRO] previous client joint error\n",encoding="utf-8")
            result=report(series)
            self.assertFalse(result["strictReady"])
            self.assertFalse(result["attempts"][0]["valid"])
            self.assertEqual(result["attempts"][0]["tickSamples"],2)
            self.assertEqual(result["attempts"][0]["malformedRows"],1)
            self.assertEqual(result["attempts"][0]["completedNative"],920)
            self.assertEqual(result["attempts"][0]["logErrorCount"],1)
            self.assertTrue((series/"diagnostic-summary.html").exists())

    def exercise_series(self, diagnostic=True, fail_first=True, source_changed=False, port_blocked=False):
        tools=Path(__file__).resolve().parent
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); scripts=root/"tools"/"bench"; scripts.mkdir(parents=True)
            for name in ("load_series.ps1","diagnostic_report.py"):
                shutil.copyfile(tools/name,scripts/name)
            (scripts/"benchmark_lock.ps1").write_text("function Enter-KiasBenchmarkLease($x) {return 'test'}; function Exit-KiasBenchmarkLease($x) {}; function Test-KiasBenchmarkPorts {}",encoding="utf-8")
            if port_blocked:
                (scripts/"benchmark_lock.ps1").write_text("$script:portCalls=0; function Enter-KiasBenchmarkLease($x) {return 'test'}; function Exit-KiasBenchmarkLease($x) {}; function Test-KiasBenchmarkPorts {$script:portCalls++;if($script:portCalls -gt 1){throw 'injected occupied port'}}",encoding="utf-8")
            (scripts/"fullgame_report.py").write_text('print("validated complete report")',encoding="utf-8")
            (scripts/"source_fingerprint.py").write_text('print({"sha256":"test"}.__str__().replace(chr(39),chr(34)))',encoding="utf-8")
            (scripts/"fullgame_scenario.py").write_text('import sys; from pathlib import Path; Path(sys.argv[-1]).write_text("scenario")',encoding="utf-8")
            (scripts/"native_proof.py").write_text('print("PASS")',encoding="utf-8")
            (scripts/"partial_archive.py").write_text('print("archive checkpoint")',encoding="utf-8")
            (scripts/"native_replay.ps1").write_text("param($Variant,$Ships,$WarmupTicks,$MeasuredTicks,$Scenario,$OutputDirectory,$ManifestPath,$Seed,[switch]$KeepNativeNpcAwake,$NetworkClients,[switch]$Diagnostic)\nNew-Item -ItemType Directory -Path $OutputDirectory | Out-Null\nif ((Split-Path $OutputDirectory -Leaf) -eq '1-A') {throw 'injected crash'}\n'{}' | Set-Content (Join-Path $OutputDirectory 'completed.json')",encoding="utf-8")
            manifest=root/"manifest.json"; manifest.write_text(json.dumps({"sourceFingerprint":"changed" if source_changed else "test"}),encoding="utf-8")
            if not fail_first:
                replay=scripts/"native_replay.ps1"
                replay.write_text(replay.read_text(encoding="utf-8").replace("if ((Split-Path $OutputDirectory -Leaf) -eq '1-A') {throw 'injected crash'}", ""),encoding="utf-8")
            process=subprocess.run(["pwsh","-NoProfile","-File",str(scripts/"load_series.ps1"),"-OutputDirectory",str(root/"series"),"-ReplayRoot",str(root/"runs"),"-ManifestPath",str(manifest),"-FullGame"]+(["-Diagnostic"] if diagnostic else []),capture_output=True,text=True,timeout=45)
            state=json.loads((root/"series"/"status.json").read_text(encoding="utf-8-sig"))
            return state, process

    @unittest.skipUnless(shutil.which("pwsh"),"PowerShell 7 required")
    def test_series_continues_after_failed_run(self):
        state, process=self.exercise_series()
        self.assertEqual(process.returncode,0,process.stdout+process.stderr)
        self.assertEqual(state["status"],"DIAGNOSTIC_ABBA_FINISHED_WITH_ERRORS")
        self.assertEqual([row["variant"] for row in state["attemptedRuns"]],["A","B","B","A"])
        self.assertEqual(len(state["completedRuns"]),3)
        self.assertIn("injected crash",state["attemptedRuns"][0]["error"])

    @unittest.skipUnless(shutil.which("pwsh"),"PowerShell 7 required")
    def test_source_drift_stops_before_any_run(self):
        state, process=self.exercise_series(source_changed=True)
        self.assertNotEqual(process.returncode,0)
        self.assertEqual(state["status"],"FAILED_INCOMPLETE")
        self.assertEqual(state["attemptedRuns"],[])

    @unittest.skipUnless(shutil.which("pwsh"),"PowerShell 7 required")
    def test_occupied_port_stops_after_checkpoint(self):
        state, process=self.exercise_series(port_blocked=True)
        self.assertNotEqual(process.returncode,0)
        self.assertEqual(state["status"],"FAILED_INCOMPLETE")
        self.assertEqual(len(state["attemptedRuns"]),1)
        self.assertIn("occupied port",state["error"])

    @unittest.skipUnless(shutil.which("pwsh"),"PowerShell 7 required")
    def test_strict_mode_still_stops_on_first_failure(self):
        state, process=self.exercise_series(diagnostic=False)
        self.assertNotEqual(process.returncode,0)
        self.assertEqual(state["status"],"FAILED_INCOMPLETE")
        self.assertEqual(state["attemptedRuns"],[])

    @unittest.skipUnless(shutil.which("pwsh"),"PowerShell 7 required")
    def test_clean_diagnostic_still_not_strict_ready(self):
        state, process=self.exercise_series(fail_first=False)
        self.assertEqual(process.returncode,0,process.stdout+process.stderr)
        self.assertEqual(state["status"],"DIAGNOSTIC_ABBA_COMPLETE")
        self.assertFalse(state["strictReady"])
        self.assertEqual(len(state["completedRuns"]),4)
