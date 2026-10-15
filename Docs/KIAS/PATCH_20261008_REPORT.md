# Отчёт о патче KIAS от 2026-10-08

## Окружение

Ветка до и после изменений: `KIAS`, HEAD `e32aecc197fed90b5947354bbad7b2c24add7911`. Windows, .NET SDK 10.0.101. Коммиты и push не выполнялись. Исходные пользовательские правки README и документов 00–09, новые документы 10–12/COPYPASTE_PROMPT и три изображения references сохранены.

Первый проход выполнен примерно с 21:49 до 22:33 по Asia/Novosibirsk; затем по уточнению пользователя исправлен T06. Затраты токенов не измерялись. Debug и Release проверены отдельно. Исходный baseline не завершился: старый сервер держал DLL; после сообщения пользователя об остановке выполнена обычная пересборка.

## Результаты T01–T10

| TODO | Изменённые файлы и причина | Доказательство | Статус |
| --- | --- | --- | --- |
| T01 Компактные ID | KiasControllerWindow, KiasControllerUi, KiasControllerUiSystem: полный ID занимал начало строки. Сохраняемые 12 символов и NetEntity не менялись; префикс учитывает также отключённые закреплённые устройства грида | NativeWindowsArrangeWithEmptyAndPopulatedGraphs: два ID с общей частью, имя первым, полный tooltip | PASS автоматический; игровой вид не проверен |
| T02 Иерархия | KiasControllerWindow: жирный раздел, стрелка рядом с названием, дополнительные отступы устройств, сохранение сворачивания при поиске | Нативный layout и существующие тесты группировки/редактора | PASS автоматический; визуальная оценка не проверена |
| T03 Палитра | KiasControllerWindow: ширина 215–310 по доступному месту; вторичная строка переносится. Сохранены инспектор и холст при 850×500 | NativeWindowsArrangeWithEmptyAndPopulatedGraphs, InspectorCapabilitiesSummariesAndLocalWindowsStayReadable; ширины 850/1200/1600 | PASS автоматический |
| T04 Шаблон | KiasControllerWindow, RU/EN: локализованная подпись перед списком, сжатие списка при минимальной ширине | NativeWindowsArrangeWithEmptyAndPopulatedGraphs, полный набор редактора | PASS автоматический |
| T05 Комплект | devices.yml: только Sprite.scale 0.75 → 0.60; Item.size и коллизии не менялись | YAML/RSI validation, ресурсные тесты | PASS статический; руки/мир визуально не проверены |
| T06 Ядро | kias.yml, KiasSpriteTests, CORE_SPRITE: пользователь подтвердил перекрытие направленным окном и персонажем. Клиентская регрессия обнаружила Objects=0 у ядра против Mobs=5 у обоих соседей. Назначен Mobs, как у высоких статуй; 32×64, offset и коллизии сохранены | TallCoreSharesDepthWithDirectionalWindowAndHuman: RED (ожидалось 5, получено 0) → GREEN, реальные клиентские спрайты после сетевой репликации | PASS автоматический; итоговый игровой вид не проверен |
| T07 Реле | KiasRelaySystem, RU/EN: popup после смены, examine канала/состояния, цикл по enum, доступ и дальность повторно проверяются в callback. Неизменённое значение не перестраивает сеть | ToolAndVerbReportSelectedChannelAndRespectAccess, SelectedChannelReallySplitsAndReconnectsNetworks | PASS автоматический; popup визуально не проверен |
| T08 Сканер | KiasIntegrationSystem, KiasCrewSystem: прежнее добавление интеграции не удаляло устаревшее Scanner. Сначала собираются кандидаты, затем handoff/очистка автоматической связи. Direct и native DeviceList не удаляются | ShrinkingCoverageHandsOffAndClearsOnlyAutomaticBindings: 10→2, два сканера, ручной комплект, 30 idle ticks, крепление/Connector | PASS |
| T09 Динамик | KiasDisplaySystem, KiasLocalWindow, RU/EN: Position3→KiasAnnounce имеет обработчик; пустое сообщение связи перекрывало заданный текст динамика. Теперь пустая/пробельная строка использует текст динамика; добавлена подсказка | WarningAudioAndLocalSpeechComeFromAddressedSpeaker: настоящий LinkDefaults/SendSignal, клиент получает текст при пустом override | PASS |
| T10 Тушение | KiasActuatorSystem, RU/EN: штатная вода уже тушит hotspot; реагенты сохранены. Добавлены понятные отказы и проверка ручного действия, защита второго расхода | DeviceLinkSuppressionCreatesFoamAndExtinguishesNativeHotspotOnce, NativeFireAlarmRunsSuppressionGraphAndManualVerbReloads, LightGroupsCartridgeConsumptionAndHardOff | PASS |

## Команды и доказательства

Журналы находятся в локальном игнорируемом `.kias/patch-20261008/`.

```powershell
# Рабочая папка: корень Monolith
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -m:2 -nr:false --filter 'FullyQualifiedName~Kias' --logger 'console;verbosity=normal'
# full.log: 90 passed, 0 failed, 0 skipped

# Рабочая папка: Monolith\Scripts\bat
cmd.exe /d /c "buildAllRelease.bat < nul"
# release.log / release-exit.txt: exit 0; 0 errors; 4237 warnings; 00:01:45.17

# Рабочая папка: корень Monolith
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Kias' --logger 'console;verbosity=normal'
# production-tests.log: 90 passed, 0 failed, 0 skipped

git diff --check
# Без ошибок пробелов; Git предупреждает о преобразовании LF/CRLF.
python .kias/patch-20261008/validate.py
# resources.log: 6 prototype files; RU/EN по 665 уникальных ключей;
# 65 RSI / 128 состояний; размеры кадров, RGBA корректны; ядро 32×64.
```

