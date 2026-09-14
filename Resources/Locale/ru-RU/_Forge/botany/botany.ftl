agro-scan-program-name = АгроСкан

plant-mutation-exude-gas-guidebook = Мутирует растение, чтобы оно выделяло газ. Чем реже газ, тем меньше шанс.
plant-mutation-consume-gas-guidebook = Мутирует растение, чтобы оно требовало газ, и подстраивает температуру/давление.
plant-mutation-chemicals-guidebook = Добавляет реагент в урожай. Уникальные фитохимикаты требуют дополнительных мутаций или условий выращивания.

hydroponics-console-title = Монитор гидропоники
hydroponics-console-subtitle = Сеть гидропонных лотков
hydroponics-console-os = HydroMonitor#OS ™ v1.0
hydroponics-console-no-trays = Нет подключённых лотков. Привяжите лотки сетевым конфигуратором.
hydroponics-console-tray-count = Подключённые лотки: {$count}
hydroponics-console-list-header = Лотки
hydroponics-console-list-empty = Нет устройств
hydroponics-console-list-empty-tray = пусто
hydroponics-console-detail-header = Данные лотка
hydroponics-console-detail-none = Выберите лоток из списка.
hydroponics-console-detail-empty = В этом лотке нет растения.
hydroponics-console-section-status = Состояние
hydroponics-console-section-warnings = Предупреждения
hydroponics-console-section-atmos = Атмосфера
hydroponics-console-section-genes = Генетика
hydroponics-console-field-status = Статус
hydroponics-console-field-health = Здоровье
hydroponics-console-field-age = Возраст
hydroponics-console-field-water = Вода
hydroponics-console-field-nutrition = Питание
hydroponics-console-field-toxins = Токсины
hydroponics-console-field-weeds = Сорняки
hydroponics-console-field-pests = Вредители
hydroponics-console-field-heat = Идеальная t°
hydroponics-console-field-pressure = Давление
hydroponics-console-field-consume = Потребляет
hydroponics-console-field-exude = Выделяет
hydroponics-console-field-chems = Реагенты
hydroponics-console-field-mutations = Мутации
hydroponics-console-warnings-none = Среда стабильна
hydroponics-console-tray-header = {$tray} [{$address}] — {$plant}
hydroponics-console-tray-empty = {$tray} [{$address}] — пусто
hydroponics-console-status-growing = Растёт
hydroponics-console-status-harvest = Урожай
hydroponics-console-status-dead = Мертво
hydroponics-console-tray-stats = {$status} | HP {$health}/{$endurance} | Возраст {$age} | Вода {$water} | Питание {$nutrition} | Токсины {$toxins} | Сорняки {$weeds} | Вредители {$pests}
hydroponics-console-warnings = Предупреждения: {$warnings}
hydroponics-console-warn-heat = температура
hydroponics-console-warn-pressure = давление
hydroponics-console-warn-light = свет
hydroponics-console-warn-gas = нет газа
hydroponics-console-warn-radiation = радиация
hydroponics-console-warn-grab = хищное
hydroponics-console-atmos = Атмос: {$heat}K ±{$heatTol} | {$lowP}-{$highP} кПа | потребляет {$consume} | выделяет {$exude}
hydroponics-console-genes = Реагенты: {$chems} | Мутации: {$mutations}

plant-grab-caught = Лозы {$plant} хватают вас!
plant-grab-damage = Лозы сжимаются сильнее!
plant-grab-break-free = Вы вырываетесь из лоз.

plant-holder-component-radioactive-warning = [color=yellow]Растение излучает ионизирующую радиацию.[/color]
plant-holder-component-grabber-warning = [color=red]Растение готово схватить любого, кто подойдёт слишком близко.[/color]

ent-ActionPlantGrabBreakFree = Вырваться
    .desc = Вырваться из лоз растения.

device-address-prefix-hydroponics = ГИД-
device-address-prefix-hydroponics-console = ГКН-

reagent-name-phyto-aether = фитоэфир
reagent-desc-phyto-aether = Редкий фитохимикат высокопотентных растений. Слабый стимулятор, токсичен при передозировке, слегка мутагенен для других растений.
reagent-name-lignin-resin = лигниновая смола
reagent-desc-lignin-resin = Густой сок древесных растений. Медленно сращивает плоть, но сковывает суставы.
reagent-name-cryo-chlorophyll = криохлорофилл
reagent-desc-cryo-chlorophyll = Холодоадаптированный хлорофилл растений из холодных лотков. Охлаждает тело.
reagent-name-photozyme = фотозим
reagent-desc-photozyme = Светофиксирующий фермент светолюбивых культур. Сытный и отличное удобрение.
reagent-name-necrotoxin-sap = некротоксиновый сок
reagent-desc-necrotoxin-sap = Сок кудзу-мутации, разъедающий живую ткань. Работать в перчатках.
reagent-name-radbloom = экстракт радиоцвета
reagent-desc-radbloom = Дистиллят радиоактивной флоры. Малые дозы лечат радиацию, большие — вызывают её.
reagent-name-vine-ichor = лозовая лимфа
reagent-desc-vine-ichor = Лимфа хищных лоз. Затягивает порезы, но душит лёгкие.
reagent-name-atmo-phyte = атмофит
reagent-desc-atmo-phyte = Газоперерабатывающий фитохимикат атмосферных мутаций. Помогает дышать.

