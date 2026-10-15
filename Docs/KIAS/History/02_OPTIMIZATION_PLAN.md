# 02 — Практический план оптимизации трёх зон

## Общая тактика

До вмешательства поставить фазовый профайлинг малого overhead с целочисленными счётчиками без allocations on hot path. В Release собирать bucketed timings (Stopwatch.GetTimestamp), sampled allocations, queue depth MAX + area/actor counts. Не измерять каждый `GetEntitiesInRange` отдельной объектной телеметрией. Сравнить однотипные runs, включая uninstrumented control, и rank by actual cost. Ни одна оптимизация не должна проходить только по старому профилю.

## A. KiasCrewSystem — приоритет №1

1. Разделить `RebuildCoverage` и каждый `Scan` на 5 измеряемых стадий: topology/indices; tracked entities; threat/fauna; transponders/registration; outputs. Счётчики `scannerCount`, `roomCount`, `coveredTiles`, `worldQueries`, `trackedScanned`, `faunaCandidates`, `lookupCalls`, `allocatedBytes`, `controllerEmissions`, `changedEmissions`, `queuePending`.
2. Ключ — из `03_ROOM_TOPOLOGY.md` и матрицы `08_`: строить immutable room coverage **по событиям геометрии**, а не круг для каждого scanner. `tile -> roomId`, `roomId -> scanners`, предвычисленные flags модулей, per-room online scanner indices. Объединить одинаковые области для нескольких сенсоров; не копировать тайлы per scanner. Инкрементально обновлять агрегаты, когда сущность перешла из комнаты в комнату или изменила relevant state. Полный reconciliation оставлять периодическим safety fallback, но не recompute permanent geometry каждый scan.
3. Tracked people: использовать `_people[grid]` один раз за pass; tile/roomId вычислять один раз на сущность; агрегаты модулей брать из room cache. Убрать внутрицикловые `Aggregate`, `OrderBy`, повторный `Any`, вызовы `EntityManager.System<T>()` и `ScannersCovering` в nested loops; повторные `CrewUnavailable` на каждом сервере.
4. Threat fauna: заменить per-scanner `_lookup.GetEntitiesInRange` на candidate enumeration per room/grid, затем проверять точную принадлежность комнате. Кешировать на текущий scan faction disposition. Не терять hostile fauna без Mind и не включать её в обычный `Entities` без права.
5. Registration: per-grid индекс serials с dirty updates при Register/Transponder move/remove; initial full rebuild при bootstrap. Использовать общий индекс для `Crew` и `CrewUnavailable`.
6. Для постоянных портов `Entities`, `Occupied`, `Crew`, `Unavailable` — change-only emit с **гарантированным initial snapshot/replay для новых графов и после грязной геометрии**. Event-pulse `Motion/Threat/PersonDead/Radiation` не подавлять. Проверить snapshots `TryOutput`. Изменение комнаты само по себе не должно порождать ложный Motion.
7. **Не оставить радиус в соседней системе:** перенести `KiasIntegrationSystem.Reconcile` и `CanControl` на комнатный индекс; собственный native target index, долгие комнаты, wall-mounted fixtures, общей порог двери, deterministic scanner auto-owner, сохранение Direct upgrade kits; исключить рекурсивную invalidation-петлю. UI и legacy Range — по `09_`.

8. Производительность и грязные rebuilds отдельно проверять по `10_SCANNER_PERF_AND_REBUILD.md`; не маскировать дорогой geometry rebuild за коротким Scan.

## B. KiasControllerRuntimeSystem

1. Измерить стадии Update: boots, timers, events, work, commands; max queue age (ticks), backlog, fanout, failures.
2. `Available` (частая `Enumerable.Range(0, 8).Any`) заменить прямой проверкой восьми слотов или event-invalidated membership cache.
3. `Command` сейчас создает `.ToDictionary(...)` на вызов и `matches.ToArray()` на broadcast. Предкомпилировать immutable input metadata при Boot; формировать snapshot inputs только если это требуется реальной команде; avoid per-command redundant allocations.
4. `QueueWork`, `FaultSubscribers`, shutdown используют `endpoints.ToArray()` для reentrant safety. Нельзя просто заменить на foreach: подписки могут мутировать в callbacks. Ввести versioned stable snapshot / per-dispatch reusable scratch buffer / copy-on-write index и тест на принудительное выключение/отключение DATA **внутри** сигнала.
5. `OnTopology`/`Reconcile` не должны заново индексировать unchanged cards при изменении, не затрагивающем match sets; selective reconciling / topology revision. Не отправлять лишние UI RefreshRack. Сохранить `anti-loop`, лимиты и порядок событий.

## C. PhysicsSystem (движок; кандидат на косвенную цену KIAS)

1. Измерить fleet 0/10/20/40: A vanilla, B1 physical KIAS entities with runtime disabled, B2 active KIAS. Fixture count and active/broadphase count report; один и тот же физический сценарий.
2. Проинвентаризировать все KIAS proto: `Physics`, `Fixtures`, `CanCollide`, `Anchored`, masks/layers, extra transform churn и `Airtight` invalidations. Добавить причинный профиль physics Update с реальными collisions/damage.
3. Убирать только доказанно ненужные fixtures/коллизии у конкретных KIAS entities; проверять interaction/damage/explosion/assembly. **Не менять RobustToolbox Physics** без измерений и tests.
4. Если нагрузка — неизбежная цена добавленных физических приборов, зафиксировать это честно; не выдавать отсутствие правки Physics за исправление.

## D. Общесистемные затраты и критерии

- Проверить синхронизацию периодических опросов: пики каждые 15/30/60 тиков. Разнести фоновый polling по детерминированным phase offsets между grid, но **никогда не размазывать запрошенные 40-event waves**, которые должны начаться в одном тике.
- Dirty topology queue: bounded rebuild, max stale latency, fail on persistent stale state. Не создавать новые HashSet/List/StringBuilder каждый тик, если возможен повторный локальный буфер с корректным lifecycle.
- Baseline and after: per-system mean/p95/p99, global tick p50/p95/p99/p99.9, allocations/tick, GC, max backlog, delivered native events, count of actual actions, runtime faults, client/physics state parity.
- Победа по скорости при пропущенных действиях/выключенном сканировании недопустима. Любую регрессию повторно гонять на full 27-program gate.
