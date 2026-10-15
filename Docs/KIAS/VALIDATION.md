# KIAS: validation, 2026-10-08

## 2026-10-15 — независимое ревью

Автономное дерево без 11 сторонних изменений и без engine patch: Release **188/188**, Debug **188/188**, Python **111/111**. Интеграционные прогоны последовательные; каждый включает 178 тестов KIAS и 10 нативных DeviceLink-контрактов. RU/EN: 866 ключей, без дублей и расхождений. Точные TRX, изменения по 12 находкам и ограничения описаны в локальном `KIAS_REVIEW/07_FIXES_AND_VALIDATION.md`. Новый A–B–B–A и ручной игровой smoke не выполнялись. Ниже сохранена история прежних проверок.

## 2026-10-09 — редактор на замороженной карте маппинга

База `KIAS`, HEAD `78ec19324beb9da3d975593ccce99fa66ddfd8f1`. Условия и регрессии описаны в [MAPPING_EDITOR.md](MAPPING_EDITOR.md). Исключение допускает только редактирование программатора активным администратором Mapping на неинициализированной замороженной карте; обычная пауза игрового раунда не даёт обхода питания.

- **92/92** в Debug (`mapping-final.log`) и **92/92** в Release (`mapping-production-tests.log`), 0 failed/0 skipped.
- Настоящий buildAllRelease.bat: exit 0, 0 ошибок, 4240 предупреждений, 00:01:37.60 (`mapping-release.log`). Итоговые сборки выполнены в штатных каталогах.
- Настоящий runQuickAll.bat создал оба приложения; повторный запуск его дочерних скриптов с захватом stdout подтвердил MainScreen/Ready (`mapping-client-startup.log`, `mapping-server-startup.log`), UDP 1212. Ручной игровой smoke не выполнен.
- RED→GREEN: реальная вставка/WRITE на замороженной карте; SPECIFIC offline, удалённая сущность, отзыв Mapping/deadmin, запрет на инициализированной карте с паузой и без неё, нативный UI с Mapping=true/Online=false. Питание и runtime не подменяются.
- YAML/локализации/RSI корректны: RU/EN по 666 ключей, 65 RSI/128 состояний (`mapping-resources.log`); git diff --check без ошибок. Коммит/push не выполнялись.

## Патч T01–T10 от 2026-10-08

Дополнение после уточнения T06: **91/91 в Debug и 91/91 в Release**, журналы `core-full.log`, `core-production-tests.log`. Ядро переведено в слой `Mobs` после RED→GREEN проверки с направленным окном и персонажем (`core-red.log`). Повторный настоящий buildAllRelease.bat: exit 0, 0 ошибок, 1852 предупреждения, 00:01:03.16 (`core-release.log`). Последующие пункты с 90/90 сохраняют историю первого прохода.

Ветка `KIAS`, база `e32aecc197fed90b5947354bbad7b2c24add7911`. Актуальный отчёт и точные команды: [PATCH_20261008_REPORT.md](PATCH_20261008_REPORT.md). Исторические результаты ниже относятся к предыдущим патчам.

- Полный KIAS-фильтр: **90 passed, 0 failed, 0 skipped** в Debug и отдельно в Release; `.kias/patch-20261008/full.log`, `production-tests.log`.
- Настоящий `Scripts/bat/buildAllRelease.bat`: exit **0**, **0 ошибок**, 4237 предупреждений, 00:01:45.17; `release.log`, `release-exit.txt`.
- Настоящий `runQuickAll.bat` создал оба приложения. Повторный запуск его дочерних скриптов с записью stdout подтвердил клиентский **MainScreen** и серверный **Ready**; `client-startup.log`, `server-startup.log`. Сервер слушает UDP 1212. Скрипты запускают Debug; Release проверен отдельно.
- Проверка ресурсов: 6 файлов прототипов, RU/EN по 665 уникальных ключей, 65 RSI/128 состояний, ядро 32×64. `git diff --check` без ошибок.
- Проверены scanner handoff/detach/Direct, 30 idle ticks, реальные DeviceLink-сигналы динамика и пожаротушения, физическое охлаждение hotspot, один расход картриджа и перезарядка, диапазоны layout 850/1200/1600.
- **T06 дополнительно исправлен после уточнения пользователя**: слой ядра `Objects → Mobs`; регрессия на клиентских спрайтах направленного окна и персонажа воспроизвела 0 вместо 5 до исправления. Новейшие результаты приведены в дополнении отчёта. Ручной игровой smoke и новые screenshots не выполнены; заменены нативными headless UI/сетевыми/физическими тестами. Коммиты и push не выполнялись.

