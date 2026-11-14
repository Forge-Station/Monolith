wf-defcon-sender = Командование обороны сектора
wf-defcon-title = DEFCON {$level}: {$name}
wf-defcon-chip-title = DEFCON {$level}
wf-defcon-pda = DEFCON: [color={$color}]{$level}[/color]
wf-defcon-body-rising = Сектор переведён на DEFCON {$level}, {$name}.
wf-defcon-body-falling = Сектор снижен до DEFCON {$level}, {$name}.
wf-defcon-body-reasons = Зафиксированные угрозы:
wf-defcon-body-reason = - {$reason}

wf-defcon-name-5 = FADE OUT
wf-defcon-name-4 = DOUBLE TAKE
wf-defcon-name-3 = ROUND HOUSE
wf-defcon-name-2 = FAST PACE
wf-defcon-name-1 = COCKED PISTOL

wf-defcon-desc-5 = Известных угроз нет. Обычное движение.
wf-defcon-desc-4 = Сообщено о враждебной активности. Держите наблюдение.
wf-defcon-desc-3 = Враждебная активность не стихает. Вооружитесь и летайте группой.
wf-defcon-desc-2 = Сектор под ударом. Гражданским держаться станций.
wf-defcon-desc-1 = Сектор захвачен. Всем экипажам — по боевым постам.

wf-defcon-reason-ads = Вторжение ADS, ступень {$tier}
wf-defcon-reason-reaver = Кампания Жнецов, ступень {$tier}
wf-defcon-reason-war = Открытая война TSF и PDV
wf-defcon-reason-threats = {$count ->
    [one] Один враждебный контакт в секторе
    [few] {$count} враждебных контакта в секторе
   *[other] {$count} враждебных контактов в секторе
}
wf-defcon-reason-distress = {$count ->
    [one] Один открытый сигнал бедствия
    [few] {$count} открытых сигнала бедствия
   *[other] {$count} открытых сигналов бедствия
}
wf-defcon-reason-deaths = {$count ->
    [one] Одна смерть за последние минуты
    [few] {$count} смерти за последние минуты
   *[other] {$count} смертей за последние минуты
}
wf-defcon-reason-bias = Оценка командования сектора

cmd-wf_defcon-desc = Показывает и ведёт DEFCON сектора.
cmd-wf_defcon-help = Использование: {$command} status | set <1-5> | lock <1-5> | unlock | bias <score> | eval
    set сразу ставит уровень и снова отдаёт его угрозе; lock держит уровень до unlock; bias добавляет очки поверх источников; eval считает угрозу сейчас.
cmd-wf_defcon-hint-sub = Подкоманда
cmd-wf_defcon-hint-level = Уровень, от 1 (самый тяжёлый) до 5
cmd-wf_defcon-hint-bias = Добавляемые очки
cmd-wf_defcon-bad-level = Уровень должен быть от 1 до 5.
cmd-wf_defcon-bad-bias = Смещение должно быть числом.
cmd-wf_defcon-set = DEFCON установлен на {$level}.
cmd-wf_defcon-locked = DEFCON зафиксирован на {$level}.
cmd-wf_defcon-unlocked = DEFCON снова следует за угрозой.
cmd-wf_defcon-biased = Смещение DEFCON установлено на {$bias}.
cmd-wf_defcon-status = DEFCON {$level} ({$name}), счёт {$score}, пол {$floor}{$locked ->
    [true] , зафиксирован
   *[false] {""}
}
cmd-wf_defcon-status-line = {"  "}{$score}: {$reason}

cmd-wf_defconpanel-desc = Открывает панель DEFCON сектора.
cmd-wf_defconpanel-help = Использование: {$command}

wf-defcon-panel-button = DEFCON сектора
wf-defcon-panel-title = DEFCON сектора
wf-defcon-panel-level = [font size=20][color={$color}]DEFCON {$level}: {$name}[/color][/font]
wf-defcon-panel-status = Счёт {$score}, пол DEFCON {$floor}{$locked ->
    [true] , зафиксирован
   *[false] {""}
}
wf-defcon-panel-disabled = DEFCON выключен (wf.defcon.enabled).
wf-defcon-panel-set = Задать уровень
wf-defcon-panel-level-button = {$level}
wf-defcon-panel-lock = Зафиксировать
wf-defcon-panel-lock-tooltip = Держать уровень, пока его не снимут, независимо от угрозы.
wf-defcon-panel-announce = Объявить
wf-defcon-panel-unlock = Снять фиксацию
wf-defcon-panel-threat = Угроза
wf-defcon-panel-bias = Смещение
wf-defcon-panel-bias-tooltip = Очки поверх источников. Отрицательное значение успокаивает сектор.
wf-defcon-panel-bias-apply = Применить
wf-defcon-panel-evaluate = Посчитать сейчас
wf-defcon-panel-no-threats = Угроз не зафиксировано.
wf-defcon-panel-reason = {$score}: {$reason}
