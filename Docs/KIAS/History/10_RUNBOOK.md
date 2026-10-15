# 10 — запускаемая процедура для локального Windows-проекта

## Команды для агента (не слепой copy/paste в произвольном окружении)

```powershell
# Только диагностика, не модифицирует ветку:
git status --short
git branch --show-current
git rev-parse HEAD
git submodule status --recursive
dotnet --info
Test-Path 'Resources/Maps/_Forge/Shuttles/Archive/Mercenary/briarKIAS.yml'
```

После инженерной реализации toolchain должна иметь одну главную точку входа (например `Tools/KiasBenchmark/benchmark.ps1` / `benchmark.bat` / GUI). **Имя и синтаксис команд ниже — целевой интерфейс для разработки, пока не существующие в репозитории команды.** Не заявлять их рабочими до реализации.

```powershell
# Пример целевого CLI, подлежит реализации и проверке:
./Tools/KiasBenchmark/benchmark.ps1 -Action Diagnose
./Tools/KiasBenchmark/benchmark.ps1 -Action AuditShip
./Tools/KiasBenchmark/benchmark.ps1 -Action PrepareScannerModules
./Tools/KiasBenchmark/benchmark.ps1 -Action FunctionalGate
./Tools/KiasBenchmark/benchmark.ps1 -Action CompileScenario -Seed 20261009 -Ships 40
./Tools/KiasBenchmark/benchmark.ps1 -Action BuildPair -Configuration Release
./Tools/KiasBenchmark/benchmark.ps1 -Action DryRun -Seconds 60
./Tools/KiasBenchmark/benchmark.ps1 -Action FullAB -Sequence ABBA -Minutes 20
./Tools/KiasBenchmark/benchmark.ps1 -Action Compare
./Tools/KiasBenchmark/benchmark.ps1 -Action OpenReport
```

Если GUI — Start/Stop/Status/Diagnostics/Audit/Functional/Generate/Build/Run A/Run B/ABBA/Compare/Open Report. Все кнопки реальные, показывают PID/progress/state, elapsed game ticks/wall time, live TPS, current phase, actions/events, errors. Защита от двойного запуска, крэша, пропадания консоли и превышения disk size. Отмена — graceful stop/flush/partial report; не завершать без сохранения данных.

## Реальный порядок

1. `Diagnose`: работающий .NET SDK, нужный RobustToolbox commit, никаких изменений оригинала. Проверить имена commands и конфигурацию из текущего проекта.
2. `AuditShip`: анализ 2391 entities, 26 cards/27 presets, positions/rotations and scanner modules, dry map-load и source hash.
3. `PrepareScannerModules`: только совместимые и одобренные scanner module изменения исходной сохранённой карты; backup, diff, validation, reload. Если нельзя безопасно сериализовать — прекратить и объяснить.
4. `FunctionalGate`: реальный чистый Briar clone, тестовый owner/company/crew, один за другим все 27 (с aux fixtures для отсутствующих зависимостей); сохранить logs/screenshots and statuses; отдельный UI smoke.
5. `BuildPair`: baseline A/B worktrees and manifest; сверка vanilla-state parity, не переделывать старые карты ради совпадения.
6. `CompileScenario`: один фиксированный seed, заранее известные события, hash; всего 40 реальных кораблей.
7. `DryRun`: 60 секунд sim actions на каждой сборке, сравнить dispatch tick identities; проверка 0 telemetry loss и чистые логи. Никаких fake effects.
8. `FullAB`: каждый сервер запускать последовательно, warmup отдельно, 72 000 записанных тиков на прогон; если нужна статистическая репрезентативность — ABBA.
9. `Compare`: preconditions validation, raw report HTML, summary txt/md, spike categories, causal links, issues.

## Полезные штатные команды / ссылки

- В исходниках движка есть консольная `showtime`, показывающая Paused, CurTick, CurTime, RealTime; runtime `cvar` команды описаны в документации SS14. Применять только для диагностики.
- Release build: `Scripts/bat/buildAllRelease.bat` → `dotnet build -c Release` с актуальным submodule. Запуск `Scripts/bat/runQuickServer.bat` по умолчанию **не гарантирует Release**, поэтому использовать явный Release путь.
- Метрики после включения `metrics.enabled` — `http://localhost:44880/metrics/` (подтвердить реально открывающийся endpoint).
- `prof.tracy.enabled` применять только в dedicated diagnostic profile, `TRACY_ONLY_LOCALHOST=1`.

## При любом блокере

Покажи конкретный command/output, SHA, subsystem, список выполненных проверок и минимальный воспроизводимый кейс. Не исправляй вслепую чужие KIAS системы, если задача — измерение: сначала branch/code freeze, затем отдельный fix и новая A/B пара.
