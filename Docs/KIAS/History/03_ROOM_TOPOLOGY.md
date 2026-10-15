# 03 — ТЗ v2: настоящий RoomScanner на физическую комнату

**Статус:** обязательное implementation specification. Версия source-аудита: `theg33ka/Monolith@KIAS` commit `97178c6cfed4b6eda7855759da0f796b54c55ca0` (10.10.2026); агент перед патчем сверяет HEAD, код и все callsites. Главная цель — единый источник истины о комнатах для всех KIAS-потребителей, а не косметическая замена кругов.

## 0. Проблема и границы изменений

В актуальном коде `Content.Server/_Forge/KIAS/KiasCrewSystem.cs` метод `RebuildCoverage` создаёт круг до десяти тайлов, `Scan` и `HasCoverage` читают круговую карту. Но радиус применён **также** в `Content.Server/_Forge/KIAS/KiasIntegrationSystem.cs`: `Reconcile` использует `GetEntitiesInRange(scanner, range + 1.5f)` и `CanControl` отклоняет удалённые устройства. В `Content.Server/_Forge/KIAS/KiasDisplaySystem.LocalUi.cs` `BuildCoverage`, `SetSensorRange` и local UI показывают/редактируют круг. Существующий тест `KiasScannerReconciliationTests.ShrinkingCoverageHandsOffAndClearsOnlyAutomaticBindings` построен на изменении `.Range` и устаревает. Эти callsites исправлять **в одном патче**.

Круг/радиус оставить у **других** классов (`Horizon`, `PdcRadar`, `WeaponFlashDetector`, `ProximitySensor`, `HullSensor`): речь только о `KiasRoomScanner` и `KiasAdvancedRoomScanner`.

## 1. Продуктовые инварианты (MUST)

1. Покрытие — **все доступные внутренние тайлы одного физического помещения**, не расстояние от сканера. Длинный зал / Г-образный коридор / ниша обнаруживаются целиком, даже за 10+ тайлов.
2. Исходная точка сканера — стеновой тайл установки плюс соседняя клетка **по направлению внутрь комнаты**. Проверить реальное соответствие `Transform.LocalRotation` четырём направлениям на `briarKIAS.yml` и в тестах; нельзя угадывать по спрайту. Не выбирать произвольную «ближайшую» комнату при неверном повороте.
3. Физические стены, окна, направленные Airtight blockers, гермозатворы, airlocks/firelocks/blast doors и двери образуют преграды. **Состояния Open/Opening/Closed/Closing не отменяют семантическую границу двери**. Человек в комнате B за открытой дверью не появляется на сканере A.
4. Дверной тайл — терминальная граница: **включён** в обе непосредственно примыкающие комнаты, но обход через него не проходит. Граница совместима с одной, двумя и несколькими соседними комнатами, в т.ч. многостворчатыми дверями/створками. Сканеры обеих соседних комнат видят человека непосредственно на общем пороге; каждый учитывает его **однократно**. Глобальный счётчик существ и регистрации на гриде дедуплицируется по сущности/серийному номеру, не по числу комнат.
5. Несколько сканеров в комнате разделяют один и тот же `roomId`/список interior tiles, но **модули и порты каждого сканера остаются индивидуальными**. Сканер без конкретного модуля не должен излучать его сигналы только потому, что модуль есть у соседа. `room.moduleUnion` — индекс для быстрого предварительного отбора, **не выдача всех модулей всем сканерам**.
6. Поломка стены может объединить комнаты; установка перегородки/гермозатвора — разделить. Удаление двери убирает её статическую границу, даже если дверь перед удалением была открыта. Модульный состав, питание и DATA меняют доступность сканера, **но не геометрию комнаты**. Убирание сканера — перераспределение sensor membership.
7. Решение не зависит от наличия воздуха, его давления и огня; вакуумная каюта остаётся каютой. Ограничивать поиск реальными гридом, пригодным полом/объёмом и границами корпуса. Никогда не делать обход через `Space` в бесконечную пустоту. При нехватке данных — явное `NO_INTERIOR_SEED / OPEN_TO_SPACE / UNSUPPORTED_GEOMETRY / ROOM_TOO_LARGE / REBUILD_PENDING`, **никакого fallback на круг или случайную соседнюю область**.
8. Удалённый/перенесённый за другую стену KIAS-устройством сканер не должен сохранять старые привязки. Контроль чужих устройств/соседнего grid запрещён; совместная дверь помечается shared boundary, не создаёт сетевого мостика и не отменяет разрешения KIAS.

