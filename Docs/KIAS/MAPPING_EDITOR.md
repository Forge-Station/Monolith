# Редактирование схем на карте маппинга

Программатор позволяет редактировать и записывать физические платы без питания **только на замороженной неинициализированной карте**, например созданной командой `mapping`. Требуется активный администратор с правом `Mapping`, находящийся на той же карте. Штатные ограничения расстояния, одного редактора, ревизии черновика, типов портов и записи валидной схемы сохраняются.

В интерфейсе показывается «Маппинг (замороженная карта)». `Online` остаётся false: редактор не выдаёт питание, не включает ядро, шкаф, устройства или выполнение программ. Каталог для маппинга получает отдельный снимок закреплённых устройств текущего грида, включая их обычные профили и DeviceLink-порты. Можно заранее записать SPECIFIC-привязку по той же сущности; привязки по короткому ID не используются. Снимок ограничен 4096 устройствами, в BUI остаётся прежний предел 256 строк; обычные runtime-кеши не подменяются.

После инициализации карты исключение сразу перестаёт действовать, даже если карта остаётся на паузе. На обычной игровой карте для изменения и записи по-прежнему нужна работающая сеть KIAS и питание программатора. Deadmin или потеря права Mapping также отменяют исключение при следующем запросе. Отдельного переключателя, CVar или сохраняемого компонента обхода нет.

## Проверка 2026-10-09

База: ветка KIAS, HEAD `78ec19324beb9da3d975593ccce99fa66ddfd8f1`. Изменения не коммитились и не отправлялись.

`FrozenMappingAllowsUnpoweredEditingButInitializedPausedMapsDoNot` до исправления падал на реальной вставке платы: ожидалось true, получено false (`.kias/patch-20261008/mapping-red.log`). После исправления проходит: без ядра и питания доступны вставка, открытие BUI, переименование, конкретная привязка динамика и WRITE. Проверены `Mapping=true`, `Online=false`, наличие динамика в каталоге; запреты для обычного персонажа, отсутствующего Mapping, deadmin, инициализированной карты на паузе и активной карты. При проверке обычной карты ядро обеспечивает право настройки, но не обходит отсутствие питания программатора.

Расширен `NativeWindowsArrangeWithEmptyAndPopulatedGraphs`: настоящий клиентский холст и WRITE доступны при Mapping=true/Online=false и блокируются при Mapping=false/Online=false. Это headless-проверка нативного UI; ручной графический проход не выполнялся.

Последняя Debug-проверка в обычных каталогах: **92 passed, 0 failed, 0 skipped** (`.kias/patch-20261008/mapping-final.log`). Включена защита от SPECIFIC-привязки к удалённой сущности. Команда из корня проекта:

```powershell
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -m:2 -nr:false --filter 'FullyQualifiedName~Kias' --logger 'console;verbosity=normal'
```

Ресурсы: 6 файлов прототипов, RU/EN по 666 уникальных ключей, 65 RSI/128 состояний, ядро 32×64 (`mapping-resources.log`). `git diff --check` без ошибок (`mapping-diff-check.log`). Первый RED был собран в отдельном OutDir для сохранения тогда работающего сервера; после указания пользователя вся итоговая сборка и проверки выполняются в штатных каталогах.

Настоящий `Scripts/bat/buildAllRelease.bat` выполнен из Scripts/bat через `cmd.exe /d /c "buildAllRelease.bat < nul"`, включая обновление подмодулей и завершение pause. Итог: **exit 0, 0 ошибок, 4240 предупреждений, 00:01:37.60** (`mapping-release.log`, `mapping-release-exit.txt`). Предупреждения всего решения не устранены этим патчем.

Release-проверка: **92 passed, 0 failed, 0 skipped**, `mapping-production-tests.log`:

```powershell
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~Kias' --logger 'console;verbosity=normal'
```

Настоящий `runQuickAll.bat` запущен из Scripts/bat через `Start-Process cmd.exe -ArgumentList '/d','/c','runQuickAll.bat'`; он создал клиент PID 45848 и сервер PID 32380 (`mapping-launcher-processes.txt`). Для записи stdout только эти приложения затем перезапущены настоящими `runQuickClient.bat < nul` / `runQuickServer.bat < nul`, без изменения скриптов. Последние клиент PID 29668 и сервер PID 45256 оставлены запущенными: `mapping-client-startup.log` подтверждает MainScreen, `mapping-server-startup.log` — Ready, сервер слушает UDP 1212. Оба startup-error.log пусты. Скрипты запускают обычную Debug-конфигурацию; Release отдельно собран и проверен. Ручная проверка графического клиента в игровом раунде не выполнялась.

Финальная ветка/HEAD: KIAS / `78ec19324beb9da3d975593ccce99fa66ddfd8f1`. Изменения остаются в рабочем дереве.
