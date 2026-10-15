# 05 — Честное сравнение и измерения без самообмана

## Три версии, две сравниваемые задачи

1. **Vanilla A vs optimized KIAS B**: действительная итоговая цена включённого KIAS для 40-корабельного сектора.
2. **KIAS before vs KIAS optimized**: доказательство улучшения, а не простой смены карты/сборки. Предыдущий архив — исторический baseline, после обновления кода нужен fresh measured before snapshot на том же рабочем revision (или воспроизводимый изолированный baseline branch).
3. При исследовании Physics допустимо B1: KIAS устройства присутствуют, runtime OFF; B2: runtime ON, чтобы отделить стоимость entity fixtures от активных систем. B1 не заменяет основную Vanilla A.

## Источники вариативности

- Один общий исходный Monolith commit/engine SHA/SDK/cvars, одинаковый `.NET Release`, identical job/thread env & instrumentation. База A использует механизмы overlay из `prepare_pair.py`, но после оптимизаций его необходимо актуализировать и повторно проверить минимальность KIAS feature diff; произвольные изменения Monolith между builds аннулируют тест.
- Для A/B: одинаковая изначальная численность/координаты/физика экипажа, светильников, оружия, сетей; B имеет дополнительные KIAS компоненты, но не missing native weapons/air/crew. Контрольный hash inventory/ship map, его обоснованный diff, runtime startup parity, headless/cvars/port parity.
- Все действия задаются тиком: например `targetTick 48000`, `ships: ship000..ship039`, same per-ship parameters + stable seed; log `scheduledTick`, `actualTick`, `nativeSuccessTick`, `postconditionTick`, failures. Если tick lagged, wall times не совпадут — это естественная разница производительности, не корректировать план случайными additional events.
- Separate CPU/ram/battery/power plan/host process scheduling, server instance data dirs, port isolation. Запускать A-B-B-A, периодические повторения; использовать run-level confidence intervals, а не считать 216000 отдельных тиков независимыми экспериментами.

## Какие цифры собирать

1. Per-tick wall timing TickStart→TickStop + отдельно game loop full frame/Input/PreEngine/PostEngine, с единицами измерения и местами входа. Цель `net.tickrate=60`: tick budget ~16.67ms.
2. Mean, median, p95, p99, p99.9, max tick time; count and % >16.67/33.33/50/100/250ms, rolling TPS/tick scheduling drift; cold-start/warmup separated. Считать фазовые и per-wave latency distributions и `time-to-recover`.
3. Per-system `robust_entity_systems_update_usage`/`robust_server_update_usage` and `robust_game_loop_frametime` histograms из Prometheus — *агрегаты*, не причинный trace. Добавить efficient sampled stage metrics для Crew, Runtime, Physics cause, topology rebuild. Избегать тяжелого синхронного мониторинга в тик.
4. Allocated bytes per tick/thread and process; Gen0/1/2, GC pause, heap/working set trend, CPU % + clock, threadpool, execution queue depths/MAX/oldest age, KIAS running cards, room topology pending, PDC projectile count, actual Fire/Collision/FTL/Crew processed.
5. **Functional postconditions are performance validity**: количество `planned`, `attempted`, `real native triggered`, `postcondition`, `KIAS response`, false positive/negative, recovery, dropped. Nonzero event delivery mismatch invalidates clean A/B performance verdict unless reported as divergent/invalid.

## Измерительный артефакт и накладные расходы

- CSV/JSONL writer reads buffers **outside** measured tick; console/UI progress throttled; compare enabled/disabled instrumentation to estimate overhead. Existing minute checkpoint spikes should remain in report as observed, but use separate test-no-checkpoints for true max latency claims. Never delete outliers without displaying raw results.
- Per phase annotated HTML timeline: IDLE, stochastic, critical/death waves, projectiles/FTL waves, recovery. For each lag spike show tick, event IDs, affected grids, system durations, GC, queue lag, frame vs tick time.
- In native replay do not confuse `return NativeAction(...)` with verified postcondition. Re-query actual game state after engine ticks and log `NativeSuccess` and `DeliveredToKias`. Spawns/kills must obey native gameplay rules, not direct fabricated state fields.

## Минимальные итоговые графики

- Time series of tick time and event markers; before/after distributions and percentiles; per-system KIAS hotspots; allocated bytes, Gen0/1/2 & pauses; active machines/queue backlog; simulated 40-wave response time; event delivery/postcondition heatmap by type; physics collision body/fixture rates; room build time vs room size/scanners.

## Особенности benchmarking room scanner

Нельзя выдавать выигрыш по Crew, если сканеры перестали учитывать дальние тайлы или сложные комнаты. Сделать paired validation с реальными sensor arrays, разной геометрией и одинаковыми персонажами; report coverage completeness, errors, rebuild latency and mean scan cost. Room topology build может быть дороже один раз, но recurring Scan должен быть дешевле; считать и peak rebuild cost при 40 simultaneous wall changes.


## Изменения измерения для физической геометрии комнат (v2)

Репорт должен содержать отдельно `KiasCrewSystem.Scan`, `KiasIntegrationSystem.Reconcile/CanControl`, `KiasRoomTopologySystem` geometry-build, source adapter/caches и reactor queues; иначе оптимизация, переносящая стоимость из одного типа в другой, будет казаться ложным ускорением. Покажи три режима: стабильная карта; частые open/close (ожидаемые **0 rebuild**), реальный wall destruction/split (обязательные rebuild и latency). Сравнения делай old KIAS vs optimized KIAS on identical legacy workload + new geometry-native scenario separately. Не требовать одинаковых Room outputs между Old/Optimized, когда семантика изменилась; требовать одинаковых **физических** inputs, все native-effects и корректные room-outputs по oracle tests.

Для UI измерять размер и частоту network geometry payload, ограничить открытые интерфейсы, не делать повторное создание tile arrays каждый GameLoop tick. В отчёте добавить Max/95% dirty→commit latency, amount of geometry dirty events, number of room rebuilds, number of auto reassignments, phantom scanner readings/commands (должно быть 0). Разделить `geometry performance PASS`, `room accuracy PASS`, `full-game benchmark PASS`.