## 2. Единая архитектура

Добавить отдельный модуль KIAS, например `KiasRoomTopologySystem` с изолированными pure/core-функциями геометрии и mutable кешем на грид. **Не делать вторую атмосферную симуляцию**. Возможная структура:

```
GridRoomIndex {
  Grid; GeometryRevision; DeviceRevision;
  CellToRoom: Dictionary<Vector2i, RoomId>;    // одиночная interior-комната
  BoundaryToRooms: Dictionary<Vector2i, SmallRoomSet>; // порог / допустимая общая грань
  Rooms: Dictionary<RoomId, RoomSnapshot>;
  ScannerBindings: Dictionary<EntityUid, ScannerRoomBinding>;
  DirtyCells; PendingJobs; Status;
}
RoomSnapshot { Id; Tiles; BoundingBox; BoundaryCells; DoorEntities; Scanners; ModuleUnion; Version; }
ScannerRoomBinding { Scanner; RoomId; Orientation; Modules; Online; Status; }
```

`RoomId` — runtime ID, не сериализуется в ship YAML. Его идентичность после rebuild не обязана сохраняться, **все KIAS-привязки пересвязываются транзакционно**; в диагностике сохранить старый/new id и revision. Геометрические tile maps и read-only snapshots общие для всех сканеров одной комнаты. Не дублировать длинные tile lists на каждый scanner.

Обязательные публичные read-only API (имена адаптируй к conventions):

- `TryGetScannerRoom(scanner, out roomId, out status)`;
- `GetRoomsAt(grid, tile)` — 0/1 interior или несколько boundary rooms;
- `Contains(scanner, target, module)` — полное условие: тот же grid, online/anchored/powered, module present, valid snapshot, tile membership; для wall-mounted target см. ниже;
- `GetScannersCovering(grid, target, module, ref reusableBuffer)` — без LINQ/ToArray на hot path; результат неизменяем на время dispatch;
- `GetCandidatesForAutoIntegration(grid, target)` / `CanConnectorControl(scanner,target)` — **тот же** room index;
- `GetRoomDebugSnapshot(scanner)` — UI/сервисное средство, только по запросу, с лимитом сериализации.

Все callsites (`HasCoverage`, `ScannersCovering`, `RoomLabel`, `CrewUnavailable`, `HasUnknownAlongside`, `Scan`, `Integration.Reconcile`, `Integration.CanControl`, диагностическая геометрия) должны использовать эти методы. Нельзя оставлять hidden radius-check в одном потребителе.

## 3. Конкретный алгоритм построения

**3.1 Предобработка грида, один раз на геометрическую ревизию.** Для каждого релевантного тайла определить:
- является ли он реальной внутренней/проходимой deck-ячейкой (MapGrid + actual tile / физические преграды; не использовать давление как фильтр);
- какие cardinal edges заблокированы **физическими** стенами/окнами/направленными Airtight blockers;
- пересекает ли тайл/ребро **постоянная дверная граница** (DoorComponent и все реальные аналоги, найденные поиском прототипов; open/close не меняют границу);
- не является ли тайл дырой наружу / unsupported diagonal / переходом на другой grid.

Использовать существующий `AtmosphereSystem.IsTileAirBlocked(grid,tile, direction)` с проверкой `direction` **и противоположного направления соседнего tile**; при необходимости сделать минимальный **read-only** API в `AtmosphereSystem` для эффективного доступа к уже рассчитанным `AirtightData`. Не читать напрямую `[Access]`-закрытые `TileAtmosphere`/`GridAtmosphereComponent` из KIAS. `RoomSafety.IsSealedSafeRoom` — референс BFS, но **не** готовый API комнаты: он отбрасывает небезопасный воздух и учитывает текущую открытость дверей. Не править airflow/physics ради KIAS.

**3.2 Seed.** Калибровать 4 ориентации на реальных сканерах. Найти `wallTile` сканера и `interiorSeed = wallTile + inwardCardinal`; валидировать: seed имеет пол/внутреннее пространство, не статическая дверь и не стеновой blocker; scanner всё ещё anchored/powered/online по соответствующим проверкам. Не пытаться обходить стену, чтобы подобрать «удачный» seed. Для ошибочного направления логировать координату/rotation/status. Наличие сканера на тайле без полноценной стены разрешать только с **явной** проверенной геометрией и ориентацией, а не привязкой к ближайшей области.

