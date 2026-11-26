## The contract board, its messages and the Contracts PDA app.

wf-contract-board-title = Менеджер контрактов
wf-contract-board-name = доска контрактов
wf-contract-program-name = Контракты
wf-contract-station-unknown = неизвестная станция
wf-contract-designation-unknown = (не подтверждено)
wf-contract-thalers = {$amount} {$amount ->
        [one] талер
        [few] талера
       *[other] талеров
    }

wf-contract-rank-hostile = Враждебность
wf-contract-rank-distrusted = Недоверие
wf-contract-rank-applicant = Соискатель
wf-contract-rank-junior = Младший подрядчик
wf-contract-rank-member = Подрядчик
wf-contract-rank-experienced = Опытный подрядчик
wf-contract-rank-senior = Старший подрядчик

wf-contract-category-all = Все заказы
wf-contract-category-delivery = Доставка
wf-contract-category-hauling = Грузоперевозки
wf-contract-category-supply = Снабжение
wf-contract-category-mining = Шахтёрство
wf-contract-category-bounty = Охота за головами
wf-contract-category-salvage = Утилизация
wf-contract-category-escort = Сопровождение
wf-contract-category-science = Наука
wf-contract-category-planetary = Планетарные

wf-contract-tier-short = Ур. {$tier}
wf-contract-tier = Уровень риска {$tier}

wf-contract-status-offered = Предложен
wf-contract-status-active = Выполняется
wf-contract-status-completed = Выполнен
wf-contract-status-failed = Провален
wf-contract-status-abandoned = Отменён
wf-contract-status-void = Отозван

wf-contract-time-left = Осталось {$time}
wf-contract-time-allowed = Лимит: {$minutes} мин

## Board window

wf-contract-tab-offers = Заказы ({$count})
wf-contract-tab-accepted = Взятые ({$count}/{$max})
wf-contract-tab-history = История
wf-contract-tab-standing = Репутация
wf-contract-rail-entry = {$name} ({$count})
wf-contract-board-slots = {$crew ->
    [1] В одиночку: {$slots} слот контракта. Экипаж из {$next} чел. вместит больше.
   *[other] Экипаж из {$crew} чел.: {$slots} общих слотов контрактов. Экипаж из {$next} чел. вместит на один больше.
} Отслеживайте их в приложении «Контракты» на вашем КПК.
wf-contract-board-no-character = Контракты оформляются на персонажа. Призраки могут только просматривать доску.
wf-contract-board-owed = Причитающиеся вам {$pay} {$pay ->
        [one] талер
        [few] талера
       *[other] талеров
    } будут выплачены, как только ваш счёт сможет их принять.

wf-contract-empty-offers = В этой категории сейчас нет заказов. Наниматели обновляют список каждые несколько минут.
wf-contract-empty-planetary = Планетарные операции откроются после выдачи лицензий на разведку поверхности. Загляните позже.
wf-contract-empty-accepted = У вас нет активных контрактов. Возьмите заказ на любой доске контрактов.
wf-contract-empty-history = В эту смену ещё ничего не завершено.

