# 06 — сопоставимые Release-сборки и физическая идентичность Vanilla/KIAS

## Почему нельзя сравнивать HEAD main и HEAD KIAS напрямую

На момент первого аудита GitHub ветки расходились примерно на 11 уникальных коммитов KIAS и 13 upstream main, общий предок `b069ad386d7599ac6b51d9dfcdfba9b6a311f676`. При любом новом patch пересчитать diff. Функционал, не относящийся к KIAS, может изменить производительность. Разница сторон должна быть **строго описана, пофайлово доказана и по возможности сведена к KIAS**.

### Подготовка

- В изолированных git worktrees составить `BASE_CONTENT_COMMIT`, `ROBUST_SUBMODULE_COMMIT`; зафиксировать оба.
- B = base+точечный KIAS overlay + lab harness; A = тот же base + тот же neutral lab harness + без KIAS runtime/prototypes/integration. При портировании учитывать всех зависимостей, корректность компиляции и семантику KIAS, не подсовывать другие неигровые патчи.
- Сначала сохранить доказуемый diff manifest: список added/removed/modified `KIAS_*`, изменённых vanilla API/систем. Если какая-то общая модификация не обязательна для KIAS, перенести её симметрично либо исключить; каждый случай описать.
- Не менять состояние оригинального репозитория пользователя, не force-update его ветку. Если некоторые новые изменения в KIAS ещё не закоммичены, сохранить их отдельным patch bundle и включить в fingerprint.

### Корабли и parity

Источник A/B должен быть равен по всем vanilla объектам: геометрия грида, tilemap, инженерная начинка, engine, guns, machines, atmos, power, fixtures, coords, room topology, grid count, initial species/equipment/transponders, IFF/company flags, event map. B дополнительно содержит только требуемый KIAS overlay. **Не брать другую старую карту с похожим названием**: Briar baseline при отсутствии готового vanilla saved-grid сделать детерминированным strip overlay на чистой копии `briarKIAS.yml`; log entity deletion/remapping/transform/container refs и хеш vanilla-part. При наличии authentic Briar сравнить оба по vanilla предметам и выбрать 1:1 достоверный вариант.

`shipyard` prototype не нужен: карты можно создавать через актуальный MapLoader. Раздача owner/crew/company — через существующие системы с идентичной историей действий в двух режимах. Компания не обязана быть покупателем для теста, но семантика доступа должна остаться игровой.

**Особый риск:** «установленный KiasIntegrationKit» мог менять `KiasIntegrated`, power/logic vanilla devices и их функционирование. При strip не удалить сам vanilla свет/двигатель/консоль и не оставить «отключённые» vanilla устройства. A и B должны начинать с одинаковых vanilla состояния/входов и одинакового питания. В B KIAS может изменять состояние после старта — это ожидаемая часть эффекта.

### Fleet

Общий флот 40 grid (предпочтительно 30–40 разнообразных) и минимум один 1:1 контрольный Briar vs Briar+KIAS. Разные типы по размеру/массе/пушкам/атмосфере/предметам, одинаковые позиции и параметры в A/B. Для B разумно использовать несколько BriarKIAS overlay на части флота с разными наборами активных/пассивных систем; остальные корабли могут не иметь KIAS, но должен быть зафиксирован точный KIAS distribution. Указывать X KIAS-enabled из 40 и количество программаторных карт/нод/проводов/датчиков.

### Сборка

`git submodule update --init --recursive` только внутри изолированного проекта после проверки commit pointer; `dotnet restore`, `dotnet build -c Release --no-restore` или штатный `Scripts/bat/buildAllRelease.bat`, отдельные выходные каталоги для A/B, hash binaries/assets. Убедиться в одинаковом `DOTNET`, `gcServer`, ReadyToRun/JIT, process priority/affinity, threadpool и полной конфигурации OS. Профилировщик выключен в основном измерении; Prometheus одинаков, уровень логирования одинаков.

**Не держать одновременно два игровых сервера:** A → завершить процесс и освободить ресурсы → B (или A-B-B-A с холодным стартом между). Сбросить persistent data-dir/snapshots, логи, process-specific dirs. Прогреть одинаковым числом тиков/профилем JIT **до** recorded segment.

### Проверка parity (до прогона)

Сохранить таблицу:

| Invariant | A | B | Result |
|---|---|---|---|
| Base content / engine SHA | … | … | exact |
| Non-KIAS prototype/tile/entity fingerprint | … | … | exact |
| 40 grids stable labels / positions | … | … | exact |
| Vanilla power/atmos/physics at tick 0 | … | … | equivalent |
| Game rules, env, CVar / logging | … | … | exact |
| Scenario seed / SHA256 | … | … | exact |
| KIAS-only overlay | absent | present | expected difference |
| Runtime active EntitySystem type diff | expected | expected | explained |

Недоказуемая parity = `AB_INVALID`; нельзя подсчитать «KIAS overhead» как достоверный результат.

### Доп. режимы отдельно от основного теста

- **KIAS loaded, disabled** — измерить стоимость только присутствия registries/prototypes/idle background. Это НЕ заменяет Vanilla no-KIAS.
- **KIAS passive** — без физических effects для выяснения listener overhead.
- **KIAS active** — основной игровой вариант.
- Разные числа гридов 10/20/40/80 и различные роли систем для scaling test.