**3.3 Flood fill.** Один BFS/DFS для каждой ещё не размеченной интерьерной области, в 4 соседей. Переход A→B разрешать только если оба тайла interior, нет static edge blockage (в обе стороны), B не является door terminal, и переход не пересекает дверной барьер. При встрече door terminal: записать boundary + door entity, не enqueue. Диагонали поддерживать через геометрию направленных препятствий; *нельзя* проводить диагональный BFS через угол. Corner junctions должны проверяться отдельными тестами. Не использовать `GetEntitiesInRange` для вычисления формы помещения.

**3.4 Door ownership.** После разметки интерьеров для каждого door terminal найти непосредственно примыкающие внутренние области по разрешённым сторонам; добавить дверной тайл к `BoundaryToRooms` для каждой. Не соединять области через дверную клетку, даже если дверь Open. В многотайловой герме учитывать весь проём по фактической занимаемой геометрии/footprint и нативным fixture/door metadata, **не только Transform одной entity**; не размножать doorway person на несколько объектов. Глобально присоединить entity к максимум одному grid, но возможны несколько room membership на boundary. При споре wall-mounted device без однозначного направления не назначать «первому попавшемуся» — `AMBIGUOUS_BOUNDARY`.

**3.5 Пристройка сканеров.** Каждый валидный scanner получает комнату seed. Индексы разделены на физический `RoomSnapshot` и `ScannerCapabilities` (модули/включённость). Обычный и advanced используют одинаковую геометрию; advanced не получает лишних модулей автоматически. Не расширять 6 слотов и не менять сохранённые карточки/стены Briar.

**3.6 Открытый корпус.** Если обход достигает пустоты/внешней кромки, не перебирать всю карту: ограничить реальным GridTile footprint и определить `OPEN_TO_SPACE`/`BREACHED` по факту. Если можно однозначно удержать фактический интерьер — работать с отмеченным набором и показывать предупреждение; если нельзя — статус `UNRESOLVED`, не экспортировать ложные цели и не подменять радиусом. Лимит построения, напр. стартовое `MAX_ROOM_TILES = 4096` (конфигурация/измерения, не устоявшийся баланс). Превышение = статус, а не молчаливое усечение.

### Псевдокод

```text
BuildGeometry(grid, changedArea):
  cells, staticEdges, doorTerminals = ReadExistingMapAndAirtightTopology(grid)
  for each unlabelled walkable interior cell:
    room = BFS(cell):
      for neighbor in CARDINAL_4:
        if isDoorTerminal(neighbor): addSharedBoundary(room, neighbor); continue
        if !IsWalkableInterior(neighbor) || BlockedEitherSide(cell, neighbor): continue
        label neighbor and enqueue
  assign doorTerminals -> adjacent rooms
  bind each scanner via its authoritative wall rotation -> one room
  calculate module indices and output availability
  validate snapshot, then AtomicallyCommitAndNotify(roomDiff)
```

## 4. Wall-mounted sensors and integrated target devices

**Сканер:** стеновая ориентация определяет ровно одно помещение. **Приборы внутри:** по `CellToRoom`. **Настенные приборы в стене:** предпочтительно определять «смотрит в комнату» по собственному Transform/типу/anchor. Если направление недостоверно, брать единственную внутреннюю комнату по доступной грани; если их 2+, `AMBIGUOUS_BOUNDARY` и explicit diag. **Гермодверь:** `BoundaryToRooms`, может принадлежать двум комнатам одновременно. Для automatic connector сохранить единственный существующий `KiasIntegratedComponent.Scanner`: выбрать **детерминированного** подходящего scanner с Connector среди непосредственно соседних комнат по стабильному ключу (например, room identity/uid), никогда не перепрыгивая стену и не меняя выбор на open/close. Перепривязка — только при реальном изменении состояния/геометрии/доступности, без oscillation.

`KiasIntegrationSystem.Reconcile` не должен искать приборы кругом; использовать indexed eligible native candidates per grid/room либо per-room tile entity index, обновляемый через entity/anchor/move/grid/placement/removal events. Для старта разрешён **однократный grid scan**, но не каждую комнату/кадр. `CanControl` **обязан** проверять same-room membership согласно выбранному scanner, затем текущие питание, anchoring, KIAS topology/cable connectivity, Direct-vs-Auto и access; никаких `module.Range` там. `Direct` integration kit **не трогать**. Не создавать циклическую рекурсию `RebuildGeometry -> Integration.Reconcile -> _kias.Invalidate -> RebuildGeometry`: разбить на раздельные geometry dirty vs device topology dirty, диффить привязки перед мутацией, coalesce notifications.

