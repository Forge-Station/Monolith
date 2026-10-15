# 02 — briarKIAS.yml: аудит и безопасная подготовка модулей

## Входной файл

`Resources/Maps/_Forge/Shuttles/Archive/Mercenary/briarKIAS.yml`.

**Текстовый inventory проверен на commit `38c67ac...`**, обновить если HEAD изменился:

| Объект | Количество |
|---|---:|
| EntityCount (meta) | 2391 |
| KiasRoomScanner | 13 |
| KiasAdvancedRoomScanner | 8 |
| KiasSuppression | 13 |
| KiasSpeaker | 10 |
| KiasWeaponFlashDetector | 6 |
| KiasControllerRack | 4 |
| KiasProgrammableController | 26 |
| KiasDataCable | 200 |
| KiasCore | 1 |
| KiasPdcRadar | 1 |
| KiasDeviceAdapter | 1 |
| AirAlarm | 8 |
| AirSensor | 11 |
| KiasLightController | 0 по proto |
| KiasControllerProgrammer | 0 по proto |
| KiasIntegrated components в YAML | 83 |

Установленные карты с метаданными:
`battle-impact, battle-flash, battle-manual, flash-unknown, hull-damage, collision, atmosphere, anomaly, contact, arrival, proximity, crew-critical, crew-dead, vessel-critical, power-deficit, fire, radiation, local-threat, boarding, greeting, atmos-clear, medical-assistance, quiet, power-lost, boot, shutdown`.

Среди 27 каталоговых шаблонов отсутствует физическая карточка `fire-clear`. Нельзя тихо «доустановить» её на постоянный корабль: оформить блокер, а для независимого теста шаблона создать временную карточку вне исходной копии.

## Модули сканеров — реальные ограничения

### Обычный `KiasRoomScanner`

`kias.yml`: 6 slots, whitelist `KiasBasicModule`.
- Default `startingItem` slot 1: `KiasMotionModule` (Motion)
- slot 2: `KiasIdentityModule` (Identity)
- slot 3: пустой в прототипе
- slot 4: пустой в прототипе
- slot 5: `KiasOpticalModule` (Optical)
- slot 6: `KiasConnectorModule` (Connector)

Другие допустимые базовые по tag: `KiasIdModule` (Identity), `KiasTransponderModule` (Identity). **Внимание:** это не новые категории сенсорики — те же `module: Identity`. Не выдавать три Identity за три новых вида обнаружения. Если пользовательское «все доступные» трактуется буквально по prototype IDs, можно заполнить оставшиеся 2 слота этими допустимыми экземплярами; зафиксируй, что для семантического покрытия они избыточны. Если в реальном `KiasCrewSystem` дубликаты создают ошибки — не добавлять, задокументировать решение.

### Продвинутый `KiasAdvancedRoomScanner`

`devices.yml`: 6 slots, whitelist `KiasScannerModule`, defaults уже:
1 Motion; 2 Identity; 3 Threat; 4 Spectral; 5 Biometric; 6 Radiation.

Эти 6 разных возможностей приоритетны для функциональных тестов. Advanced потенциально принимает и другие модули, например Optical/Connector, но **все разные модули одновременно не помещаются**. Не расширять слоты, не ломать прототипы и не вытеснять важные комплектующие. Отдельные test-only варианты с временной заменой слота разрешены в изолированной копии для проверки дополнительных модулей, не в исходном Briar.

### Правильная YAML-процедура

1. Сделать копию исходного файла и SHA256; проверить map format/version/список UID/связность parent/ContainerSlot.
2. Загрузить исходный grid настоящим MapLoader, провести MapInit, выгрузить runtime snapshot каждого scanner: UID, proto, advanced, число slots, actual occupied proto/module, owner, grid, powered, online.
3. Если `ent: null`, НЕ заключать «пусто»: prototyped `startingItem` может быть создан на MapInit. Проверять runtime.
4. Для явно недостающих, совместимых и полезных модулей использовать имеющиеся API serializer/savegrid или аккуратно изменить YAML: создать entity с правильным `uid` / `Transform.parent` и валидным container reference `ent`; не ставить `startingItem` напрямую в сериализованный `ContainerSlot` как fake entity. Никогда не переиспользовать UID.
5. Не добавлять `KiasAdvancedModule` в базовый scanner (`KiasBasicModule` whitelist).
6. После изменения выполнить map-load, прототипную валидацию, occupancy test, save/reload test; проверить отсутствие удвоения defaults после повторной загрузки.
7. Сохранить diff только scanner modules/UID/container references и сопутствующие изменения map serializer, если они действительно обязательны; для любого другого изменения требуется согласование. При невозможности корректной сериализации — отдельный test-only runtime initializer с протоколом, не мутировать исходный YAML и не говорить «готово».

## Геометрия и смежность

- Каждая комната: обычный scanner + suppression, на стенном тайле, повернуты внутрь комнаты.
- Часть комнат: advanced scanners, тоже на стенном тайле.
- Некоторые комнаты: speakers на стене внутрь.
- 6 weapon-flash detectors: 2 влево, 2 вправо, 1 вперёд, 1 назад; возможен некрайний тайл на обшивке/решётке. **Не проверять как ошибка то, что датчик не на внешнем крайнем тайле**, проверять реальные сектор/LOS/дальность и orientation.
- Отдельно устройство `KiasDeviceAdapter` у DAM, проверять реальное управление адаптером и связь с DAM.
- Световые контроллеры групп и четырёхпозиционные реле **осознанно не установлены**. Не требовать от исходного грида того, чего на нём нет; проверить fallback современных `ALL Lighting` и корректно заблокированные функции, которым нужен controller/relay.

## Важные возможные blockers — подтвердить runtime

1. `AirAlarm` (8 шт.) не содержит сериализованного `KiasIntegrated` в соответствующей proto-группе. `KiasSafetySystem.OnAtmos` требует его; граф `atmosphere` может не получать настоящий сигнал без подходящей интеграции. Проверить, происходит ли интеграция динамически.
2. В исходном гриде не обнаружена proto-группа `FireAlarm`, а `KiasProtocolSystem.OnFireAlarm` проверяет `FireAlarmComponent` вместе с `KiasIntegrated`. Не путать `Firelock` (дверь) с `FireAlarm`.
3. Программы `battle-impact`, `battle-flash`, `battle-manual` используют `LightController` profile, которого не видно на карте. Проверить: существует ли иной резолвер/новое название профиля и есть ли actual targets.
4. `KiasControllerProgrammer` на самом корабле отсутствует. Новый патч программатора проверяется на **отдельном физическом тестовом стенде** с новой карточкой и настоящими BUI-сообщениями.
5. Имеется 1 `KiasPdcRadar`, но ванильное вооружение не обязательно поддерживает автоматическую ПКО; проверить штатный запрет и работу на test-only совместимом PDC-оружии.

## Ship ownership без shipyard

После загрузки грид не имеет обязательного покупного prototype. В test harness назначь owner/company/crew/IFF через реальные существующие системы и компоненты/эквивалентные игровым механикам API. Не подменять проверки доступов флагом «всем разрешено»; пройти сценарии `owner ID`, `company`, `unowned claim`, `POI restrictions`, если реальные механики присутствуют. Создай минимум 2 команды экипажа и чужаков; учитывай KIAS crew/transponder semantics.

## Deliverables

`ship_inventory.json` с uid/proto/position/rotation/power/online/modules/room/owner/group; `MAP_AUDIT.md`; diff scanner changes; map-loading screenshot + console logs; список `BLOCKERS_FOR_USER.md` с точными предложениями по другим объектам и их минимальными позиционными координатами.
