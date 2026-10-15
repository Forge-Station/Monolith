> Действующая политика — пакет v4.1 от 13.10.2026: все игровые категории обязательны; прежние Diagnostic/Essential и отсрочки не дают strict-допуск. После успешных предварительных проверок агент самостоятельно запускает корневой BAT с START и ждёт четыре VALID результата. Ручной BAT сохраняет ожидание START. Исторические статусы ниже относятся к своим исходникам; текущие доказательства — в конкретном каталоге запуска и FINAL_READINESS.json.

# Полный реестр проверок KIAS и исправленных проблем

Срез: 2026-10-12, ветка KIAS. **FULL GAME FINAL RUN NOT YET EXECUTED. Строгая готовность: NOT_READY.**

Это навигационный реестр требований, реально существующих проверок и доказательств. Он не заменяет TRX, журналы игры и машинный preflight. «Исторический PASS» относится к указанной сборке/сценарию; изменение fingerprint требует проверки актуальности. Наличие обработчика или тестового метода само по себе не означает PASS.

## 1. Как читать результаты и запускать проверки

- Регрессия: `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Content.IntegrationTests.Tests._Forge.KIAS`; Debug проверяется отдельно. Все cases должны пройти, пропуск не равен успеху.
- Инструменты: `python -m unittest discover -s Tools/KiasBenchmark -p "test_*.py"`.
- `Run_KIAS_Tests.bat -CheckOnly`: только строгая готовность, актуальность исходников/сборок и матрица 16 требований. NOT_READY запрещает объявлять окончательную готовность.
- `Run_KIAS_Tests.bat -Quick`: свежая пара, регрессия, реальные короткие игровые группы B и четыре настоящих клиента. Требует START. Не заменяет полную серию A/B40.
- Обычный `Run_KIAS_Tests.bat`: START, строгий preflight, сборки/проверки, длинный A–B–B–A. Запускает владелец.
- `Run_KIAS_Intermediate_Tests.bat`: явный сокращённый EssentialPreflight; также START. Расширенная v3-сертификация этим режимом не подтверждается.
- Результаты пишутся в ignored `.kias-benchmark/`. Старые неудачные запуски сохраняются. Авторская карта не исправляется ради прохождения теста.

Готовый архив — доказательства, а не признак PASS. Сначала читать launcher-status/stage, failed/run-failed, completed и отчёт; отсутствие процесса и exit 0 без postconditions недостаточны.

## 2. Сводка реально полученных результатов

| Проверка | Получено | Предел доказательства / расположение |
|---|---|---|
| Ночной запуск 20261012-041737-a960d6b2 | FAILED_INCOMPLETE на прогреве первого A | Измеряемого ABBA нет; `rerun/20261012-041737-a960d6b2/NIGHT_FAILURE.md` |
| Регрессия ночного gate | 165/165 PASS | Исторический Release TRX в `rerun/20261012-041737-a960d6b2/tests/gate.trx`; не новый запуск после всех правок инструментов |
| 27 физических программ | Gate и отдельный JSON-валидатор | 27 presets; 28 физических случаев с отдельным пороговым вариантом; не требование 27 установленных карточек на Briar |
| Новые пять игровых категорий A1/B1 | По 13/13 PASS | `v3/postnight68/deferred-r3/`; реальные действия и postconditions |
| Новые пять категорий A40/B40 | По 520/520 PASS | `v3/postnight68/deferred-40/`; B: 40 гридов, 1040 карточек, 0 faults/offline, семь пустых очередей |
| Четыре настоящих клиента A/B | PASS | `v3/postnight68/network4-*/`; замороженные ночные бинарники + исправленный runner, не новая полная серия |
| Повторная проверка новым валидатором | 18/18 PASS | `v3/checks69/revalidation.json`; 14 ночных smokes + A1/B1 + A40/B40. Повторный анализ журналов, не 18 новых игровых запусков |
| Инструменты после усиления валидатора | 77/77 PASS | `v3/checks69/tool-tests-final.log` |
| Масштаб crew/DATA/power 1/10/20/40 | Исторический PASS | `v3/scaling52/`; 10/100/200/400 завершений, 26/260/520/1040 карточек |
| Восемь разных корпусов | Debug 8/8 и Release 8/8, затем общий ночной gate | [HULL_COMPATIBILITY.md](HULL_COMPATIBILITY.md); AUX_FIXTURE с настоящими питанием/DATA |
| Составная волна B40 | Исторический PASS | 560 доставок, 440 асинхронных завершений; это не текущая полная A/B40 матрица |
| Physics | 12 контролируемых запусков на 40 Briar | `v3/physics-timing66/`; причинные ограничения ниже |
| Runtime/observer | 8 диагностических запусков | `v3/performance-research67/`; конкретная Regex-оптимизация, не весь исторический Runtime |
| Финальный CheckOnly | NOT_READY | `v3/postnight68/checkonly.log`; после изменений реестра/валидатора fingerprint снова требует актуализации |

Все пути артефактов в таблице отсчитываются от `.kias-benchmark/`.

## 3. Шестнадцать обязательных условий финального preflight

| ID | Что должно быть доказано | Что ещё нужно |
|---|---|---|
| rooms | Реальный Briar (-11,2), все scanner cells/границы/наружные зоны, UI | Актуальная машинная выгрузка и ручная игровая визуальная приёмка |
| physical_cards | Все 27 presets через настоящие предметы, runtime и world output | Свежий полный gate и проверка соответствующих артефактов |
| native_fire_atmos | Настоящий огонь, подавление, атмосфера, вентиляция и двери | Текущая симметричная A/B40 проверка всей категории |
| native_guns | Настоящий выстрел, полёт/попадание, наружная вспышка и IFF | Текущая A/B40 проверка |
| native_pdc | Враждебный перехват, friendly/occlusion/empty-slot, reload/jam | Текущая A/B40 проверка ПКО; 100% перехват не требуется |
| native_ftl_navigation | Питание/дальность/энергия FTL, завершение входа/выхода и прибытие | Текущая A/B40 проверка |
| native_collision | Физический контакт, урон корпусу и пороги | Текущая A/B40 интеграционная проверка |
| native_crew | Critical/death/revive, уход, посетители, угрозы и fauna | Текущая полная A/B40 категория |
| native_data_power | Разрыв/возврат DATA, питание core/rack, восстановление cards/queues | Текущая полная A/B40 категория |
| native_other | Аномалия, радиация, структура, свет, стыковка и ALL/ANY | Пять возвращённых категорий A1/B1/A40/B40 PASS; остальные подкатегории ещё проверить на текущей паре |
| sync_composite | Все требуемые волны на 40 кораблях в одном игровом тике, составные аварии и restore | Текущая полная A/B40 матрица; новые пять категорий уже проверены |
| scaling | Ступени 1/10/20/40 и восемь настоящих корпусов | Актуализировать доказательства и повторить обязательный свежий gate |
| network | Настоящие клиенты, состояние игры, ненулевая сериализация, sandbox | Проверить сеть на текущей окончательной паре |
| runtime_comparison | Сопоставимые CPU/allocations/latency/commands, ноль тихих потерь | Полное историческое сравнение Runtime и стоимость обязательных наблюдателей |
| physics_experiment | A/B/C/D, инвентарь, повторы, разброс и overhead | Свести актуальные доказательства; безопасное изменение production collider не обосновано |
| fresh_builds | Замороженные исходники, Release A/B, Debug/Release gate, настоящий Quick | После завершения правок обновить весь пакет и получить строгий CheckOnly PASS |

