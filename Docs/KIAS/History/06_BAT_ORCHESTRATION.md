# 06 — Изменить уже существующий Run_KIAS_Tests.bat, а не создавать несвязанный новый

## Как сейчас

Root `Run_KIAS_Tests.bat` вызывает `pwsh.exe -File Tools/KiasBenchmark/rerun.ps1 %*`. Скрипт умеет `-CheckOnly`, проверяет legacy manifest SHA готовых бинарников и запускает `load_series.ps1`, который использует 7 native driver pairs. **Это старые сборки и старый 20-минутный нагрузочный тест**. Без правки `rerun.ps1` двойной клик после оптимизаций НЕ проверит новый сценарий.

## Что реализовать

Изменить `Run_KIAS_Tests.bat`, `rerun.ps1`, `benchmark.ps1` и нужные `Tools/KiasBenchmark/*` минимально, без разрушения старых данных. Реальные аргументы интерфейса на выбор после проверки PowerShell conventions:

```powershell
Run_KIAS_Tests.bat -CheckOnly
Run_KIAS_Tests.bat -Quick
Run_KIAS_Tests.bat
Run_KIAS_Tests.bat -FullGame -Ships 40 -Minutes 30 -Seed 20261010
Run_KIAS_Tests.bat -Legacy
```

**Double-click без аргументов = полноценный новый тест**, а не старый replay и не silent no-op. `-Legacy` воспроизводит старый сценарий лишь для сравнения с предыдущей серией, сохраняет старые таблицы и маркируется LIMITED_7_PAIRS.

### Полный pipeline в одном батнике

0. Lock, preflight: git HEAD, dirty status snapshot, SDK/net deps/Python/PyYAML/pwsh, native binaries + options, required ports, working space, old runs. Создать уникальный runId, **не удалять** уже выполненные эксперименты; tmp/current swap only under safe owned directory. Все результаты сохранить даже on failure.
1. Always rebuild matched A/B Release from current optimized source/engine and compare baseline inventory/hash, instrumented code versions. `BASELINE_MANIFEST.json` должен смениться на manifest именно новой сборки. Build failure → FAIL, не запускать предыдущую сборку.
2. Run unit + integration regression, room geometry suite and all 27 programs physical functional gate, device IFF/PDC/FTL and persistence check. Если критический FAIL — остановить performance claim, сохранение logs/results. `-Quick` может запускать сокращённый smoke, без FINAL PASS.
3. Compile deterministic `full-game-scenario.jsonl` и validate coverage; 40 stable ship IDs; deterministic target ticks; all required drivers and wave postconditions. Bind native systems; abort invalid native fixture with reason; no blanket catch returning success.
4. For each A/B/B/A: load actual fleet; warmup >=1800 ticks / 3min recommended, verify active 40 grids + runtime cards actually present on each ship (the saved Briar may have 26 cards while the repository has 27 available templates); test all 27 templates separately in the functional gate; run 30min default measurement at 60 TPS, monitor progress/results, cleanup between runs, health checks; no interleaving concurrent server processes on same host.
5. Create final HTML/MD/CSV+JSONL reports, variant/phase/wave comparisons, before/after CPU/GC, faults, status, error logs and a ZIP incl reproducible instructions + hashes. EXIT CODE nonzero on fail/incomplete; do not print success otherwise.

### Safety & practicality

- `-CheckOnly`: verify tools, syntax, driver list/ports/manifest/build freshness and rough estimate; **не запускать часы тестов и не удалять существующее**. If build stale, report `REBUILD_REQUIRED`, not PASS.
- `-Quick`: small fleet native driver smoke, no conclusions on load; generate report.
- Default `-FullGame`: full build + gates + 4 x 30min + waves + reports.
- `-Legacy`: old 7-pair reproducer in separate output folder with explicit coverage warning.
- Timeout: configurable per-run wall deadline, if missed checkpoint/watchdog/no game tick progress server terminate and mark FAILED_INCOMPLETE; no infinite hang. Resume strategy must avoid accidental mixing of two git SHA or seeds.
- PowerShell 7, no new global software installations without explicit requirement; local process flags. Avoid pausing endlessly at test end if run in CI; for double-click Windows `pause` only if interactive.
- No port collisions with user's live server; no destructive changes to user saved grids or branch.

### Mandatory execution steps by coding agent

Once implementation and compile/regressions pass, **invoke**:

```cmd
Run_KIAS_Tests.bat -CheckOnly
Run_KIAS_Tests.bat -Quick
Run_KIAS_Tests.bat -FullGame -Ships 40 -Minutes 30 -Seed 20261010
```

If actual local environment prohibits running one step, do not fake a test log; attach precise missing prereq/error and mark stage BLOCKED/NOT_RUN. In any case leave full runnable user-facing `Run_KIAS_Tests.bat` for double click.


## Дополнение v2 — room acceptance gate перед тяжёлым A/B

После BuildPair и до любого FullGame запуска добавить отдельный action/suite `RoomGate`: прогон G01–G15 + I01–I09 + S01–S08 и saved Briar geometry smoke; TRX и room-map экспорт обязателен. `-Quick` выбирает репрезентативные G01/G02/G04/G06/I01/I02/S01 и отчёт `QUICK_NOT_FULL`. `-FullGame` требует полный RoomGate + FunctionalGate 27 программ (а также остальные native gates). `-Legacy` сохраняет старые radius test expectation **только** в отдельном historical baseline checkout и не должен падать на новой семантике RoomScanner. В `-CheckOnly` проверить, что новые scanner callsites не остались с `Range` (audit grep + tests), а сборки действительно содержат новый RoomTopology code. Учитывать статус `RoomGate FAIL` и прекращать FullGame performance claim до фикса.
