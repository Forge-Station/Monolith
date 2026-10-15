# 05 — Детерминированная запись и воспроизведение случайных событий

## Общая архитектура

Разделить на `ScenarioCompiler`, `ScenarioManifest`, `EventDriver`, `WorldObserver`, `CausalityRecorder`, `ReplayValidator`. Общая часть не должна ссылаться на KIAS namespaces — это необходимо для сравнимых A/B binaries. Разместить в lab-only коде, который не изменяет игровую логику при обычном запуске.

## Случайность

Генерировать расписание заранее алгоритмом с документированными seed+version (например PCG32/Xoshiro с собственной state machine; фиксированная реализация в репозитории). **НЕ** использовать `Random.Shared`, `DateTime.UtcNow`, `Guid.NewGuid`, `EntityUid`, wall-clock, SQL ids, временные значения или активность физики как источник выбора событий. В каждом generation phase логировать PRNG state, seed и SHA256 итогового файла.

Сценарий создаётся ОДИН РАЗ и включается в A и B без изменений. У каждого корабля устойчивый индекс (`ship-0001..ship-0040`); при загрузке создать stable mapping индекса к фактическому GridUid. Различные EntityUid допустимы, stable labels и физические характеристики должны совпадать.

## Пример реального файла scenario.jsonl (иллюстрация формата, НЕ результаты теста)

```jsonl
{"id":"evt-000001","tick":600,"ship":"ship-0012","type":"fire.start","room":"engineering","params":{"ignition":"verified-source"},"ttlTicks":600}
{"id":"evt-000002","tick":902,"ship":"ship-0003","type":"weapon.fire","target":"ship-0017","params":{"weapon":"existing-compatible-proto","shots":6,"aimOffset":[0.0,1.2]}}
{"id":"evt-000003","tick":1440,"ship":"ship-0021","type":"ftl.arrive","near":"ship-0009","params":{"point":"replay-defined-point"}}
{"id":"evt-000004","tick":1530,"ship":"ship-0012","type":"fire.clear","room":"engineering","relatesTo":"evt-000001"}
```

### Правила event compiler

- Построить распределение категорий/целей/локализаций/углов и периоды восстановления. Избегать «один корабль горит все 20 минут».
- Синтаксически и семантически проверить каждое действие до полного прогона: prototype existence, target identity, physics constraints, resources available, FTL reachable point, matching rooms.
- При несовместимости типов оружия, FTL cooldown или отсутствующих помещениях не `skip silently`: заменить при генерации на валидную независимую реализацию того же класса с трассировкой решения или записать BLOCKED.
- Сосуществование событий разрешено; предотвращать неконтролируемые бесконечные цепочки, default TTL, recovery events, cap simultaneous dangerous effects.
- Поддержать событийные пары `fire.start/fire.clear`, `power.cut/power.restore`, `data.cut/data.restore`, `entity.critical/entity.healed`, `hull.damage/repair` (если ремонт реализуем), `dock/undock` и др.

## Выполнение по тикам

Подписаться на стабильный server-side lifecycle. На `IGameTiming.CurTick == scheduledTick` ставить оригинальное игровое действие (fire ignition, shot command, docking request, real FTL start, damage routed through real systems, etc.). Не переносить события из-за долгого wall-clock тика. При ошибке или отказе записать `execution_state=REJECTED/DEFERRED`, фактические ticks и причины.

Строго различать:

- `scheduledTick`: запланированное воздействие;
- `dispatchTick`: фактическое исполнение команд лаборатории;
- `engineEffectTick`: реальное игровое следствие (например FTL arrived, projectile hit, atmosphere danger);
- `detectedTick`: KIAS sensor;
- `graphTick`: выполнение графа;
- `actuatorTick`: изменение целевого устройства;
- `completedTick`: завершение проверки/cleanup.

**Нельзя обещать равные миллисекунды реального времени между серверами**: 60 TPS = 16.666… мс виртуального времени на тик, но реальный tick duration меняется. Гарантируется равенство входного schedule по tick index, а не физического состояния мира. Выходы автоматики KIAS изменяют world state, и это источник честного divergence, который надо измерять.

## Сверка событий

Перед прогоном рассчитать immutable SHA256 всех событий. По завершении сравнить coverage по (event.id, scheduledTick, target stable ID, params). Также сравнить dispatchTick, effectTick, успех/отказ, причины, количество фактически доставленных/попавших снарядов, реальные состояния энергии, газа, живых существ, координат.

Если KIAS перехватил снаряд, починил свет или потушил пожар — **это ожидаемое расхождение**. Не пытаться насильственно сломать честную автоматику только ради одинаковых downstream состояний. Вывести два отдельных режима:

1. **End-to-end A/B:** мир меняется от реальных KIAS действий — показывает стоимость системы «как играет игрок».
2. **Passive sensor/graph diagnostic** (test-only): KIAS обнаруживает, компилирует/исполняет логику, но actuator side-effects перехватываются безопасным lab shim. Служит для изоляции собственных вычислительных затрат KIAS при более одинаковом мире. Шим одинакового overhead в A/B, отключён в primary result; не считать его основным реальным тестом.

Проверку совпадения completed events делай с категоризацией `IDENTICAL`, `EXPECTED_DIVERGENCE`, `UNEXPECTED_DIVERGENCE`, `REJECTED`, `MISSING`. При большом unexpected divergence считать сравнение некорректным и объяснить это в report.

## Асинхронность

«Асинхронные события» = конкурентно действующие в игровом мире, а не произвольный `Task.Run` с мутацией EntityManager. Все игровые мутации — только с правильного серверного main thread и соблюдением ECS event ordering. Отдельные фоновые потоки — допустимы для записи очередей результатов и построения отчёта, с bounded queue и backpressure, не блокирующей игровой loop. `await Task.Delay` не используется для синхронизации игровых воздействий.

## Ошибки / сбои / воспроизводимость

Write-ahead журнал событий, checksum, flush в bounded batches, no logging full component trees каждый тик. Event driver предоставляет deterministic diagnostics dump: last 10s, current queues, event IDs, stack traces. При crash сохранить partial artifacts с `INCOMPLETE`; следующий прогон — новый процесс и чистая world snapshot, никакого «доигрывания с середины» без полного state restore.