necrotoxin-sap-warning = Вены жжёт растительный токсин!

ent-ClothingEyesGlassesBotany = ботанические очки анализатора
    .desc = Зелёные очки: полоска HP и квадраты статуса на лотках — синий вода, зелёный сорняки, жёлтый урожай, золотой радиация.
ent-AgroScanCartridge = картридж АгроСкан
    .desc = Программа, позволяющая КПК сканировать семена и лотки как анализатор растений.
ent-ComputerHydroponics = консоль гидролотков
    .desc = Список лотков с фильтрами урожай / вода / радиация / хищное и журналом сортов.
ent-HydroponicsConsoleCircuitboard = плата консоли гидролотков
    .desc = Печатная плата компьютера для консоли гидролотков.
ent-BotanyCultivarDisk = дискета сорта
    .desc = Хранит именованную линию растения для журнала консоли гидропоники.

hydroponics-console-alerts = Урожай {$harvest} · Вода {$water} · Рад {$rad} · Хищное {$grab}
hydroponics-console-filter-all = Все
hydroponics-console-filter-harvest = Урожай
hydroponics-console-filter-water = Мало воды
hydroponics-console-filter-rad = Радиация
hydroponics-console-filter-grab = Хищное
hydroponics-console-view-trays = Лотки
hydroponics-console-view-journal = Журнал ({$count})
hydroponics-console-journal-header = Сорта
hydroponics-console-journal-empty = Нет сохранённых линий. Выберите лоток и нажмите «Сохранить линию».
hydroponics-console-journal-none = Выберите сохранённую линию.
hydroponics-console-journal-detail = Линия сорта
hydroponics-console-journal-name-placeholder = Имя линии
hydroponics-console-save-line = Сохранить линию
hydroponics-console-rename-line = Переименовать
hydroponics-console-print-packet = Печать пакета
hydroponics-console-eject-disk = Дискета
hydroponics-console-delete-line = Удалить
hydroponics-console-cycle-light = День/тень
hydroponics-console-field-light-mode = Свет
hydroponics-console-field-ideal-light = Идеальный свет
hydroponics-console-save-ok = Сохранена линия «{$name}».
hydroponics-console-save-empty = В лотке нет растения.
hydroponics-console-journal-full = Журнал полон (24 линии). Удалите одну.
hydroponics-console-print-packet-ok = Напечатан пакет семян «{$name}». Осталось печатей: {$remaining}.
hydroponics-console-print-limit = Для этой линии исчерпан лимит в 5 печатей семян.
hydroponics-console-print-remaining = Печать семян
hydroponics-console-print-remaining-value = использовано {$used} / 5 (осталось {$remaining})
hydroponics-console-save-already-archived = Данные этого растения уже сохранены в журнал.
hydroponics-console-save-printed-seeds = Напечатанные семена нельзя добавить в журнал — для обмена генами используйте палочку с пыльцой.
hydroponics-console-save-unavailable = Это растение нельзя сохранить в журнал.
hydroponics-console-eject-disk-ok = «{$name}» записана на дискету.
hydroponics-console-disk-blank = Дискета пустая.
hydroponics-console-disk-imported = Импортирована линия «{$name}».

botany-cultivar-disk-blank = Дискета пустая.
botany-cultivar-disk-contents = Линия: [color=lightgreen]{$name}[/color] ({$plant})
botany-cultivar-disk-prints = Печать семян: {$used} / 5 (осталось {$remaining})

plant-holder-light-ambient = обычный
plant-holder-light-day = день
plant-holder-light-shade = тень
plant-holder-light-verb = Свет: {$mode}
plant-holder-light-mode-set = Режим света лотка: {$mode}.
plant-holder-light-mode-examine = Режим света: [color=lightgreen]{$mode}[/color]

plant-mutation-add-chemical-guidebook = Добавляет {$reagent} в урожай.
plant-mutation-add-consume-gas-guidebook = Растение начинает потреблять {$gas}.
plant-mutation-add-exude-gas-guidebook = Растение начинает выделять {$gas}.
reagent-effect-guidebook-plant-lock-genes =
    { $chance ->
        [1] Закрепляет
        *[other] закрепляют
    } гены растения: мутаген больше не крутит эту линию

