# 10 — Room topology perf benchmark и защита от лагспайков

## Что мерить отдельно

Исторический 40-Briar A/B показывал `KiasCrewSystem ~+1.565ms/tick`, но это не причинная стоимость только геометрии: сюда входят опросы, сигналы графов, fauna spatial queries, регистрация и вложенные callbacks. Нужны отдельные профили **на текущем checkout, до и после**.

### Профиль 1: стабильный флот

- 1/10/20/40 Briar, 21 scanner/ship по историческому инвентарю (проверить фактическое количество). 0 игроков, потом 1/5/20 tracked persons на грид, несколько враждебных существ и зарегистрированных людей.
- Не трогать стены; no geometry rebuild after init. Показатели: `Crew Scan` CPU/tick, `Integration` CPU, `GetEntitiesInRange` calls, GC allocated bytes, p50/p95/p99/p99.9 whole tick, number of actual sensors and `RUNNING` cards.
- Выполнить до/после с одинаковыми warmup/ticks. После патча geometry cache lookup не приводит к фоновому BFS.

### Профиль 2: последовательные изменения

- На одном Briar неоднократно (каждые 60 тиков) открывать/закрывать 10 дверей: **geometry revision не должен изменяться**, если дверь не заменена. На втором сценарии разрушать/строить стены и удалять/восстанавливать дверные entity через игровые системы.
- Проверить сколько раз перестроилась геометрия, сколько cells обработано, сколько targets перепривязано. Частое открывание door без geometry rebuild/GC flood обязательно.
- Изменять модули/питание: module dirty target reconciliation без полного flood fill.

### Профиль 3: worst case 40 grids

- На одном серверном тике разрушить/восстановить критические wall sections на всех 40 кораблях; на другом тике сделать 40 перемещений/поворотов сканеров; также 40 одновременно заходящих в помещения Mind-persons и независимый FTL/collision drive отдельными настоящими системами.
- Зафиксировать **tick инициирования**, geometry invalidation tick, actual commit tick, first correct `Entities/Motion/AutoBinding` tick; p99/p99.9 и максимальный topology backlog, некорректные команды во время pending = 0.
- Не принуждать rebuild всех 40 карт в одном callback, применять bounded jobs и coalescing; но и не растягивать stale state бесконечно. Для очереди собирать max age + total deferred across all fleets.

## Требуемые данные и SLO

| Metric | Назначение |
|---|---|
| `geometry_rebuilds_total`, `dirty_reasons`, `door_state_changes_ignored` | Нет лишних пересборок от открытия дверей |
| `geometry_tiles_visited`, `rooms_created`, `scanners_bound`, `invalid_seed_count` | Реальные работы и корректность |
| `geometry_build_ms/p95/p99/max`, `geometry_build_alloc_bytes` | Сколько стоит расчёт формы |
| `dirty_to_commit_ticks/p95/p99/max`, `pending_rebuild_count` | Насколько быстро применяются исправления |
| `auto_integration_candidates`, `reassigned`, `can_control_rejected_wrong_room` | Нет радиуса/утечки доступа |
| `scan_people`, `scan_fauna_candidates`, `spatial_query_count`, `emissions_changed/total` | Затраты Crew и событийная оптимизация |
| `runtime_dispatch_depth/max_age/faults`, global GC, whole-tick latency | Эффект на работу систем и лагспайки |

Измерять на Release, в двух проходах с диагностикой и без (отдельный overhead report). Не создавать JSON/строки/per-target logs на горячем пути; counters и histogram samples публиковать из буфера вне timed section.

**Начальные SLO-ориентиры, которые агент валидирует на реальной машине, не магические гарантии:** единичный room rebuild commit <=60 server ticks (1 сек при 60TPS); simultaneous dirty 40 grids <=180 server ticks (3 сек); no stale positive events while pending; serviceable rooms report no unknown boundaries; ни один бесконечно pending; стабильная геометрия не перестраивается от открытия/закрытия двери.

Нельзя записывать `PASS` только по среднему тика: при новом алгоритме возможен редкий дорогой rebuild. Замеры отдельно на реальном долгом Briar и на 40-grid mass wave. По timeout/limit — status `FAILED_INCOMPLETE`, отчёт о том, сколько реально построено, а не «passed partially».

## Оптимизационный порядок

1. Сделать правильную и атомарную геометрию + tests (`08`), снять начальные цифры. Не накладывать сторонние оптимизации одновременно при атрибуции.
2. Убрать O(scanner × rooms) spatial queries и per-scan LINQ/hash allocations; room-based aggregates + unique tracked locations per sweep.
3. Сделать event/index updates for eligible target candidates and registration; убрать повторы `Reconcile` при KIAS topology notification without room geometry change.
4. Измерить, что стоимость стабильного 40 fleet упала **не из-за отключённых модулей/уменьшенных outputs**. Число реально срабатывающих сценариев физического functional gate должно остаться корректным.
5. Только потом делать нагрузку с настоящими авариями из `04_FULL_GAME_STRESS.md` и сравнивать Vanilla A vs new KIAS B, а также old KIAS vs new KIAS.
