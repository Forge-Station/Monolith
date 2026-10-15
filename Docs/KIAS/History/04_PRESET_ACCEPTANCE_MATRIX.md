# 04 — 27 схем: индивидуальная приёмочная матрица

**Не подменять этот файл показателем `KiasProtocolSystem.Trigger()`.** Каталог `Resources/Prototypes/_Forge/KIAS/controller_presets.yml` на момент аудита содержит 27 `kiasControllerProgram`; карточки на Briar физически содержат 26 из них. Синтаксис пресетов в этом проекте — **JSON**, несмотря на расширение `.yml`. Читай actual `nodes/wires`, ports, operator thresholds и enabled, **не придумывай** семантику по названию. Любая новая редакция source пересчитывает матрицу.

`KiasControllerRack` — максимум 8 карт на шкаф, 4 шкафа достаточно для 26 карт. Карты должны работать параллельно с собственными программами и реальными источниками событий. Для всех сценариев проверять cooldown, повтор, clear/restore, false positives и связи ALL/ANY/SPECIFIC.

| ID пресета | Минимальный настоящий стимул / проверка | Критически важная проверка |
|---|---|---|
| `battle-impact` | боевой hit/damage/impact по обшивке, корабль с экипажем | эскалация + речь/recorder; `LightController`, `DefenceController` — **проверить targets** |
| `battle-flash` | внешний реальный выстрел в coverage детектора | сторона, относительное направление, IFF и выходы света/защиты |
| `battle-manual` | настоящий доступный игроку ручной запуск через UI | штатная авторизация, действия, запрет бесконечного повторения |
| `flash-unknown` | обнаруженный оружейный flash без идентификации/нейтральный источник | событие, сообщение, отсутствие ложного hostile |
| `hull-damage` | реальный ущерб конкретной стене/тайлу/структуре | правильная физическая локализация и запись |
| `collision` | реальное столкновение двух MapGrid с относительной скоростью | threshold, не путать с прямым event invoke |
| `atmosphere` | настоящая AirAlarm danger после изменения смеси/давления | `DeviceList`, `AirSensor`, `KiasIntegrated` и `AtmosSafety.Danger` |
| `anomaly` | реальный рост/изменение аномалии в зоне Spectral scanner | Spectral coverage обязателен, clear/log |
| `contact` | настоящий FTL прыжок другого грида в Horizon range | FTL completed, IFF class, spatial proximity |
| `arrival` | настоящее завершение autopilot/arrival (проверить по source) | не выдавать обычный FTL за автопилот, если это разные события |
| `proximity` | приближение чужого грида с валидным сенсором | distance thresholds, friend/foe, разные направления |
| `crew-critical` | персонаж реально входит в критическое состояние | registered crew / Biometric coverage / graph signal |
| `crew-dead` | персонаж реально умирает | корректный однократный transition, no duplicates |
| `vessel-critical` | повреждения/условия превышают реальный порог vessel critical | damage window, mayday и navigation effects при условиях |
| `power-deficit` | реальный дефицит питающей сети под нагрузкой | power monitor supply/consumption, восстановление |
| `fire` | истинная fire alarm опасность после настоящего возгорания | FireAlarmComponent + KiasIntegrated + suppression effect |
| `radiation` | реальное radiation field в coverage Radiation module | scanner detection, location and result |
| `local-threat` | hostile/unknown разумный субъект в зоне угрозы | threat module, faction/IFF, false-positive tests |
| `boarding` | чужой экипаж проникает на корабль при registered crew | не считать хозяина/робота/манекен автоматом врагом |
| `greeting` | настоящий капитан/владелец появляется в relevant coverage | запуск один раз по реальному lifecycle |
| `fire-clear` | после реального пожара FireAlarm возвращается в Normal | **на Briar отсутствует физическая card**, tested in temporary rack, исходный `BLOCKED` |
| `atmos-clear` | нормализация атмосферы и AirAlarm state | переход Danger→Normal, не просто удаление огня |
| `medical-assistance` | осмысленный medical distress при живом экипаже | таймеры, доступность антенны/канала, никаких ложных broadcast |
| `quiet` | авторизованное включение режима quiet | **карта есть, preset disabled**; явно описать enable policy и проверить реальное состояние |
| `power-lost` | потеря питания у конкретного подключенного KIAS устройства | event actually emitted, outage/restore |
| `boot` | включение powered core и подключённого rack | однократный запуск при Boot, без бесконечных loop |
| `shutdown` | штатное отключение KIAS/core/электропитания | завершение всех card jobs, отсутствие утечек и dead outputs |

### Общие требования к каждому из 27

Для каждого прогона собери:

```
TEST: <preset-id>
MAP: <clean clone hash>
CARD: <UID / serial name / rack slot / program hash>
PRE: <device coverage, required dependencies, power/data/room/IFF>
STIMULUS: <real game action, tick(s), actor, targets>
PATH: <game event> -> <KIAS detection> -> <controller node> -> <device command>
EXPECTED: <list actual world-visible effects from current graph source>
ACTUAL: <observable results + log/screenshots + sound/visible device state>
LATENCY_TICKS: <value or blocked>
STATE: PASS | FAIL | BLOCKED | INCONCLUSIVE
BUG: <reproduction, expected vs actual, related source>
```

### Обязательные дополнительные cross-tests

1. **ВСЕ** `RoomScanner` (13+8) — проверить coverage комнаты; test entity enters/exits every room.
2. **ВСЕ** `KiasSuppression` (13) — безопасный воспроизводимый пожар/срабатывание по координатам, расход ресурсов/результат; не разрушать исходник.
3. **ВСЕ** `KiasSpeaker` (10) — адресная проверка + однократное ALL fan-out. Сравнить 10 отдельных outcomes и suppress duplicates/incorrect cooldown.
4. **ВСЕ** 6 weapon detectors — ориентация и реальные выстрелы с контрольных точек спереди/сзади/слева/справа, близкие углы и отрицательные кейсы.
5. **4** racks — топология, статус, слоты/карты, включение/выключение, вынимание/вставка, cold boot, обновление после WRITE.
6. **83 интегрированных компонента** по сериализованному YAML — реальный список + power OFF→ON и проверка доступности управления.
7. **1** `KiasDeviceAdapter` рядом с DAM — реальный DeviceLink/DeviceList в зависимости от типа, не смешивать игровые шины.
8. **Фальшивые источники** — вручную вызываемые event/Trigger допустимы только как unit test bridge, не как end-to-end PASS.
9. **Отсутствующие устройства** — не добавлять LightGroupController, четырёхпозиционное реле или ПКО-пушки в авторский грид ради зелёного статуса. Отдельная чистая test-only копия разрешена и помечается `AUX_FIXTURE`.
10. **Покрытие классов scanner modules** — раздельные кейсы Motion, Identity, Optical, Connector, Threat, Spectral, Biometric, Radiation, если актуальные источники подтверждают эти классы; advanced имеет лишь 6 слотов, поэтому дополнительные классы через независимые контрольные конфигурации.

### Функциональный oracle

Не строить expected actions по одному `preset-id`. Распарсить каждый граф, скомпилировать через текущий `KiasGraphCompiler` с реальными profiles, определить reachable nodes, trigger inputs, conditional gates, cooldown, target selection и конечные effects. Логировать `expected action present` и `actual target count` по отдельности. Результат `no mapped target` — это не PASS даже если graph успешно загружается.
