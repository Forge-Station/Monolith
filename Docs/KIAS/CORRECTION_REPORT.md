# KIAS: исправления карточек, устройств и интерфейса

База: `3e067d069ebb45b192db06db8a38354f9f053cca`, ветка `KIAS`.

## Причины и решения

| Проблема | Причина | Исправление и доказательство |
|---|---|---|
| Имя карточки | WRITE изменял программу, но не MetaData | WRITE синхронизирует имя и описание; черновик предмет не переименовывает. Editor test и map round trip, включая загрузку без MapInit. Старые карточки синхронизируются также при ComponentStartup. |
| ALL Speaker | Один ключ подавления повторов использовался для разных адресатов | Ключ включает target. Runtime → настоящий динамик → Local chat проверен для двух выбранных и одного исключённого адресата; повтор сохраняет cooldown. |
| Светильник нельзя включить после OFF | Доступность интегрированного адресата зависела от его функционального питания | Адресуемость проверяет работающий core, DATA, крепление и scanner; собственные машины KIAS продолжают требовать питание. Lighting OFF→ON test. |
| «Освещение» выбирало контроллеры | Единственный старый профиль был привязан к KiasLightController | Добавлен Lighting для интегрированных ламп. Старый ID LightController сохранён для групп и сохранённых схем. |
| Непонятные группы | Название группы хранилось в Message; изменения не обновляли выборки | Отдельный Group, текущая группа и тип, нормализация имени, обновление selector cache и сравнения отправляемых UI states. Старый Group из Message восстанавливается при загрузке инструмента. |
| Мультитул уходил в связь портов | DetermineMode считал добавленные DeviceLink-порты единственной возможностью устройства | Для двойной возможности DeviceNetwork + DeviceLink сохраняется явно выбранный режим. Проверяются обычные и интегрированные устройства, List и Link. |
| Разгерметизация не запускала схему | Нарушалась штатная upstream-настройка списка AirAlarm | Проверка проходит через список sensor/vent/scrubber, регистрацию native network, газ датчика, Danger, AtmosDanger, настоящий preset и AtmosClear. |
| Инспектор и типы | Общая форма всех параметров, сырые enum и общие описания | Поля по возможностям узла, именованные enum-домены, вывод значений, отдельные описания портов и обратная связь соединения. |
| Узкие и длинные элементы UI | Неограниченное измерение текста и неразделённые панели | Ограничения палитры, инспектор 300–340, вертикальная прокрутка, рамки, переносы и подсказки, отдельные действия карточки и выбор шаблонов. |
| Локализация | Названия без описаний и смешанное написание продукта | RU/EN name и desc для всех 91 создаваемых KIAS сущностей; Latin KIAS. |

## Совместимость

- `KiasControllerProgram.CurrentVersion` остаётся 1: новые EnumDomain поля необязательны.
- Старый `Unspecified` enum получает домен из соединённых портов без изменения сохранённого объекта. Числовые значения остаются прежними.
- Старые схемы, соединявшие одну enum-константу с разными известными доменами, требуют отдельной константы для каждого домена. Ошибка явно видна в редакторе. Разные известные домены не соединяются, в том числе через EnumCompare. Известные домены допускают значения 0–3.
- `LightController` не переименован в serialized данных. В UI это «Контроллеры групп освещения»; новый `Lighting` выбирает лампы.
- Native DeviceLink snapshots и их ID не изменяются. SPECIFIC адресует выбранный объект независимо от старых Room/Group фильтров.

## Общий код

Единственное изменение вне KIAS — `NetworkConfiguratorSystem.DetermineMode`: dual-capability устройство больше не сбрасывает выбранный режим мультитула. Удалена прежняя частная привязка к BoardingTeleport. Сохранять native список посредством KIAS-local удаления DeviceLink нельзя: эти порты нужны штатной интеграции. Новое условие опирается только на общие возможности компонентов и покрывается регрессией без KIAS.

## Проверки

Точные итоговые команды, количества тестов, изображения и ограничения записываются в `VALIDATION.md`. Временные помощники локального smoke не входят в поставляемый код.

## Изменённые файлы

Полный состав патча, включая обновлённую пользователем исходную спецификацию:

