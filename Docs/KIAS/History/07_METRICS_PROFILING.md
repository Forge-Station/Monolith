# 07 — Сбор метрик, аллокации, задержки и происхождение лагспайков

## Главный принцип

**Перфоманс = скорость всей симуляции + стабильность тика + реакция KIAS + издержки GC и сенсоров, а не один FPS.** Доказуемость до определённого тика и event id. Отдельно сохранять внешнюю нагрузку и распределение event-effect.

## Штатный Prometheus

В RobustToolbox обнаружены:

- `robust_server_update_usage` — histogram времени update-stage; labels area, Units **seconds** (проверить через `_sum / _count`).
- `robust_game_loop_frametime` — histogram; описание в исходнике «frametimes in ms», но `Histogram.NewTimer()` обычно seconds. **Не путать подпись и единицы: проверить implementation/настоящие sample counts и stopwatch!**
- `robust_entity_systems_update_usage` — histogram Update per EntitySystem label system. Отдельно учесть, что event handlers, вызываемые из другой системы, могут быть учтены не там, где ожидается.
- `robust_server_curtime`, `robust_server_curtick`, `robust_server_uptime` — gauges.
- .NET runtime metrics: `metrics.runtime`, `metrics.runtime_gc`, `metrics.runtime_thread_pool`, `metrics.runtime_contention` и др. Имена фактически проверить по scrape.

Снять метрические snapshots A/B в одинаковом частотном режиме и логировать scrape latency. Prometheus-гистограммы — **агрегаты**, не заменяют per-tick trace.

## Высокочастотная telemetry

Разработать low overhead `BenchmarkTickRecorder`, binary ring-buffer/отдельная очередь с выгрузкой после тика и буферизованным flush на background writer. На каждый тик записать:

```
runId, mode, tick, simTime, monoTimestamp,
tickElapsedUs, scheduledTickIntervalUs, laggedTicksEstimate,
threadAllocatedBytes, processTotalAllocatedDelta,
GCCollectionCount0/1/2, GCHeapBytes, workingSetBytes,
processCpuTimeDeltaUs, threadpoolQueue (low-freq),
numGrids, numEntities, numProjectiles, numActors,
activeKiasGrids, onlineKiasDevices, totalKiasCardNodes,
activeEvents, finishedEvents, failedEvents,
selectedPhase, spikeMarker
```

Поля KIAS в A = `NOT_PRESENT`, а не «0 ms доказывает отсутствие KIAS». Для KIAS B добавить granular counters:

- `KiasSystem`: pending dirty-grid count, rebuild count, worst rebuild duration, topology revision.
- `KiasDefenceSystem`: indexed projectiles, grid scan count, bucket/proximity totals, scan duration, shots/intercepts.
- `KiasExternalSensorSystem`: sensor updates, nearby candidates, detections.
- `KiasCrewSystem`: covering scanners, room scans, number of tracked entities, per-module scans.
- `KiasControllerRuntimeSystem`: event/work/command queues current and max; processed/deferred/discarded per tick; active cards and node evaluation count; graph faults/command rate.
- `KiasNavigationSystem`: FTL completed callbacks, examined horizon, emitted contact count, latency.
- KIAS protocol/recorder/audio/suppression: event/command fan-out counts, actual effects, cooldown suppressions.

Метрики только через изолированный lab adapter / counters; избегать per-tick `LINQ`, string interpolation, stack trace captures and allocation-heavy labels при каждом event. Большие подробности включать только на spikes и конкретных triggers.

## Срезы событий

`events.jsonl`: scheduled→dispatch→engine-effect→detector→graph→actuator, actual status, `eventId`, `shipId`, `linkedEventIds`, proto/type, coords, target.

`exceptions.jsonl`: full exception type/message/stack, first and last tick, count, underlying subsystem. `faults.jsonl`: graph errors, device no-power/no-path, queue saturation, missed cooldown, late command, replay divergence.

`system_series.csv`: per-system counts/sums, nearest tick interpolation only when semantically valid (Prometheus scrape 1s cannot точно привязать call to one tick); use scoped profiling spans with causal tick id where possible.

## Spike capture

Пороговые уровни **для 60 TPS**:

- >16.67 ms CPU/server tick — выход за бюджет;
- >33.33 ms — тяжелый тик;
- >50 ms — лагспайк;
- >100 ms — критический лагспайк.

Снять максимальные 100 пиков и связанный контекст ±5/10 секунд: latency, event list, CPU, GC, system spans, queue counters. Нельзя автоматически списывать всё на KIAS — сначала сопоставить A/B и проверить `GC`/`Engine/Physics/Atmos`.

Результирующие статистики: mean, median, stddev, p90, p95, p99, p99.9, max, thresholds, spike frequency, sustained simulation lag, alloc bytes/s, GC pauses, process cpu%, peak/steady RSS. Считать корректные percentiles по **всем** тирам каждого прогона (например 72 000 элементов при 60 TPS×20 min), а не по 1s averages.

## Особые моменты измерений

- `GC.GetAllocatedBytesForCurrentThread` покрывает только текущий thread. Фоновые allocations показывать через process/runtime counters и распределённые spans отдельно; не путать `bytes per server thread` и `bytes per entire process`.
- Instrumented `KiasUpdateMeasurement` суммирует runtime updates, **не все event callbacks**; добавить диагностику источников событий (FTL/fire/projectiles/crew/atmos) в standalone profiling build с идентичными overhead на A/B, где применимо.
- Включение `prof.enabled` / Tracy меняет overhead. Основное сравнение с minimal telemetry; профилировочное повторение отдельно и всегда помечать.
- Для Tracy задать `TRACY_ONLY_LOCALHOST=1` (в рабочей версии CVar предупреждает об exposed listener), включать только на локальном ПК и собирать scoped traces вокруг поиска Horizon, PDC, topology rebuild, graph handlers.
- Не выполнять тяжелый stats/report generator в игровом процессе. Writer queue overflow записывать отдельно и считать измерения недостоверными при потере значимых samples.
- Не выдавать уменьшение CPU времени при серверном rate drop за улучшение — если пропущены тики или time dilation, показать это явно.

## Автотесты самой телеметрии

Проверка единиц `us ↔ ms ↔ seconds`, monotonicity tick ids, full capture 72 000 ticks, no duplicate tick, phase boundaries, overflow counter zero, stable CPU stats, nullable events для no-KIAS. Прогон 60 секунд с telemetry disabled/enabled для измерения overhead recorder; включить sanity check, что общая CPU нагрузка статистически не взорвалась из-за логов.
