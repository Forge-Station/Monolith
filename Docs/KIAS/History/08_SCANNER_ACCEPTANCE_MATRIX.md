# 08 — Обязательная матрица приёмки RoomScanner v2

Это НЕ пожелания. Агент реализует deterministic integration tests в `Content.IntegrationTests/Tests/_Forge/KIAS/`, дополнительные pure/unit tests геометрии, проверку настоящего `briarKIAS.yml` и сохраняет TRX + геометрические артефакты. Действия применять **на сервере через реальные системы**; KIAS trigger не вызывать вместо игрового события.

## A. Геометрия: ожидаемая матрица

| ID | Fixture и действие | Проверяемая постусловия |
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

## B. Устройства и графы — устранить рассинхрон радиусов

| ID | Сценарий | Обязательный результат |
|---|---|---|
| I01 | Basic с Connector, AirAlarm на дальнем конце одной длинной комнаты (>10 tiles) | Auto integration succeeds; `CanControl=true` при корректных питании, DATA, access |
| I02 | AirAlarm в соседней комнате через ОТКРЫТУЮ дверь | Не интегрируется сканером из A и не управляется из A через его Connector |
| I03 | Дверь на shared boundary с двумя подходящими сканерами | Stable deterministic scanner owner, без частого переключения Open/Close; binding не даёт доступа на другой grid |
| I04 | Стеновой speaker, vent, air alarm, FireAlarm, door, optional device adapter | Общая методика wall-boundary association; устройства корректно привязаны, нативные действия действительно исполняются |
| I05 | Device integrated `Direct=true` через upgrade kit | Сохраняет Direct при пересчёте комнат, отключении scanner и перезагрузке |
| I06 | Автоматическая привязка после удаления стены, отключения scanner/Connector, разрыва DATA | Освобождение/смена target owner; любые команды проходят актуальные проверки и не продолжают работать со старой комнатой |
| I07 | Два scanner внутри комнаты: один Connector, другой Advanced без Connector | Auto-интеграция только через Connector-способного; advanced не получает чужих модулей |
| I08 | KIAS Fire, FireClear, AtmosDanger/Clear на настоящих AirAlarm/FireAlarm | Реальные Sensors→Alarm→KIAS→physical action/graph, не прямой вызов Trigger |
| I09 | KiasScannerReconciliationTests прежний `Range`-кейc | Переписать на физическое соседство двух комнат; old radius thresholds больше не критерий |

## C. Сущности, модули и события

| ID | Сценарий | Проверить |
|---|---|---|
| S01 | Один Mind-player ходит A→порог→B→обратно | `Entities`/`Occupied`, правильно 0→positive `Motion`, отсутствие ложных повторов только от topology rebuild |
| S02 | Два/три Mind-player, один borg, non-Mind hostile fauna, обычный предмет | Существ=разумных tracked по игровому правилу; fauna отдельно Threat; предмет не попадает в Entities |
| S03 | Зарегистрированный и незарегистрированный транспондер, перенос в инвентаре, включённый/выключенный Bio | Корректный Crew per scanner и global dedup; `CrewUnavailable` и RoomLabel; отсутствие cross-room leakage |
| S04 | Игрок упал в critical и умер, другой держит оружие, в комнате аномалия | Только сканеры с подходящим модулем получают PersonCritical/Dead/Threat/Spectral; KIAS-протоколы корректно срабатывают |
| S05 | Hostile creature в дальней нише, затем вне комнаты | FaunaThreat в границах room (независимо от прежнего Radius), без дополнительного сканирования всего мира на каждую камеру |
| S06 | Радиация около sensor и в дальнем конце комнаты | Не выдавать показание дальнего объёма за местное показание `RadiationReceiver`; документировать текущую ограниченность или реализовать настоящий room reading с тестом |
| S07 | Подключение новой карточки к уже стабильному `Entities`/`Crew` | Новая программа получает актуальный snapshot; постоянные значения change-only, сигналы/пульсы не теряются |
| S08 | Синхронная массовая topology волна на 40 гридов, меняются стены/двери | bounded rebuild, нет вечных REBUILD_PENDING, no false positives, очереди восстанавливаются |

## D. Авторский Briar и совместимость

- Загрузить `Resources/Maps/_Forge/Shuttles/Archive/Mercenary/briarKIAS.yml`; автоматически инвентаризировать все сканеры, стены, гермозатворы, FireAlarm/AirAlarm и ориентации. Из предыдущего инвентаря ориентир **13 Basic + 8 Advanced**, перепроверить при checkout, не принимать как жёсткую цифру после патчей.
- Выгрузить `briar-room-map.json` и PNG/превью coverage для **каждого** из 21 сканера, с цветом для interior/boundary/status и подтверждением корректного seed. Пометить реальные комнаты без scanner и scanner без комнаты; **не подделывать покрытие**.
- Проверить automatic bindings на дальних end-to-end устройствах, 27 controller graph tests, 4 racks, сохранение/перезагрузку и фактические actions. Сравнить с original `briarKIAS.yml`; в оригинал не писать заранее вычисленные cells/rooms и не исправлять ориентацию молча. Необходимые пользовательские перестановки — в `BriarPlacementRecommendations.md`, по coordinates/UID.
- Оставить `Range` legacy field read-only/ignored в новых world saves и проверить чтение старого serialized YAML. Не менять 6 module slots и их default prototypes. До любых изменений авторского YAML показать diff.

## E. Доказательства приёмки

Каждый тест выводит `expectedRoomIds`, `actualRoomIds`, expected/actual tile counts, door boundary cells, affected scanner/target UIDs, graph outputs, clocks/revisions, geometry+device performance counters, PASS/FAIL. TRX обязателен, JSON не может отменять FAIL в TRX. Снимок UI без проверки корректности — не PASS.

Для каждой ошибки: `FAIL` с причинами и минимальной воспроизводимой картой. Если выявляется физическое ограничение конкретного прототипа стены/двери, агент **добавляет конкретный адаптер и тест**, а не помечает всё автоматически PASS.