- `00_AGENT_MASTER_PROMPT.md`
- `01_REPO_FINDINGS.md`
- `02_TARGET_ARCHITECTURE.md`
- `03_DEVICE_PROTOCOL_SPEC.md`
- `04_IMPLEMENTATION_PLAN.md`
- `05_PERFORMANCE_AND_TESTS.md`
- `06_REPO_SOURCE_MAP.md`
- `07_CONTROLLER_GRAPH_SPEC.md`
- `08_PROTOCOL_MIGRATION.md`
- `09_THIRD_PARTY_REFERENCES.md`
- `APPLY.md`
- `Content.Client/_Forge/KIAS/Controllers/KiasControllerLabels.cs`
- `Content.Client/_Forge/KIAS/Controllers/KiasControllerWindow.cs`
- `Content.Client/_Forge/KIAS/Controllers/KiasGraphCanvas.cs`
- `Content.Client/_Forge/KIAS/KiasLocalWindow.cs`
- `Content.Client/_Forge/KIAS/KiasUi.cs`
- `Content.Client/_Forge/KIAS/KiasWindow.xaml`
- `Content.Client/_Forge/KIAS/KiasWindow.xaml.cs`
- `Content.IntegrationTests/Tests/_Forge/KIAS/KiasControllerCorrectionTests.cs`
- `Content.IntegrationTests/Tests/_Forge/KIAS/KiasControllerEditorTests.cs`
- `Content.IntegrationTests/Tests/_Forge/KIAS/KiasControllerLayoutTests.cs`
- `Content.IntegrationTests/Tests/_Forge/KIAS/KiasGraphTests.cs`
- `Content.IntegrationTests/Tests/_Forge/KIAS/KiasPersistenceTests.cs`
- `Content.Server/DeviceNetwork/Systems/NetworkConfiguratorSystem.cs`
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerIoSystem.Commands.cs`
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerIoSystem.cs`
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerPhysicalSystem.cs`
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.Compilations.cs`
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerUiSystem.cs`
- `Content.Server/_Forge/KIAS/KiasActuatorSystem.cs`
- `Content.Server/_Forge/KIAS/KiasDisplaySystem.LocalUi.cs`
- `Content.Server/_Forge/KIAS/KiasDisplaySystem.State.cs`
- `Content.Server/_Forge/KIAS/KiasIntegrationSystem.cs`
- `Content.Server/_Forge/KIAS/KiasServiceSystem.cs`
- `Content.Server/_Forge/KIAS/KiasSystem.cs`
- `Content.Shared/_Forge/KIAS/Controllers/KiasControllerProgram.cs`
- `Content.Shared/_Forge/KIAS/Controllers/KiasGraphCatalog.cs`
- `Content.Shared/_Forge/KIAS/Controllers/KiasGraphCompiler.cs`
- `Content.Shared/_Forge/KIAS/KiasActuatorComponents.cs`
- `Content.Shared/_Forge/KIAS/KiasDisplay.cs`
- `Content.Shared/_Forge/KIAS/KiasLocalUi.cs`
- `Docs/KIAS/ARCHITECTURE.md`
- `Docs/KIAS/CONTROLLERS.md`
- `Docs/KIAS/PARITY_AUDIT.md`
- `Docs/KIAS/PLAYER_GUIDE.md`
- `Docs/KIAS/PULL_REQUEST.md`
- `Docs/KIAS/README.md`
- `Docs/KIAS/THIRD_PARTY.md`
- `Docs/KIAS/VALIDATION.md`
- `MANIFEST.md`
- `PATCH_PROMPT.md`
- `Resources/Locale/en-US/_Forge/kias-controller-help.ftl`
- `Resources/Locale/en-US/_Forge/kias.ftl`
- `Resources/Locale/ru-RU/_Forge/kias-controller-help.ftl`
- `Resources/Locale/ru-RU/_Forge/kias.ftl`
- `Resources/Prototypes/_Forge/KIAS/controller_profiles.yml`
- `references/ui_graph_current_overview.png`
- `references/ui_graph_inline_value_reference.png`
- `references/ui_shuttle_reference.png`
- `Docs/KIAS/CORRECTION_REPORT.md`