wf-contract-details-none = Выберите контракт.
wf-contract-details-title = [font size=14][bold]{$title}[/bold][/font]
wf-contract-details-objectives = ЗАДАЧИ
wf-contract-details-objective = {$done ->
    [true] [color=#7fd18b]+[/color] [color=#8b98a5]{$text}[/color]
   *[false] [color=#5fc3e4]>[/color] {$text}
}
wf-contract-details-location = МЕСТОПОЛОЖЕНИЕ
wf-contract-details-coordinates = Координаты: {$x}, {$y} (введите их на панели путевых точек за штурвалом)
wf-contract-details-terms = УСЛОВИЯ
wf-contract-details-reward = Награда
wf-contract-details-deposit = Залог
wf-contract-details-deposit-none = Нет
wf-contract-details-time = Лимит времени
wf-contract-details-deadline = Осталось времени
wf-contract-details-offer-ends = Предложение истекает через

wf-contract-action-accept = {$deposit ->
    [0] Принять контракт
   *[other] Принять контракт (внести залог {$deposit})
}
wf-contract-action-abandon = Отказаться от контракта
wf-contract-action-abandon-confirm = {$deposit ->
    [0] Нажмите ещё раз для отказа
   *[other] Нажмите ещё раз для отказа с потерей залога ({$deposit})
}

wf-contract-standing-help = Репутация сохраняется между сменами. Она растёт с каждым выполненным контрактом и падает при провале, отказе, работе против интересов нанимателя или атаке цели по чужому контракту. Каждую смену она немного смещается обратно к нейтральной. Для 2-го уровня нужен ранг «Соискатель», для 3-го и выше — ещё больше; при «Недоверии» доступен только 1-й уровень, а наниматели со статусом «Враждебность» не станут иметь с вами дел.
wf-contract-standing-absent = {$description} (отсутствует в секторе в эту смену)
wf-contract-standing-progress = {$points} / {$next} до следующего ранга
wf-contract-standing-max = {$points}: максимальный ранг

wf-contract-pda-hint = На досках доступно заказов: {$offers}. {$crew ->
    [1] В одиночку: {$slots} слот.
   *[other] Экипаж из {$crew} чел.: {$slots} общих слотов.
}

## Why an offer cannot be taken

wf-contract-locked-no-character = Чтобы брать контракты, нужно играть за персонажа.
wf-contract-locked-rank = Требуется ранг «{$rank}» у этого нанимателя.
wf-contract-locked-full = Ваш экипаж уже взял {$max ->
    [one] свой единственный контракт. Более крупный экипаж сможет взять больше.
   *[other] все {$max} доступных контракта. Более крупный экипаж сможет взять больше.
}
wf-contract-locked-requires = Необходимо иметь при себе: {$items}.
wf-contract-locked-crew = Требуется экипаж из {$needed} чел. в секторе; в вашем сейчас {$size}. Соберите команду в меню настройки экипажа.
wf-contract-locked-company = {$contractor} работает только со своими людьми.
wf-contract-locked-hostile = {$contractor} не станет иметь с вами дел. Ваша репутация у них — «Враждебность».
wf-contract-error-gone = Кто-то другой уже забрал этот контракт.
wf-contract-error-deposit = На вашем счёте недостаточно средств для залога в {$deposit} {$deposit ->
        [one] талер
        [few] талера
       *[other] талеров
    }.
wf-contract-error-start = Контракт сорвался ещё до начала. Ваш залог возвращён.

## Messages to the holder

wf-contract-message-wrap = [color=#9fd3e0][bold]Контракты:[/bold] {$message}[/color]
wf-contract-accepted = Принят контракт «{$title}». У вас есть {$deadline} мин. Отслеживайте его в приложении «Контракты» на вашем КПК.
wf-contract-abandoned = Вы отказались от контракта «{$title}». Залог удержан.
wf-contract-completed = Контракт «{$title}» выполнен. На ваш счёт зачислено {$pay} {$pay ->
        [one] талер
        [few] талера
       *[other] талеров
    }{$crew ->
        .
       *[other] — ваша доля от общей выплаты экипажу.
    }
wf-contract-completed-owed = Контракт «{$title}» выполнен. Причитающиеся вам {$pay} {$pay ->
        [one] талер будет выплачен
        [few] талера будут выплачены
       *[other] талеров будут выплачены
    }, как только ваш счёт сможет принять перевод.
wf-contract-failed = Контракт «{$title}» провален: {$reason} Залог удержан.
wf-contract-void = Контракт «{$title}» отозван: {$reason} {$deposit ->
    [0] {""}
   *[other] Ваш залог в {$deposit} {$deposit ->
        [one] талер возвращён
        [few] талера возвращены
       *[other] талеров возвращены
    }.
}
wf-contract-owed-paid = Задолженность по завершённым контрактам в размере {$pay} {$pay ->
        [one] талера зачислена
        [few] талеров зачислены
       *[other] талеров зачислены
    } на ваш счёт.
wf-contract-package-delivered = «{$title}»: доставлено {$done} из {$count}.
wf-contract-anomaly-surveyed = «{$title}»: просканировано аномалий — {$done} из {$count}.

wf-contract-reason-expired = время вышло.
wf-contract-reason-lost = груз был уничтожен или утерян.
wf-contract-reason-escaped = цель ушла.
wf-contract-reason-target-gone = цель больше не находится в секторе.
wf-contract-reason-escort-lost = сопровождаемое судно было потеряно.
wf-contract-reason-anomaly-collapsed = аномалия схлопнулась до завершения сканирования.
wf-contract-reason-hull-lost = корпус был уничтожен.

wf-contract-interfered = Вы открыли огонь по цели, закреплённой за {$claimant} по контракту от {$contractor}. Ваша репутация у {$contractor} снизилась на {$points}.

## Contract cargo

wf-contract-package-name = {$name} для {$destination}
wf-contract-crate-name = контрактный ящик №{$id} для {$destination}
wf-contract-crate-void-name = пустой контрактный ящик
wf-contract-wreck-name = Обломки (контракт №{$id})
wf-contract-item-examine = [color=#9fd3e0]Контракт №{$id}, «{$title}», исполнитель: {$holder}. Доставить на {$destination}.[/color]
wf-contract-crate-line = {$done ->
    [true] [color=#7fd18b]{$name}: {$have}/{$count}[/color]
   *[false] {$name}: {$have}/{$count}
}
wf-contract-crate-missing-line = {$count} {$name}
wf-contract-crate-short = Недостача по накладной: {$missing}.

## Where to go, and what to do

wf-contract-location-courier = Заберите на {$origin}, доставьте на {$destination} ({$km} км).
wf-contract-location-supply = Заберите ящик по накладной у доски и доставьте его закрытым на {$destination} ({$km} км).
wf-contract-location-bounty = {$active ->
    [true] Последний раз цель видели примерно в {$km} км от {$home}. Она отмечена на радаре сектора.
   *[false] Цель замечена примерно в {$km} км от {$home}. Точные координаты — после принятия.
}
wf-contract-location-recovery = {$active ->
    [true] Обломки примерно в {$km} км от {$home}. Доставьте находку на {$destination}.
   *[false] Обломки обнаружены примерно в {$km} км от {$home}. Точные координаты — после принятия; доставьте находку на {$destination}.
}

wf-contract-objective-courier = Доставить груз на {$destination} ({$done}/{$count})
wf-contract-objective-goods = {$active ->
    [true] Упаковать {$name} ({$have}/{$count})
   *[false] Упаковать {$count} {$name}
}
wf-contract-objective-crate = Закрыть ящик на борту {$destination}
wf-contract-objective-bounty = Уничтожить: {$target}
wf-contract-objective-recover = Забрать с обломков: {$item}
wf-contract-objective-return = Доставить {$item} на {$destination}
wf-contract-location-survey = {$active ->
    [true] Заброшенное судно примерно в {$km} км от {$home}. На борту возможны враги.
   *[false] Заброшенное судно обнаружено примерно в {$km} км от {$home}. Точные координаты — после принятия.
}
wf-contract-location-tow = {$active ->
    [true] Заброшенное судно примерно в {$km} км от {$home}. Отбуксируйте его в радиус {$range} м от {$home}.
   *[false] Заброшенное судно обнаружено примерно в {$km} км от {$home}. Точные координаты — после принятия.
}
wf-contract-objective-tow = {$gap ->
    [-1] Отбуксировать заброшенное судно в радиус {$range} м от {$destination}
   *[other] Отбуксировать заброшенное судно в радиус {$range} м от {$destination} (осталось {$gap} м)
}
wf-contract-objective-survey = Просканировать все аномалии на борту сканером аномалий ({$done}/{$count})
wf-contract-objective-escort-meet = Встретить {$target} в точке рандеву
wf-contract-objective-escort = Безопасно сопроводить {$target} до {$destination}
wf-contract-location-escort = {$active ->
    [true] Точка рандеву примерно в 1–2 км от {$home}; сопровождение до {$destination} ({$km} км). Судно ждёт вас там.
   *[false] Точка рандеву у {$home}, сопровождение до {$destination} ({$km} км). Точные координаты — после принятия.
}

wf-contract-claimed-name = {$name} [взято: {$claimant}]
wf-contract-marker-name = Контракт №{$id}
wf-contract-row-holder = исполнитель: {$holder}
wf-contract-harms = -{$points} репутации у {$contractor}
wf-contract-credits = {$amount} {$credit}
wf-contract-details-bonus = Бонус
wf-contract-details-holder = Исполнитель
wf-contract-details-harms = Репутация
wf-contract-employer = Работа на {$contractor}, в интересах {$employer}.
wf-contract-details-requires = Взять с собой
wf-contract-requires-rope = моток прочного троса или буксировочный кабель
wf-contract-requires-tether-installer = установщик привязи
wf-contract-row-crew = экипаж от {$crew} чел.
wf-contract-details-crew = Требуется
wf-contract-details-crew-bonus = Бонус за экипаж
wf-contract-details-share = Доля экипажа
wf-contract-crew-bonus = +{$percent}% за каждого члена экипажа сверх {$past}, но не более +{$most}%
wf-contract-share = {$pool} талеров на экипаж из {$size} чел., по {$each} каждому

wf-contract-goods-entry = {$count} {$name}

## Admin commands

cmd-contractpost-desc = Размещает заказ на доске контрактов по шаблону контракта.
cmd-contractpost-help = Использование: contractpost <wfContract id>
cmd-contractpost-hint = ID прототипа wfContract
cmd-contractpost-invalid = Ожидался один ID прототипа wfContract.
cmd-contractpost-failed = Не удалось разместить {$template}: отсутствует доска, станция нанимателя не в раунде или нет подходящего пункта назначения.
cmd-contractpost-done = Размещён заказ №{$id} «{$title}» с наградой {$reward} {$reward ->
        [one] талер
        [few] талера
       *[other] талеров
    }.
cmd-contractlist-desc = Выводит список всех предложений и активных контрактов на доске.
cmd-contractlist-help = Использование: contractlist
cmd-contractlist-none = На доске контрактов пусто.
cmd-contractlist-entry = №{$id} [{$status}] {$title}: {$reward} {$reward ->
        [one] талер
        [few] талера
       *[other] талеров
    }, исполнитель: {$holder}

## Guidebook

guide-entry-wf-contracts = Контракты

## The second set of objectives.

wf-contract-location-mail = Заберите посылки у доски; доставьте каждую на свою станцию (остановок: {$count}, около {$km} км).
wf-contract-objective-mail = Доставить посылку для {$destination}
wf-contract-location-sweep = {$active ->
    [true] {$count} точек от {$home}, до {$km} км. Следующая отмечена в ваших путевых точках.
   *[false] {$count} точек от {$home}, до {$km} км. Координаты — после принятия.
}
wf-contract-objective-sweep-count = Зафиксировать {$count} точек в открытом космосе
wf-contract-objective-sweep = Зафиксировать точку по координатам {$x}, {$y}
wf-contract-location-derelict = {$active ->
    [true] Заброшенное судно примерно в {$km} км от {$home}.
   *[false] Заброшенное судно обнаружено примерно в {$km} км от {$home}. Точные координаты — после принятия.
}
wf-contract-objective-clear = Уничтожить все гнёзда на борту ({$done}/{$count})
wf-contract-objective-repair-weld = Заварить маяк
wf-contract-objective-repair-cell = Вставить батарею
wf-contract-objective-download = Находиться у терминала до завершения загрузки ({$percent}%, всего {$minutes} мин)
wf-contract-objective-inspect = Найти подозрительный грузовой ящик и осмотреть его

wf-contract-point-logged = «{$title}»: зафиксировано точек — {$done} из {$count}.
wf-contract-download-started = «{$title}»: загрузка начата. Оставайтесь у терминала — она остановится, если рядом никого не будет.
wf-contract-follow-up = Новая зацепка: заказ «{$title}» зарезервирован на доске для вашего экипажа на ближайшие {$minutes} мин.
wf-contract-inspect-verb = Осмотреть груз
wf-contract-inspect-done = Вы сверяете содержимое ящика с накладной и фиксируете, что там находится на самом деле.
wf-contract-reason-device-lost = цель на борту заброшенного судна была уничтожена.