Настоящий buildAllRelease.bat выполнен целиком, включая подмодули; pause завершён через EOF. Настоящий runQuickAll.bat запущен из Scripts/bat через Start-Process cmd.exe -ArgumentList '/d','/c','runQuickAll.bat'. Он создал Content.Client.exe PID 52876 и Content.Server.exe PID 40928 из bin этой рабочей копии; сервер слушал UDP 1212. Скрипты штатно используют dotnet run --no-build без -c Release: игровой запуск использует проверенную Debug-сборку. Release также пересобран и проверен отдельно.

Для получения stdout только созданные этим запуском приложения перезапущены через настоящие runQuickClient.bat/runQuickServer.bat (cmd.exe /d /c, EOF для pause). client-startup.log подтверждает `Switching to state Content.Client.MainMenu.MainScreen`; server-startup.log — `Server Version 290.0.0.0 -> Ready`. Новый клиент PID 53912, сервер PID 49004 оставлены запущенными. Отдельные *-error.log пусты; имеются общие предупреждения прототипов, локализаций и MainLoop. Ручное подключение этого графического клиента не проверено.

RED→GREEN: red.log содержит 8 тестов, 4 passed/4 failed. Три падения воспроизводят старый каталог ID, сохранение Scanner вне зоны и пустое сообщение динамика. Четвёртое было ошибкой тестовой сцены атмосферы, а не доказательством бага тушения. Ранние baseline/scanner-red/behavior-red содержат проблемы блокировки DLL/пути ресурсов, а не доказательства игровых багов. В промежуточных green-прогонах исправлены ограничения layout и неверные предположения тестовых сцен о начальном канале DATA и дополнительном штатном Connector. Итоговые full.log и production-tests.log полностью зелёные.

## Риски и ограничения

### Дополнение T06 после уточнения пользователя

Пользователь воспроизвёл перекрытие ядра **направленным окном и персонажем**. Новая регрессия `TallCoreSharesDepthWithDirectionalWindowAndHuman` до исправления упала: ожидалось 5, фактически 0 (`core-red.log`). Изменена только строка `drawdepth: Mobs` у KiasCore. По коду Clyde сортировка сначала учитывает DrawDepth, затем положение нижней границы спрайта; общий слой устраняет безусловное перекрытие. Ориентир — StatueVenusRed/Blue с тем же offset. Это не установка ядра поверх всех объектов: двери и эффекты сохраняют более высокие слои.

Последние результаты заменяют прежние 90/90: **91 passed, 0 failed, 0 skipped** в Debug (`core-full.log`) и Release (`core-production-tests.log`). Команды те же, Debug-прогон после RED использовал `--no-build --no-restore`, поскольку тестовая сборка уже была скомпилирована, а правка находится в YAML. Настоящий buildAllRelease.bat повторно выполнен: exit 0, 0 ошибок, 1852 предупреждения, 00:01:03.16 (`core-release.log`, `core-release-exit.txt`). Ресурсы и git diff --check повторно проверены. Главный экран/Ready после повторного запуска фиксируются в `core-client-startup.log` и `core-server-startup.log`; ручной графический smoke не заявляется.

Повторный runQuickAll.bat создал клиент PID 37300 и сервер PID 25704 (`core-launcher-processes.txt`). После перезапуска только этих приложений с захватом stdout оставлены клиент PID 52832 (MainScreen) и сервер PID 43928 (Ready, UDP 1212 на IPv4/IPv6); error.log пусты. Финальная ветка/HEAD прежние: KIAS / e32aecc197fed90b5947354bbad7b2c24add7911. Коммит/push не выполнялись.

- После 30 спокойных ticks revision топологии не меняется. Два сканера имеют детерминированный приоритет; автоматическая связь передаётся второму, ручная Direct сохраняется.
- Сохраняемые ID и программные ссылки не мигрировались. Короткий ID — только интерфейсный текст; aliases кешируются по составу ID грида, включая offline.
- Создан реальный горячий oxygen/plasma hotspot; настоящий KiasSuppress создаёт одну пену, снижает температуру и убирает hotspot. Повторный сигнал до удаления картриджа не создаёт вторую пену.
- Настоящий FireAlarm.ForceAlert запускает граф Any AtmosSafety.Fire → Specific/All Suppression; после перезарядки работает ручное действие. Предустановленный fire — уведомительный граф без Suppression; патч не добавляет в него автоматическое тушение.
- Полный фильтр включает штатные DeviceLink sink ports, редактор/legacy, доступ, питание и FleetSchedulingAndEventStress на 200 гридах. Это регрессионный тест, не сравнительный benchmark; изменение производительности НЕ ИЗМЕРЕНО.
- Автоматические UI-проверки используют настоящий Robust layout и подключённый headless-клиент. Новых игровых screenshots до/после нет: инструменты сессии не позволяют управлять нативным игровым окном.
- Требуется ручной проход 11_LIVE_SMOKE_CHECKLIST.md: дерево/toolbar/popup, предмет в руках, граф в игровом раунде; особенно ядро на полу рядом со стеной, решёткой, трубами и кабелем. Исправление T06 реализовано после уточнения пользователя; графическая оценка и screenshot до/после остаются непроверенными.