Точные ID определены в `Tools/KiasBenchmark/fullgame_contract.json`; валидатор `readiness.py` проверяет свежесть fingerprint, существование и SHA256 каждого доказательства. Нельзя переносить общий PASS только по названию категории.

## 4. Игровые группы стресс-проверок

Для каждой группы входы генерируются на каждый корабль с одинаковым scheduled tick. Проверяется фактический engine tick доставки; завершения могут быть асинхронными, но обязаны уложиться в индивидуальный deadline и окно измерения. Ниже длительности в тиках измерения; прогрев native_suite дополнительно 1800 тиков. 60 тиков соответствуют одной секунде симуляции, реальное время зависит от скорости игры.

| Группа | Воздействия и обязательные postconditions | Измерение |
|---|---|---:|
| systems | ALL/ANY fanout, critical/recover/death/revive, DATA, core/rack power, gun fire/clear; настоящие команды/сигналы/возврат cards | 7200 |
| vent | Нативная разгерметизация, действие вентиляции/помпы и восстановление; реальные gas/alarm состояния | 4200 |
| fire | Нативное возгорание и подавление, последующая очистка; настоящий огонь и сигналы alarm/KIAS | 2760 |
| combat | Friendly/hostile, физическое препятствие, извлечение/пустая кассета/возврат, повторный hostile; реальный выстрел и перехват | 4200 |
| collision | Настоящий физический контакт/урон выше порога, затем восстановление и прекращение контакта | 3960 |
| world | Двери, посетитель/оружие/fauna, flesh anomaly, breach/repair, radio jam, уход crew, DATA, ПКО, FTL вход/выход и navigation arrival | 26000 |
| composite | Одновременно critical/death/fire/gun/DATA/power/breach; далее восстановление зависимостей и очистка | 2400 |
| deferred | Повторное облучение/очистка, свет от питания и команды, структура damage/repair, dock/undock, finite magazine exhaust/reload | 11500 |
| collision_rotated | Реальный low/high удар и восстановление; 40 разных углов, включая малые повороты обоих знаков | 3960 |
| pdc_operator | Основной crew в Critical, независимый AUX оператор извлекает кассету, native empty control, та же кассета возвращается, crew восстанавливается | 1800 |
| fanout / crew_power / gun | Отдельные части systems для локализации ошибки | 2100 / 3600 / 1500 |
| pdc_controls / magazine | Отрицательные контроли ПКО либо отдельное извлечение/возврат кассеты | 3000 / 600 |

Стандартный native_suite включает первые восемь групп, обязательные pdc_operator и collision_rotated. Дополнительные узкие группы служат диагностике. Конечный магазин проверяется на Mk58: начальные 18 патронов, опустошение, 90 тиков отрицательного контроля, штатная замена магазина и новый реальный выстрел. Это не доказательство исчерпания бесконечной кассеты ПКО.

Радиация: реальный источник, CurrentRadiation crew >= 1, реальный Radiation pulse B; удаление источника и dose <= 0.01, затем повторное воздействие. Это не автоматическое доказательство дальнего комнатного замера S06.

Свет: проверяются CurrentLit и PointLight.Enabled, для команд также On; в B команда проходит через настоящий KiasControllerIoSystem.Command. Native A использует собственный переключатель как физический baseline.

Структура: штатный DamageableSystem, конкретная существующая destructible Wall, реальный рост Structural, сигнал IntegrityMonitor по этому target, затем точный возврат к исходному урону.

Стыковка: отдельный AUX grid с AirlockShuttle, штатные CanDock/Dock/CanUndock/Undock, взаимные DockedWith и настоящий physics joint. В B наблюдаются только существующие порты Docked/Undocked. На Briar нет отдельного docking sensor, поэтому тест явно добавляет AUX_FIXTURE на настоящие питание и DATA.

Полный сценарий дополнительно содержит независимые seeded background события между синхронными составными волнами. Идентичные inputs/seed не означают идентичную физическую эволюцию A и B: присутствие устройств и действия KIAS вызывают естественную дивергенцию.

## 5. Обязательная матрица сканеров G/I/S

Ниже полный перечень требований из `08_SCANNER_ACCEPTANCE_MATRIX.md`. Таблица задаёт ожидаемое поведение, **не объявляет каждый пункт отдельно принятым**. Реализация и регрессии перечислены в инвентаре ниже; ручная визуальная приёмка открыта.

| ID | Сцена / действие | Обязательная проверка |
|---|---|---|
| G01 | Квадратная закрытая комната, scanner в стене, 0°/90°/180°/270° | В каждой ориентации seed находится в правильной комнате; все её floor tiles покрыты; другой стороне стены нет доступа |
| G02 | Два помещения A/B, airlock closed→open→closing→closed, человек проходит | `A.RoomId != B.RoomId` и не меняются от состояния двери; человек в B невидим A; дверь отражена в обеих границах |
| G03 | Существо стоит на общей дверной клетке | Оба сканера могут обнаружить при нужном модуле; на каждом ровно `Entities=1`; global Entities/Crew без дублирования |
| G04 | Дверь удалена, затем восстановлена | Комнаты объединяются только после удаления, вновь разделяются после установки; dirty→commit в бюджете; обновлены outputs |
| G05 | Разрушаем межкомнатную стену без двери и восстанавливаем | Merge/split без промежуточного bleed и без ложного Motion от одного rebuild |
| G06 | Г-образная комната, слепой угол, >25 клеток до цели | Полное покрытие всех internal cells даже за пределами старого Range; отсутствие «просвечивания» стен |
| G07 | Diagonal wall/window/углы (включая диагональное окно) | Соседство по кардинальным рёбрам не пересекает преграду, diagonal corner-cut нет; если конкретный особый объект не поддерживается — явный status и обязательный fixture/fix |
| G08 | Firelock/BlastDoor/гермозатвор, многотайловой проход, locked/open, power loss | Независимо от состояния створок помещение не объединяется, boundary видима в обеих примыкающих областях |
| G09 | Scanner на стеновом tile повернут наружу/в глухую стену | `NO_INTERIOR_SEED`; чужая комната не выбирается; никакого кругового покрытия |
| G10 | Два сканера в одной комнате с разным набором модулей | Совпадает RoomId, но сигналы Threat/Radiation/Identity исходят только от модульно способного сканера |
| G11 | Scanner вытащен из стены, перенесён/повёрнут, выключен DATA/питание | Старые bindings очищены, новая комната назначена лишь после валидного seed и online; геометрия от смены питания не перерасчитывается |
| G12 | Разгерметизация через повреждение внешней стенки, отсутствие атмосферы в комнате | Нет бесконечного BFS в Space и ложного покрытия наружных объектов; статус OPEN_TO_SPACE / BREACHED; решение об объёме явно зафиксировано |
| G13 | 2 гриды близко вплотную, смещение/вращение одного, grid split | Пересечение по мировым координатам не даёт общего RoomId; очистка старого кеша после grid removal |
| G14 | Модуль вставлен/извлечён без изменения стен | Только capabilities dirty; tile map/hash геометрии не меняется; outputs and integration пересверены |
| G15 | Room > 4096 cells и malformed unsupported boundary | `ROOM_TOO_LARGE/UNSUPPORTED_GEOMETRY`, без частичных ложных показаний и memory runaway |
| I01 | Basic с Connector, AirAlarm на дальнем конце одной длинной комнаты (>10 tiles) | Auto integration succeeds; `CanControl=true` при корректных питании, DATA, access |
| I02 | AirAlarm в соседней комнате через ОТКРЫТУЮ дверь | Не интегрируется сканером из A и не управляется из A через его Connector |
| I03 | Дверь на shared boundary с двумя подходящими сканерами | Stable deterministic scanner owner, без частого переключения Open/Close; binding не даёт доступа на другой grid |
| I04 | Стеновой speaker, vent, air alarm, FireAlarm, door, optional device adapter | Общая методика wall-boundary association; устройства корректно привязаны, нативные действия действительно исполняются |
| I05 | Device integrated `Direct=true` через upgrade kit | Сохраняет Direct при пересчёте комнат, отключении scanner и перезагрузке |
| I06 | Автоматическая привязка после удаления стены, отключения scanner/Connector, разрыва DATA | Освобождение/смена target owner; любые команды проходят актуальные проверки и не продолжают работать со старой комнатой |
| I07 | Два scanner внутри комнаты: один Connector, другой Advanced без Connector | Auto-интеграция только через Connector-способного; advanced не получает чужих модулей |
| I08 | KIAS Fire, FireClear, AtmosDanger/Clear на настоящих AirAlarm/FireAlarm | Реальные Sensors→Alarm→KIAS→physical action/graph, не прямой вызов Trigger |
| I09 | KiasScannerReconciliationTests прежний `Range`-кейc | Переписать на физическое соседство двух комнат; old radius thresholds больше не критерий |
| S01 | Один Mind-player ходит A→порог→B→обратно | `Entities`/`Occupied`, правильно 0→positive `Motion`, отсутствие ложных повторов только от topology rebuild |
| S02 | Два/три Mind-player, один borg, non-Mind hostile fauna, обычный предмет | Существ=разумных tracked по игровому правилу; fauna отдельно Threat; предмет не попадает в Entities |
| S03 | Зарегистрированный и незарегистрированный транспондер, перенос в инвентаре, включённый/выключенный Bio | Корректный Crew per scanner и global dedup; `CrewUnavailable` и RoomLabel; отсутствие cross-room leakage |
| S04 | Игрок упал в critical и умер, другой держит оружие, в комнате аномалия | Только сканеры с подходящим модулем получают PersonCritical/Dead/Threat/Spectral; KIAS-протоколы корректно срабатывают |
| S05 | Hostile creature в дальней нише, затем вне комнаты | FaunaThreat в границах room (независимо от прежнего Radius), без дополнительного сканирования всего мира на каждую камеру |
| S06 | Радиация около sensor и в дальнем конце комнаты | Не выдавать показание дальнего объёма за местное показание `RadiationReceiver`; документировать текущую ограниченность или реализовать настоящий room reading с тестом |
| S07 | Подключение новой карточки к уже стабильному `Entities`/`Crew` | Новая программа получает актуальный snapshot; постоянные значения change-only, сигналы/пульсы не теряются |
| S08 | Синхронная массовая topology волна на 40 гридов, меняются стены/двери | bounded rebuild, нет вечных REBUILD_PENDING, no false positives, очереди восстанавливаются |

