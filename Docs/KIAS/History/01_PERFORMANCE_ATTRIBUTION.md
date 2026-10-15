# 01 — Причинная атрибуция дополнительного ~1 мс/tick

## Важное различие

`median(B.wholeTick)-median(A.wholeTick)` **не равно** сумме разностей медиан отдельных systems. Поэтому отчёт обязан содержать (1) точный p50 полного тика, (2) аддитивное распределение **mean difference of per-tick exclusive costs**, (3) статистическую оценку того, какие участки типичного/медианного тика изменились, без ложной арифметики квантилей. Не выдавать результаты неудачной ABBA после mass-unanchor за обычную стоимость KIAS.

## Сбор

1. На зафиксированных Release A/B с одинаковыми engine, baseline, прогревом, схемой мира, tickrate 60, правилами, конфигурациями, scheduler/RNG seed и клиентами провести 3+ независимых запуска на **1/10/40** grid в IDLE и реальных умеренных MIXED. До/после KIAS оптимизации сравнивать **тот же B-сценарий**; сравнение A/B — полная цена функции, не чистый прирост ускорения. В отчёте отдельно учесть фактическое число физических тел/fixture/contact, онлайн-сканеров, карточек, персонажей и проигранных воздействий.
2. Дешёвые scope-markers с `Stopwatch.GetTimestamp()`, `GC.GetAllocatedBytesForCurrentThread()` и заранее выделенными счётчиками; отдельно `inclusive` и `exclusive`; nested не суммировать с parent. Снимать за каждый тик полный loop TickStart..TickStop, Physics, content systems, callbacks (их `Update` не видит), poll/observer/driver, JSON/Prometheus overhead и остаток. Не сериализовать события на каждом замеренном hot callback. Точный C# stopwatch-to-ms перевод. Вложенный Stopwatch и counters надо калибровать экспериментом instrument ON/OFF на одной сборке.
3. Отдельные поля по каждой KIAS части (ms/tick, вызовы/tick, bytes/tick, peak, p95/p99 execution duration, event latency, queue pending/dropped):
   - Crew Update/Scan/Threat/Fauna/registered-person/transponder, локализация персон, spatial queries;
   - RoomTopology + RebuildCoverage + integration/CanControl, counts of dirty tiles, rebuild frequency;
   - Controller Runtime Boot, Index, Timers, input event, queued work, VM eval, Command fanout, Execute; `KiasControllerIoSystem.Emit/Command`, каждый actuator, вложенные команды/GC;
   - KiasSystem topology, dirty queue/rebuild, device lifecycle + callbacks;
   - Protocol, Safety, Defence/PDC, Power, external sensors, navigation, audio/recorder and any unexpectedly large system;
   - Engine Physics: body/fixture/broadphase/contacts counts; cost specific queries and common physics callbacks, other affected engine systems; real and observer-driven allocations;
   - Native driver, proof observer, client/network sync, collector/checkpoint, GC and **unattributed residual**.
4. Сохранять потиковые сырые CSV/JSONL без дыр и без contamination записью checkpoint внутри измеренного тика, `phase`, `world divergence`, wall CPU, GC pauses, throughput TPS, p50/p95/p99/p99.9/max, top100 spikes с типом события/числом KIAS очередей/GC physics. Измерить стабильность >16.67 мс и recovered state.

## Причинные проверки

- Сопоставлять систему с собственным фоновым профилем, затем диагностические ablation внутри B: runtime graph execution off (с сохранением 1040 сущностей), room periodic scan off, control of topology churn/room rebuild, profiling instrument on/off, physics contacts/proxies/lookup cost. Каждый эксперимент **только в test build**, с явным описанием изменённых игровых последствий: ablation не полноценное A/B gaming.
- Отдельно показать, сколько стоит простой факт 40 KIAS гридов idle без очередей, сколько при сигнализации, и сколько изменяет событие DATA с простым удалением/установкой проводов. DATA world divergence и PDC-перехваты вынести из «нормальной дополнительной медианы» в игровые phase comparisons.
- Для уверенности repeated runs + интервал неопределённости (например, block-bootstrap по группам последовательных тиков и диапазон по повторениям). Если runtime не может объяснить значительную часть difference — `ATTRIBUTION_INCOMPLETE` + top unknown suspects, а не фантазии.

## Формат обязательного `COST_OF_KIAS.md`

Показать A и B mean/p50 tick, затем **таблицу exclusive MEAN differences** с raw data links:

| source group | A exclusive ms/tick mean | B exclusive ms/tick mean | delta | calls/tick | alloc B/tick | evidence confidence |

Группы: core/topology, crew+rooms+integration, runtime VM, runtime actuation+IO, protocols/defence/other KIAS, Physics exclusive, common engine/non-KIAS systems, GC/other loop, native observer/instrumentation, residual. NESTED детализация Runtime/Crew/Room — отдельная таблица **не суммировать повторно**. Колонки `explained fraction of delta MEAN`, а не «% от median delta». Отдельная секция «что происходит в типичном p50 whole tick» с честной оценкой; минимум 3 проверяемых кандидатных оптимизации, измеренный before/after, доказанные функциональные invariants.
