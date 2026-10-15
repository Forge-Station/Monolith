# 09 — Миграция, UI и изменения существующих тестов

## Изменения code-level (ориентиры, не навязываемая структура)

| Код | Ожидаемое изменение |
|---|---|
| `Content.Server/_Forge/KIAS/KiasCrewSystem.cs` | Убрать генератор круга. Внедрить единый room-index в `Scan`, HasCoverage, ScannersCovering, RoomLabel, HasUnknownAlongside, CrewUnavailable, fauna, transponders. Оставить native event handlers и KIAS trigger semantics. |
| Новый `KiasRoomTopologySystem.cs` (или несколько KIAS-only файлов) | Grid caches, seed rotation, physical barriers, door terminals, targeted dirty invalidation, atomic revisions, cheap API, server debug export. Избежать монолитного файла >1000 строк. |
| `Content.Server/_Forge/KIAS/KiasIntegrationSystem.cs` | `Reconcile` ищет подходящие devices **в комнатах**, а не через `_lookup.GetEntitiesInRange` по радиусу; `CanControl` проверяет same-room + Power/DATA + Direct semantics; дифф авто bindings без oscillation. |
| `Content.Server/_Forge/KIAS/KiasDisplaySystem.LocalUi.cs` | UI scanner coverage реальным `Cells`/RoomStatus, больше не Range круг; другие датчики не менять. Пересмотреть `SetSensorRange` и BUI сообщения для scanner, legacy ignored. |
| `Content.Shared/_Forge/KIAS/KiasCoverage.cs` | Добавить `Room` shape (только в конец enum), revision/status/count fields с корректными `[NetSerializable]` и ограниченным payload. Проверить client renderer и network compatibility. |
| `Content.Shared/_Forge/KIAS/KiasCrewComponents.cs` | Сохранить `Range` для старого YAML, пометить obsolete/ignored; `Advanced`, `Modules`, `Entities` не ломать сериализацией; не добавлять runtime room IDs в DataFields карты. |
| `Content.Client/_Forge/KIAS/...` | Форма помещения на actual tiled overlay. Отдельные стили interior/door boundary/invalid. UI не собирает геометрию локально. Переиспользовать текущий renderer если способен рисовать Cells. |
| `Content.IntegrationTests/Tests/_Forge/KIAS/KiasScannerReconciliationTests.cs` | Старый radius shrink/grow тест перестаёт быть валидным: переписать на реальные комнаты, передачу автоматических bindings при изменении перегородки/устройства; Direct сохранён. |
| Новые `KiasRoomTopologyTests.cs`, `KiasRoomPerformanceTests.cs` | Fixtures из `08_SCANNER_ACCEPTANCE_MATRIX.md`, isolated tests и Briar load smoke. |

До внесения изменений агент **по поиску ссылок** составляет `SCANNER_CONSUMER_MAP.md`: все использования `.Range` применительно к `KiasRoomScannerComponent`, `ScannersCovering`, `HasCoverage`, `BuildCoverage`, `RebuildCoverage`, `KiasCoverageShape`, а также запросы `GetEntitiesInRange` на scanner. Не ограничиваться файлами таблицы, не менять дальние радары/сканеры оружия.

## UI/UX критерии

- Клик по комнатному сканеру → `Room scanner` / `Покрытие: помещение` / `Геометрия: OK` / `Площадь: 42 тайла` / `Граница: 2 двери` / `Подключение: online` / `Модули: ...`, актуальная версия комнаты; не вводить editable `Range` для данного типа.
- В режиме диагностического инструмента coverage **видно конфигурацию точных тайлов, включая общий порог**, а не круг, не area-of-effect и не DATA-радиус. При открытии двери форма не расширяется.
- Если scanner повернут неверно: `Нет помещения со стороны сканирования` и направление/координаты. Если граница недостоверна — статус, а не самопроизвольная привязка к соседней комнате.
- Geometry UI в сеть только по явному запросу/открытому окну/изменению revision; лимит данных для диагностики не должен ограничивать серверную геометрию. Каждый клиент получает только доступные ему объекты, с сохранением существующих checks BUI/ownership.
- Все сохранённые UI-поля Range не должны снова активировать старую radius logic через незаметный handler. Старые saves с Range=1/2/7/10 должны создавать одинаковое покрытие при одинаковых стенах. Модульные слоты не изменять.

## Связь с существующим функционалом и баланс

- Оптический модуль использует штатную камеру и её игровую видимость. Нельзя искусственно превращать camera в wallhack; логическая принадлежность существ комнате не равна передаче фактического видео/visibility.
- `Connector` меняет **геометрическую** область автопоиска: длинный зал действительно позволяет связать устройство далее 10 тайлов, но только если оно в той же комнате/корректном boundary, датчик онлайн, установлен нужный модуль, сохранён Power/DATA/access. Это сознательное изменение механики, должно быть в release notes.
- Любое ограничение безопасной формы комнаты (`ROOM_TOO_LARGE`, `OPEN_TO_SPACE`) показывать в UI. При неизвестной топологии команды по неоднозначной авто-привязке запрещать, не «мягко» разрешать.
- В нескольких комнатах есть двери с двухсторонней принадлежностью. Сохранить один физический KIAS target owner (`KiasIntegratedComponent.Scanner`) по детерминированному приоритету; в UI отмечать shared-door, чтобы отсутствие второй owner не считалось багом. Если test discovers direct conflict, фиксировать явно.
- Данные графов сохраняют названия портов и типы: `RoomScanner.Entities`, `Occupied`, `Motion`, `Crew`, `PersonCritical`, `PersonDead`, `Threat`, `FaunaThreat`, `Radiation`, `AnomalyGrowth`, `Person` и текущие иные outputs — не переименовывать и не пересоздавать шаблоны без необходимости. Порядок и event latency должны проходить regression.

## Миграционные правила

1. Ни одного изменения серверных storage records старых карточек, template names/IDs, 27 programs и ID устройств без теста save→load.
2. Новую room geometry не сериализовать внутрь Map YAML: пересчитать на MapInit/TopologyReady после загрузки; проверить раннюю стадию, когда Atmos adjacency ещё не готова, и пометить `PENDING` до инициализации.
3. При патче default UI локализации добавить русский/английский текст. Для новых Enum статусов не переиспользовать ordinal старых сериализуемых enums.
4. Legacy `Range` **не удалять из save при первом обновлении**, только игнорировать для RoomScanner. Отдельно проверить `Range` не используется при управлении/приборах/графах/cover preview.
5. После roll-out оставить временный diagnostic old-vs-new coverage comparator **только в тестах / dev diagnostics** для поиска ошибок; в обычном runtime второго алгоритма и круга не должно быть.
6. Перед сохранением `briarKIAS.yml` убедиться, что геометрия не привела к автоматической перезаписи ориентаций, стен или дверей пользователя. Авто-биндинги, вычисленные комнаты и serial runtime state не должны загрязнять YAML.
