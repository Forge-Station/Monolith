# 04 — FULL GAME STRESS: никаких урезанных «7 пар событий»

## Исполняемые сценарии и проверка фактического результата

Все сценарии использовать штатные Systems SS14/RobustToolbox. Запрещено напрямую дергать `KiasProtocolSystem.Trigger` / KIAS event только ради накрутки нагрузки. Допускается вызвать API штатной игровой системы (атмосфера, оружие, FTL, damage, movement, cable interactions), затем зарегистрировать **доказательство игровой реакции** (реальные снаряды, hit/contact, состояние моба, fire tiles, broken wall, tracker state, alarms, issued commands, actual PDC intercept). Каждый tick-indexed action имеет `planned`, `attempted`, `nativeStarted`, `postconditionTrue`, `completed`, `failureReason`, `causalParent`, ship ID and tick, player/mob stable ID.

Обязательные независимые drivers с setup/teardown и детерминированными параметрами:

1. Огонь: настоящий горящий tile/plasma hotspot, распространение/тушение; alarm/suppression и контрсценарий без модуля.
2. Атмосфера: breach, depressurization, pressure alarm, sealing/recovery, работа вентиляции; исключить химеры, где отключена симуляция атмо.
3. Damage/repair: физические projectile hits, structural damage, torn walls/doors, patch and topology convergence.
4. Вооружение/ПКО: штатные ганы (не обязательно gun prototype на Briar), реальные bullets и WeaponFired, KIAS detection, IFF own/friendly/hostile, compatible PDC auxiliary fixtures, ammo drain, direct intercept/hit/LOS; multiple simultaneous hostiles.
5. FTL: реальные штатные attempts and `FTLCompleted` into empty space, into contact sensor range and out of range; jump failures reported; autopilot arrival отдельно, не путать.
6. Физика: реальные movement input, acceleration, anchored/static impacts, grid-grid collisions с реальным relative velocity, docking/undocking through docking systems. Повторить ниже/выше thresholds.
7. Crew: actor с Mind, зарегистрированным KIAS transponder, настоящие damage→Critical→Dead thresholds, revive/recover separately, stranger boarding, armed intruder, crew count, health/death alerts; **не считать MobHuman без Mind/registration за проверенный CrewCritical**.
8. Электропитание: отключение и восстановление сети, электроприборов, светильников, реле/помп, AME, отказ частей системы; изменения `KIAS online/offline` и игра продолжается.
9. DATA: реальный разрыв кабеля и восстановление, split KIAS topology, loss/reboot cards, 4 racks and 27 templates, сохранение состояния без ghost events.
10. Общая автоматизация: все 27 graphs; ALL/ANY, Specific, timers/cooldowns, real physical actuator outputs, addresses, integrated doors, local area lights/suppression/speakers.
11. Radiation, anomaly growth, contact/proximity, defense radar, sonar, distinct heavy AI movement/doors/collision; safe isolated fixtures and cleanup.
12. Кросс-нагрузка: много игроков/ботов движутся, стреляют, работают с устройствами; сетевые клиенты подключены в отдельном network phase, чтобы учесть PVS/state replication. Клиенты изолированы, одинаковые count/action logs для A/B.

### Синхронные волны, которых в предыдущем benchmark не было

Каждую волну выполнять в **одном целевом server tick**, доставив по одному независимому native stimulus на каждый из 40 кораблей. Проверить 40/40 `attempted`, затем 40/40 actual outcome (возможные gameplay refusals классифицировать отдельно). Не фальсифицировать 40 «смертей», если реально 39/40.

| Wave | Одновременность | Проверяемый результат |
|---|---|---|
| Critical-40 | 40 registered characters damage in tick T | 40 реальных state transitions и crew graph alarms |
| Death-40 | 40 registered characters lethal damage in tick T | 40 реальных deaths, stale refs/cleanup |
| Fire-40 | 40 hotspots ignite in tick T | actual fire tiles, alarms, suppress/react |
| Hull-40 | 40 destructive hits/breaches in tick T | wall damage, topology/atmos changes |
| Gun-40 | 40 gun volleys at T (plus targets) | fired projectiles, impacts/interceptions/IFF |
| FTL-40 | 40 distinct native jumps at T | launched, completed/denied by native rules; sensor reaction |
| Collision-40 | 40 independent collision initiations at T | real physics contact callbacks/relative velocity |
| Power-40 | 40 power disruptions at T | actual power grid/device states |
| DATA-40 | 40 cable cuts at T | actual KIAS disconnected, rebuild, reconnect |
| Room-40 | 40 wall/door changes at T | rebuild queue latency, no room leakage |
| Mixed-40 | 40 randomly assigned DIFFERENT native disruptions in tick T | correct independent modes + cleanup |

