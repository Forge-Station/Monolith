# KIAS documentation

Готовая нагрузочная серия A–B–B–A не означает прохождения строгой игровой приёмки.

Текущий запуск и ограничения покрытия описаны в [инструкции стенда](../../Tools/KiasBenchmark/RUNBOOK.md). [Отложенные, но необходимые проверки](../../Tools/KiasBenchmark/DEFERRED_CHECKS.md) не считаются пройденными. Корневой `Run_KIAS_Tests.bat` ждёт команду `START`; полная серия разрешается только после строгого preflight.

Current documentation is split by audience:

- `SPRITE_PACK.md` — полный набор спрайтов, размеры машин и состояния программатора;
- `SPRITE_PROMPTS.json` — итоговые промпты генерации ресурсов;
- `FAUNA_MONITOR.md` — обнаружение фауны, мониторинг выходов и проверка кириллицы;
- `CORE_SPRITE.md` — новый спрайт ядра 32×64, происхождение и промпт;
- `UI_STABILITY.md` — дополнительное исправление пересчёта сети, кнопок шкафа и подсказок;
- `CORRECTION_REPORT.md` — причины ошибок, решения и совместимость;
- `ARCHITECTURE.md` — current technical model;
- `CONTROLLERS.md` — programmable graph semantics and profiles;
- `PLAYER_GUIDE.md` — how a player actually configures KIAS;
- `VALIDATION.md` — acceptance/test plan and real results after the patch;
- `PARITY_AUDIT.md` — known gaps/regressions and their status;
- `PULL_REQUEST.md` — PR checklist/summary skeleton;
- `THIRD_PARTY.md` — references/attribution notes.

Исторические спецификации и отчёты находятся в `History/`. Они описывают этапы разработки и не заменяют текущую инструкцию стенда. Исходники и референсы арта сохранены в `ArtSources/`; игровые RSI находятся в `Resources/Textures/_Forge/KIAS`.

Important terminology: product name is **KIAS** in both English and Russian text. Do not render it as «КИАС».
# Последующие правки устройств

[Выбор устройств, диагностика, размеры и вращение](DEVICE_IDENTIFICATION.md), [примерка на стене](WALL_DEVICE_PREVIEW.png).

[Исправления и результаты предварительных проверок 2026-10-11](PREFLIGHT_FIXES_20261011.md).

## Поведение после независимого ревью

Сервисный Test выполняет ограниченную проверку копии состояния графа. Настоящие команды и таймеры не запускаются; рабочая автоматика продолжает исполняться. Палитра конкретных устройств поддерживает серверный поиск по имени, помещению, идентификатору и профилю, а также страницы по 256 записей. При потере DATA оптическая камера отключается штатным механизмом. При перестроении сохраняются только валидные несигнальные выходы прежних устройств того же грида.