reagent-name-gene-stabilizer = стабилизатор генов
reagent-desc-gene-stabilizer = Ботанический фиксатив. Пшикни на живой куст — геном линии закрепится, нестабильный мутаген её больше не крутит.

ent-GeneStabilizerChemistryBottle = бутылочка стабилизатора генов
    .desc = Закрепляет гены растения: мутаген больше не крутит эту линию.
ent-AloeCream1 = крем из алоэ
    .desc = Мазь от ожогов.
    .suffix = Одна
ent-BotanyDriedProduce = сушёный урожай
    .desc = Высушенная зелень. Цвет и вкус как у сорта, с которого сняли.

plant-holder-harvest-container-slot = ёмкость для урожая
plant-holder-harvest-container-filled = Урожай налит в ёмкость: {$name}.
plant-holder-harvest-container-examine = Ёмкость для урожая: [color=lightgreen]{$container}[/color]
plant-holder-harvest-container-empty-examine = [color=gray]Ёмкость не вставлена — вставьте мензурку или стакан для сбора сока.[/color]
plant-holder-harvest-container-missing = В лотке нет ёмкости — сок не собран.
plant-holder-harvest-container-full = Ёмкость полная — сок не собран.

hydroponics-console-rename-tray = Переименовать лоток
hydroponics-console-tray-name-placeholder = пшеница-рад-3
hydroponics-console-rename-tray-ok = Лоток переименован: «{$name}».
hydroponics-console-warn-pest-eater = санитар
hydroponics-console-warn-locked = гены закреплены

plant-holder-component-pest-eater-warning = [color=lightgreen]Растение жрёт сорняки и жуков в лотке.[/color]
plant-holder-component-gene-locked-warning = [color=cyan]Закреплённые гены: {$traits}. Мутаген эту линию не крутит.[/color]
plant-analyzer-mutation-pest-eater = Санитар
plant-analyzer-mutation-gene-locked = Гены закреплены

hydroponics-console-warn-locked-detail = закреплены: {$traits}

plant-mutation-name-change-water-consumption = Изменение потребления воды
plant-mutation-name-change-nutrient-consumption = Изменение потребления питательных веществ
plant-mutation-name-change-ideal-heat = Изменение идеальной температуры
plant-mutation-name-change-heat-tolerance = Изменение термостойкости
plant-mutation-name-change-toxins-tolerance = Изменение токсиностойкости
plant-mutation-name-change-low-pressure-tolerance = Изменение низкого давления
plant-mutation-name-change-high-pressure-tolerance = Изменение высокого давления
plant-mutation-name-change-pest-tolerance = Изменение устойчивости к вредителям
plant-mutation-name-change-weed-tolerance = Изменение устойчивости к сорнякам
plant-mutation-name-change-endurance = Изменение выносливости
plant-mutation-name-change-yield = Изменение урожайности
plant-mutation-name-change-lifespan = Изменение продолжительности жизни
plant-mutation-name-change-maturation = Изменение созревания
plant-mutation-name-change-production = Изменение производства
plant-mutation-name-change-potency = Изменение крепости
plant-mutation-name-change-ideal-light = Изменение идеального света
plant-mutation-name-change-chemicals = Изменение химикатов
plant-mutation-name-change-exude-gasses = Изменение выделяемых газов
plant-mutation-name-change-consume-gasses = Изменение потребляемых газов
plant-mutation-name-change-harvest = Изменение сбора урожая
plant-mutation-name-change-species = Смена вида
plant-mutation-name-carbon-filter = Углеродный фильтр
plant-mutation-name-antirad-fruit = Антирад-плод
plant-mutation-name-drought-tolerant = Засухоустойчивость
plant-mutation-name-nitrogen-fixer = Азотофиксатор
plant-mutation-name-shade-adapted = Тенелюбивость
plant-mutation-name-sun-lover = Солнцелюб
plant-mutation-name-perennial = Многолетник
plant-mutation-name-pest-ward = Защита от вредителей
plant-mutation-name-thin-air = Разреженный воздух
plant-mutation-name-oxygen-bloom = Кислородный цвет
plant-mutation-name-aloe-sap = Алоэ-сок
plant-mutation-name-bitter-antidote = Горький антидот

botany-swab-from-packet = Вы снимаете пыльцу с пакета семян.
botany-swab-graft-packet = Пыльца привита: мутации перешли на пакет {$name}.

seed-component-gene-locked = [color=cyan]Закреплённые гены: {$traits}. Мутаген эту линию не крутит.[/color]
seed-component-cultivar-journal-locked = [color=gray]Напечатанная линия — нельзя добавить в журнал гидропоники.[/color]

botany-plant-cloth-name = волокно ({$name})
botany-plant-juice-name = сок ({$name})
botany-plant-dried-name = сушёный {$name}
botany-aloe-tea-name = чай из алоэ ({$name})
botany-antirad-tea-name = антирад-чай ({$name})
