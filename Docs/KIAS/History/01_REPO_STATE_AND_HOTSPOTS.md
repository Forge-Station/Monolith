# 01 — Состояние актуальной KIAS и доказанные hotspots

## Проверенное 2026-10-10

- Remote KIAS: `97178c6cfed4b6eda7855759da0f796b54c55ca0` (`added benchmark`, 2026-10-09 19:54 UTC); **повторить проверку на машине агента**.
- Предыдущий корабельный коммит: `38c67ac61a0f2565e372c66431590e37da5e7ed0`.
- Upstream main SHA при проверке: `3226889714d52cad147566c623dd7f18debd21ed`; это не основание создавать A/B напрямую без сравнения общего предка.
- Новейший benchmark commit изменил 67 файлов, включая Runtime, proto, grid, tests, benchmark scripts и `.bat`. **Не применять старые патчи вслепую.**
- Рабочий root: `Run_KIAS_Tests.bat`, он вызывает `Tools/KiasBenchmark/rerun.ps1`; `benchmark.ps1` имеет набор actions (`FunctionalGate`, `Regression`, `CrossCheck`, `PhysicalGate`, `PowerCheck`, `AuditShip`, и др.).
- Сейчас double-click запускает старый семь-парный ABBA с **уже скомпилированными** A/B (manifest + hashes). После изменения KIAS этот путь откажет либо запустит старое. Исправить оркестрацию.

## Источник измерений: прошлый архив `KIAS_ABBA_20261010_web-agent.zip`

144000 measured ticks на каждую версию; 40 Briar grids; 60 TPS; warmup 7200 ticks; 2202 event lines на прогон; A→B→B→A.

- A mean ~1.3513ms / p99 ~4.7991ms; B mean ~3.6114ms / p99 ~19.4347ms; B p99.9 ~38.6973ms.
- `KiasCrewSystem` delta ~+1.565ms/tick по system Update-гистограммам; `PhysicsSystem` delta ~+0.292ms; `KiasControllerRuntimeSystem` delta ~+0.203ms. **Гистограммы включают синхронные обратные вызовы/вложенную работу; точная причинная атрибуция не доказана.**
- B allocations около 173–242KB/tick; A ~55–76KB/tick; Gen0 коллекции в B существенно чаще; не утверждать leak без тренда heap/GC и воспроизведения.
- В прошлом BURST максимум 2 events/tick и ~8 overlapping active events; 40-событийных волн **не было**. Клиенты/FTL/реальное ПКО/столкновения/разрыв DATA отсутствовали.

## Конкретные доступные кодовые точки

**Crew** `Content.Server/_Forge/KIAS/KiasCrewSystem.cs`:
- `RebuildCoverage`: вложенный x/y круг радиусом max 10; `Dictionary<Vector2i,List<EntityUid>>`; поле `scanner.Modules` вычисляется по контейнерам; topology change инициирует повторную сборку.
- `Scan` (~387+): на каждом grid scan создаёт `Dictionary<EntityUid,int>`, `StringBuilder`, `HashSet<string>`×2, позже `Dictionary<EntityUid,HashSet<string>>`; содержит `runtime.Online.ToArray()`, `ScannersCovering(...).ToArray()`, `scanners.Aggregate`, `watched.Any`, `GetEntitiesInRange` **для каждого threat scanner**, `Emit` неизменённых Entities/Occupied/Crew, повторные `CrewUnavailable`, `EntityManager.System<T>()` внутри циклов.
- `CrewUnavailable`, `IsRegisteredPerson`, `HasUnknownAlongside`, `RoomLabel` повторно обходят списки/токены/coverage. Пользователь хочет геометрическую комнату — решение должно быть совместным с perf.

