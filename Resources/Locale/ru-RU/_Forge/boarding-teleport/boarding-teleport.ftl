boarding-teleport-window-title = Блюспейс-абордаж
boarding-teleport-window-status-header = Статус захвата
boarding-teleport-window-status-none = Цель не выбрана.
boarding-teleport-window-sector-help = Наведите курсор на шаттл или на карте сектора и щёлкните, чтобы назначить цель.
boarding-teleport-window-grid-help = Щёлкните по любой клетке на карте судна — точка высадки (в том числе стена или закрытая зона).
boarding-teleport-window-back = К карте сектора
boarding-teleport-window-flavor-sector = [color=#9ecbff]Скан сектора:[/color] найдите цель и зафиксируйте блюспейс-захват.
boarding-teleport-window-flavor-grid = [color=#ffd27f]Укажите точку высадки.[/color] Обратный прыжок — только с привязанного пульта на вашей платформе.
boarding-teleport-window-clear-target = Сбросить цель
boarding-teleport-window-platform-cooldown = Перезарядка платформы: {$seconds} с
boarding-teleport-window-engine-stats = Двигатель: дальность {$range} | скорость цели ≤ {$speed}
boarding-teleport-window-engine-missing = Блюспейс-двигатель на этом судне не найден
boarding-teleport-window-return-window = Окно возврата: {$seconds} с
boarding-teleport-window-return-remaining = До конца канала возврата: {$seconds} с
boarding-teleport-window-apc-risk = Недогруз СМ: +{$percent}% к риску
boarding-teleport-window-mode-sector = [color=#87b7ff]Режим:[/color] поиск и захват цели
boarding-teleport-window-mode-grid = [color=#ffd27f]Режим:[/color] точка высадки
boarding-teleport-window-mode-ready = [color=#8dff99]Режим:[/color] канал стабилен, платформы в строю
boarding-teleport-window-mode-summary-Stealth = [color=#6bb8ff]Режим:[/color] Скрытный — умеренный риск, сбалансированная скорость
boarding-teleport-window-mode-summary-Precise = [color=#9bff96]Режим:[/color] Точный — дольше заряд, минимальный разброс
boarding-teleport-window-mode-summary-Rapid = [color=#ffae63]Режим:[/color] Ускоренный — быстрый заряд, высокий риск
boarding-teleport-window-mode-button-Stealth = Скрытный
boarding-teleport-window-mode-button-Precise = Точный
boarding-teleport-window-mode-button-Rapid = Ускоренный
boarding-teleport-window-mode-stats = Задержка: {$delay} с | Разброс: {$scatter} | Риск: {$risk}%
boarding-teleport-sector-settings = Настройки
boarding-teleport-sector-scan = Сканировать сектор
boarding-teleport-sector-select = Выбрать цель для абордажа
boarding-teleport-sector-objects = Объекты в секторе
boarding-teleport-sector-selected-none = Цель: не выбрана
boarding-teleport-sector-selected-grid = Цель: {$name}
boarding-teleport-sector-tip = Сначала просканируйте сектор, затем выберите цель из списка.

boarding-teleport-status-None = Цель не выбрана.
boarding-teleport-status-TargetSelected = Судно выбрано. Укажите клетку высадки.
boarding-teleport-status-LandingSelected = Точка высадки задана. Платформы готовы.
boarding-teleport-status-InvalidTarget = Это судно нельзя выбрать целью.
boarding-teleport-status-TargetTooFar = Цель вне дальности блюспейс-захвата.
boarding-teleport-status-TargetMoving = Цель движется слишком быстро — захват нестабилен.
boarding-teleport-status-InvalidLanding = На эту клетку высадка невозможна (нет тайла или открытый космос).
boarding-teleport-status-NoGrid = Консоль должна стоять на судне.
boarding-teleport-status-NoEngine = На судне нет блюспейс-двигателя. Установите его на том же шаттле, что и консоль.
boarding-teleport-status-TargetShielded = У цели активны щиты — блюспейс-захват заблокирован.
boarding-teleport-status-TargetShieldTooStrong = Щиты цели сильнее класса двигателя. Усильте привод или дождитесь спада щитов.
boarding-teleport-status-SourceShieldBlocksTeleport = Щит вашего корабля блокирует исходящий блюспейс-абордаж.
boarding-teleport-status-TargetInFtl = Цель в FTL — захват высадки невозможен.
boarding-teleport-status-NoEnginePower = Блюспейс-двигатель обесточен.
boarding-teleport-status-EngineRecharging = Двигатель перезаряжается после скачка.
boarding-teleport-status-TargetScrambled = На целевом судне работает блюспейс-глушитель — захват заблокирован.
boarding-teleport-status-TargetFriendly = Нельзя абордировать своё судно или пристыкованный союзный шаттл.
boarding-teleport-status-TargetGridProtected = Этот грид защищён — телепортация на него запрещена.
boarding-teleport-status-LockExpired = Захват устарел. Заново подтвердите цель в консоли.

boarding-teleport-window-sync-volley = Запустить все готовые платформы
boarding-teleport-window-lock-age = Возраст захвата: {$seconds} с
boarding-teleport-window-lock-degrade = Дрейф захвата: +{$scatter} к разбросу, +{$risk}% к риску
boarding-teleport-window-platform-list-header = Связанные платформы
boarding-teleport-window-platform-entry = Платформа {$slot}: {$name} — {$cooldown} | Высадка: {$landing}
boarding-teleport-window-platform-entry-ready = готова
boarding-teleport-window-platform-entry-cooldown = перезарядка {$seconds} с
boarding-teleport-window-platform-entry-landing-yes = своя
boarding-teleport-window-platform-entry-landing-no = общая

boarding-teleport-console-volley-none = Ни одна платформа не готова к запуску.
boarding-teleport-console-volley-started =
    { $count ->
        [one] Синхронный залп: заряжается {$count} платформа.
        [few] Синхронный залп: заряжаются {$count} платформы.
       *[other] Синхронный залп: заряжаются {$count} платформ.
    }

boarding-teleport-platform-lock-broken = Блюспейс-захват сорван — цель недействительна!

boarding-teleport-remote-no-anchor = Канал возврата неактивен.
boarding-teleport-remote-return-remaining = Канал возврата: осталось {$seconds} с.
boarding-teleport-remote-emergency-available = Канал обрушился — доступен экстренный возврат (высокий риск).
boarding-teleport-remote-return-expired = Канал возврата утерян безвозвратно.

alerts-boarding-teleport-return-name = Канал возврата
alerts-boarding-teleport-return-desc = Сколько ещё можно вернуться на свою платформу.

boarding-teleport-platform-cooldown = Платформа ещё перезаряжается.
boarding-teleport-platform-pending = Катушка уже набирает заряд.
boarding-teleport-platform-departure-delay = Захват установлен. Скачок через {$seconds} с.
boarding-teleport-platform-unpowered = Платформа обесточена.
boarding-teleport-platform-not-on-platform = Встаньте на связанную платформу.
boarding-teleport-platform-no-console = Платформа не связана с консолью.
boarding-teleport-platform-no-target = На консоли не задана точка высадки.
boarding-teleport-platform-wrong-platform = Пульт привязан к другой платформе.
boarding-teleport-platform-destabilized = Блюспейс-дестабилизация! Пространственная когерентность нарушена.
boarding-teleport-platform-landing-invalid = Зона высадки больше не годится.
boarding-teleport-platform-home-invalid = Платформа возврата недоступна. Блюспейс-якорь рушится.
boarding-teleport-platform-return-expired = Канал возврата обрушился.
boarding-teleport-platform-charge-cancelled = Заряд прерван.

boarding-teleport-platform-return-started = Открывается канал возврата. Скачок через {$seconds} с.
boarding-teleport-platform-emergency-return-started = Экстренный возврат! Нестабильный скачок через {$seconds} с.

boarding-teleport-emergency-return-confirm-title = Экстренный блюспейс-возврат
boarding-teleport-emergency-return-confirm-button = Прыгнуть
boarding-teleport-emergency-return-confirm-message = [color=#ffd27f]Канал возврата обрушился.[/color] Можно один раз попытаться сорваться на свою платформу. [bullet/] Заряд: [color=#ffae63]{$seconds} с[/color] [bullet/] Риск дестабилизации: [color=#ff6868]{$risk}%[/color] [bullet/] Разброс высадки: [color=#ff6868]до {$scatter} клеток[/color] [bullet/] При сбое — [color=#ff6868]оглушение[/color] и смещение в пространстве. Повторно воспользоваться нельзя. Подтверждайте только если вы в ловушке.
boarding-teleport-emergency-return-cancelled = Экстренный возврат отменён.
boarding-teleport-emergency-return-pending = Сначала подтвердите или отмените окно экстренного возврата.
boarding-teleport-emergency-return-no-session = Сейчас нельзя открыть окно экстренного возврата.

boarding-teleport-early-return-confirm-title = Досрочный возврат
boarding-teleport-early-return-confirm-button = Вернуться сейчас
boarding-teleport-early-return-confirm-message = [color=#ffd27f]Канал возврата ещё не стабилизировался.[/color] Ранний прыжок опасен. [bullet/] До стабилизации: [color=#ffae63]{$remaining} с[/color] [bullet/] Риск [color=#ff6868]полного распада тела[/color]: [color=#ff6868]{$risk}%[/color] Чем дольше ждёте — тем безопаснее скачок.
boarding-teleport-platform-early-return-started = Досрочный возврат! Нестабильный скачок через {$seconds} с.
boarding-teleport-platform-early-return-swelling = Тело искажается и раздувается — нестабильная материя затапливает вас изнутри!
boarding-teleport-platform-early-return-catastrophe = Блюспейс-разрыв на платформе! Тело не смогло собраться обратно!

boarding-teleport-window-shared-landing-on = Общая точка высадки: вкл.
boarding-teleport-window-shared-landing-off = Своя точка на платформу

boarding-teleport-port-name = Телепорт
boarding-teleport-port-description = Активирует связанную платформу абордажа.

boarding-teleport-instructions = [head=2]Блюспейс-абордаж[/head]

    Кратко: консоль + двигатель + платформы + пульты на одном гриде → захват цели в консоли → высадка с платформы → возврат тем же пультом, пока открыт канал.

    Полное руководство и [bold]таблицы параметров по тирам[/bold] — в NT Guidebook: раздел Фронтир → [bold]Блюспейс-абордаж[/bold] ([textlink="обзор" link="BoardingTeleport"], [textlink="таблица" link="BoardingTeleportBalanceTable"]).

research-discipline-forge-boarding-teleport = Блюспейс-абордаж
research-discipline-forge-boarding-teleport-advanced = Продвинутый блюспейс-абордаж
research-discipline-forge-boarding-teleport-tier3 = Военный блюспейс-абордаж
research-discipline-forge-boarding-teleport-experimental = Экспериментальный блюспейс-абордаж
research-technology-forge-boarding-teleport-tier1 = Базовый комплект абордажа
research-technology-forge-boarding-teleport-tier1-desc = Открывает сборку базовых узлов блюспейс-абордажа после изучения суперкомпонентов.
research-technology-forge-boarding-teleport-tier2 = Улучшенный блюспейс-привод
research-technology-forge-boarding-teleport-tier3-base = Военный абордаж (в разработке)
research-technology-forge-boarding-teleport-tier4 = Фазовый привод T4
research-technology-forge-boarding-teleport-tier4-desc = Флатпаки экспериментального двигателя и платформы. Нужен диск T4 в сервере исследований и пройденный абордаж T2.

# Названия предметов и машин — ss14-ru/prototypes/_Forge/.../boarding_teleport.ftl и disks.ftl (ent-DisciplinesDiskBoardingTeleportTier4)
