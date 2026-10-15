# 14 — The owner's .bat: full pipeline, detailed console UI, ETA, hard preflight

Modify ROOT `Run_KIAS_Tests.bat` + `Tools/KiasBenchmark/rerun.ps1`, `load_series.ps1`, collector and reporting. Must work by *double click* on Windows, keep visible console until user acknowledges finish, and produce failure exit codes for CI/noninteractive commands. Scripts must not wipe old runs or patch author ship. Use unique runId and well-separated outputs.

CLI: `Run_KIAS_Tests.bat -CheckOnly` (strict fresh-build+fixture completeness, quick no long runs); `-Quick` (fast **real** physics/crew/FTL/PDC/room native smoke on small fleet; no FINAL claims); default double click / `-FullGame` (all tests, then 40 ships ABBA, postreports, ~30min measured each); `-Status`/`-Resume`/`-Report` only if safely implemented with exact SHA+scenario hash matching. `-Legacy` is labeled old 7-pair, must never be default. Ensure `-CheckOnly` and `-Quick` are successfully invoked by agent before handing off; agent MUST NOT run long full test.

Print compact dashboard updated e.g. every 5 seconds or 300 sim ticks, no massive streaming logs:
```
KIAS FULL GAME | run=20261010-... | git=<sha> | seed=20261010
STAGE 04/11: Native stress — B run 2/4 | STATUS RUNNING
Sim tick 48,000 / 108,000 (44.4%) | ship count 40/40 | TPS 59.8
Stage ETA 00:18:42 | overall ETA 01:22:10 (rolling observed rates)
Current wave: 40x FTL entries 39/40 completed | backlog 18 | faults 0
Physics p99 7.8ms | tick p99 14.2ms | dropped 0 | native errors 0
Results: .kias-benchmark/runs/<id> | recent error: none
```
Never invent ETA; before enough samples `ETA calculating...`; use monotonic wall-clock and stage progress plus rolling actual samples. Distinguish SIM duration and actual wall-time; update ETA at same cadence, recompute on slow build, report stall/timeout. Do not treat PASS as simply server process exit 0. Emit `progress.json` atomic and optional pretty progress ANSI/pwsh safe fallback. `-CheckOnly` prints stage readiness matrix of ALL required drivers and paths; no running test in old build, no stale binaries.

Pipeline: preflight SHA/deps/disk/ports/worktrees; always rebuild fresh comparable Release A/B with frozen source hashes; RoomGate/FunctionalGate/27 cards and physical/visual acceptance, all native driver smoke; 40-grid staged tests, synchronized waves, warmups, ABBA with zero silent skipped actions; collect end-to-end stats, report, archive and terminal PASS/FAILED_INCOMPLETE with pointer to failed stage. Nonzero exit on any blocker. A test failure must save partial CSV/JSONL before exit. Server crashes: mark test failure, no blind auto-resume with changed config. 2+ hours expected; don't sleep fixed durations when sim tick hasn't advanced; watchdog on tick heartbeat.

**Owner-facing handoff:** Agent must say `READY_FOR_USER_FULL_RUN` only after all short/native gates and CLI preflight pass, with executable line `Run_KIAS_Tests.bat` (double-click) and optional command with seed; list all previous successful checks, remaining warnings, clear expected stages/time/space, location ZIP output. Do not run long final itself; do not claim 40-ships final ABBA passed. If missing FTL, PDC or real death driver, produce NOT_READY and implement missing parts before finishing. User expects to run **ONE** full test only, no later 'we forgot X'.
