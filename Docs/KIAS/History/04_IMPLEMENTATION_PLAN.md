# Implementation plan — time-boxed, evidence-first

This is a prioritized **implementation task**. Time goals guide scheduling but never justify skipping verification or claiming green before running tests.

## Phase A, 0–8 min: baseline & reproduction

- `git status --short`, `git branch --show-current`, `git rev-parse HEAD`, note dirty changes. Record timestamp and current branch. If not `KIAS`, stop before modifying the wrong branch.
- Read code path map and real tests. Check contributor/PR rules, existing engine/dependency readiness.
- Run quick baseline relevant tests, identify current failures before patch. Read screenshot files.
- Make a compact red/green board for T01–T10. For uncertain T09/T10, first ascertain exact instance and trigger route, not invent a fix.

## Phase B, 8–32 min: observable functional failures first

- T07: relay popup and examine + enum cycling, couple of tests.
- T08: scanner reconciliation and stale cleanup. This is highest loop/regression risk: test scanner 1/2, out-of-range, unanchor, direct kit and idempotence first.
- T09: trace misleading button/speaker suggestion; narrow metadata/link correction, verify vanilla ports and speaker graph unaffected.
- T10: end-to-end suppression, real cartridge/foam/fire, false-negative diagnostics; repair true failing step, avoid new system.
- Run focused tests after each change; maintain patch checkpoints, not branches full of dead debug instrumentation.

## Phase C, ~32–45 min: UI and sprites

- T01 + T02 together (short label in hierarchical device palette + full diagnostic ID).
- T03 adaptive sidebar and readability while preserving inspector/canvas, test small and large widths.
- T04 localized `Шаблон:` label.
- T05 kit 0.75 → 0.60 appearance test.
- T06 core screenshot reproduction then minimal world-sprite/placement correction, real before/after captures.

## Phase D, ~45–60 min: broad validation

- Targeted red→green, then all KIAS integration tests, YAML/RSI/locales, diff check.
- Run `Scripts\bat\buildAllRelease.bat` from `Scripts\bat` working directory. Collect exact log, monitor exit status; script includes `pause`. Do not rely on an unrelated command to claim this step succeeded.
- Run `Scripts\bat\runQuickAll.bat` from its folder. Verify launched server and client actually start without KIAS errors; connect game, not just processes.
- Game smoke step-by-step from `11_LIVE_SMOKE_CHECKLIST.md`, screenshot proof for all three UI images at minimum and gameplay action logs.

## Phase E, ~60–75 min as useful: protect future regressions

- Recheck old passes: all KIAS cards/presets, ALL/ANY/SPECIFIC binding, air alarm + vents + scrubbers DeviceList, speaker Cyrillic text, fauna detection, UI rack control focus, idempotent topology and tooltip stability, relay isolation with multi-channel cables, direct/auto scanner precedence, real extinguish. Include non-KIAS DeviceLink/atmos smoke.
- Amend `Docs/KIAS/VALIDATION.md` with only actually executed tests, exact limitations; concise final report with file paths.

## Exit/decision criteria

- **Done** means code changed + regression covered + build script finished successfully + client/server started + real user path smoke or explicit human follow-up marked.
- If Release build fails: fix if from patch; if external, isolate and report exact command/log/reason. **Never** start an old binary while claiming new build verified.
- If no GUI access: test with actual native UI lifecycle and add explicit manual verification steps; do **not** mark visual smoke PASS.
- Do not add frivolous features, blanket catch blocks, global architecture rewrites, re-generated sprite packs, or endless test loops merely to fill time.
