kias-shutdown = KIAS отключается.
ent-KiasProgrammableController = программируемая схема KIAS
    .desc = Переносная программируемая схема автоматики KIAS. Для запуска программы вставьте её в подключённый шкаф контроллеров.
kias-sensor-range = Дальность
kias-scanner-modules = Установленные модули
kias-log-filter = Фильтр журнала
kias-service-target = Источник: {$source}; выбранное устройство: {$target}
kias-service-mode = Режим инструмента
kias-display-page = Страница дисплея
kias-service-current-group = Текущая группа ({ $kind }): { $group }
kias-service-group-lighting = освещение
kias-service-group-speaker = динамики
ent-KiasManagementConsole = управляющая консоль KIAS
    .desc = Управляет ядром KIAS и показывает состояние корабельной автоматики.
ent-KiasManagementConsoleBoard = плата: управляющая консоль KIAS
    .desc = Плата для сборки устройства «управляющая консоль KIAS».
kias-claimed = KIAS закреплена за владельцем предъявленной ID. Для доступа используйте ID или КПК; перевыпущенная карта того же владельца также подходит.
kias-claim-protected = У KIAS уже есть политика доступа. Предъявление ID не меняет владельца защищённой системы.
kias-online = KIAS в сети.
kias-enable = Включить KIAS
kias-disable = Отключить KIAS
kias-device-status = KIAS: {$status}
kias-status-offline = ОТКЛЮЧЕНО
kias-status-online = ИСПРАВНО
kias-status-nopower = НЕТ ПИТАНИЯ
kias-status-nodatapath = НЕТ ЛИНИИ ДАННЫХ
kias-status-disconnected = НЕ ПОДКЛЮЧЕНО
kias-status-duplicatecore = СБОЙ: НЕСКОЛЬКО ЯДЕР
kias-sender = KIAS
kias-window-title = KIAS — Автоматика корабля
kias-refresh = Обновить
kias-port-announce = Объявление
kias-port-announce-description = Передать настроенное сообщение через KIAS.
kias-custom-message = Свой текст объявления
kias-save = Сохранить
kias-mode-link = Связать
kias-mode-diagnose = Диагностика
kias-mode-coverage = Показать покрытие
kias-mode-test = ТЕСТ
kias-source-selected = Источник выбран. Примените инструмент к динамику KIAS.
kias-test-cooldown = ТЕСТ перезаряжается (10 секунд).
kias-test-start = Самопроверка KIAS запущена.
kias-test-done = Самопроверка KIAS завершена.
kias-coverage-legend = Покрытие DATA: X = устройство, + = связь есть, . = связи нет
kias-port-motion = Присутствие
kias-port-motion-description = Срабатывает при появлении отслеживаемой сущности в зоне сканера.
kias-registration-locked = Регистрация заблокирована или сервер отключён.
kias-transponder-registered = Транспондер экипажа зарегистрирован.
kias-lock-registration = Заблокировать регистрацию
kias-unlock-registration = Разблокировать регистрацию
kias-biometric-alive = Жив
kias-biometric-critical = Критическое состояние
kias-biometric-dead = Мёртв
kias-radiation = {$name}: {$value} рад/с
kias-anomaly-growth = Внимание. Обнаружен рост аномалии: {$location}.
kias-atmos-danger = Внимание. Опасное состояние атмосферы: {$location}.
kias-sector-center = центр корабля
kias-sector-fore-port = нос, левый борт
kias-sector-fore-starboard = нос, правый борт
kias-sector-aft-port = корма, левый борт
kias-sector-aft-starboard = корма, правый борт
kias-contact = Блюспейс-возмущение. {$disposition} корабль. Дистанция {$range} км, пеленг {$bearing}°.
kias-contact-friendly = Дружественный
kias-contact-neutral = Нейтральный
kias-contact-unknown = Неизвестный
kias-contact-hostile = Враждебный
kias-autopilot-arrived = Автопилот: пункт назначения достигнут.
kias-owner-only = Главный ключ доступен только владельцу корабля.
ent-KiasCore = ядро KIAS
    .desc = Ядро корабельной автоматики. Объединяет устройства через кабель данных KIAS.
ent-KiasDefenceServer = сервер обороны KIAS
    .desc = Обрабатывает события обороны и разрешает управление корабельным вооружением.
ent-KiasAtmosServer = сервер атмосферы KIAS
    .desc = Подключает атмосферные тревоги и пожарную безопасность к автоматике корабля.
ent-KiasPowerServer = сервер энергоснабжения KIAS
    .desc = Следит за энергоснабжением и сообщает о дефиците мощности.
ent-KiasCrewServer = сервер экипажа KIAS
    .desc = Учитывает зарегистрированные транспондеры экипажа. Регистрацию может закрыть владелец корабля.
ent-KiasNavigationServer = сервер навигации KIAS
    .desc = Подключает навигационные события и связь к корабельной автоматике.
ent-KiasRecorder = регистратор KIAS
    .desc = Хранит последние события и сообщения корабельной автоматики.
ent-KiasSpeaker = динамик KIAS
    .desc = Произносит сообщения KIAS от своего лица. Может получать текст от программируемой схемы.
ent-KiasDisplay = дисплей KIAS
    .desc = Показывает состояние подключённых устройств и экипажа.
ent-KiasServiceTool = сервисный мультитул KIAS
    .desc = Настраивает связи, помещения и группы устройств KIAS. Также помогает проверить состояние сети.
ent-KiasRoomScanner = модульный сканер помещения KIAS
    .desc = Сканирует помещение с помощью установленных модулей. Подключающие модули открывают KIAS доступ к совместимым устройствам.
