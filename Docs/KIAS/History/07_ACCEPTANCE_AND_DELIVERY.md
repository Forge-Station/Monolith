# 07 — Приёмка, регрессии и итоговый отчёт

## Functional Gate: нельзя пропустить

1. Debug/Release build + compile `Content.Server`/`Content.Client`/Integration, static analyzer/formatter as supported; no new build errors.
2. `KiasBriar*` и все `Tests._Forge.KIAS` из существующей integration suite; **все 27 preset templates**: save/reload, write/read via updated programmer, bound selectors/ALL/ANY, four racks, cooldown, cross-grid transfer. Физические драйверы должны подтверждать actual event→controller execution→real physical action, а не только machine status RUNNING.
3. Room topology tests **полностью по G01–G15, I01–I09, S01–S08** из `08_SCANNER_ACCEPTANCE_MATRIX.md` и контракту `03_ROOM_TOPOLOGY.md`: especially open-door isolation, doorway included, long rooms, corners, destruction/splits and fanout. Separate robot/player/registered transponder/hostile fauna/threat and module semantics.
4. Power/DATA/collision/FTL/native PDC and friendly IFF tests; отсутствие лишних ошибок/очередей с восстановлением после mass wave; all 40 ships actual consistent controller counts, no memory growth after recovery.
5. Server/client visual sanity of programmable controller UI in normal configurations (existing VisualCheck artifacts helpful but never equate generated PNG with visual review).

## Performance Gate

- Одинаковая свежая Release и engines; explicit A/B overlay diff validated. В manifest SHA commit/engine, tool source hash, executable, maps and scenario.
- Сборки `KIAS-before`, `KIAS-after`, `Vanilla` — proof of actual delta. **Нельзя принудительно отключить полезные модули/действия и объявить выигрыш.**
- Сравнить `IDLE`, `MIXED`, `FULL_WAVE`, `RECOVERY`, same 40 grid fleet и разнородный fleet; 60 TPS tick budget 16.67ms. Percentiles, GC, CPU, allocations; memory after recovery.
- Per-wave: scheduled 40, attempted 40, native actual count explicitly, KIAS outputs/events; no false 40/40 if driver rejects the event.
- Queue depths/max age/pending: 40 simultaneous scripts must not produce unbounded queue or persistent disabled program. Missed native postconditions → run PARTIAL/INVALID, never silently drop.
- Для комнаты: O(1) interior tile→room lookup, shared door boundary→rooms, no repeated BFS per scan; 100% pass G/I/S fixtures с native-effects; все scanner consumers (Crew, Integration, CanControl, BuildCoverage/UI) используют одну геометрию. Ноль hidden radius checks в этих callsites; старая Range migration documented. Dirty rebuild bounded, максимум stale positive commands=0, и реальные перф-данные.
- Improvement goals (не подгонять данные): reduce `KiasCrewSystem` and `KiasControllerRuntimeSystem` CPU/allocations appreciably; explain any physics delta by actual cause. Target B p99 lower than old ~19.43ms on identical historical workload and better full-wave tail, without regressions. Targets are expectations, not proof.

## Mandatory output: ONE folder + ZIP

```
.kias-benchmark/fullgame/<timestamp>/
  README.md
  MANIFEST.json
  repository-diff.patch
  source-file-list.md
  functional/   # trx, logs, validated postconditions
  rooms/        # area maps, boundary/rotation cases, profiling
  baselines/    # initial/optimized snapshots and hashes
  runs/A1/ runs/B1/ runs/B2/ runs/A2/
  scenarios/full-game.jsonl
  scenarios/40-wave-matrix.json
  metrics/raw-ticks.csv  # per run ideally separate
  metrics/per-system.prom
  report.html
  report.md
  findings.md   # hot methods, failed cases, new regressions
  status.json   # COMPLETE_VALID / PARTIAL / FAILED_INCOMPLETE / BLOCKED
```

Make an archive `KIAS_Optimized_FullGame_ABBA_<timestamp>.zip`, include raw information not only final averages. If incomplete, include logs, exact blocker, proof of files actually generated, and explicit not-run stages.

## Final reply schema

1. Current revision/engine + changed files/short diffs.
2. Verified improvements and evidence before/after per system; do not substitute expected performance for measured.
3. Room-scanner demo: actual door-open/door-close and wall-break before/after tiles, 4 directional orientations, snapshot.
4. Tests: 27 templates pass/fail, full native driver matrix pass/fail, 40-wave executed/native-postconditions, crashes.
5. Full ABBA results, per phase and per wave mean/p99/p99.9, GC, wall time and queue latency; explain any divergence.
6. `.bat`: exact command and exit codes actually run; check that **double click default** triggers new full-game workflow, not legacy 7-pair old build. User instructions if any.
7. Provide paths/downloadable file(s) + honest `PASS`/`PARTIAL`/`FAIL`/`NOT_RUN` per stage.

**Definition of done:** working patched project with new room semantics, measurable optimized hotspots, regression-proof native driver tests, reproducible full A/B scripts, actual game server runs and usable saved artifacts. If execution unavailable, return a partial result with code+tests and explain blocker, not a fake completed benchmark.


## Дополнительные обязательства v2

- Приложить `SCANNER_CONSUMER_MAP.md`, `BriarPlacementRecommendations.md`, все G/I/S тесты со status/expected/actual, отрисовку room coverage и статус каждой нестандартной гермы. Показать, что `KiasIntegrationSystem` больше **не** проверяет радиус для RoomScanner.
- Серверная геометрия не зависит от открывания двери и опасности атмосферы. Настоящий пожароизвещатель, вентиляция, `Direct` kit, UI сервисника и старый save с `.Range` проходят испытания.
- Все отдельные цифры `KiasCrew`, `KiasIntegration`, `KiasRoomTopology` + `ControllerRuntime`, общесистемный GC, физика; no report of speed improvement based solely on moving CPU from Crew to new topology system.
- Конкретные лимиты SLO/результаты тестов — в `10_SCANNER_PERF_AND_REBUILD.md`. Уровень проверки: *несколько типовых прямых/Г-образных/Briar rooms*, физическое открывание дверей, разрушение стен и 40-grid wave.