Дополнительно v3: Briar (-11,2) сравнивается с (-5,2) без переноса устройств; наружный Advanced получает конечную направленную область с учётом ступенчатого корпуса и occlusion; обычная разгерметизация внутренней комнаты не переключает её автоматически в ExteriorSector. Открытая палуба ограничивается достижимыми клетками грида в радиусе 7 без перехода через дверь. Door tile относится к границам соседних комнат; локальное обнаружение может быть в обеих, глобальный crew/entity счёт не дублируется, владелец управления выбирается стабильно.

Экспедиция проверяется настоящим приземлением и возвратом: корабельные клетки/покрытие сохраняются, местность планеты не входит в корабельную комнату; тест `KiasExpeditionTests.NativeExpeditionLandingKeepsShipCoverageAndExcludesTerrainThenReturns`. Это не повод отключить питание-зависимые проверки или приписать чужой grid кораблю.

## 6. Остальные регрессии и физические программы

- Физическая карточка: insert/remove, programmer READ/WRITE, имя/описание, копирование, preset templates, migration и save/reload. Редактирование без питания разрешено только Mapping-admin на неинициализированной paused/frozen карте; обычная игра, initialized paused карта и исполнение программ остаются power-gated.
- Runtime: порядок событий, input snapshot без aliasing, cause/epoch, fanout ALL/ANY и исключённые targets, cooldown, feedback/anti-loop budgets, backpressure, shutdown и отключение DATA/power. Метрики очередей/задержек не должны скрывать rejected/исключения.
- Устройства: access, anchoring, DATA integration, Direct bindings, новые модули, групповые selectors, конфигурация, обслуживание и relay. Команды проходят актуальные native prerequisites.
- Актуаторы: лампы, динамики/chat, двери, vents/pumps, alarm, engines/autopilot/FTL, оружие/ПКО. Проверяются фактические world actions, а не только Trigger.
- UI/ресурсы: layout/типизация/порты, zoom и выбор порта, стабильность состояний, русский текст, sprites/RSI. Серверный UI-state test не заменяет игровую визуальную приёмку.
- Восемь корпусов: Striks, SPunisher, seeker, Incinerator, dreadnought, Corvet, briar, achilles. Разные реальные корпуса, отдельные powered AUX fixtures; DATA cut/restore и power cut/restore, card OnStart→Recorder и scanner coverage.

Полный список параметризованных регрессий и Python-тестов приведён в приложении. Физические presets: boot, shutdown, hull-damage, crew-critical, crew-dead, fire, fire-clear, atmosphere, atmos-clear, battle-manual, quiet, radiation, local-threat, power-lost, greeting, anomaly, boarding, medical-assistance, vessel-critical, battle-impact, battle-flash, flash-unknown, collision, contact, arrival, proximity, power-deficit. vessel-critical имеет отдельный вариант порога/очистки часов.

## 7. Производительность и физический эксперимент

- Physics: A без KIAS, B активная KIAS, C только paused Runtime в диагностике, D2 тот же B с отключёнными выбранными KIAS collider groups. По 40 Briar, 1800 warmup + 18000 measured, 12 запусков с зеркальными порядками и query counters on/off.
- Сравниваются idle/collision/recovery, entity/fixture инвентарь, активные тела/контакты, query counters, CPU/allocations, разброс и интервалы. Разный инвентарь A/B не позволяет приписывать весь delta коллайдерам.
- Получено B−D2 около +0.2216 мс/тик median idle. Отключение реальных физических устройств не доказано безопасным; production collider и общий движок не изменены.
- Runtime: legacy/cached Regex в одной диагностической сборке, одинаковые native inputs. Whole allocations −45.77%, RuntimeCommands −98.78%; принятые команды/порты совпали, rejected/feedbackRejected/exceptions 0.
- Общий CPU/TPS выигрыш не подтверждён; интервалы включают ноль. Process CPU/tick — сумма времени потоков, не wall duration игрового тика.
- Full/Coarse observer controls оценивают отключаемую часть измерений, но не полную стоимость обязательных наблюдателей и не универсальную поправку A−B.

Отчёты: `.kias-benchmark/v3/performance-research67/RESULTS.md`, `PhysicsInterpretation.md`, `research-results.json`; исходные physics прогоны — `v3/physics-timing66/`. Эти результаты не переименовывать в успешный полный ABBA.

## 8. Уже найденные проблемы и исправления