**Runtime** `Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.cs`:
- `OnEmission` создаёт Cause для самостоятельных emission, `_events` с пределом 4096, budgets 64 event/256 work/128 command/2048 evaluations, boots 16 & 4ms; не менять семантику этих бюджетов вслепую.
- `QueueWork` делает `endpoints.ToArray()` на доставке; `Command` делает `Graph.DataInputs...ToDictionary(...)` при каждом вызове и `matches.ToArray()` при broadcast; `Available` использует `Enumerable.Range(0,8).Any(...)` при частой проверке; `OnTopology` обход всех rack, `Reconcile` новые HashSets, переиндексация unchanged machines, UI RefreshRack на rack.
- Важная опасность: direct foreach вместо снимка `ToArray` может ломаться, если callback меняет подписчиков. Сначала доказать модель мутаций, затем безопасная snapshot strategy без аллокаций на событие или versioned iteration.

**Physics/System wide**: движковый `PhysicsSystem` — не часть KIAS. Установить причину +0.292ms/tick: дополнительные KIAS entities, anchored fixtures, collisions, broadphase, transform churn, разный состав A/B, callback physics. Сравнить версии без/с physical KIAS, count fixtures & enabled body types, per-method samples. **Не править RobustToolbox глобально только из-за этого числа.**

**Scanner architecture v2 — ОБЯЗАТЕЛЬНО учесть:** `Content.Server/_Forge/KIAS/KiasIntegrationSystem.cs` ещё содержит два скрытых радиусных контракта: `Reconcile` выполняет `_lookup.GetEntitiesInRange(scanner, range+1.5f)` и выбирает `KiasIntegratedComponent.Scanner` по кругу; `CanControl` отвергает auto-device, если `(scannerTile-targetTile).LengthSquared > Range²`. Поэтому Crew-only замена геометрии сломает end-to-end команды. `Content.Server/_Forge/KIAS/KiasDisplaySystem.LocalUi.cs`: `BuildCoverage` показывает круг, `SetSensorRange` меняет scanner.Range, `KiasScannerState.Range` виден в BUI. `Content.Shared/_Forge/KIAS/KiasCoverage.cs`: фигуры Circle/Sector/Data, `Cells` уже используется для DATA coverage — Room shape лучше отдельный. `Content.IntegrationTests/Tests/_Forge/KIAS/KiasScannerReconciliationTests.cs` требует handoff по Range и должен мигрировать на комнаты. `Content.Shared/_Forge/KIAS/KiasCrewComponents.cs` хранит Range=7, Modules/Advanced. `AtmosphereSystem.API.cs` предоставляет `IsTileAirBlocked(grid,tile,direction)`; `AirtightSystem.cs` поднимает `AirtightChanged`; `DoorSystem` открыванием меняет Airtight. Контрольный код доступен по commit `97178c6` и был перепроверен 10.10.2026.

**Additional:** `KiasControllerIoSystem.Emit` проверяет schema, записывает snapshot и вызывает `Emitted` при каждой отправке (включая прежнее значение), поэтому безопасный change-only emit из Crew может значительно разгрузить и Runtime. Но Signals/pulses передавать каждый раз; snapshot freshness timestamp, newly bound consumers and explicit refresh must continue working.

## Репозиторные ориентиры

- `Content.Server/Atmos/EntitySystems/AtmosphereSystem.RoomSafety.cs` — уже есть BFS по `TileAtmosphere.AdjacentBits/AdjacentTiles` для проверки герметичного помещения.
- `Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs` — `IsTileAirBlocked(grid,tile,directions)` и доступ к соседним смесям.
- `Content.Server/Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs` — ориентационно зависимые blocked directions учитываются в adjacency.
- `Content.Server/Atmos/EntitySystems/AirtightSystem.cs` — `AirtightChanged`, `SetAirblocked`, invalidations.
- `Content.Server/Doors/Systems/DoorSystem.cs` — открытие дверей меняет airtight; значит чистый atmosphere BFS соединит комнаты при открытой двери, что для данного запроса неприемлемо.
- `Tools/KiasBenchmark/Server/NativeLab.cs` — старая семь-парная replay реализация; `expanded_scenario.py` — её генератор; `rerun.ps1` — old-build hash enforcement.