Mass-wave scenes should not collide with each other accidentally: separate sectors/maps or spaced coordinates, distinct jump destinations, weapon targets. Destructive wave should reset through **fresh world instance** or verified rollback, not manually set result fields or fake game states. Run multiple independent seeds. Report dispatch time spike separately from delayed reaction/cleanup time. Global server does not physically perform 40 arbitrary method calls *simultaneously*; requirement means same simulation tick, not wall-clock parallel threads.

### Сценарии на длительность (recommended defaults)

- Quick smoke: 1-3 ships + 3 min warmup + 2 min measured; native drivers postconditions + brief functional gate; no benchmark verdict.
- Regression wave: 40 ships, 3 min warmup + 8 min measured, single wave type per isolated run, controlled aftershock recovery; all 11 wave types; hash and per-wave report.
- **Default full performance ABBA:** 40 ships, 3 min warmup + **30 min measured** EACH A/B/B/A (108000 measured ticks at 60 TPS), i.e. ~132min simulated/server wall time plus build/tests. 0–3 min idle, 3–20 min independent stochastic events, 20–26 min synchronized 40 waves and overlaps, 26–30 min recovery; cross-fade crowd workload to expose PVS/GC. No need to hardwire 30min: CLI switches `-Minutes 30` and `-Ships 40`, optional long 45-60min runs if weak statistics.
- Scale: 10/20/40/80 grid control series separate from default 40. User wants 30–40 normal fleet grids of DIFFERENT types; include Briar/BriarKIAS as reference, plus real other shuttle maps with valid prototypes. For clean optimization before/after run 40 identical Briars too. Never claim a homogeneous fleet measures heterogeneous Frontier perfectly.

## Randomness vs determinism

Pre-generate `seed`, stable ship IDs, event IDs, target tick and parameters; call native systems through same harness and record attempted/success/failure. Network latency, physics nondeterminism and KIAS interventions may yield different subsequent states; classify divergence and postconditions by A/B rather than quietly re-syncing a world. Do not schedule 'one ship burns for 20min' — lifetimes are bounded, order shuffled, overlapping different categories. A failure to execute an event on either variant does **not** count toward parity success. Abort or label invalid if event coverage skew exceeds tolerance.

## Client mode

Retain deterministic scripted fake players/AI actions to supply network traffic, alongside real gameplay entities. Networked clients optional in minimal headless profiling, **mandatory** for full multiplayer report. Character count/registrations equal in both variants, gameplay behaviors as close as possible, input scripts recorded. CPU profiles distinguish server sim from net snapshot/PVS.


## Дополнительные native room waves (v2)

Внести волны с настоящими игровыми объектами, а не методами, выставляющими KIAS-события напрямую: (1) 40 человек одновременно физически пересекают дверные пороги A→B, что должно переключить владельца подсчёта; (2) на всех 40 гридах в одном тике открыть двери — **нулевое** число rebuild geometry; (3) одновременно разрушить 40 межкомнатных стен → реальные merge/updated bindings; (4) восстановить перегородки и проверить обратный split; (5) на 40 гридах одновременно исчезают DATA/power у части scanner, геометрия неизменна, но capability/automation availability меняется; (6) 40 зарегистрированных игроков переходят в крит и затем умирают уже в правильных комнатах.

Следить за `scheduledTick`, `driverAttemptTick`, `nativeEffectTick`, `roomCommitTick`, `observedSignalTick`, `physicalActuationTick`, `roomRevision`, missed/late per ship. Не засчитывать запланированное событие как произошедшее без postconditions. В полном A/B на Vanilla нет RoomScanner, но геометрические последствия (разрушение стен/переход игроков/открытие дверей) должны быть идентичными. Room-specific acceptance выполняется отдельно на KIAS. Профиль перестроек из `10_` не заменяет полный сценарий боевых действий.
