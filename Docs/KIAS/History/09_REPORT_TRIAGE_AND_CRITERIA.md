# 09 — итоговый отчёт, поиск багов и критерии приёмки

## Функциональный этап (первый результат)

Создать `FUNCTIONAL_REPORT.md` + JSON results. Таблица 27 presетs: название, физический сценарий, датчик, карточка/graph hash, целевой output, latency, PASS/FAIL/BLOCKED/INCONCLUSIVE, evidence; дополнительные cross-tests 21 scanners/13 suppression/10 speakers/6 flash detectors/4 racks и UI-программатор. Перечислить все отсутствующие physical targets и конфликты (нет `fire-clear` card, LightController, потенциально неподключённые alarm, несовместимое оружие для PDC). Если есть BLOCKED, пользователь получает **минимальный список предметов/локаций/проводов** с reason и intended fix; никаких silent modifications.

## A/B отчёт

### Основная таблица

| Поле | A Vanilla | B KIAS | Delta | Метод |
|---|---:|---:|---:|---|
| mean tick ms | – | – | – | 72000 tick samples |
| median tick ms | – | – | – | raw ticks |
| p95/p99/p99.9 tick ms | – | – | – | raw ticks |
| max tick ms | – | – | – | full trace |
| >16.67/33.33/50/100 ms | – | – | – | threshold counts |
| process CPU time / CPU% | – | – | – | process/os metrics |
| alloc B/s / process heap | – | – | – | .NET metrics |
| GC pause time / count | – | – | – | runtime counters |
| end-state active entities/projectiles/fires | – | – | divergent | world observer |
| scenario input event delivery | – | – | identical expected | hash/tick validation |
| successful actual engine effects | – | – | potentially divergent | event logs |
| KIAS graph/sensor/rebuild/defence stats | N/A | – | N/A | KIAS trace |

### Интерактивная HTML-страница

- Верхние summary metrics, hashes/fingerprint, ясный статус `VALID / INVALID / PARTIAL`.
- Overlaid tick cost A/B по времени, отдельная visual phase backdrop, zoom/brush, tooltip с game tick. Не ограничиваться сглаженной линией: raw peaks должны быть видимы.
- Log-scale latency histogram/box distributions p50/p95/p99/p99.9; scatter heavy spikes tagged event category.
- «Top 100 spikes» таблица: `run, tick, elapsed, closest game events, GC, entity systems, KIAS spans, queue max, exception link`. Клик по пику — карточка с causal event IDs и строками логов.
- `World Divergence`: количество и типы различий, физическое состояние атмосферы, огня, снарядов, экипажа, движения после KIAS actions.
- `System attribution`: EntitySystem average/p99 overhead и опциональные Tracy flamegraphs как диагностическое приложение.
- Per-ship: сколько событий на каждом, в каких комнатах, сколько успешно обработано, задержка обнаружения, effect.
- Баг-лист: functional failures, regressions (в том числе vanilla), exception types и воспроизведение.
- Скачать raw csv/jsonl, link/source hash сценария, свободно открывается локально без внешнего интернета/CDN (или поставляются ассеты).

## Как формулировать вывод

Не «KIAS вызывает лаги», а например (шаблон без цифр): «При одинаковой входной частоте 40 гридов и event schedule p99 B/A изменился на X%; основная разница в ...; N событий diverged из-за работающей ПКО/пожаротушения; 3 spike examples: tick..., причина...». Если много functional BLOCKED и пропущенных событий — вывод только `PARTIAL / INVALID FOR FULL KIAS`.

## Пороговая диагностика (не жёсткие arbitrary performance pass/fail)

- Явный предупреждающий уровень p99, когда p99 tick >16.67 ms при 60 TPS или ~>33 ms при редких пиках — эксплуатационно значимо; конечное решение по игре требует сравнения с baseline.
- Любой crash, fatal exception, loss of entity state, corruption, infinite loop, permanent graph stall, processing backlog unbounded — `FAIL`.
- Любой скрытый scenario replay mismatch, пропущенный tick, невалидный baseline, несогласованные resource/prototype files — `AB_INVALID`.
- Полный `PASS` функционального gate — фактические доказательства по 27 шаблонам **на соответствующих fixtures**, при этом исходная карта может оставаться `BLOCKED` на тех действиях, где отсутствуют физические зависимости. Не маскировать это одной цифрой.
- Rerun A/B with instrumentation OFF для проверки, что profiler сам не сделал существенный overhead.

## Доставка issue / patches

Каждый баг создаётся в `BUGS.md` с краткими reproducible steps, ship UID/stable ID, точной веткой SHA, source location, expected/actual, ошибками и частотой. Прямые исправления KIAS после снятия baseline проводить **в отдельной patch branch**, не смешивать code changes во время A/B. Если после функционального gate нужны fixes: зафиксировать before/after SHA и **полностью повторить обе сборки** на идентичных исходниках для нового результата. Нельзя сравнивать A-v1 с B-v2.

## Технические acceptance criteria

1. Список всех 27 каталоговых схем, карточек и slots автоматический.
2. Обе сборки Release, одинаковый base/engine/config, объяснён overlay KIAS.
3. Briar загружается без unresolved prototype/UID errors, scanner module reserialization verified.
4. Реальные smoke tests покрывают Atmos/Fire/Hull/Collision/FTL/Weapons/Crew/Power/Graphs/UI с evidence.
5. 40 stable ship IDs в обоих режимах, coherent vanilla-part inventory.
6. Один неизменный scenario JSONL и checksum, event log deterministic by tick.
7. A/B 20*60*60=72000 **recorded** ticks в каждом, missing ticks = 0 (если есть realtime dilation — отдельно от tick sample count).
8. Полное raw telemetry, 100 top spikes, A/B delta и процент unexpected divergence.
9. Report generated из raw measurement artifacts, без sample fake datasets.
10. Критические issues перечислены без сокрытия, исходный пользовательский грид/ветка не повреждены.
