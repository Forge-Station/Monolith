# Historical TODOs = regression contract, not fresh implementation queue

The user's first TODO list is current. These previous passes were already implemented/reported by a prior agent and **must not be repeated from scratch**:

## Previous pass A — already made

1. Specific device matching by full persistent ID, grouped room/device palette; no longer displays only repeated `модульный сканер` without identity.
2. Compact wall-mounted button/key/speaker/scanners, 30% smaller loose modules, previously -25% smaller integration kit/cartridge/controller card, key/rotary 50% smaller.
3. Rotation in world for directional devices, not tied to camera rotation, no rotation after anchor.
4. DATA cable uses 16 layout states/matching power-cable geometry.
5. Diagnostic mode now includes actual outputs and hardware settings of rotary 2–4 positions/signals.

## Previous pass B — already made

1. Advanced fauna threat (carps/mining crabs/syndicate bots) should generate real signal.
2. Multitool Diagnose/outputs monitoring.
3. Speaker Russian/Cyrillic text stays intact end-to-end.
4. Core has authored 32×64 sprite.

## Previous pass C — reported fixes in current Docs/KIAS

1. Card item name after actual WRITE.
2. Speaker `Message` and `ALL Speaker` event dedup per target.
3. Inspector relevant fields, inline constants, typed port help.
4. `ALL Lighting` direct fixtures distinct from `LightController` groups, integrated light OFF→ON.
5. Normal `AirAlarm DeviceList` configuration with sensor/vents/scrubbers, even alongside DeviceLink ports.
6. Real `atmosphere` preset gets `AtmosDanger` via native sensor → alarm path.
7. Rack button stability and stale port tooltips; absence of scanner auto-integrate infinite loop for unanchored AirAlarm.

**This patch must re-run these as regression tests** especially after changes to scanner integration, DeviceLink button handling, relay topology, and UI rebuild. If something is broken again, correct the regression; don't create a second hidden automation system or duplicate old profiles.

## Persistence and migration rules

- Existing 12-character identifiers continue to load; short UI prefix needs no serialization migration.
- Saved SPECIFIC bindings remain entity based; no remapping from alias or room number.
- Legacy `LightController` profile IDs and saved controller-card programs remain stable; do not silently reinterpret older cards.
- Changing scanner ownership representation must support map load with `KiasIntegrated.Scanner` already populated; carefully handle orphaned/deleted scanners without losing `Direct=true` installations.
- Preserve normal `DeviceNetwork/DeviceList`; do not clear those lists when scanner range changes. They are not the same concept as KIAS automatic integration.
- `KiasSuppression` cartridge and `Foam` simulation use already existing prototypes/systems. Avoid introducing incompatible cartridge item IDs.
- No format-breaking changes without versioning, migration test, and clear explicit justification.
