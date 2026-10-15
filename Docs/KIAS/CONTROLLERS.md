# Programmable KIAS controllers

## Basic workflow

1. Insert controller card into programmer.
2. Name program.
3. Add nodes/devices/selectors.
4. Connect compatible typed ports.
5. Configure only relevant node parameters.
6. Resolve validation errors.
7. WRITE.
8. Eject card and insert into controller rack.

After WRITE the physical card itself uses the program name.

## Types

- Импульс — instantaneous event;
- Да/Нет — persistent logical state;
- Число;
- Строка;
- Объект;
- typed enumeration.

Impulse and Bool are intentionally not directly interchangeable.

## Selectors

- SPECIFIC — one physical device;
- ANY — any/first available matching device for commands;
- ALL — every matching device.

Selector fields like room/group are **filters**.

## Lighting

`ALL Освещение` means all directly controllable fixtures.

`ALL Контроллеры групп освещения` means physical KIAS group controllers.

These are intentionally different.

## Speaker

Wire a String into `Сообщение`, then an Impulse into `Объявить` or `Тревога`. `Ключ/ID события` is optional context for deduplication/cooldowns and is usually propagated from event sources.

## Inline readability

Constants/timers/comparators/selectors display their important configured value on the node so a graph can be read without opening every inspector panel.

## Saved-card compatibility

The group-controller serialized profile remains `LightController`; `Lighting` is the new direct-fixture profile. Existing version-1 graphs are retained. EnumDomain is optional: legacy unspecified enum ports infer their domain through connected ports, including EnumCompare. The compiler rejects mixed known domains instead of treating unrelated integer values as interchangeable. The original saved graph is not modified by inference.

SPECIFIC ignores selector Room/Group filters; ANY/ALL use them. Changing a device's room/group invalidates cached selector membership. Target power does not remove an integrated device from the control plane while core, DATA and anchoring remain valid; native KIAS machinery still needs power.

A previously shared integer constant feeding unrelated enum domains must be split into one constant per domain. Such a graph reports an explicit enum-domain error; values are never silently reinterpreted.
