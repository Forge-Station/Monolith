# Исходная диагностика лаборатории

Проверено локально 2026-10-09. Источником истины являются файлы рабочего checkout,
а не старые SHA из пакета. Удалённые main/KIAS проверены через ls-remote и
совпадают с локальными refs. Fetch и обновление веток/submodules не выполнялись.

KIAS HEAD: `38c67ac61a0f2565e372c66431590e37da5e7ed0`.
Локальный upstream/main и общий предок: `8ea71cec189dec70c6e40da91e23648cdc4e12cb`.
RobustToolbox: `c2fc1375fc8647f4a29d2e9c8a8ae86dfb35d66e`, gitlink совпадает,
вложенные submodules инициализированы, двигатель не изменён.
SDK: 10.0.101, global.json требует 10.0.100 с latestFeature.
Исходная ветка сохранена. Root AGENTS.md и CONTRIBUTING не найдены;
применены инструкции пользователя и документы указанного пакета.

## Подтверждённые точки интеграции

| Система | Что подтверждено в коде | Граница доказательства |
|---|---|---|
| KiasSystem | dirty queue, 8 rebuild/Update, examination cap 128 | нет измеренной стоимости rebuild |
| ControllerRuntime | event/work/command queues; 64 events, 256 work, 128 commands/Update; 2048 evaluation budget | 25 running cards не означают правильные physical outputs |
| ControllerRuntime boot | до 16 boots, около 4 ms cutoff после первого boot | это бюджет, не измеренная latency |
| Crew | Modules агрегируются битовыми OR; Identity объединяет Id/Transponder; range circle до 10 | направление scanner Transform не участвует в круговом coverage loop; rooms физически не проверены |
| Integration | Connector автоматически интегрирует AirAlarm/FireAlarm/monitor/vents/doors в радиусе | 8 AirAlarm controllable подтверждены, физические danger/clear проверяются PhysicalGate |
| Safety | AtmosAlarm от AirAlarm требует integrated + CanControl + Atmosphere role | прямой KiasTrigger не проверяет игровую тревогу |
| Protocol | FireAlarmComponent обязателен для Fire/FireClear | на обновлённом Briar четыре FireAlarm, связи с AirSensor добавлены |
| Defence | projectile index, scan scheduler; 40 grid budget | не составлен рейтинг дорогих методов |
| ExternalSensor | WeaponFired приходит через игровую интеграцию; собственный grid исключается; defence role/online обязательны | реальные шесть секторов не проверены |
| Navigation | FTLCompleted обрабатывается отдельно от KiasAutopilotArrived | ни FTL, ни autopilot live-сценарий не проведён |
| ControllerUI | BUI access/range/configuration, Draft, schema compiler | два editor integration tests passed; native client BUI проверяется PhysicalGate; полный editor UI smoke остаётся |

Изучены текущие preset nodes/wires: `functional_results.json` содержит graphs,
профили, stimulus, hash каждого пресета и исходные физические карточки.
Все 27 пресетов компилируются на настоящем сервере (powered snapshot).

## Метрики и запуск

Robust.Shared/Timing/GameLoop.cs имеет Input/Tick/Update события.
TickStart/Stop обрамляют Tick callback, CurTick увеличивается после него.
`robust_game_loop_frametime` использует Histogram.NewTimer вокруг Tick callback.
Описание «ms» не следует использовать как коэффициент: Prometheus timer записывает
секунды; источник реализации следует учитывать при будущем collector sanity test.
`robust_entity_systems_update_usage` обрамляет Update EntitySystem,
`robust_server_update_usage` — этапы BaseServer. Это агрегаты, не per-tick traces.

Проверены CVars metrics.enabled/host/port/update_interval, runtime_gc,
runtime_thread_pool, runtime_jit. Runtime mode CVars — **строки**, например
`"Counters"`, не boolean. HTTP metrics endpoint не запускался/не проверялся.
На этапе первоначального аудита нейтральный collector и replay drivers отсутствовали. Текущее состояние: реализованы четыре пары настоящих воздействий и частичный Content PreEngine→PostEngine collector. Это ещё не полный сценарий и не измерение всего тика.

KiasUpdateMeasurement измеряет выбранные Update с Stopwatch и thread allocation
counter. Не охватывает все callbacks и не даёт атрибуцию отдельных методов.
Заявлять по нему «полную стоимость KIAS» нельзя.

Запущена явная Release-сборка integration project, которая также собирает
Content.Server/Client/Shared и движок. Ошибок 0; исходная сборка дала 4237 warnings.
Это не две изолированные сопоставимые A/B сборки и не standalone server benchmark.

Первоначальная подготовка BuildPair зависела от функционального gate. Сейчас отдельные Release-сборки A/B подготовлены на закреплённой базе; актуальные хеши находятся в BASELINE_MANIFEST.json. Существующий branch overlay
содержит изменения vanilla APIs (damage/projectiles/guns/company/power/atmos/
DeviceLink/fire control). Полный name-status и binary patch сохранены; этот patch
не объявляется минимальным доказанным KIAS overlay.

Дополнительные исправления по физическим проверкам: выбор ближайшего порта в редакторе при уменьшенном масштабе; завершение короткого shutdown-звука с сохранением требования питания самого динамика; исключение собственных и дружественных IFF-источников из автоматического перехвата ПКО. Классификация источника кэшируется в пределах одного сканирования.

Native PDC проверяется отдельным совместимым орудием на AUX_FIXTURE: фактический перехват, перекрытие стеной, восстановление после удаления стены, две цели в близких направлениях и исключение собственных/дружественных снарядов. Несовместимые орудия исходного Briar не объявляются совместимыми и не заменяются в авторской карте.

Подготовка A исключает вложенные предметы удалённых устройств. Исторический короткий прогон с батарейкой удалённого глушителя признан недействительным. Новые прогоны отклоняются при ошибках загрузки сервера.