ent-KiasDataCable = кабель данных KIAS
    .desc = Подпольная линия данных автоматики KIAS. Обслуживает устройства рядом с кабелем.
ent-KiasDataCableStack = катушка кабеля данных KIAS
    .desc = Катушка кабеля для прокладки подпольной линии данных KIAS.
ent-KiasDataCableStack1 = катушка кабеля данных KIAS
    .desc = Катушка кабеля для прокладки подпольной линии данных KIAS.
ent-KiasMotionModule = модуль присутствия KIAS
    .desc = Сменный модуль присутствия KIAS для оснащения сканера помещения.
ent-KiasIdModule = модуль чтения ID KIAS
    .desc = Сменный модуль чтения id KIAS для оснащения сканера помещения.
ent-KiasTransponderModule = модуль приёма транспондеров KIAS
    .desc = Сменный модуль приёма транспондеров KIAS для оснащения сканера помещения.
ent-KiasBiometricModule = биометрический модуль KIAS
    .desc = Сменный биометрический модуль KIAS для оснащения сканера помещения.
ent-KiasRadiationModule = радиационный модуль KIAS
    .desc = Сменный радиационный модуль KIAS для оснащения сканера помещения.
ent-KiasSpectralModule = спектральный модуль KIAS
    .desc = Сменный спектральный модуль KIAS для оснащения сканера помещения.
ent-KiasCrewTransponder = транспондер экипажа KIAS
    .desc = Позволяет KIAS обнаруживать зарегистрированного члена экипажа.
ent-KiasHorizon = блюспейс-интерферометр Horizon KIAS
    .desc = Обнаруживает блюспейс-события поблизости от корабля.
ent-KiasMasterKey = главный ключ KIAS
    .desc = Ключ управления главным выключателем KIAS.
kias-hull-other-sectors = другие сектора
kias-pdc-manual-priority = Орудие занято точечной обороной KIAS. Ручная стрельба временно заблокирована.
kias-page-overview = Обзор
kias-page-atmos = Атмосфера
kias-page-crew = Экипаж
kias-page-power = Энергетика
kias-page-defence = Оборона
kias-page-navigation = Навигация
kias-page-faults = Неисправности
kias-alert-normal = Штатный режим
kias-alert-contact = Контакт
kias-alert-battle = Боевая тревога
kias-alert-emergency = Аварийный режим
kias-no-faults = Неисправностей устройств нет.
kias-pdc-reserved = { $weapon }: занято точечной обороной KIAS; ручная стрельба заблокирована.
kias-pdc-enabled = Автоматическая ПКО включена.
kias-pdc-disabled = Автоматическая ПКО выключена.
kias-protocols-title = Протоколы корабля (настройка владельцем)
kias-protocol-trigger = Условие запуска
kias-protocol-action = Действие
kias-protocol-target = Устройство
kias-protocol-group = Группа света
kias-protocol-port = Входной порт
kias-protocol-cooldown = Задержка повтора в секундах (1–600)
kias-protocol-enabled = Протокол включён
kias-protocol-value = Включить / замкнуть выбранное устройство
kias-protocol-new = Новый протокол
kias-protocol-whole-ship = Весь корабль / без отдельного устройства
kias-remove = Удалить
kias-action-announce = Объявление
kias-action-record = Запись в журнал
kias-action-lights = Переключить группу света
kias-action-suppression = Использовать картридж пожаротушения
kias-action-relay = Переключить реле
kias-action-pdc = Переключить ПКО
kias-action-deviceport = Активировать вход устройства
kias-action-mayday = Передать MAYDAY
kias-power-unavailable = Сервер энергетики недоступен.
kias-power-statistics = { $channel }: подача { $supply } кВт / запрос { $demand } кВт.
kias-power-deficit = Дефицит мощности: { $channel }.
kias-config-owner = Протоколы может настраивать только владелец корабля на подключённом дисплее.
kias-crew-dead = Обнаружена смерть: { $location }.
kias-crew-critical = Критическое состояние экипажа: { $location }.
kias-fire-alarm = Пожарная тревога: { $location }.
kias-mayday = MAYDAY. { $ship }. Координаты: { $x }, { $y }; карта: { $map }. { $reason }
kias-trigger-hullimpact = Попадание в корпус
kias-trigger-hulldamage = Повреждение корпуса
kias-trigger-collision = Столкновение кораблей
kias-trigger-atmosdanger = Воздушная или пожарная тревога
kias-trigger-anomalygrowth = Рост аномалии
kias-trigger-contact = Блюспейс-контакт
kias-trigger-arrival = Прибытие автопилота
kias-trigger-weaponflash = Выстрел корабельного орудия
kias-trigger-proximity = Сближение с кораблём
kias-trigger-crewcritical = Критическое состояние экипажа
kias-trigger-crewdead = Смерть члена экипажа
kias-trigger-vesselcritical = Критическое состояние судна
kias-trigger-powerdeficit = Дефицит мощности
kias-trigger-manual = Ручной запуск
kias-weapon-flash = Обнаружен выстрел корабельного орудия поблизости.
kias-proximity-contact = Контакт вблизи: { $range } м, { $disposition }.
ent-KiasWeaponFlashDetector = KIAS: детектор выстрелов
    .desc = Распознаёт оружейные вспышки в пределах своего сектора обзора.
ent-KiasProximitySensor = KIAS: датчик сближения
    .desc = Обнаруживает приближающиеся объекты и сообщает расстояние до них.
kias-pdc-enable = Включить автоматическую ПКО
kias-pdc-disable = Выключить автоматическую ПКО
ent-KiasPdcRadar = KIAS: радар ПКО
    .desc = Передаёт данные радара системе автоматической противокорабельной обороны.