| Проблема | Причина / исправление | Доказательство и ограничение |
|---|---|---|
| Ночной A не получил 4 клиента | Lidgren max_rapid_connections=3; runner задаёт конечные lab-only лимиты и четырёхклиентный preflight перед серией | `postnight68/NIGHT_FAILURE.md`, A4/B4 network proofs; глобальные игровые CVars не изменены |
| Тест структуры не видел урон | Доза 10 поглощалась штатной бронёй; выбран существующий destructible Wall и доза выше reduction, проверяется фактический урон | A1/B1/A40/B40 PASS; старый failed run сохранён |
| Стыковка B отвергалась после фактического Dock | Валидатор требовал несуществующий State port; проверяется актуальная схема Docked/Undocked + native joint | PASS; production port не придуман |
| На Briar нет docking sensor | Отдельный явно учтённый AUX sensor, нормальные питание/DATA/anchoring | final telemetry содержит auxiliaryDockingSensors; авторский YAML не менялся |
| Перезарядка падала на прототипе | MagazinePistol отсутствует в текущем Monolith; берётся prototype ID фактически извлечённого магазина | Реальное exhaust/empty/reload/fire PASS, первые отказы сохранены |
| Native proof мог принять завершение после окна измерения | Проверялся deadline без общего measured window; добавлены границы, единый engine origin, relative completion clock и строго целые тики | Четыре негативных regression tests; 18 реальных сохранённых прогонов перепроверены |
| Отчёт мог принять пустые final telemetry / queues | Smoke проверял truthiness final, а smoke/full отчёты проверяли только имеющиеся значения очередей; теперь общий recovery gate требует поля флота и все семь очередей | Негативный smoke test; старые реальные отчёты перепроверяются новым кодом |
| После Lighting OFF нельзя было ON | Адресуемость интегрированного света ошибочно зависела от функционального питания самой лампы | `CORRECTION_REPORT.md`, физический OFF→ON gate; KIAS core/DATA по-прежнему обязательны |
| ALL Speaker терял адресатов | Ключ подавления повторов не различал targets | Ключ включает target; реальный chat для выбранных динамиков, исключённый не срабатывает |
| WRITE не обновлял имя карточки | Program менялся без MetaData | WRITE синхронизирует item name/description, draft не переименовывает; startup и round trip регрессии |
| DeviceLink ломал режим мультитула | Двойная возможность DeviceNetwork/DeviceLink считалась однозначной | Явно выбранный режим сохраняется; List/Link и интегрированные приборы проверяются |
| Разгерметизация не запускала граф | Нарушение upstream alarm network setup | Проверка идёт через настоящие sensor/gas/alarm/network/preset и Clear |
| Устаревшие архивные корпуса не грузились | Удалённые прототипы в архивных картах | Использованы актуальные карты верфи; ранние отказы не скрыты; см. HULL_COMPATIBILITY.md |
| Native/metric loop мог зависнуть | Добавлены bootstrap 300 s, tick-stall 120 s и ограниченные HTTP-повторы; скорость игры не заменяется общим wall-time лимитом | metric-scrape и build-progress tests; partial failure archive и nonzero exit |

История других исправлений: [CORRECTION_REPORT.md](CORRECTION_REPORT.md), [MAPPING_EDITOR.md](MAPPING_EDITOR.md), [FAUNA_MONITOR.md](FAUNA_MONITOR.md), `Tools/KiasBenchmark/SOURCE_AUDIT.md`. Для (-11,2) и наружной геометрии обязательны конкретные diagnostics/coverage; не заменять их предположением о неправильно поставленном приборе.

## 9. Чего нельзя считать проверенным

Нет успешного ночного финального ABBA. Не завершены актуальная полная A/B40 матрица, весь финальный Quick, ручная игровая приёмка UI/coverage и полное историческое Runtime/essential-observer сравнение. После очередных правок документации/инструментов общий fingerprint меняется; старая пара не становится свежей от того, что C# не менялся. Перед READY нужны зафиксированные исходники, соответствующие сборки/сценарии/доказательства и успешный строгий CheckOnly.

Отсрочки пяти категорий сняты: радиация, лампы, структура, стыковка и конечный магазин реализованы и коротко проверены на 1 и 40 кораблях. Частичный перехват ПКО допустим. Снятие отсрочек не закрывает остальные условия v3.

## Приложение A. Все случаи последнего полного регрессионного gate

Источник: `.kias-benchmark/rerun/20261012-041737-a960d6b2/tests/gate.trx`. Это исторический исполнявшийся список, включая параметры; после добавления/удаления тестов ориентироваться на новый TRX. Здесь не перечислены вспомогательные методы C# без Test.

