# Правила проекта Monolith (SS14 / RobustToolbox / C#)

## 1. Инструменты и пути (Windows)
- Для создания и правки файлов используй встроенные инструменты `create_new_file`, `edit_existing_file` и `single_find_and_replace` (они сами открывают вкладки в VS Code).
- Для поиска используй `file_glob_search` и `grep_search`. Перед вызовом `read_file` или `edit_existing_file` убирай `.\` в начале пути и заменяй обратные слэши `\` на прямые `/`.
- Терминал (`run_terminal_command`, PowerShell) используй только для сборки/тестов `dotnet` и проверки `git status`, не используй Linux-команды (`ls`, `find`, `grep`).

## 2. Архитектура SS14 и код C#
- Стек: C# (.NET, SDK 10.0.100), ECS на RobustToolbox. Сборки: `Content.Shared` (общая логика и сеть), `Content.Server`, `Content.Client`, `Resources/Prototypes/` (YAML), `Resources/Locale/` (Fluent), а также кастомные сборки форка (`Content.Shared_Forge` и др.).
- Проверяй реальные сигнатуры API в коде и `RobustToolbox/`, не выдумывай методы и поля.
- В ECS разделяй состояние (`Component`) и логику (`EntitySystem` с `SubscribeLocalEvent`). Для сетевых компонентов проверяй `[NetworkedComponent]`, `[AutoGenerateComponentState]`, `[AutoNetworkedField]` и `Dirty(...)`.
- Стиль: UTF-8, 4 пробела, макс. 120 символов, без пробелов в конце строк. Не меняй несвязанные файлы и не делай разрушительных `git`-операций без согласования.

## 3. Команды сборки и проверок
- Сборка: `dotnet build --configuration DebugOpt --no-restore /p:WarningsAsErrors=nullable /m`
- Тесты: `dotnet test --no-build --configuration DebugOpt Content.Tests/Content.Tests.csproj -- NUnit.ConsoleOut=0`
- YAML-линтер: `dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj --no-build`

## 4. Формат работы и язык
- Перед сложными изменениями сначала предложи краткий план и дождись подтверждения. Не меняй файлы, если запрошен только анализ.
- Пиши блок размышлений кратко (2–4 предложения). Все размышления, комментарии и финальный ответ должны быть строго на русском языке.