kias-relay-close = Замкнуть выбранный канал
kias-relay-open = Разомкнуть выбранный канал
kias-relay-highvoltage = Канал: ВВ
kias-relay-mediumvoltage = Канал: СВ
kias-relay-apc = Канал: НВ
kias-relay-data = Канал: DATA
ent-KiasRelay = KIAS: четырёхканальное реле
    .desc = Замыкает и размыкает выбранный канал питания или данных.
kias-mode-group = Назначить группу
kias-light-group-set = Назначена группа: { $group }.
kias-lights-on = Включить группу света
kias-lights-off = Выключить группу света
kias-suppress = Использовать картридж пожаротушения
kias-suppression-activated = Пожаротушение активировано: { $location }.
kias-port-suppress = Пожаротушение
kias-port-suppress-description = Использует установленный картридж пожаротушения.
ent-KiasLightController = KIAS: контроллер группы света
    .desc = Управляет светильниками одной именованной группы. Группа задаётся сервисным инструментом или в настройках.
ent-KiasSuppression = KIAS: модуль пожаротушения
    .desc = Применяет установленный картридж для локального пожаротушения.
ent-KiasSuppressionCartridge = картридж пожаротушения KIAS
    .desc = Расходный картридж для модуля пожаротушения KIAS.
kias-grid-collision = Столкновение кораблей. Относительная скорость: { $speed } м/с.
kias-hull-impact = Попадание в корпус: { $location }. Попаданий: { $amount }.
kias-hull-destroyed = Разрушение корпуса: { $location }. Конструкций: { $amount }.
kias-hull-damage = Повреждение корпуса: { $location }. Урон: { $amount }.
ent-KiasHullSensor = KIAS: датчик попаданий в корпус
    .desc = Обнаруживает попадания снарядов по корпусу в пределах своей дальности.
ent-KiasIntegrityMonitor = KIAS: монитор целостности корпуса
    .desc = Сообщает о повреждениях отслеживаемого корпуса.
ent-KiasCollisionMonitor = KIAS: монитор столкновений
    .desc = Сообщает о столкновениях грида с другими гридами.

kias-trigger-fire = Обнаружен пожар
kias-run-manual = Запустить ручные протоколы
kias-reset-alert = Сбросить тревогу
kias-mode-room = Задать название помещения
kias-room-set = Помещение: { $room }

kias-coverage-colors = DATA: бирюзовый — покрытие сети; голубой — кабель; жёлтая рамка — выбранное устройство. Снимок 9×9 клеток.

kias-protocol-any-contact = Любая классификация контакта
kias-protocol-minimum = Минимальное значение: урон, скорость, дальность или дефицит
kias-protocol-crew-unavailable = Только при бедствии и отсутствии дееспособного зарегистрированного экипажа


ent-KiasAtmosServerBoard = плата: сервер атмосферы KIAS
    .desc = Плата для сборки устройства «сервер атмосферы KIAS».

ent-KiasCollisionMonitorBoard = плата: KIAS: монитор столкновений
    .desc = Плата для сборки устройства «KIAS: монитор столкновений».

ent-KiasCoreBoard = плата: ядро KIAS
    .desc = Плата для сборки устройства «ядро KIAS».

ent-KiasCrewServerBoard = плата: сервер экипажа KIAS
    .desc = Плата для сборки устройства «сервер экипажа KIAS».

ent-KiasDefenceServerBoard = плата: сервер обороны KIAS
    .desc = Плата для сборки устройства «сервер обороны KIAS».

ent-KiasDisplayBoard = плата: дисплей KIAS
    .desc = Плата для сборки устройства «дисплей KIAS».

ent-KiasHorizonBoard = плата: блюспейс-интерферометр Horizon KIAS
    .desc = Плата для сборки устройства «блюспейс-интерферометр Horizon KIAS».

ent-KiasHullSensorBoard = плата: KIAS: датчик попаданий в корпус
    .desc = Плата для сборки устройства «KIAS: датчик попаданий в корпус».

ent-KiasIntegrityMonitorBoard = плата: KIAS: монитор целостности корпуса
    .desc = Плата для сборки устройства «KIAS: монитор целостности корпуса».

ent-KiasLightControllerBoard = плата: KIAS: контроллер группы света
    .desc = Плата для сборки устройства «KIAS: контроллер группы света».

ent-KiasNavigationServerBoard = плата: сервер навигации KIAS
    .desc = Плата для сборки устройства «сервер навигации KIAS».

ent-KiasPdcRadarBoard = плата: KIAS: радар ПКО
    .desc = Плата для сборки устройства «KIAS: радар ПКО».

ent-KiasPowerServerBoard = плата: сервер энергоснабжения KIAS
    .desc = Плата для сборки устройства «сервер энергоснабжения KIAS».

ent-KiasProximitySensorBoard = плата: KIAS: датчик сближения
    .desc = Плата для сборки устройства «KIAS: датчик сближения».

ent-KiasRecorderBoard = плата: регистратор KIAS
    .desc = Плата для сборки устройства «регистратор KIAS».

ent-KiasRelayBoard = плата: KIAS: четырёхканальное реле
    .desc = Плата для сборки устройства «KIAS: четырёхканальное реле».

ent-KiasRoomScannerBoard = плата: модульный сканер помещения KIAS
    .desc = Плата для сборки устройства «модульный сканер помещения KIAS».

ent-KiasSpeakerBoard = плата: динамик KIAS
    .desc = Плата для сборки устройства «динамик KIAS».

ent-KiasSuppressionBoard = плата: KIAS: модуль пожаротушения
    .desc = Плата для сборки устройства «KIAS: модуль пожаротушения».

