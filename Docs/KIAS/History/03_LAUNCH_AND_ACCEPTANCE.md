# 03 — Финальный BAT, START, прогресс и допуск

## Неприкосновенное условие

При двойном щелчке `Run_KIAS_Tests.bat` **никакого неявного `-Diagnostic`**. Показать режим `STRICT FULL GAME`, 40 кораблей, SHA билда/карты/сценария, A–B–B–A и бюджет прогрева/прогона. До команды `START` не запускать долгие сборки/серверы/тесты. `EXIT` = отмена. `-Diagnostic` только явный и не сертифицированный.

## После `START` — ручной режим и автоматический запуск агентом

Сборка matched Release A/B → строгий функциональный preflight (включая Debug/Release compilation/regressions/27 graphs/комнаты/получение 4 клиентов, DATA, crew, docking, PDC/FTL/etc) → Quick с реальными A/B inputs/postconditions → полный A–B–B–A. `-CheckOnly` = быстрый строго проверяющий readiness без удаления артефактов; `-Profile` отдельно измеряет обычную стоимость KIAS. Не допускать `-EssentialPreflight`/waivers/deferred вместо strict default. Любой FAIL = остановить и собрать архив с честным статусом. Не обрезать обязательные игровые категории, чтобы «успеть».

## Понятная консоль с ETA

Обновлять не чаще одного сообщения за 5–10 секунд:
`этап 5/9 | B1/4 | BURST | тик 25100/108000 | 23.2% | measured TPS 57.2 | elapsed 00:08:42 | ETA этапа 00:29:11 | ETA всего 02:04:18 | clients 4/4 | KIAS cards 1040/1040 | events delivered/verified N/M | errors 0`.
ETA рассчитывается по измеренной wall-clock скорости отдельных A/B прогонов (не считать, что при падении TPS один measured 30m run займёт 30 минут). При невозможности — `ETA calculating`. Сохранять `progress.json`, `launcher-stage.json`, потоковый лог, status; atomic writes и recovery. Нельзя мешать работе чужих процессов.

## Checklist перед выдачей READY

1. На актуальной ветке поменян DATA драйвер и **0 mass Unanchor**. 2 цикла DELETE/RESTORE под чистовым полом PASS на A/B40, ни одного coil, zero floor tiles mutated.
2. Crew multiple critical/death/recover/revive cycles PASS; повторная волна не падает.
3. Four real InGame sessions alive throughout soak; dock/undock repeated without duplicate joints/client errors.
4. Все 27 графов, scanner interior/exterior, flight+PDC+combat, firing/ammo, radiation/light, atmosphere, collision, hull repairs, ROOM/geometry, power/DATA postconditions — green в настоящем composite A/B40.
5. Собран отдельный `COST_OF_KIAS.md`: +~1мс p50 подтверждён либо уточнён на **валидных** реплицированных измерениях и разбит на top sources с conf/calls/alloc/residual. Нет скрытой суммы медиан.
6. `Run_KIAS_Tests.bat -Quick` и **строгий** `Run_KIAS_Tests.bat -CheckOnly` PASS на свежем манифесте. Первый запуск без аргументов показывает `STRICT`, ждёт `START`, а не автоматически проходит Diagnostic.
7. После 1–6 агент **сам автоматически запускает корневой BAT, подаёт в stdin `START` и проходит весь строгий FullGame A–B–B–A**. Не запрашивать подтверждение владельца: разрешение уже дано. Проверить, что режим `-Diagnostic` не подставился; зафиксировать фактическую CLI-команду, SHA источников, stdout, stderr, exit code и полное время серии.
8. Серия валидна только при **4/4 VALID**, полном покрытии сценария, отсутствии fatal/error клиент-серверных сбоев, совпадении запланированных входов, всех обязательных native postconditions, стабильных 4 клиентских сессиях и корректных исходных метриках. Если FAIL — архив результатов сохраняется, ошибка исправляется, обновляются matched Release builds, прогон повторяется как отдельная новая серия; старые INVALID не удалять и не смешивать с новыми.
9. Только **после полной попытки ABBA** агент выдаёт `FULL_ABBA_PASS` или `FULL_ABBA_FAILED`/`BLOCKED`. Отчёты и сырые данные упаковываются и при PASS, и при FAIL. Передать `FULL_ABBA_REPORT.md`, `COST_OF_KIAS.md`, итоговый архив и точные пути.
10. При последующем **ручном** запуске человеком `Run_KIAS_Tests.bat` по-прежнему ждёт `START` и не должен самостоятельно запускаться от двойного щелчка без этой команды. Agent-run — автоматическая подача `START`, не обход strict gates.

## Обязательный протокол unattended запуска

- После PASS `-CheckOnly` агент запускает строгий `Run_KIAS_Tests.bat` без параметров, передавая `START` через перенаправленный стандартный ввод (PowerShell launcher уже умеет `[Console]::IsInputRedirected` и `ReadLine()`). Например, автоматизировать через `cmd.exe` с `echo START | call Run_KIAS_Tests.bat` и `KIAS_NO_PAUSE=1`; проверить фактическое поведение pipeline локально. **Не писать `-Diagnostic`** и не подменять скрипт коротким сценарием. Если перенаправление недоступно, допускается строгий эквивалент `pwsh ... launch.ps1 -FullGame` без `-WaitForStart`/диагностических флагов, но в отчёте раскрыть путь запуска.
- Ждать на протяжении всего длительного теста: не запускать его в фоне без механизма ожидания завершения и сбора кода возврата; обновлять журнал прогресса/ETA и хранить логи. Отмена неиспользуемых посторонних процессов запрещена.
- Не пересобирать DLL/не менять исходники во время ABBA. На FAIL сохранить артефакты и источник отказа, исправить код только **после** остановки запуска, пересобрать matched pair и начать отдельную чистую серию.
- `FULL_ABBA_REPORT.md` обязательно содержит таблицу A1/B1/B2/A2, duration, tick samples, event coverage, invalid runs, median/mean/p95/p99 и проверенную causal attribution +1мс. Если невалидно, явно написать `NO_VALID_ABBA_CONCLUSION`.
