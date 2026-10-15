# 08 — 20-минутный сценарий и случайно распределённые события

## Фаза подготовки вне измеряемого окна

- Seed, generator version, scenario hash и 40 stable ship IDs.
- Все корабли загружены из одних и тех же карт, world health stable, server ticks и runtime JIT прогреты.
- Занулить collector counters и пометить benchmark `tick=0`. Warm-up вне 72 000 измеряемых симуляционных тиков.
- В primary real mode: KIAS активна на заранее известном подмножестве гридов; число активных систем и graph cards фиксировано.

## 20 минут / 72 000 тиков при net.tickrate 60

| Фаза | Simulation minutes | Tick range (relative) | Смысл |
|---|---|---|---|
| IDLE | 0–2 | 0..7199 | спокойные корабли, фоновые контроллеры |
| MIXED | 2–15 | 7200..53999 | постоянные независимые случайные события |
| BURST | 15–18 | 54000..64799 | перекрытия массовых операций, PDC/FTL/fire/grid changes |
| RECOVERY | 18–20 | 64800..71999 | после cleanup: очередь, GC, lingering effects |

**Не устраивать один пожар/столкновение на всё время.** Event instances короткие и в разных местах. Вероятности и одновременно допустимое количество задаются в `scenario_config.json`; генератор случайным, но воспроизводимым образом распределяет events по всему флоту. Раз в заданный интервал подбирается ship subset, а не один и тот же ship.

## Базовые классы (реальные gameplay effect)

- `fire.start` / `fire.clear`: реальное горение, температурное развитие, suppression, recovery (контролировать state and burning limits).
- `atmos.depressurize` / `atmos.reseal`: структурный пробой + gas flow + AirAlarm danger/normal; smoke cleanup only after real recovery.
- `weapon.fire` / `projectile.hit` / `projectile.miss`: настоящие допустимые орудия из грида, реальная физика снарядов, flash detection, попадание или перехват; параметры залпа, угол, IFF/дальность.
- `ftl.depart` / `ftl.arrive_open_space` / `ftl.arrive_horizon`: штатный FTL request, state machine, cooldown, появления рядом с другим ship, detection Horizon, arrivals recorded.
- `movement.thrust` / `movement.rotate` / `collision`: включение штатных двигателей/гироскопов и физические столкновения с наблюдаемым relative speed. Контроль что корабль **действительно движется**, не просто установлен вектор в тесте.
- `dock` / `undock`: штатные механизмы и постусловия.
- `power.deficit` / `power.loss` / `power.restore`: реальные PowerNet нагрузка/отключение, реакция devices.
- `kias.data_cut` / `kias.data_restore`: повреждение/ремонт/отключение KIAS DATA-сети только в B; в A соответствующий событийный **stimulus** должен иметь эквивалентное тестовое действие или явно классифицироваться как KIAS-only и исключаться из сравнения как world-identical gameplay event. Не «удалять кабель из отсутствующей сети A».
- `crew.critical` / `crew.dead` / `crew.recover`, `crew.enter` / `crew.exit` / `boarding`: настоящие состояния персонажей, движения и scanner coverage.
- `anomaly.growth`, `radiation.exposure`: физические детектируемые события, ограниченные TTL и безопасным clean clone.
- `light.power_off` / `light.power_on`, `device.operate`: реальные изменения vanilla света/устройств в A/B; KIAS реакции учитываются дополнительно.

## Особые ограничения Briar

- На Briar есть 6 weapon flash detectors: 2 левое направление, 2 правое, передний/задний по 1. Тестовые выстрелы должны проверять сектор относительно **фактического поворота грида и устройства**, не захардкодить мир-ориентированное `left`.
- Совместимого KIAS-PDC оружия может не быть в исходном ship. Базовый тест оружия использует любые настоящие штатные пушки. Реальную автоматическую ПКО проверять отдельно в test-only compatible-weapon fixture, без модификации исходного Briar.
- Не добавлять LightGroupController и четырёхпозиционные реле; профили требующие их проверить/пометить `BLOCKED` или подготовить **временный независимый** fixture.
- `KiasSuppress` в каждой комнате не считать равным гарантированно успешному подавлению без настоящих условий работы.

## Честная нагрузка при изменяющемся состоянии мира

KIAS может снижать вред (пожар/попадания), тем самым **меняя последующую нагрузку**. Весь primary report должен содержать `world_divergence`, а не один процент «чистой накладной цены». Кроме primary A/B, сделать passive diagnostic (если технологически реализуем) и passive/idle KIAS ON/OFF для изоляции cost detection/graph. Физические ограничения и RNG других систем могут давать расхождения; фиксировать и не подгонять насильно симуляцию.

## Несколько прогонов

Для валидного вывода о небольших эффектах как минимум `A-B-B-A`, каждый запуск отдельным процессом, 20 measured min + matched warmup, идентичный seed. Порядок/повтор исключает часть drift от нагрева процессора/фоновых задач. Кроме default seed, можно протестировать минимум второй seed как sensitivity check, но **оба A/B внутри одной пары имеют одинаковый seed**.

Фактический запуск всей серии с длительностью 80 минут плюс warmup не обязателен во время разработки harness; в интерфейсе должен быть явный `Full Benchmark`. Не утверждать, что test завершен до окончания всех циклов и формирования persisted raw samples.