**Критично для Briar:** `KiasRoomScanner` с Connector может автоматически привязывать устройства всей *своей* комнаты, включая удалённые вентиляторы/сигнализации, но никогда сквозь стену. `KiasAdvancedRoomScanner` из стандартных 6 модулей не имеет Connector, не наделять его Connector по умолчанию. Проверить fire/atmos linkage после изменения геометрии.

## 5. Полная семантика наблюдений

- `Entities`: количество уникальных KIAS-tracked разумных сущностей в области **этого** сканера и при активном Motion. В каждой комнате одна персона учитывается один раз независимо от числа тайлов. Не считать шкафы, не-Mind fauna и обычные предметы как людей. Пульсы `Motion` сохраняют семантику `0→>0` и должны повторно срабатывать только при реальном новом входе после выхода, а не из-за внутреннего rebuild.
- `Crew`: уникальные зарегистрированные серийные номера/живые носители транспондеров согласно фактическим правилам CrewServer; отдельный `roomCrew` для каждого сканера с нужным модулем. `KiasScannerModules.Id`, `Transponder` и `Identity` сейчас битовые флаги (`Identity = Id|Transponder`); **не подменять оба смысла друг другом**. Сначала сохранить текущие правила и добавить тесты для Id, Transponder, Identity.
- `PersonCritical`, `PersonDead`, `Threat`, `FaunaThreat`, `AnomalyGrowth`, `Radiation`: источники проходят **те же** room membership + проверку нужного модуля, но игровые методы фактического обнаружения/классификации остаются родными. Пульсы никогда не оптимизировать «change-only» до исчезновения событий. Hostile fauna нельзя ограничивать только сущностями с Mind. `HasUnknownAlongside`/boarding сравнивает физические roomIds, а не пересечение кругов.
- `Optical`: текущее использование штатной камеры сохранить; **радиус её визуального сетевого наблюдения** — отдельно от logical room membership. Не обещать видеть сквозь стены/камерой вне обычных игровых правил.
- `Radiation`: существующий `RadiationReceiverComponent` физически измеряет радиацию **в месте сенсора**. Нельзя объявлять, что этот датчик теперь измеряет весь объём комнаты без фактического игрового источника; для действительного room-wide radiation добавить законный агрегатор измерений/локальных измерительных сущностей только после замеров и тестов, или отчётливо разделять `Local radiation reading` и `Room radiation detections`. Не имитировать показания синтетикой.
- `Connector`: теперь *та же физическая комната*, без прежнего круга; только реальные поддерживаемые интегрированные устройства. Прямые установки upgrade kit не теряют Direct.
- Кратковременные/постоянные выходы: `Entities/Occupied/Crew/Unavailable` отправлять change-only **после initial snapshot**, при появлении нового подписчика/Boot обеспечить snapshot/replay. `Emission` нельзя подавлять только потому, что такой же Value уже излучался раньше, если новый controller подписался; проверить lifecycle и сортировку операций.

## 6. Инвалидации, безопасность и latency

**Geometry dirty:** anchored wall/window create/remove/destroy, permanent door create/remove/move, airtight entity move/rotate/re-anchor/changes except transient door Open/Close, GridTile change, grid split/merge, scanner physical move/rotate. Нужна подписка на фактически существующие события актуальной ветки (найти реализацию, не угадывать название). `AirtightChanged` от открытия/закрытия двери не должен пересчитывать geometry, если identity/barrier unchanged. Постоянная смена препятствия — dirty. **Capabilities dirty:** вставка/извлечение модуля, scanner on/off/power/DATA. **Targets dirty:** native device spawn/move/anchor/despawn, automatic bind, target online status. **People dirty:** tracked movement/grid changes/MobState/registration/transponder changes. Не запускать полный BFS из обычного periodic Scan.

Coalesce изменений в bounded queue, frame/tile budgets и incremental BFS jobs при огромной карте; building snapshot невидим до atomic commit. Измерять время/аллоки rebuild, долю cache hits и максимальную задержку dirty→commit. Пока изменённая область `REBUILD_PENDING`, **не использовать старую геометрию для новых positive triggers или команд**. У unaffected rooms разрешено продолжить старую стабильную ревизию. UI показывает статус, а не устаревшую «точную» карту. Pending и invalid statuses не должны давать ложные Motion/Threat/Crew, а при успешном commit обязателен immediate reconciliation + correct snapshot. Ограничение latency задаётся тестами и отчётом: ориентир <1 сек на единичную геометрию, <3 сек на волну 40 гридов при 60TPS в целевом окружении; если выше — фиксировать FAIL, а не скрывать задержку, выбирать пороги по фактической стоимости.