Tested branch: `KIAS`. Base HEAD: `3e067d069ebb45b192db06db8a38354f9f053cca`; evidence refers to the correction patch working tree, including removal of temporary smoke commands. The final commit is recorded in Git history.

## Automated checks

```powershell
dotnet restore Content.IntegrationTests/Content.IntegrationTests.csproj -m:2 -nr:false
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -m:2 -nr:false --filter 'FullyQualifiedName~Tests._Forge.KIAS' --logger 'console;verbosity=normal'
git diff --check
```

- Restore succeeded. Original baseline: 46/46.
- Original regressions reproduced before their fixes: WRITE metadata, ALL Speaker and integrated atmosphere DeviceList.
- Final production build and full KIAS suite: **58 passed, 0 failed, 0 skipped**. This includes controller compiler/runtime/editor/persistence/layout, device integration, native atmosphere pipeline, audio/local speech, fleet and stress tests.
- Build has zero errors; repository/analyzer warnings remain and were not treated as clean-warning validation.
- After removing temporary commands, the ordinary graphical client also passed assembly sandbox verification and reached MainScreen with the sandbox enabled: `.kias/clean-client.log`; stderr was empty.
- `git diff --check`: passed after preserving Markdown hard breaks as `<br>`.

Local evidence: `.kias/correction-restore.log`, `.kias/correction-baseline.log`, `.kias/correction-reproductions.log`, `.kias/correction-final-tests.log`.

The new regression suite covers both ALL Lighting OFF→ON with native APC/light state and ALL LightController OFF→ON through the physical group controller; missing DATA and native KIAS power; SPECIFIC and ALL local speaker messages with selected/excluded senders and cooldown; native/integrated List mode; ordinary non-KIAS Link; Group UI refresh and selector cache; native AirAlarm device registration/gas/Danger → real atmosphere preset → AtmosClear. Persistence covers names and old/new service-tool groups.

Layout checks use **850×500, 1200×720, 1600×900**, empty/populated graphs and long labels, every node kind, inspector capabilities/width, localized help, summaries and mismatch feedback. Native RU localization validates all spawnable KIAS entities and graph ports; static RU/EN audit checks duplicate/missing locale keys and all 91 prototype names/descriptions.

## Actual graphical client/server smoke

Ran `bin/Content.Server/Content.Server.exe` on loopback port 1213 and `bin/Content.Client/Content.Client.exe`, user `KiasLocalSmoke`, RU locale, **1600×900, UI scale 1**. A temporary debug scene spawned physical KIAS machines, DATA/core, two speakers and actual cards. All five windows opened through server `TryOpenUi` with native BUI states; the client rendered screenshots through Clyde.

| Window | Standard | Minimum | Local screenshots |
|---|---|---|---|
| Programmer | 1200×720 | 850×500 | `.kias/programmer-standard.png`, `.kias/programmer-minimum.png` |
| Rack | 650×420 | 430×320 | `.kias/rack-standard.png`, `.kias/rack-minimum.png` |
| Service tool, Group | 430×380 | 360×300 | `.kias/service-standard.png`, `.kias/service-minimum.png` |
| Light group controller | 430×380 | 360×300 | `.kias/lighting-standard.png`, `.kias/lighting-minimum.png` |
| Management | 900×650 | 650×450 | `.kias/management-standard.png`, `.kias/management-minimum.png` |

Inspected these ten frames for readable palette, bounded node summaries, usable toolbar/dropdowns, separate inspector, wrapped local details, grouped sections, minimum-size vertical scrolling and pinned local actions. Narrow windows intentionally shorten long captions with full tooltips; scrolling exposes the remaining fields/content.

Native client UI messages additionally exercised Rename → WRITE → Eject. The replicated physical card name matched the submitted name: `.kias/native-write-result.txt` reports PASS. The rack's running program produced local speech from both speakers in the graphical client. `.kias/native-bui-complete.txt` confirms ten frames; logs are `.kias/smoke-client.log` and `.kias/smoke-server.log`.

## Exact limits

