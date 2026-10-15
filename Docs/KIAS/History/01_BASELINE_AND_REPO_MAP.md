# 01 — База исходников и существующая инструментализация

## Fingerprint каждого запуска

Собери `git status --short`, `git rev-parse HEAD`, `git rev-parse origin/main`, `git submodule status --recursive`, `dotnet --info`, `global.json`, `uname`/Windows build, процессор/ядра, RAM, `DOTNET_*`, GC server/workstation, режим энергосбережения, размеры пулов памяти, настройки ядра, номер commit `briarKIAS.yml`. В промежуточных папках НЕ работай на пользовательской ветке и не делай reset.

На момент проектирования пакета: upstream `Forge-Station/Monolith main` был `3226889714d52cad147566c623dd7f18debd21ed`, ветка KIAS после добавления карты — `38c67ac61a0f2565e372c66431590e37da5e7ed0`, `RobustToolbox` gitlink `96c6bddcebab253182a0e0eeec8e39d6a87e8264`. **Не воспринимай эти значения как вечные**: новая локальная правка программатора может быть незакоммиченной или newer HEAD. Зафиксируй текущие фактические SHA.

Сверь разность base/branch, включая KIAS-интеграции, правки `DeviceLinkSystem`, UI, прототипы; не делай сравнение «последний main» и «старый KIAS» как эталон. Используй доказуемую общую базу.

## Реальные средства движка

Подтверждённые исходники:

- `RobustToolbox/Robust.Server/BaseServer.cs`: Prometheus histogram `robust_server_update_usage` с `Inputs`, `EntitySystems`, `EntityEventBus`, `PreEngine`, `NetworkedCVar`, `Timers`, `AsyncTasks`, `PostEngine`, `GameState` и иными этапами; `robust_server_uptime`, `robust_server_curtime`, `robust_server_curtick`.
- `RobustToolbox/Robust.Shared/Timing/GameLoop.cs`: `robust_game_loop_frametime`; `IGameLoop.Input / Tick / Update` (проверить точную семантику до инструментирования).
- `RobustToolbox/Robust.Shared/GameObjects/EntitySystemManager.cs`: `robust_entity_systems_update_usage` (label имя EntitySystem).
- `RobustToolbox/Robust.Server/DataMetrics/MetricsManager.cs`, `MetricsManager.MetricsServer.cs`: HTTP Prometheus endpoint `/metrics/`, default localhost:44880, .NET runtime counters.
- `RobustToolbox/Robust.Shared/Profiling/ProfManager.cs`, `ProfManager.Tracy.cs`: встроенный profiler `prof.enabled` и Tracy `prof.tracy.enabled`.
- `RobustToolbox/Robust.Shared/CVars.cs`: `metrics.enabled`, `metrics.host`, `metrics.port`, `metrics.runtime`, `metrics.runtime_gc`, `metrics.runtime_contention`, `metrics.runtime_thread_pool`, `metrics.runtime_jit`, `metrics.update_interval`, `net.tickrate`, `net.pvs_async`, `prof.enabled`, `prof.tracy.enabled`.
- `Content.Benchmarks/Program.cs`: BenchmarkDotNet; локальные microbenchmarks НЕ эквивалентны живому серверу.
- `Content.IntegrationTests/Tests/_Forge/KIAS/KiasFleetTests.cs`, `KiasControllerStressTests.cs`: уже умеют мерить tick/ms/bytes; создают искусственные 200 гридов / контроллеры — полезно как шаблон, но не вместо Briar.
- `Content.Server/_Forge/KIAS/KiasUpdateMeasurement.cs`: `Stopwatch.GetTimestamp / GC.GetAllocatedBytesForCurrentThread`, включение через `KiasSystem.MeasureUpdates`; отслеживает только охваченные методы Update, НЕ все event callbacks.
- `Content.Server/_Forge/KIAS/KiasSystem.cs`: dirty queue, около 8 grid rebuild за Update. Проверять рост очереди.
- `Content.Server/_Forge/KIAS/KiasDefenceSystem.cs`: projectile index пересобирается примерно каждые 0.1 с; `KiasPeriodicScheduler`, до 40 grid scans за Update.
- `Content.Server/_Forge/KIAS/Controllers/KiasControllerRuntimeSystem.cs`: bounded queues; до 64 external events, 256 work items и 128 commands за Update, общий бюджет вычислений — проверить без опоры на старые числа.
- `Content.Server/_Forge/KIAS/KiasNavigationSystem.cs`: обнаружение FTL прибытия в радиусе Horizon.
- `Content.Server/_Forge/KIAS/KiasSafetySystem.cs`, `KiasProtocolSystem.cs`: требования к настоящим AtmosAlarm/FireAlarm и интеграции.
- `Scripts/bat/buildAllRelease.bat`: `git submodule update --init --recursive` и `dotnet build -c Release` — использовать подходящие локальные рабочие деревья, не перепривязывать движок в пользовательской ветке.
- `Resources/ConfigPresets/Forge/corvax.toml`: `net.tickrate = 60`; итоговую конфигурацию для бенчмарка сделать отдельной и идентичной для A/B.

Ссылки:
- https://github.com/Forge-Station/Monolith/tree/main/Content.Benchmarks
- https://github.com/Forge-Station/RobustToolbox/blob/master/Robust.Server/BaseServer.cs
- https://github.com/theg33ka/Monolith/tree/KIAS/Content.IntegrationTests/Tests/_Forge/KIAS
- https://docs.spacestation14.com/en/general-development/tips/config-file-reference.html

## План внедрения без глобальных изменений

Размести instrumentation и benchmark facilities в отдельном namespace, например `Content.Server._Forge.KIAS.Benchmark` для B и отдельном нейтральном namespace для общего replay и A. Для собирания точного общего tick-time используй адаптер к существующему lifecycle/metrics с **одинаковым минимальным патчем в A и B**. Не используй рефлексию на приватные поля в production-коде, если есть более устойчивый API. Сравни overhead коллектора отдельно пустым прогоном ON/OFF.

## Конфиг пример (проверить версию / синтаксис)

```toml
[net]
tickrate = 60
pvs_async = true

[metrics]
enabled = true
host = "localhost"
port = 44880
runtime = true

[log]
enabled = true
```

Все используемые CVars подтвердить запуском сервера; открыть `http://localhost:44880/metrics/` и проверить реальные имена histogram series (Prometheus может добавлять `_bucket`, `_sum`, `_count`). Не выключать logging, если он нужен для диагностики; одинаковый уровень на A/B, избегать log-spam.

## Проверки границы доказательств

- Считать tick wall cost, simulation delay / backlog и latency событий **раздельно**.
- Метрика имён EntitySystem показывает Update, но обработчики KIAS могут исполняться внутри чужого Update/event processing; для атрибуции добавить лёгкие tags/zones around selected event handlers в диагностическом прогоне.
- Несобранный snapshot / невалидный compile / ошибки прототипов означают `BLOCKED`, не `PASS`.