ent-KiasWeaponFlashDetectorBoard = плата: KIAS: детектор выстрелов
    .desc = Плата для сборки устройства «KIAS: детектор выстрелов».

kias-add-action = Добавить действие
kias-remove-action = Удалить действие

kias-audio-notification = Уведомление
kias-audio-warning = Предупреждение
kias-audio-battle = Боевая тревога
kias-audio-emergency = Аварийная тревога
kias-audio-preview = Прослушать
kias-tone-silent = Без звука
kias-tone-chime = Короткий сигнал
kias-tone-buzzer = Зуммер
kias-tone-bluealert = Синяя тревога
kias-tone-redalert = Красная тревога
kias-tone-reactoralarm = Реакторная сирена

kias-trigger-docked = Стыковка
kias-trigger-undocked = Расстыковка
kias-port-emergency = Аварийная кнопка
kias-position-zero = Положение 0
kias-position-one = Положение 1
kias-position-two = Положение 2
kias-position-three = Положение 3
kias-emergency-button-name = Аварийная кнопка KIAS
kias-emergency-button-description = Запускает ручной аварийный протокол через подключённый приёмник.

ent-KiasIffReceiver = приёмник опознавания KIAS
    .desc = Определяет принадлежность контактов для корабельной автоматики.
ent-KiasIffReceiverBoard = плата: приёмник опознавания KIAS
    .desc = Плата для сборки устройства «приёмник опознавания KIAS».
ent-KiasDockingSensor = датчик стыковки KIAS
    .desc = Сообщает о стыковке и отстыковке корабля.
ent-KiasDockingSensorBoard = плата: датчик стыковки KIAS
    .desc = Плата для сборки устройства «датчик стыковки KIAS».
ent-KiasDeviceAdapter = адаптер устройств KIAS
    .desc = Связывает автоматику KIAS со штатными портами устройств.
ent-KiasDeviceAdapterBoard = плата: адаптер устройств KIAS
    .desc = Плата для сборки устройства «адаптер устройств KIAS».
ent-KiasWirelessTransceiver = беспроводной трансивер KIAS
    .desc = Передаёт сигналы между настроенными устройствами KIAS в пределах дальности.
ent-KiasWirelessTransceiverBoard = плата: беспроводной трансивер KIAS
    .desc = Плата для сборки устройства «беспроводной трансивер KIAS».
ent-KiasKeySwitch = ключевой выключатель KIAS
    .desc = Включает и отключает ядро KIAS при наличии главного ключа.
ent-KiasKeySwitchBoard = плата: ключевой выключатель KIAS
    .desc = Плата для сборки устройства «ключевой выключатель KIAS».
ent-KiasRotarySwitch = поворотный переключатель KIAS
    .desc = Позволяет вручную выбрать одно из положений переключателя.
ent-KiasRotarySwitchBoard = плата: поворотный переключатель KIAS
    .desc = Плата для сборки устройства «поворотный переключатель KIAS».
ent-KiasResourceMonitor = монитор ресурсов KIAS
    .desc = Показывает количество ресурсов в выбранных объектах.
ent-KiasResourceMonitorBoard = плата: монитор ресурсов KIAS
    .desc = Плата для сборки устройства «монитор ресурсов KIAS».
ent-KiasFlipFlop = кнопка-переключатель KIAS
    .desc = Переключает сохраняемое состояние при каждом нажатии.
ent-KiasFlipFlopBoard = плата: кнопка-переключатель KIAS
    .desc = Плата для сборки устройства «кнопка-переключатель KIAS».
ent-KiasEmergencyReceiver = аварийный приёмник KIAS
    .desc = Принимает настроенные аварийные сигналы корабельной автоматики.
ent-KiasEmergencyReceiverBoard = плата: аварийный приёмник KIAS
    .desc = Плата для сборки устройства «аварийный приёмник KIAS».
ent-KiasEmergencyButton = аварийная кнопка KIAS
    .desc = Отправляет аварийный сигнал при нажатии.
ent-KiasNavigationLight = навигационный огонь KIAS
    .desc = Навигационный огонь для обозначения корабля.
ent-KiasAdvancedRoomScanner = продвинутый сканер KIAS
    .desc = Сканер помещения с расширенным набором установленных модулей.
ent-KiasAdvancedRoomScannerBoard = плата: продвинутый сканер KIAS
    .desc = Плата для сборки устройства «продвинутый сканер KIAS».
ent-KiasIntegrationKit = комплект интеграции KIAS
    .desc = Подключает совместимое устройство к управлению KIAS. После установки требуется доступная линия данных.
ent-KiasMaydayAntenna = аварийная антенна KIAS
    .desc = Передаёт сигналы бедствия через штатную систему корабельной связи.
ent-KiasMaydayAntennaBoard = плата: аварийная антенна KIAS
    .desc = Плата для сборки устройства «аварийная антенна KIAS».
ent-KiasIdentityModule = модуль идентификации KIAS
    .desc = Сменный модуль идентификации KIAS для оснащения сканера помещения.
ent-KiasConnectorModule = модуль подключения KIAS
    .desc = Сменный модуль подключения KIAS для оснащения сканера помещения.
ent-KiasOpticalModule = оптический модуль KIAS
    .desc = Сменный оптический модуль KIAS для оснащения сканера помещения.
ent-KiasThreatModule = модуль оптического распознавания угроз KIAS
    .desc = Сменный модуль оптического распознавания угроз KIAS для оснащения сканера помещения.