- Live interaction was driven through temporary native game commands/UI messages. It was not a manual mouse/keyboard session; the available desktop automation runtime failed to initialize. Dragging, wheel navigation and hover timing were not exercised as user gestures. Their event handlers were reviewed; automated tests cover layout, help strings and connection compatibility.
- The debug scene bypassed machine power and granted local debug access to isolate BUI rendering; real APC/control-plane behavior is covered by integration regressions.
- Screenshot saving used `ROBUST_DISABLE_SANDBOX=1` only for the temporary local client process. Both temporary C# commands were removed before final production build/tests; no global setting or shipped debug command was added.
- All ten images are local ignored artifacts, not media attached to a published PR. The PR checklist remains explicit about this.
- Live screenshots cover RU at UI scale 1. EN descriptions/keys and other sizes have automated/static coverage; other display scales and a public multiplayer server were not tested.
- The default server map logged an unrelated pre-existing `invalid FTL state Available` warning. No KIAS assertion or native UI crash occurred during the completed capture.

Compatibility: version 1 and existing profile IDs remain; a legacy enum constant spanning different known domains now needs separate constants. See [CORRECTION_REPORT.md](CORRECTION_REPORT.md).

## Дополнение: стабильность сети и интерфейса

База дополнения: `052bed52ad5b3a0d534e3e6508e2c17ab5c5b067`. Причины и игровой сценарий описаны в [UI_STABILITY.md](UI_STABILITY.md).

```powershell
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore -m:2 -nr:false -p:OutDir=D:/Workspace/ss14_lua/corvax/Monolith/.kias/ui-stability-bin/ --filter 'FullyQualifiedName~Kias' --logger 'console;verbosity=normal'
```

- До исправления воспроизведены зацикливание топологии с незакреплённой AirAlarm и пересоздание нажатой кнопки шкафа.
- Три новые регрессии после исправления: **3/3**. Подсказка проверена через настоящее всплывающее окно Robust; нажатие кнопки — через штатные обработчики клиентского интерфейса.
- Расширенный полный прогон: **71 passed, 0 failed, 0 skipped** — 61 тест из `Tests._Forge.KIAS` и 10 параметризованных проверок штатных DeviceLink sink ports для прототипов KIAS.
- Сборка завершилась без ошибок. Существующие предупреждения сборки не устранены этим дополнением.
- Сборка в отдельный каталог позволила сохранить работающий пользовательский сервер. Ручная игровая проверка дополнения не выполнялась; интерфейсные проверки выполняются в headless-клиенте.
- Локальные журналы: `.kias/flicker-repro.log`, `.kias/flicker-fixed.log`, `.kias/flicker-full.log`.

## Дополнение: фауна, мониторинг, кириллица и ядро

База: `e369a076a6587314cfa14f05a49719a44078f69d`. Описание изменений — [FAUNA_MONITOR.md](FAUNA_MONITOR.md), происхождение спрайта — [CORE_SPRITE.md](CORE_SPRITE.md).

Использована та же команда с отдельным OutDir и фильтром `FullyQualifiedName~Kias`, приведённая в предыдущем дополнении. Итог: **79 passed, 0 failed, 0 skipped** — 69 проверок в `Tests._Forge.KIAS` и 10 проверок штатных DeviceLink sink ports. Сборка завершилась без ошибок; прежние предупреждения не исправлялись этим патчем.

- Пять сценариев фауны: карп, кварцевый рудокраб, пехотинец Синдиката, уборочный бот Синдиката, карп с вручную установленным модулем. Все проверяют отсутствие зависимости от игроков рядом, исключение мёртвых/дружественных целей и извлечение модуля.
- Мониторинг: реальное применение инструмента открывает BUI; изменение счётчика приходит в состояние открытого окна автоматически; импульс показывает возраст, отключение скрывает старые значения, удаление цели безопасно. Отказ в доступе не выбирает новую цель.
- ASCII и русская строка проверены для SPECIFIC и ALL через настоящие клиентские Configure/WRITE, физическую плату, runtime и принятую локальную IC-речь. Контрольные поля черновика/платы/runtime совпадают с исходной строкой; каждый выбранный динамик передаёт текст, исключённый не передаёт, cooldown сохраняется. Кодировочная ошибка в рабочем пути не обнаружена.
- `KiasCore.rsi` загружен штатным клиентским `IResourceCache` без fallback: размер 32×64 и состояние `core` подтверждены. PNG имеет канал прозрачности; визуально проверен экспортированный кадр в увеличении nearest-neighbor.
- Локальные журналы: `.kias/fauna-repro.log`, `.kias/fauna-monitor-fixed.log`, `.kias/monitor-final.log`, `.kias/fauna-monitor-full.log`. В журнале воспроизведения исходный случай `MobOreCrab` был ошибкой тестовой сцены (абстрактный прототип) и не является доказательством бага; итоговые проверки используют `MobQuartzCrab`.
- Проверки клиентского редактора, чата, BUI и RSI выполняются в headless-клиенте. Ручной игровой прогон и новые графические снимки игрового мира не выполнялись.