На grid remove/map unload полностью освободить кеш и индексы; никаких ссылок на мёртвые EntityUid/транспондеры/сканеры. Не нарушать `KiasPeriodicScheduler`/tick budgets и запреты повторного Enter/Leave из-за пересборки.

## 7. UI/сервисное отображение (обязательная часть)

- `KiasDisplaySystem.LocalUi.BuildCoverage`: вместо радиусного круга scanner рисовать **фактический набор клеток** (`Cells`, revision, статус), отличая border-door от interior при поддержке UI. Для сети — добавить новый `KiasCoverageShape.Room` в конец enum (не менять существующие ordinal), либо обосновать использование `Data` с раздельным renderer; не смешивать с существующим отображением DATA-кабелей.
- Локальный UI обычного/advanced scanner и режим «Покрытие» сервисного инструмента показывают `Room / N tiles / N doors / status / revision / modules`. **Убрать редактирование Range для RoomScanner**. Для обратной совместимости сохранённое `[DataField] Range` можно оставить как legacy ignored с документированной миграцией; сетевое `SetSensorRange` для room scanner больше не меняет логику и не провоцирует rebuild. Настройки прочих радаров/датчиков сохраняются.
- Показывать диагностические аномалии: `NO_INTERIOR_SEED`, `OPEN_TO_SPACE`, `AMBIGUOUS_BOUNDARY`, `ROOM_TOO_LARGE`, `REBUILD_PENDING`. Не спамить сеть геометрией каждый тик: отправлять только при открытом UI, после изменения revision, с cap/компрессией/тайловыми spans. При слишком большой карте — preview cut с предупреждением; **серверная** геометрия и детекция не должны усекаться из-за лимита UI.
- Сервисный debug export `room-map.json`: grid, scenario, orientation mapping, room IDs, spans/tiles, door boundaries, scanner module flags, integrated bindings, orphan/invalid reasons, rebuild latency, hash.

## 8. Производительность и контроль регрессий

В старом профиле `KiasCrewSystem` самый дорогой KIAS модуль. Новая room-геометрия должна быть **дешевле круга при стабильной карте**: O(1) lookup `CellToRoom`, обход людей и relevant fauna, per-room aggregate без per-scanner spatial query, no nested LINQ, no `new Dictionary/HashSet/StringBuilder` per grid scan, no default room BFS per tick. Статические `RoomSnapshot` делятся между scanner. Беречь latency первых сканирований при 40 ships и шквале разрушений: dirty processing bounded, не перекладывать задержку бесконечно.

Обязательные измерения **до/после**: `Crew.Update`, `Scan` stage timers, `Integration.Reconcile`, `CanControl` queries, room BFS total/changed tiles, `GetEntitiesInRange` count, tick allocations, system p95/p99, GC, topology queue age, scanner latency on real movement, number of emitted/replayed signals. При baseline 40 Briar × 21 scanner → 840 сканеров: зафиксировать counts и убедиться, что новый код не уменьшил обработку отключением сенсоров.

Критерий: корректная геометрия и полное покрытие модулей по тестам; ускорение должно быть **измерено**, а не постулировано. Сравнивать старый KIAS → новый KIAS на одном и том же физическом сценарии; затем Vanilla A vs B с новой функцией и отдельной пометкой о семантической разнице.

## 9. Запрещённые shortcut'ы

- Искать комнату через `IsSealedSafeRoom` (это bool безопасности и зависит от газа); использовать текущую воздухопроницаемость открытых дверей.
- Пропускать стены из-за диагоналей; объединять две комнаты через открытый шлюз; вычислять для каждого scanner круг + клиппинг стенами.
- Оставить radius-автопривязку в `Integration`, UI или других KIAS-callsites.
- Менять `PhysicsSystem`, `AtmosphereSystem` processing, door open/close алгоритмы ради зоны сканера.
- Записывать room IDs или тысячи рассчитанных cells непосредственно в `briarKIAS.yml`.
- Молча расширять сканеры, увеличивать число слотов или править layout оригинального грида.
- Оптимизировать заменой функциональности фиктивными результатами, запрещать повторные pules или подавлять фактические критические события.

Дополнительные тесты и матрица приёмки — `08_SCANNER_ACCEPTANCE_MATRIX.md`, миграция/совместимость — `09_SCANNER_MIGRATION_AND_UI.md`, отдельный benchmark геометрии — `10_SCANNER_PERF_AND_REBUILD.md`.
