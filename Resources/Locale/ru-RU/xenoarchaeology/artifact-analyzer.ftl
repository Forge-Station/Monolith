analysis-console-menu-title = Аналитическая консоль широкого спектра модель 3
analysis-console-server-list-button = Сервер
analysis-console-extract-button = Извлечь очки
analysis-console-info-no-scanner = Анализатор не подключён! Пожалуйста, подключите его с помощью мультитула.
analysis-console-info-no-artifact =
    Артефакт не найден!
    Поместите артефакт на платформу  для получения данных о узлах.
analysis-console-info-ready = Все системы запущены. Сканирование готово.
analysis-console-no-node = Выберите узел для просмотра
analysis-console-info-id = [font="Monospace" size=11]ID: [color=yellow]{ $id }[/color][/font]
analysis-console-info-depth = [font="Monospace" size=11]ГЛУБИНА: { $depth }[/font]
analysis-console-info-triggered-true = [font="Monospace" size=11]АКТИВИРОВАН: ДА[/font]
analysis-console-info-triggered-false = [font="Monospace" size=11]АКТИВИРОВАН: НЕТ[/font]
analysis-console-info-effect = [font="Monospace" size=11]Эффект: [color=gray]{ $effect }[/color][/font]
analysis-console-info-trigger = [font="Monospace" size=11]Стимуляторы: [color=gray]{ $trigger }[/color][/font]
analysis-console-info-edges = [font="Monospace" size=11]СВЯЗЕЙ: { $edges }[/font]
analysis-console-info-value = [font="Monospace" size=11]НЕИЗВЛЕЧЁННОЕ_ЗНАЧЕНИЕ: { $value }[/font]
artifact-analyzer-hint-prototype-fallback = (нет подсказки — { $id })
analysis-console-info-class = [font="Monospace" size=11]Класс:[/font]
analysis-console-info-class-value = [font="Monospace" size=11]{ $class }[/font]
analysis-console-info-locked = [font="Monospace" size=11]Статус:[/font]
analysis-console-info-locked-value = [font="Monospace" size=11][color={ $state ->
        [0] red]Заблокирован
        [1] lime]Разблокирован
       *[2] plum]Активен
    }[/color][/font]
analysis-console-info-durability = [font="Monospace" size=11]Прочность:[/font]
analysis-console-info-durability-value = [font="Monospace" size=11][color={ $color }]{ $current }/{ $max }[/color][/font]
analysis-console-info-scanner = Сканирование...
analysis-console-info-scanner-paused = Пауза.
analysis-console-progress-text =
    { $seconds ->
        [one] T-{ $seconds } секунда
        [few] T-{ $seconds } секунды
       *[other] T-{ $seconds } секунд
    }
analysis-console-extract-value = [font="Monospace" size=11][color=orange]Узел { $id } (+{ $value })[/color][/font]
analysis-console-extract-none = [font="Monospace" size=11][color=orange] У разблокированых узлов не осталось очков для извлечения [/color][/font]
analysis-console-extract-sum = [font="Monospace" size=11][color=orange]Всего изучено: { $value }[/color][/font]
analyzer-artifact-extract-popup = Поверхность артефакта мерцает энергией!

analysis-console-scan-button = Сканировать
analysis-console-scan-tooltip-info = Сканирует артефакт, чтобы получить сведения о его структуре.
analysis-console-print-button = Печать
analysis-console-print-tooltip-info = Печатает отчёт с текущими данными об артефакте.
analysis-console-extract-button-info = Извлекает очки из артефакта за вновь изученные узлы.
analysis-console-bias-up = Вверх
analysis-console-bias-down = Вниз
analysis-console-bias-button-info-up = Направляет переходы между узлами артефакта к нулевой глубине.
analysis-console-bias-button-info-down = Направляет переходы между узлами артефакта к большей глубине.
analysis-console-no-server-connected = Невозможно извлечь очки: сервер не подключён.
analysis-console-no-artifact-placed = На сканере нет артефакта.
analysis-console-no-points-to-extract = Нет очков для извлечения.
analyzer-artifact-component-upgrade-analysis = длительность анализа
analysis-console-print-popup = Консоль напечатала отчёт.
analysis-report-title = Отчёт об артефакте: узел {$id}
artifact-analyzer-upgrade-duration = Время сканирования