## Дополнение: полный набор спрайтов KIAS

База: `ce3550fa7df1b529e4087bb60d557696f269c8d9`. Описание — [SPRITE_PACK.md](SPRITE_PACK.md), карта всех 91 сущностей — `SPRITE_ENTITY_MAP.json`. Нативные кадры визуально проверены в четырёх `SPRITE_ATLAS_*.png`, увеличенных без сглаживания.

- Финальный полный прогон той же командой с фильтром `FullyQualifiedName~Kias`: **83 passed, 0 failed, 0 skipped** — прежние 79 проверок и четыре новых проверки ресурсов и отображения. Сборка завершилась без ошибок; прежние предупреждения сохранены.
- Подключённый headless-клиент получает все 91 сущности, созданные сервером. Размеры RSI и состояния всех слоёв проверяются после обычной сетевой репликации, без fallback на прежние спрайты.
- Реальная вставка и извлечение схемы меняют состояние программатора `empty → card → empty` на клиенте. Отказ в извлечении несохранённого черновика сохраняет вставленное состояние.
- Проверена физическая площадь: обе консоли имеют корпус шириной два тайла; ядро и остальные серверы сохраняют корпус одного тайла. Кадр ядра — 32×64, обеих консолей — 64×32, остальных сущностей — 32×32.
- Навигационный светильник сохраняет состояния пустого, выключенного, включённого, разбитого и перегоревшего прибора. Подавитель связи сохраняет три индикатора заряда и выключенный индикатор.
- Статическая проверка: **62 RSI, 109 состояний**. Проверены размеры кадров/листов, RGBA, ограниченная палитра и отсутствие повторяющихся имён состояний. Контрольные суммы MANIFEST перепроверены.
- Первые ошибки новых проверок относились к тестовым сценам: локальное создание серверных сущностей на клиенте и неверное предположение о типе коллайдера. Сцены переведены на создание сервером и обычную репликацию. Затем подключённый клиент выявил необходимость явной карты основного слоя для GenericVisualizer; карта добавлена, все четыре новые проверки прошли, после них полный прогон прошёл 83/83.
- Журналы: `.kias/sprite-build.log`, `.kias/sprite-full.log`, `.kias/sprite-native.log`, `.kias/sprite-connected.log`, `.kias/sprite-fixed.log`, `.kias/sprite-final-full.log`. Проверки выполняются через headless-клиент и нативные ресурсы; ручной игровой прогон после замены графики не проводился.
## 2026-10-08 — идентификаторы, диагностика и настенные устройства

Исходная ветка `KIAS`, HEAD `b6fdb36aa47e1ff67fd45156213d4bf0374364c5`. До правок: 83/83 теста KIAS. После правок: **86/86**, журнал `.kias/device-patch-final-tests.log`.

Новые и расширенные проверки: разделение названных/неназванных помещений стенами; сохранение идентификаторов и настроек выключателя после загрузки карты; отклонение недопустимых настроек и запросов без доступа; выдача назначенных импульсов выключателем; изменение дальности без сброса помещения; текущие выходы в режиме диагностики; все 16 соединений кабеля без рабочего ядра; настоящий клиент загружает спрайты всех 91 прототипов и проверяет направленные приборы. Проверка экспорта: 62 RSI, 125 состояний, нативные размеры, прозрачность и палитры. `git diff --check` без ошибок.

Выполнен настоящий `Scripts/bat/buildAllRelease.bat` из его рабочей папки: **0 ошибок**, 4237 предупреждений, 00:01:44.34. Журнал `.kias/device-patch-release.log`. Скрипт выполнен целиком, включая обновление подмодулей и `dotnet build -c Release`; `pause` получил EOF для автоматического завершения.

Затем запущен настоящий `Scripts/bat/runQuickAll.bat`: созданы процессы `Content.Server.exe` и `Content.Client.exe` из `bin/Content.Server` и `bin/Content.Client`. Запуск приложений не заменяет ручную проверку размещения и поведения в игровом раунде.
