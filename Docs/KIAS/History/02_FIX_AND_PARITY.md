# 02 — Обязательные фиксы и проверка сопоставимости

## DATA — ни одного mass `Unanchor`

Проверено: в `KiasTelemetry.cs` сейчас `data.cut` делает `.Unanchor(cable.uid)` для всех `KiasDataCableComponent`, а restore `.AnchorEntity` или `Spawn`. Сам `KiasDataCable` — закреплённый `SubFloorHide`-кабель `Visibility layer: 1`, выпадение игровой `KiasDataCableStack1` при настоящем демонтаже допустимо, **но лабораторный teardown этого делать не должен**.

Алгоритм: снимок исходных entities (proto, grid, tile, EntityCoordinates, rotate, anchored, layer), floor tile map и SHA/checksum для ВСЕХ затронутых клеток → корректное `DeleteEntity`/очередь и подтверждение удаления без дропа предметов → отдельная проверка состояния DATA (изменение KIAS online, no-data path, topo rebuilt) → `SpawnEntity` сохранённого оригинального кабельного proto при той же grid/локальной позиции, корректная инициализация anchored/cable connections/underfloor layer → ожидание карты connectivity и 26 graph cards с сохранёнными програмами/ревизиями. **Не изменять MapGrid tiles и НЕ укладывать Plating вместо уже уложенного пола.** Не пытаться восстановить прежние EntityUid: проверять функционально одинаковое покрытие, индекс и роли. `SubFloorHide` должен сохранить визуально подпольную укладку. Провал lifecycle — STOP и архив.

Гейты: после двух волн на 1 и 40 кораблях число кабелей по proto и grid tile совпадает с исходным; ни одного `KiasDataCableStack1`/orphan Physics тела; число awake physics bodies не растёт; floor tile matrix and hash идентичны; DATA реально offline→online; число активных cards/graph-hashes and expected ports восстановлены. Baseline A без KIAS не может демонстрировать потерю DATA и это **отдельная семантическая дивергенция**: сравнивать нормальный overhead по IDLE и matched effects, а не считать разрушительный DATA этап справедливым одинаковым физическим stimulus.

## Crew

`NativeGameDrivers.cs::ChangeCrew()` падает на проверке `CurrentState != Alive` в повторной волне. Выяснить фактическое состояние и damage, трансфер Mind, регистрацию, действия дефибриллятора/лечения между волнами. Нужен реальный переход Critical→Alive и Dead→Alive; гарантировать `Alive` **до** следующих повреждений, не устанавливая MobState руками. Допустим новый MobHuman/Mind/transponder fixture для следующего повторения, но только при сохранении настоящих medical postconditions и одинаковой A/B процедуре, с полной чисткой предыдущего actor. Провести 2+ повторных цикла на 40 кораблях A/B без exception.

## Clients + Dock

4 настоящих сетевых клиента обязаны быть InGame на всём прогоне. Исправить AFK через нормальную тестовую активность/обоснованную test-only одинаковую настройку A/B; не выкручивать параметры глобального live-сервера. Учитывать реальное wall-clock время при сильном падении TPS, heartbeat не привязан только к simulated tick. Замкнутый native докинг должен пройти connected grid joints, create/update/remove, повторный dock/undock и 4 клиентов **без `joint already existed`**. Искать double creation/network snapshot duplication и fixture cleanup; не whitelist client errors. Общие исправления должны попасть в оба билда A/B. При нулевых connected sessions либо клиентских ERRO/FATL строгий запуск INVALID, независимо от серверных native-postconditions.

## Source/build parity

Build A/B с одним engine/build config, source SHA/Overlay manifest, общей игровой baseline и одинаковым observer/driver; B добавляет KIAS. Физические entity/inventory differences зафиксировать. `briarKIAS.yml` автора не менять: лабораторные копии и auxiliary fixtures отдельны. Корректно разделять same inputs / actual world effects / KIAS response; PDC/suppression естественно меняют world в B. Архив содержит отличия и объяснения, а не заявление о «побайтово одинаковой игре».