kias-run-selected-protocol = Запустить выбранный протокол
kias-diagnostic-chain = { $core } → DATA ({ $nodes } кабелей) → { $target }. Статус: { $status }. Питание: { $power }.
kias-fire-locked = LockedKIAS: стрельба заблокирована протоколом KIAS.
kias-action-firelock = Блокировка стрельбы
kias-trigger-powerlost = Потеря питания устройства
kias-trigger-boot = Включение KIAS
kias-trigger-shutdown = Выключение KIAS
kias-device-power-lost = Потеря питания: { $device }.
kias-preset-power-lost = Потеря питания
kias-preset-boot = Включение KIAS
kias-preset-shutdown = Выключение KIAS
ent-KiasJammer = модуль радиоэлектронных помех KIAS
    .desc = Создаёт радиоэлектронные помехи по команде системы обороны.
ent-KiasJammerBoard = плата модуля радиоэлектронных помех KIAS
    .desc = Плата для сборки устройства «модуль радиоэлектронных помех KIAS».
kias-boarding = Подозрительная активность при поражении экипажа: { $location }.
kias-fire-clear = Пожар ликвидирован: { $location }.
kias-atmos-clear = Атмосфера восстановлена: { $location }.
kias-captain-greeting = Добро пожаловать на борт, капитан.
kias-local-threat = Оптический сканер обнаружил оружие или агрессивную фауну: { $location }.
kias-radiation-background = Повышенный радиационный фон: { $location }, { $value } рад/с.
kias-medical-help = KIAS судна «{ $ship }»: возможно требуется медицинская помощь. Экипаж длительно недоступен. Координаты: { $x }, { $y }. Причина: { $reason }.
kias-light-color = Цвет света (#RRGGBB)
kias-light-brightness = Яркость (0–2)
kias-module-motion = Движение/присутствие
kias-module-identity = ID/транспондер
kias-module-biometric = Биометрия
kias-module-radiation = Радиация
kias-module-spectral = Спектральный
kias-module-connector = Подключение устройств
kias-module-optical = Камера
kias-module-threat = Оптическое распознавание угроз
kias-trigger-radiation = Радиационный фон
kias-trigger-localthreat = Локальная угроза
kias-trigger-boarding = Противоабордажная тревога
kias-trigger-captaingreeting = Приветствие владельца
kias-trigger-fireclear = Пожар ликвидирован
kias-trigger-atmosclear = Атмосфера восстановлена
kias-trigger-crewunavailable = Экипаж длительно недоступен
kias-trigger-quietmode = Тихий режим
kias-action-medicalhelp = Запрос медицинской помощи
kias-action-jammer = Постановка помех
kias-action-decoy = Отстрел приманок
kias-action-restoreventilation = Восстановить вентиляцию
kias-preset-battle-impact = Бой: попадание
kias-preset-battle-flash = Бой: враждебная вспышка
kias-preset-battle-manual = Бой: аварийная кнопка
kias-preset-flash-unknown = Неопознанная вспышка
kias-preset-hull-damage = Повреждение корпуса
kias-preset-collision = Столкновение
kias-preset-atmosphere = Разгерметизация
kias-preset-anomaly = Рост аномалии
kias-preset-contact = Прибытие контакта
kias-preset-arrival = Прибытие автопилота
kias-preset-proximity = Сближение
kias-preset-crew-critical = Критическое состояние экипажа
kias-preset-crew-dead = Гибель экипажа
kias-preset-vessel-critical = Критическое состояние корабля
kias-preset-power-deficit = Дефицит питания
kias-preset-fire = Пожар
kias-preset-radiation = Радиация
kias-preset-local-threat = Вооружённая угроза
kias-preset-boarding = Противоабордажная тревога
kias-preset-greeting = Приветствие владельца
kias-preset-fire-clear = Пожар ликвидирован
kias-preset-atmos-clear = Атмосфера восстановлена
kias-preset-medical-assistance = Запрос медицинской помощи
kias-preset-quiet = Тихий режим (добровольный)

kias-wireless-trust-updated = Привязка доверенного передатчика обновлена.
kias-wireless-trusted = Доверенные передатчики: { $devices }
kias-controller-rack-full = В шкафу нет свободного слота контроллера.
kias-controller-rack-examine = Работает: { $running }. KIAS: { $status }. Контроллеры: { $count }/8. Нагрузка: { $load } Вт.
kias-controller-dirty-eject = Сначала запишите программу или явно отмените изменения.
kias-controller-slot = Контроллер
kias-role-controller = Контроллеры
kias-controller-slot-1 = Контроллер 1
kias-controller-slot-2 = Контроллер 2
kias-controller-slot-3 = Контроллер 3
kias-controller-slot-4 = Контроллер 4
kias-controller-slot-5 = Контроллер 5
kias-controller-slot-6 = Контроллер 6
kias-controller-slot-7 = Контроллер 7
kias-controller-slot-8 = Контроллер 8
kias-controller-editor-title = Программатор KIAS
kias-controller-rack-title = Шкаф контроллеров KIAS
kias-controller-rename = Переименовать
kias-controller-load-preset = Загрузить шаблон
kias-controller-write = WRITE — записать
kias-controller-discard = Отбросить изменения
kias-controller-eject = Извлечь
kias-controller-search = Поиск узлов и устройств
kias-controller-editor-help = Перетащите узел из списка; соединяйте порты одного типа. ПКМ на фоне — перемещение, колесо — масштаб, ПКМ на порту — удалить провод.
kias-controller-editor-status = Карточка: { $card }; сеть: { $online }; изменения: { $dirty }. Узлы: { $nodes }/128, провода: { $wires }/256.
kias-controller-rack-status = Сеть: { $online }. Работают: { $running }/8. Потребление: { $load } Вт.
kias-controller-logic = Логика
kias-controller-devices = Конкретные устройства
kias-controller-text = Текст
kias-controller-number = Число
kias-controller-seconds = Интервал, секунды (0.1–600)
kias-controller-enum = Значение перечисления
kias-controller-matches = Найдено устройств
kias-controller-remove-node = Удалить узел
kias-controller-invalid-number = Введите корректное число.
kias-controller-comparison-equal = Равно
kias-controller-comparison-notequal = Не равно
kias-controller-comparison-less = Меньше
kias-controller-comparison-lessequal = Меньше или равно
kias-controller-comparison-greater = Больше
kias-controller-comparison-greaterequal = Больше или равно
kias-controller-node-onstart = При запуске
kias-controller-node-boolconstant = Да/Нет
kias-controller-node-numberconstant = Число
kias-controller-node-stringconstant = Строка
kias-controller-node-enumconstant = Перечисление
kias-controller-node-and = И (AND)
kias-controller-node-or = ИЛИ (OR)
kias-controller-node-xor = Исключающее ИЛИ (XOR)
kias-controller-node-not = НЕ (NOT)
kias-controller-node-nand = И-НЕ (NAND)
kias-controller-node-nor = ИЛИ-НЕ (NOR)
kias-controller-node-xnor = Совпадение (XNOR)
kias-controller-node-if = Условие
kias-controller-node-timer = Таймер
kias-controller-node-clock = Генератор импульсов
kias-controller-node-latch = RS-защёлка
kias-controller-node-toggle = Переключатель
kias-controller-node-counter = Счётчик
kias-controller-node-edge = Детектор фронта
kias-controller-node-numbercompare = Сравнение чисел
kias-controller-node-boolcompare = Сравнение логических значений
kias-controller-node-stringcompare = Сравнение строк
kias-controller-node-enumcompare = Сравнение перечислений
kias-controller-node-cooldown = Ограничитель частоты
kias-controller-port-description = Типизированный порт контроллера KIAS.
kias-controller-import-legacy = Импорт старой записи
kias-controller-node-stringlatch = Память строки

kias-controller-management-help = Автоматика выполняется карточками в серверном шкафу. Схемы редактируются в программаторе.
kias-controller-management-status = Работающих карточек: { $running }
kias-controller-quiet = Тихий режим

kias-controller-port-a = А
kias-controller-port-alarm = Тревога
kias-controller-port-alert = Уровень тревоги
kias-controller-port-alertreset = Тревога сброшена
kias-controller-port-allcrewunavailable = Весь экипаж недоступен
kias-controller-port-amount = Количество
kias-controller-port-announce = Объявить
kias-controller-port-anomalygrowth = Рост аномалии
kias-controller-port-arrival = Прибытие
kias-controller-port-atmosclear = Атмосфера безопасна
kias-controller-port-atmosdanger = Опасная атмосфера
kias-controller-port-automatic = Автоматический режим
kias-controller-port-b = Б
kias-controller-port-bearing = Направление
kias-controller-port-boarding = Вторжение
kias-controller-port-boot = Запуск
kias-controller-port-cancel = Отменить
kias-controller-port-captaingreeting = Приветствие капитана
kias-controller-port-channel = Канал
kias-controller-port-clear = Сбросить
kias-controller-port-close = Закрыть
kias-controller-port-closed = Закрыто
kias-controller-port-collision = Столкновение
kias-controller-port-condition = Условие
kias-controller-port-consumption = Потребление
kias-controller-port-contact = Контакт
kias-controller-port-contactentity = Объект контакта
kias-controller-port-crew = Экипаж
kias-controller-port-crewcritical = Экипаж в критическом состоянии
kias-controller-port-crewdead = Смерть члена экипажа
kias-controller-port-crewunavailable = Экипаж недоступен
kias-controller-port-damage = Урон
kias-controller-port-danger = Опасность
kias-controller-port-decrement = Уменьшить
kias-controller-port-deficit = Дефицит
kias-controller-port-deploy = Выпустить
kias-controller-port-difference = Разница
kias-controller-port-disable = Отключить
kias-controller-port-disposition = Отношение
kias-controller-port-distance = Расстояние
kias-controller-port-docked = Стыковка
kias-controller-port-elapsed = Время истекло
kias-controller-port-enable = Включить
kias-controller-port-enabled = Включено
kias-controller-port-entities = Объекты
kias-controller-port-escalatealert = Повысить тревогу
kias-controller-port-eventkey = ID события
kias-controller-port-falling = Переход в Нет
kias-controller-port-false = Нет
kias-controller-port-faunathreat = Опасная фауна
kias-controller-port-fire = Пожар
kias-controller-port-fireclear = Пожар потушен
kias-controller-port-firelock = Блокировка огня
kias-controller-port-hasany = Есть устройство
kias-controller-port-hulldamage = Повреждение корпуса
kias-controller-port-hullimpact = Удар по корпусу
kias-controller-port-impact = Удар
kias-controller-port-increment = Увеличить
kias-controller-port-key = ID события
kias-controller-port-localthreat = Местная угроза
kias-controller-port-lock = Заблокировать
kias-controller-port-manual = Ручной запуск
kias-controller-port-matchedcount = Совпадения
kias-controller-port-mayday = MAYDAY
kias-controller-port-maydaymessage = Причина MAYDAY
kias-controller-port-medicalhelp = Запросить помощь
kias-controller-port-message = Сообщение
kias-controller-port-motion = Движение
kias-controller-port-occupied = Помещение занято
kias-controller-port-off = Выключить
kias-controller-port-on = Включить
kias-controller-port-onlinecount = В сети
kias-controller-port-open = Открыть
kias-controller-port-othergrid = Другой грид
kias-controller-port-person = Персона
kias-controller-port-personcritical = Критическое состояние
kias-controller-port-persondead = Смерть
kias-controller-port-powerdeficit = Дефицит питания
kias-controller-port-powerlost = Питание потеряно
kias-controller-port-proximity = Сближение
kias-controller-port-quietmode = Тихий режим
kias-controller-port-radiation = Радиация
kias-controller-port-ready = Готов
kias-controller-port-record = Записать
kias-controller-port-relativespeed = Относительная скорость
kias-controller-port-reset = Сбросить
kias-controller-port-resetalert = Сбросить тревогу
kias-controller-port-restore = Восстановление
kias-controller-port-rising = Переход в Да
kias-controller-port-runmanual = Запустить вручную
kias-controller-port-saved = Строка сохранена
kias-controller-port-set = Установить
kias-controller-port-setalert = Задать тревогу
kias-controller-port-setquietmode = Включить тихий режим
kias-controller-port-shutdown = Выключение
kias-controller-port-source = Источник
kias-controller-port-started = Запущен
kias-controller-port-state = Состояние
kias-controller-port-store = Сохранить строку
kias-controller-port-stored = Сохранённая строка
kias-controller-port-structure = Объект корпуса
kias-controller-port-supply = Подача
kias-controller-port-threat = Угроза
kias-controller-port-tick = Такт
kias-controller-port-toggle = Переключить
kias-controller-port-trigger = Запуск
kias-controller-port-triggered = Сработал
kias-controller-port-true = Да
kias-controller-port-unavailable = Недоступны
kias-controller-port-undocked = Расстыковка
kias-controller-port-unlock = Разблокировать
kias-controller-port-value = Значение
kias-controller-port-vesselcritical = Судно в критическом состоянии
kias-controller-port-weaponflash = Вспышка оружия
kias-controller-profile-roomscanner = Сканер помещений
kias-controller-profile-hullsensor = Датчик корпуса
kias-controller-profile-integritymonitor = Монитор целостности
kias-controller-profile-collisionmonitor = Монитор столкновений
kias-controller-profile-atmossafety = Атмосферная безопасность
kias-controller-profile-powermonitor = Монитор питания
kias-controller-profile-horizon = Горизонт
kias-controller-profile-weaponflashdetector = Датчик оружейных вспышек
kias-controller-profile-proximitysensor = Датчик сближения
kias-controller-profile-crewmonitor = Монитор экипажа
kias-controller-profile-navigationcomms = Навигационная связь
kias-controller-profile-speaker = Динамик
kias-controller-profile-recorder = Регистратор
kias-controller-profile-lightcontroller = Контроллеры групп освещения
kias-controller-profile-suppression = Пожаротушение
kias-controller-profile-relay = Реле
kias-controller-profile-defencecontroller = Оборонный контроллер
kias-controller-profile-jammer = Глушилка
kias-controller-profile-decoy = Ложные цели
kias-controller-profile-ventilation = Вентиляция
kias-controller-profile-dockingsensor = Датчик стыковки
kias-controller-profile-deviceadapter = Адаптер устройств
kias-controller-profile-automation = Ядро KIAS
kias-controller-error-version = Неподдерживаемая версия схемы
kias-controller-error-name = Недопустимое имя
kias-controller-error-limits = Превышен размер схемы
kias-controller-error-node = Недопустимый узел
kias-controller-error-config = Недопустимые параметры узла
kias-controller-error-comparison = Недопустимое сравнение
kias-controller-error-profile = Неизвестный тип устройства
kias-controller-error-schema = Недопустимые порты
kias-controller-error-endpoint = Конец провода не найден
kias-controller-error-wire = Несовместимый или повторный провод
kias-controller-error-multiple-source = У входа уже есть источник
kias-controller-error-fan-out = Слишком много проводов от одного выхода
kias-controller-error-combinational-cycle = Цикл без памяти или таймера
kias-controller-error-evaluation-budget = Превышен предел вычислений
kias-controller-error-external-feedback-budget = Зацикливание через устройства
kias-controller-error-cooldown-budget = Слишком много ключей задержки
kias-controller-error-event-queue-limit = Переполнена очередь событий
kias-controller-error-work-queue-limit = Переполнена очередь вычислений
kias-controller-error-command-queue-limit = Переполнена очередь команд
kias-controller-error-legacy-record = Старая запись повреждена
kias-controller-error-legacy-crew-condition = Условие экипажа не поддерживается для этого события
kias-controller-error-legacy-target = Устройство не поддерживает старое действие
kias-controller-error-legacy-record-target = Старая запись журнала содержит неподдерживаемую цель
kias-controller-values = Значения
kias-controller-state = Память и время
kias-controller-any = ANY — любое подходящее
kias-controller-all = ALL — все подходящие
kias-controller-bool = Логическое значение
kias-controller-native-profile = Порты устройства
kias-controller-node-specific = Конкретное устройство
kias-controller-node-any = Любое подходящее
kias-controller-node-all = Все подходящие

kias-controller-status-running = Выполняется
kias-controller-status-fault = Ошибка
kias-controller-status-offline = Отключён
kias-controller-status-invalid = Схема некорректна
kias-controller-error-actuator-exception = Ошибка исполнительного устройства

kias-controller-present = вставлена
kias-controller-absent = отсутствует
kias-controller-unsaved = не записаны
kias-controller-saved = записаны

kias-controller-type-signal = Импульс

kias-controller-type-bool = Да/Нет

kias-controller-type-number = Число

kias-controller-type-string = Строка

kias-controller-type-entity = Объект

kias-controller-type-enum = Перечисление

kias-controller-direction-input = Вход

kias-controller-direction-output = Выход

kias-controller-domain-unspecified = Выберите вид перечисления

kias-controller-domain-audiochannel = Звуковой канал

kias-controller-domain-contactdisposition = Опознавание контакта

kias-controller-domain-alert = Режим тревоги

kias-controller-domain-powerchannel = Канал питания

kias-controller-enum-unselected = Сначала выберите вид перечисления

kias-controller-enum-domain = Вид перечисления

kias-controller-yes = Да

kias-controller-no = Нет

kias-controller-duration = { $seconds } с

kias-controller-match-summary = Найдено: { $count }

kias-controller-filter-room-value = помещение: { $value }

kias-controller-filter-group-value = группа: { $value }

kias-controller-filter-room = Фильтр помещения

kias-controller-filter-group = Фильтр группы

kias-controller-inspector = Свойства узла

kias-controller-select-help = Выберите узел на схеме, чтобы изменить параметры и прочитать подсказки портов.

kias-controller-values-help = Цвет порта обозначает тип данных. Импульс — одно событие, Да/Нет — сохраняемое состояние.

kias-controller-node-section = Узел

kias-controller-selector-section = Выборка устройств

kias-controller-parameters-section = Параметры

kias-controller-actions-section = Действия

kias-controller-ports-section = Входы и выходы

kias-controller-initial-state = Начальное состояние: Да

kias-controller-comparison = Оператор сравнения

kias-controller-wire-direction = Соединение требует одного входа и одного выхода.

kias-controller-wire-type = Несовместимые типы: { $first } и { $second }.

kias-controller-wire-conversion = Используйте детектор фронта для Да/Нет → Импульс, защёлку или переключатель для обратного преобразования.

kias-controller-error-enum-domain = Нельзя соединить перечисления разных видов.

kias-controller-profile-lighting = Освещение

kias-controller-enum-audiochannel-0 = Уведомление

kias-controller-enum-audiochannel-1 = Предупреждение

kias-controller-enum-audiochannel-2 = Бой

kias-controller-enum-audiochannel-3 = Авария

kias-controller-enum-contactdisposition-0 = Неизвестный

kias-controller-enum-contactdisposition-1 = Дружественный

kias-controller-enum-contactdisposition-2 = Нейтральный

kias-controller-enum-contactdisposition-3 = Враждебный

kias-controller-enum-alert-0 = Штатный режим

kias-controller-enum-alert-1 = Контакт

kias-controller-enum-alert-2 = Бой

kias-controller-enum-alert-3 = Авария

kias-controller-enum-powerchannel-0 = Высокое напряжение

kias-controller-enum-powerchannel-1 = Среднее напряжение

kias-controller-enum-powerchannel-2 = Низкое напряжение

kias-controller-enum-powerchannel-3 = Данные

ent-KiasControllerRack = шкаф контроллеров KIAS
    .desc = Запускает до восьми программируемых схем. Для работы нужны питание и связь с ядром KIAS.

ent-KiasControllerProgrammer = программатор KIAS
    .desc = Позволяет собрать схему из узлов и записать её на переносную карточку KIAS.

ent-KiasControllerRackBoard = плата: шкаф контроллеров KIAS
    .desc = Плата для сборки устройства «шкаф контроллеров KIAS».

ent-KiasControllerProgrammerBoard = плата: программатор KIAS
    .desc = Плата для сборки устройства «программатор KIAS».

kias-overview-counts = Объекты: { $devices } · Экипаж: { $crew }
kias-crew-detected = Обнаружено членов экипажа: { $crew }

kias-controller-disable = Отключить
kias-controller-enable = Включить

kias-controller-status-empty = Пустой слот
kias-group-name = Название группы

kias-mode-monitor = Мониторинг выходов
kias-monitor-title = Текущие выходы (обновляются раз в секунду)
kias-monitor-offline = Устройство отключено: текущие выходы недоступны.
kias-monitor-unset = ещё нет данных
kias-monitor-pulse = последний импульс: { $seconds } с назад
kias-monitor-no-outputs = У устройства нет выходных портов.

kias-room-number = Помещение { $number }
kias-rotary-positions = Количество положений
kias-rotary-signal = Выход { $number }
kias-rotary-position-1 = Сигнал положения 1
kias-rotary-position-2 = Сигнал положения 2
kias-rotary-position-3 = Сигнал положения 3
kias-rotary-position-4 = Сигнал положения 4
kias-relay-channel-changed = Канал реле: { $channel }
kias-relay-channel-highvoltage = ВВ
kias-relay-channel-mediumvoltage = СВ
kias-relay-channel-apc = НВ
kias-relay-channel-data = DATA
kias-relay-examine = Канал реле: { $channel }. Состояние: { $state }.
kias-relay-closed = замкнуто
kias-relay-opened = разомкнуто
kias-controller-template = Шаблон:
kias-speaker-link-help = Для связи с выключателем задайте сообщение здесь или в мультитуле. Пустой текст связи использует сообщение динамика.
kias-suppression-offline = Пожаротушение недоступно: проверьте питание и DATA.
kias-suppression-no-atmosphere = Нужен подключённый атмосферный сервер KIAS.
kias-suppression-testing = Пожаротушение отключено в режиме проверки.
kias-suppression-empty = Установите новый картридж пожаротушения.
kias-controller-mapping = Маппинг (замороженная карта)
kias-controller-page-previous = Назад
kias-controller-page-next = Далее

kias-crew-acknowledge = Экипаж на связи
kias-crew-response-request = Экипаж, подтвердите, что вы на связи, в консоли управления KIAS. При отсутствии ответа через { $seconds } секунд будет отправлен MAYDAY.