- `AdvancedInteriorScannerDoesNotSwitchToExteriorAfterNativeFloorBreach` — Passed.
- `AdvancedScannerDetectsHostilesWithoutNearbyPlayers("MobCarp",False)` — Passed.
- `AdvancedScannerDetectsHostilesWithoutNearbyPlayers("MobCarp",True)` — Passed.
- `AdvancedScannerDetectsHostilesWithoutNearbyPlayers("MobCleanBotSyndie",False)` — Passed.
- `AdvancedScannerDetectsHostilesWithoutNearbyPlayers("MobQuartzCrab",False)` — Passed.
- `AdvancedScannerDetectsHostilesWithoutNearbyPlayers("MobSyndicateFootsoldier",False)` — Passed.
- `AllDefaultPresetsCompileAndLegacyRecordsRequireAnInsertedCard` — Passed.
- `AnyChoosesFirstOnlineAndFailsOverAndAllTogglesEachMatchExactlyOnce` — Passed.
- `ApproachingRecedingAndMissTrajectories` — Passed.
- `AutopilotCompletionIsPerShipPerTripAndCancellationDoesNotArrive` — Passed.
- `BurstSourcesEscalationAndCooldownRemainIndependent` — Passed.
- `CableCutReconnectDuplicateCoreAndShutdown` — Passed.
- `CableMasksFollowNeighborsWithoutAWorkingCore` — Passed.
- `CooldownKeepsIndependentSourceKeysAndBoundsUntrustedStreams` — Passed.
- `CooldownTestModeForeignTargetMaydayAndHardOff` — Passed.
- `DamageAggregationCollisionDebounceAndHardOff` — Passed.
- `DeedCompanyPoiClaimAndNativeWireAccessMatrix` — Passed.
- `DenseRackAndFiftyTwoHundredGridWarmColdTopologyAndOffStress` — Passed.
- `DeviceFeedbackFaultsItsCardWithoutStoppingAnIndependentCard` — Passed.
- `DeviceLinkSuppressionCreatesFoamAndExtinguishesNativeHotspotOnce` — Passed.
- `DiagonalCablesDoNotConnectAndEmptyRebuildClearsCoverage` — Passed.
- `DirtyTopologyTemporarilyChangesAvailabilityWithoutStoppingMachine(False)` — Passed.
- `DirtyTopologyTemporarilyChangesAvailabilityWithoutStoppingMachine(True)` — Passed.
- `DraftLeaseAtomicWriteStaleMessagesRangeAndDirtyEjection` — Passed.
- `DueTimerWaitsForTopologyCommitWithoutLosingMachineOrOutput` — Passed.
- `EightPhysicalSlotsDynamicPowerAndDirtyDraftEjectProtection` — Passed.
- `EnumDomainsInferLegacyValuesAndRejectCrossDomainConnections` — Passed.
- `EveryKiasEntityLoadsNativeSpritesWithoutMissingStates` — Passed.
- `ExplicitShutdownGraphRunsBeforeOffAndQueuedEventsAndTimersAreCancelled` — Passed.
- `ExteriorSectorIncludesOpenLatticeButStopsAtItsHull` — Passed.
- `ExteriorSectorIsFiniteFacesOutwardAndDoesNotSeeThroughHull(-1,0)` — Passed.
- `ExteriorSectorIsFiniteFacesOutwardAndDoesNotSeeThroughHull(0,-1)` — Passed.
- `ExteriorSectorIsFiniteFacesOutwardAndDoesNotSeeThroughHull(0,1)` — Passed.
- `ExteriorSectorIsFiniteFacesOutwardAndDoesNotSeeThroughHull(1,0)` — Passed.
- `ExteriorSectorRejectsInteriorSeedsAndUnboundedReach` — Passed.
- `FeedbackBudgetFaultsLocallyAndDraftCopyIsIndependent` — Passed.
- `FleetSchedulingAndEventStress` — Passed.
- `FleetTopologyStressHarness` — Passed.
- `FrozenMappingAllowsUnpoweredEditingButInitializedPausedMapsDoNot` — Passed.
- `G01_AuthoritativeRotation(0,0,-1)` — Passed.
- `G01_AuthoritativeRotation(180,0,1)` — Passed.
- `G01_AuthoritativeRotation(270,-1,0)` — Passed.
- `G01_AuthoritativeRotation(90,1,0)` — Passed.
- `G01_G09_G12_WallOrientationAndBoundedBreach(0,2,4)` — Passed.
- `G01_G09_G12_WallOrientationAndBoundedBreach(180,2,0)` — Passed.
- `G01_G09_G12_WallOrientationAndBoundedBreach(270,4,2)` — Passed.
- `G01_G09_G12_WallOrientationAndBoundedBreach(90,0,2)` — Passed.
- `G02_G03_G04_G05_G08_OpenDoorsRemainSharedTerminals("Airlock")` — Passed.
- `G02_G03_G04_G05_G08_OpenDoorsRemainSharedTerminals("Firelock")` — Passed.
- `G06_LongBentInteriorHasNoRadiusCap` — Passed.
- `G07_CardinalEdgesBlockBothDirectionsAndCorners` — Passed.
- `G15_OverLimitDoesNotExposePartialCoverage` — Passed.
- `GraphSelectorsSpeakFromEverySelectedSpeakerAndKeepCooldown(False,"graph-speaker-broadcast")` — Passed.
- `GraphSelectorsSpeakFromEverySelectedSpeakerAndKeepCooldown(False,"Внимание! В отсеке № 3 опасная фауна. Ёж, щит, подъём.")` — Passed.
- `GraphSelectorsSpeakFromEverySelectedSpeakerAndKeepCooldown(True,"graph-speaker-broadcast")` — Passed.
- `GraphSelectorsSpeakFromEverySelectedSpeakerAndKeepCooldown(True,"Внимание! В отсеке № 3 опасная фауна. Ёж, щит, подъём.")` — Passed.
- `I01_I05_I06_I09_PhysicalRoomsHandOffAndPreserveDirectBindings` — Passed.
- `IdentitySurvivesRebuildAndRotaryRejectsInvalidOrUnauthorizedSettings` — Passed.
- `IfFanOutConstantsAndPulseStreams` — Passed.
- `ImportReportsUnsupportedPoliciesInsteadOfSilentlyChangingThem` — Passed.
- `InspectorCapabilitiesSummariesAndLocalWindowsStayReadable` — Passed.
- `InterceptAndSweptCollision` — Passed.
- `LeavingAPortInsideTheCanvasImmediatelyHidesItsNativeTooltip` — Passed.
- `LightGroupsCartridgeConsumptionAndHardOff` — Passed.
- `LightingProfilesRestoreIntegratedFixtures(False)` — Passed.
- `LightingProfilesRestoreIntegratedFixtures(True)` — Passed.
- `ListModeStillCollectsAtmosDevicesWithNativeOrIntegratedPorts(False)` — Passed.
- `ListModeStillCollectsAtmosDevicesWithNativeOrIntegratedPorts(True)` — Passed.
- `LogicTruthTablesAndComparisonTypes` — Passed.
- `MedicalAssistanceRequiresDistressAndUnavailableRegisteredCrew` — Passed.
- `NamedAndNumberedRoomsRemainSeparatedByDoorsAndWalls` — Passed.
- `NativeClientChangesProgrammerSpriteFromAppearance` — Passed.
- `NativeExpeditionLandingKeepsShipCoverageAndExcludesTerrainThenReturns` — Passed.
- `NativeFireAlarmRunsSuppressionGraphAndManualVerbReloads` — Passed.
- `NativeGridSplitSeparatesDeviceRegistries` — Passed.
- `NativeGunneryFiresAndReservationLocksManualCommand` — Passed.
- `NativeLightAndJammerAppearanceKeepTheirFunctionalStates` — Passed.
- `NativeProjectileHullHitAndShieldAbsorptionStayDistinct` — Passed.
- `NativeSensorDeviceListDrivesAtmospherePresetAndRecovery` — Passed.
- `NativeSourceWithoutPhysicalLinksReachesPortableGraph` — Passed.
- `NativeWindowsArrangeWithEmptyAndPopulatedGraphs` — Passed.
- `OpenAreaFallbackUsesReachableGridTilesWithinSevenAndStopsAtDoors` — Passed.
- `OpenGridFallbackExcludesDistantPeopleAndWallDevices` — Passed.
- `OrdinaryLinkModeStillLinksAnAirAlarmToALight` — Passed.
- `OwnerConfigurationRejectsRetiredProtocolEditsAndClientWindowBuilds` — Passed.
- `PhysicalIffDockingKeySwitchAdapterAndWirelessWorkWithNativeDevices` — Passed.
- `PlayerBodyRemainsTrackedAfterDeathAndMindTransfer` — Passed.
- `PortableSelectorsSpecificIsolationAndPhysicalLifecycle` — Passed.
- `ProgrammerCardAppearanceFollowsPhysicalSlotAndRejectedEjection` — Passed.
- `QueueAgeIncludesDelayedTailAndKeepsExactOverflowMaximum` — Passed.
- `QueuedMessagesKeepTheirOwnSnapshotsAcrossDirtyTopology` — Passed.
- `RackUpdatesKeepTheHoveredButtonsAndUseCurrentSlotState` — Passed.
- `RealBriarDevicesRespondAndRecover("ame-adapter")` — Passed.
- `RealBriarDevicesRespondAndRecover("docking")` — Passed.
- `RealBriarDevicesRespondAndRecover("pdc")` — Passed.
- `RealBriarDevicesRespondAndRecover("power-data")` — Passed.
- `RealBriarDevicesRespondAndRecover("programmer")` — Passed.
- `RealBriarDevicesRespondAndRecover("programmer-ui")` — Passed.
- `RealBriarDevicesRespondAndRecover("racks")` — Passed.
- `RealBriarDevicesRespondAndRecover("scanners")` — Passed.
- `RealBriarDevicesRespondAndRecover("speakers")` — Passed.
- `RealBriarDevicesRespondAndRecover("suppression")` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("anomaly","AnomalyGrowth",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("arrival","Arrival",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("atmos-clear","AtmosClear",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("atmosphere","AtmosDanger",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("battle-flash","WeaponFlash",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("battle-impact","HullImpact",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("battle-manual","Manual",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("boarding","Boarding",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("boot","",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("collision","Collision",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("contact","Contact",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("crew-critical","CrewCritical",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("crew-dead","CrewDead",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("fire","Fire",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("fire-clear","FireClear",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("flash-unknown","WeaponFlash",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("greeting","CaptainGreeting",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("hull-damage","HullDamage",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("local-threat","LocalThreat",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("medical-assistance","CrewUnavailable",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("power-deficit","PowerDeficit",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("power-lost","PowerLost",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("proximity","Proximity",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("quiet","QuietMode",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("radiation","Radiation",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("shutdown","Shutdown",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("vessel-critical","VesselCritical",False)` — Passed.
- `RealBriarStimulusReachesPhysicalCardAndWorldOutput("vessel-critical","VesselCritical",True)` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("Corvet")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("Incinerator")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("SPunisher")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("Striks")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("achilles")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("briar")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("dreadnought")` — Passed.
- `RealHullPowersPhysicalGraphAndRecoversData("seeker")` — Passed.
- `ReinsertedCardDoesNotConsumeAnEventFromBeforeItsColdBoot` — Passed.
- `RelayTokenProtocolAndRecorderSurviveMapRoundTrip` — Passed.
- `RepeatedAnnouncementsKeepTextBoundariesAndFollowCultureChanges` — Passed.
- `RestoreRequiresSettledSealedSafeAirVolume` — Passed.
- `RussianEntitiesAndEveryGraphPortHaveLocalizedNamesAndHelp` — Passed.
- `S08_G13_FortyGridSameTickWaveCommitsWithoutStaleCoverage` — Passed.
- `SavedBriarDoorBoundariesAndScannerCellsSurviveFarRotatedGridRebuilds` — Passed.
- `SavedBriarLoadsAndScannerModulesSurviveRoundTrip` — Passed.
- `SavedBriarReceivesRealBatteryPowerAndResolvesGraphTargets` — Passed.
- `SavedBriarRoomGeometryExportsAllScannerPlacements` — Passed.
- `SelectedChannelReallySplitsAndReconnectsNetworks` — Passed.
- `SeparateSourcesDoNotHideCriticalProtocolsAndFireLockHasHardOffFallback` — Passed.
- `ServiceGroupsUseSeparateFieldsAndRefreshRunningSelectors` — Passed.
- `ServiceRadiusAndCuts` — Passed.
- `StarterModulesRoomBridgeKitAndNativeJammerHaveGameplayEffects` — Passed.
- `StateNodesAreIndependentAndReset` — Passed.
- `StringLatchStoresBeforePulseAndResetsOnColdBoot` — Passed.
- `SubscriberBurstDrainsWithoutFaultsOrLostPulses(False)` — Passed.
- `SubscriberBurstDrainsWithoutFaultsOrLostPulses(True)` — Passed.
- `TallCoreSharesDepthWithDirectionalWindowAndHuman` — Passed.
- `TestExpiresAndMovementPowerLossInvalidateRuntime` — Passed.
- `TimerCancelClockStopAndColdBoot` — Passed.
- `ToolAndVerbReportSelectedChannelAndRespectAccess` — Passed.
- `ToolOpensOutputsAndRefreshesValuesWithoutWritingOrShowingOfflineData` — Passed.
- `UnanchoredAtmosDeviceNearConnectorDoesNotKeepGridDirty` — Passed.
- `ValidatorRejectsMalformedGraphsAndAllowsStateFeedback` — Passed.
- `WarningAudioAndLocalSpeechComeFromAddressedSpeaker` — Passed.

## Приложение B. Все текущие тесты инструментов

Имена методов ниже описывают проверяемые ошибки; файлы расположены в `Tools/KiasBenchmark/`. Основные классы доказательств: scenario/seed, native dispatch/completion, A/B parity, network sandbox/bytes/serialization, frame samples, availability/recovery, physics inventory/variance, hull proofs, freshness/readiness, partial ZIP, build progress и live UI evidence. PowerShell tests перечислены отдельно.

### test_availability.py

- `test_dirty_topology_must_recover_within_two_seconds`
- `test_stopped_machine_is_rejected`
- `test_fault_is_rejected`
- `test_unchecked_drop_is_rejected`
- `test_persistent_or_non_topology_drop_is_rejected`

### test_compare_native.py

- `test_complete_native_trace_is_accepted`
- `test_repeated_driver_cannot_replace_missing_driver`
- `test_missing_tick_is_rejected`
- `test_late_input_is_rejected`
- `test_unbound_ship_is_rejected`
- `test_failed_world_effect_is_rejected`
- `test_server_load_error_invalidates_completed_trace`
- `test_whole_tick_shorter_than_content_is_rejected`
- `test_declared_unimplemented_driver_is_rejected`
- `test_anomaly_requires_flesh_and_component_audit`

### test_expanded_scenario.py

- `test_seed_is_reproducible_and_changes_ship_choices`
- `test_every_driver_has_a_bounded_cleanup_pair`
- `test_anomalies_explicitly_use_short_lived_flesh`
- `test_too_short_workload_is_rejected`

### test_fullgame_report.py

- `test_background_is_seeded_and_does_not_overlap_composite_restores`
- `test_reopened_requirements_have_native_smoke_and_full_fleet_waves`
- `test_required_events_preserve_collision_and_geometry`
- `test_combined_systems_retains_every_event`
- `test_wave_rejects_split_engine_ticks`
- `test_wave_rejects_missing_ship`
- `test_tick_evidence_rejects_nan_and_missing_samples`
- `test_scaling_recovery_rejects_lost_cards_faults_and_pending_work`
- `test_smoke_cannot_pass_with_empty_or_incomplete_final_telemetry`

### test_headless_client.py

- `test_preserves_windowed_preload_and_is_idempotent`
- `test_unknown_source_fails`

### test_lab.py

- `test_prng_known_vector`
- `test_schedule_is_repeatable_and_changes_with_seed`
- `test_recovery_covers_every_stimulus_and_ends_before_window`
- `test_no_simultaneous_danger_on_same_ship`
- `test_invalid_parameters_are_rejected`
- `test_real_map_inventory_retains_robust_tags`
- `test_vanilla_map_removes_nested_items_without_orphan_parents`

### test_live_ui_evidence.py

- `test_complete_current_evidence_is_accepted`
- `test_changed_source_is_rejected`
- `test_changed_capture_requires_new_review`
- `test_missing_native_ejection_step_is_rejected`

### test_native_proof.py

- `test_valid_native_completion`
- `test_validator_matches_native_tick_then_id_scheduling`
- `test_reordered_native_dispatch_is_still_rejected`
- `test_missing_completion_rejected`
- `test_late_dispatch_rejected`
- `test_null_proof_rejected`
- `test_completion_after_deadline_rejected`
- `test_server_error_rejected`
- `test_completion_outside_measurement_is_rejected_even_within_deadline`
- `test_completion_clock_cannot_be_forged`
- `test_boolean_and_fractional_ticks_are_rejected`
- `test_events_at_different_ticks_must_share_engine_origin`

### test_network_proof.py

- `test_native_serialization_label`
- `test_missing_counter_rejected`
- `test_nan_rejected`
- `test_duplicate_counter_rejected`
- `test_complete_functional_network_keeps_performance_limit`
- `test_incomplete_failed_or_unsafe_network_is_rejected`
- `test_server_errors_invalidate_otherwise_positive_network_metrics`

### test_partial_archive.py

- `test_failed_run_keeps_evidence_and_verifiable_hashes`

### test_physics_experiment.py

- `test_upgrade_old_instrumentation_and_repeat`
- `test_plan_counterbalances_and_keeps_native_synchronous_waves`
- `test_completed_probe_preserves_open_overhead_and_performance_limit`
- `test_missing_counter_toggle_is_rejected`
- `test_disabled_counter_must_really_stop_counting`
- `test_recovery_not_included_in_idle_after`
- `test_inventory_mismatch_is_not_a_causal_comparison`
- `test_corrupt_tick_evidence_is_rejected_before_phase_statistics`
- `test_unpaused_runtime_c_is_rejected`

### test_readiness.py

- `test_unproven_pass_is_not_ready`
- `test_changed_proof_fails`
- `test_stale_source_invalidates_evidence`

### test_validate_hulls.py

- `test_accepts_observed_native_recovery`
- `test_rejects_bypassed_power_and_missing_recovery`
- `test_rejects_overlapping_machines_and_ineffective_disruption`
- `test_rejects_empty_or_misbound_scanner_geometry`

PowerShell: `test_metric_scrape.ps1` — bounded retry/partial HTTP response; `test_build_progress.ps1` — настоящая успешная сборка и ожидаемый отказ, exit code и progress.

## Объединённая проверка full-preflight70

Первый объединённый A40 с четырьмя клиентами остановился на tick 31800: общий crew actor не смог поднять извлечённую кассету ПКО (ship-0006). Все 40 FTL входов, выходов и navigation arrivals до этого подтверждены. 1926 доставок записаны до исключения; весь A — FAILED_INCOMPLETE, B не запускался. Точное медицинское состояние actor старый exception не записал, поэтому конкретная причина травмы не утверждается.

Драйвер ПКО отделён от травмируемого crew: настоящий AUX operator, обычные hands/slots, исходная кассета, явная очистка actor/grid после reload. В стандартный короткий gate добавлена pdc_operator: основной crew остаётся Critical до завершения кассетного цикла. После исправления нужны A40/B40 этой регрессии и новый объединённый A/B40; старый отказ не считается PASS. Доказательства и исходник до фикса: `.kias-benchmark/v3/full-preflight70/`.


### Продолжение: изоляция оператора и запусков

Регрессия `pdc_operator` A40/B40 прошла: по 200/200 доставок, 160/160 асинхронных завершений. Основной экипаж оставался Critical (native enum 2) на момент извлечения кассеты; независимые AUX actor/grid очищены после reload. В B faults = 0, конечные очереди пусты. Доказательства: `.kias-benchmark/v3/full-preflight70/operator-regression-r2/`. Это устраняет воспроизведённую зависимость кассетного драйвера от состояния crew, но не заменяет повтор объединённого A/B40.

Операторский BAT `20261012-124838-38c60995` завершился FAILED_INCOMPLETE: исходники изменились после сборки, длинная серия не началась. Причина пересечения подготовки — отдельные native-запуски не участвовали в блокировке rerun. Добавлена общая эксклюзивная файловая блокировка для rerun, build_pair, native_suite, native_replay, load_series и physics_experiment; вложенные этапы одного процесса используют счётчик владения. Блокировка удерживается между прогонами и освобождается в finally. Проверяются TCP 12299/44880 и UDP 12299. Реальные проверки отдельного процесса, вложенного владения, освобождения после исключения и занятых UDP/TCP портов прошли (`benchmark-lock-tests.log`); Python 77/77 PASS (`tools-r3.log`).


### Объединённый сценарий: столкновение после предыдущих фаз

`full-preflight70/combined-r2/A` прошёл прежний отказ ПКО: по 40 native empty/reload, 40 FTL входов/выходов и 40 navigation arrivals. Четыре настоящих клиента оставались InGame. Затем FAILED_INCOMPLETE на collision.damage_threshold: 2200 доставок, только 4 из 40 ударов подтвердили урон; ship-0040 истёк по deadline. В native-timeout.json сохранены phase 2, контакт 39.767 м/с и состояние тарана. Полный A не PASS, B не начат.

Обнаружена ошибка координат самого драйвера: площадка удара — в локальных координатах корпуса, смещение и скорость тарана были по мировой X. Исправлены согласованные поворот, размещение и скорость, исходная скорость корпуса обнуляется перед контролируемым воздействием. Production PhysicsSystem и авторская карта не менялись. Это корректирует постановку эксперимента; единственная причина исходного таймаута по старому логу не доказана. Добавлена обязательная collision_rotated на 40 разных углах: A40/B40 PASS, по 80/80 воздействий и завершений (40 low/high damage cycles + 40 restore), B faults 0, очереди пусты. Доказательства: `full-preflight70/collision-rotated-r1/`. Объединённый A/B40 после этого исправления всё ещё требуется.


### Текущий блокер после уточнения диагностики

Объединённый `combined-r3/A` снова FAILED_INCOMPLETE: на collision.damage_threshold подтверждены 28/40, 12 остались до deadline; native-timeout — ship-0021, угол fixture 0.0283139°. При этом предыдущие 40 PDC reload, 40 FTL входов/выходов и 40 navigation arrivals прошли. Всё это частичные результаты, а не PASS всего прогона. B не начинался.

Эксперимент со сдвигом тарана на -0.5 по поперечной оси не помог: `collision-rotated-r2` FAIL A/B. Сильный контакт действительно наблюдался: normalVelocity 39.767 м/с, две точки контакта. Сдвиг отменён; версия с ним сохранена в frozen-source данного запуска. Последний observer хранит до 16 контактов на грид за один тик, а не только последний контакт. После отмены сдвига A40 прошёл малые/большие углы: `collision-contact-trace`, 80/80 доставок и завершений. Это не устраняет отказ после предшествующих фаз; точная причина пока не доказана. Production обработчик столкновений и RobustToolbox не менялись.

Приоритет продолжения: минимизировать последовательность, после которой удар теряет native damage (в первую очередь navigation → collision и сохранённые transforms/contacts); получить короткое воспроизведение с новой последовательностью контактов, исправить доказанную причину и проверить A40/B40; затем повторить объединённый сценарий. До этого FULL GAME NOT_READY и полный ABBA не запускать. Остальные требования строгого preflight остаются в разделе 3.

## Устранение последних блокеров: ready71

Этот раздел уточняет исторические NOT_READY/невыясненные причины выше; окончательное состояние определяется новым строгим CheckOnly, а не редактированием этого документа.

- Минимизация navigation → collision воспроизвела потерю урона. За один tick у ship-0021 сначала пришёл касательный контакт normalVelocity 1.8626451e-09, затем сильный 39.76725 м/с. `ShuttleSystem.Impact` раньше отмечал пару обработанной до проверки порогов. Теперь тяжёлая обработка резервирует пару только после принятия удара; KIAS contact signal отдельно ограничен одной парой за tick. RobustToolbox и авторская карта неизменны. `ready71/COLLISION_ROOT_CAUSE.json`; исходные контакты и неудачный результат сохранены. Исправленная регрессия `ready71/navigation-collision-fixed-r1`: A40/B40 PASS, по 120/120 доставок и 120/120 native-завершений. Это адресный production bugfix, а не основание объявлять ускорение всего PhysicsSystem.
- Стандартный native gate теперь содержит 11 групп, включая navigation_collision (навигация, затем пороговый удар и очистка; 15000 ticks). PDC operator и collision_rotated остаются обязательными.
- Реальная сетевая UI-приёмка `scanner-ui/20261012-165212`: четыре сканера, серверный ACK и клиентский ACK, точное совпадение всех клеток/границ и actual BUI geometry; четыре PNG проверены визуально агентом. Это настоящий клиент/OpenGL, не нарисованная схема и не заявление о ручной проверке владельцем. (-11,2): Ok/9/4; (-5,2): Ok/13/4; наружные: ExteriorSector/21/0 каждый. Радиус read-only. Диагностические fullbright и actor godmode явно указаны в manifest, в production отсутствуют.
- При UI-проверке обнаружены и исправлены испорченные UTF-8 русские строки rooms.ftl. Машинная проверка теперь отклоняет mojibake даже при положительном флаге клиента. Пять негативных/положительных тестов UI evidence прошли.
- Неудачные UI попытки сохранены: ранний доступ к ECS до подключения, гонка записи server ACK, кодировка строки самого диагностического assertion. Финальный runner ждёт оба завершения, восстанавливает обычные сервер/клиент сборки и удерживает общую benchmark lease.
- Пять прежних отсрочек снова обязательны: радиация, свет, структура damage/repair, dock/undock, finite magazine. Частичный ПКО-перехват допустим. Финальная сертификация не должна содержать DEFERRED_REQUIRED для этих категорий.

До окончательного PASS нужны замороженные свежие сборки, общий Debug/Release gate, фактический Quick, объединённый A/B40, Runtime-сравнение и строгая матрица 16 требований. Итоговые машинные результаты сохраняются в ignored `.kias-benchmark/`; изменение этого файла после сборки инвалидирует fingerprint.

## Решение владельца о дополнительных замерах 2026-10-12

Владелец явно выбрал: «Продолжай функциональные проверки, замеры позже», поскольку запущен Corvax Frontier. Отложен повтор расширенного исторического/текущего Runtime CPU/TPS на свободном компьютере. Это не PASS чистого CPU-исследования. Сравнение функциональности и очередей продолжается, CPU-цифры при фоновой игре описательны и не доказывают ускорение. Ранее завершённые изолированные Physics и конкретное Regex-сравнение сохраняют свои исходные границы доказательств. Новый CPU-повтор нужен отдельно; длинный пользовательский ABBA не сравнивает исторический Runtime с текущим.

Пять игровых категорий (радиация, свет, повреждения/ремонт, стыковка и конечный магазин) обязательны и не отложены. Для запуска полного ABBA нужны свежие сборки, Debug/Release gate, Quick, полный функциональный A/B40 и строгая проверка доказательств. Изолированный дополнительный замер Runtime не должен называться пройденным и не должен мешать подготовке согласованного функционального запуска.

## Повтор Quick после ошибки Discord RPC

Первый свежий Quick `20261012-172156-72386148`: четыре Release-сборки без ошибок, 165/165 регрессий, 27 программ/28 физических случаев, 8 корпусов, все 11 isolated native-групп PASS. Последняя сетевая фаза FAILED_INCOMPLETE: все четыре клиента InGame, но Discord RPC записал ошибки handshake/broken pipe. Это не PASS всего Quick. Архив сохранён. Лабораторные клиенты теперь запускаются с `discord.enabled=false`, это явно записано в manifest; production и обычная игра не меняются. Валидатор продолжает отвергать любые клиентские ERRO/FATL, включая Discord; добавлен негативный тест.

По указанию владельца объединять совместимые проверки повторный Quick использует общий полный сценарий B на малом флоте (45000 measured ticks), содержащий все категории, составные волны и navigation → collision. Отдельно сохраняются focused pdc_operator и collision_rotated, затем четыре настоящих сетевых клиента. Это не финальный ABBA. Стандартный native_suite по-прежнему предоставляет все 11 изолированных групп для локализации; длительный пользовательский запуск сохраняет полноценный preflight A/B. Старые прогоны не переименовываются в успешные.


## Сетевая регрессия переноса экипажа — 2026-10-12

Quick `20261012-182320-f7e417f7` PASS: четыре свежие Release-сборки, 85 Python-проверок после последующего расширения (в самом этом Quick было 84), Release 165/165, 27 программ/28 физических сценариев, 8 корпусов, совмещённый B1 86 доставок/77 native завершений, отдельные ПКО и повёрнутое столкновение, четыре настоящих клиента без ошибок Discord. Debug этого source-state: 165/165, без пропусков. Эти результаты исторические после следующей правки драйвера; итог требует свежей пары.

Общий функциональный A40/B40 `ready71/combined-r4` FAILED_INCOMPLETE на A: потерян один из четырёх клиентов. Клиентские исключения broadphase появились при удалении вспомогательных гридов. Изолированный FTL visitor in/out A40 с четырьмя клиентами PASS 80/80; FTL сам по себе не воспроизвёл сбой. Повтор префикса общего сценария остановился уже на crew.return. Минимальные crew.depart/crew.return на A40 с четырьмя клиентами воспроизвели клиентские decal/broadphase исключения при удалении площадки одновременно с переносом персонажа. Это дефект жизненного цикла тестовой площадки; исправление движка не заявляется.

Драйвер сохраняет отдельную 3×3 площадку каждого корабля для повторных настоящих переходов экипажа. Площадка не удаляется в сетевом состоянии переноса. Активные поездки по-прежнему очищаются, повторный выход использует ту же площадку; авторская карта не меняется. Площадки одинаковы в A/B и учитываются фактическим физическим инвентарём. Валидатор требует nativeGridTransfer, transferPlatformRetained и идентификатор площадки. Негативный Python-тест отклоняет удалённую площадку; всего Python 85/85.

Native launcher теперь читает ERRO/FATL из stdout и stderr всех клиентов и сервера на каждом шаге наблюдения, сохраняет путь ошибки, завершает только свои процессы. Потоковое чтение сохраняет конец предыдущей порции и ловит разорванный маркер ошибки. Отдельный PowerShell-тест проверяет INFO, разорванный клиентский ERRO и клиентский stderr FATL; он включён в корневой BAT. Минимальное воспроизведение подтвердило реальную немедленную остановку, а не ожидание конца сценария.

Сетевая часть Quick больше не простаивает: четыре клиента проходят два последовательных crew.depart/crew.return с native- и network-проверками, 1200 measured ticks после прежнего прогрева 10800. Для финальной готовности нужны повтор переноса A40/B40, новый Quick, Debug/Release gate, полный функциональный A40/B40 и строгий CheckOnly. Успех исправления заранее не объявляется. Длительный финальный ABBA не запущен.


## Ночной диагностический запуск — актуальное решение 2026-10-12

Владелец поручил готовить ABBA без оставшихся предварительных проверок. Двойной щелчок `Run_KIAS_Tests.bat` теперь выбирает `-Diagnostic` и ждёт START; свежие сборки и проверки защит сохранены, дополнительные preflight отложены. Все игровые воздействия остаются в ночном сценарии. Это не строгий READY v3; `-CheckOnly` остаётся строгим.

Открыты: клиентская ошибка дублирующего docking joint в общем A/B40, завершение общего A/B40 и дополнительного масштабирования, итоговая строгая проверка доказательств; отдельно ранее отложен чистый CPU Runtime. Уже пройденные игровые проверки сохраняют свои результаты. Известная клиентская ошибка joint записывается и позволяет собрать серверные данные до конца, делая прогон INVALID; остальные ошибки останавливают прогон, сохраняют его доказательства и позволяют следующую независимую попытку. Нарушение сопоставимости, занятые порты или отказ сохранения останавливают серию. Подробная политика, пределы, статусы и инструкция: `Docs/KIAS/NIGHT_DIAGNOSTIC_RUN.md`.
