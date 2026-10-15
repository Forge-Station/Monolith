# Target architecture / invariants

## Stable device identity vs compact human label

Persistent identity belongs to `KiasDeviceComponent.Identifier`. Physical binding belongs to `NetEntity`/actual entity. Presentation derives a short unique prefix per grid/visible candidate set. Never serialize or use the prefix as an authoritative target key. An offline but mounted physical device should not cause an ambiguous alias for another device in the same selection UI. A short-name collision merely expands those affected labels. Full ID remains inspectable and searchable. The alias calculation/cache should be bounded, deterministic, invalidated on device inventory change, and not trigger routine KIAS topology rebuild.

## Two complementary scopes

1. **KIAS data path/management**: grid, role, core, DATA connectivity, server authority, power status.
2. **Scanner coverage**: physical reach/module state determining who can be auto-integrated.

`Direct=true` from the **physical integration kit** is a distinct human action. Auto-integration cannot override it. A stale auto-assigned scanner should not remain a valid control endpoint after coverage is lost; a newly eligible scanner may take over deterministically. Avoid turning global DeviceList/DeviceNetwork memberships into KIAS scanner subscriptions: these are different relationships.

## One side effect, one source of truth

- Relay channel adjustment updates component + topology then optionally confirms to the initiating actor, with no emission for routine programmatic polling.
- Suppression executes through existing `KiasActuatorSystem.Suppress` and existing native smoke/water system, subject to role/online/testing/cartridge checks.
- DeviceLink connections only advertised when they map to real matching source→sink gameplay; preserve normal vanilla DeviceLink.
- Graph ALL/ANY/SPECIFIC, typed port schema, and hardcoded high-performance physical subsystems remain as implemented.
- Core 32×64 stays a one-world-object sprite; no invisible duplicated entity for draw order.

## UI hierarchy and performance

Native Robust UI, local labels from `.ftl`, clear section hierarchy and secondary metadata, no per-frame rebuilding. Keep graph canvas usable from min 850×500 through 1600×900. Palette hierarchy expansion/search should work without losing focus; if dynamic sizing is unsafe, prefer simple responsive widths + intentional multi-line labels. Never allow text/tooltip to be the sole discoverability mechanism for core operations.

## System boundaries

Local KIAS classes, `.yml` prototypes and locale first. Changes to generic `DeviceLinkSystem`, `NetworkConfiguratorSystem`, `SignalSwitchSystem`, global Sprite drawdepth or Robust UI need an **actual failing non-KIAS regression** and a narrowly scoped fix. Preserve map/save compatibility, entity status/roles, performance ~200 active KIAS grids.
