# Regression matrix, UI acceptance, and performance budget

Every test must distinguish: **observed before**, **actual after**, and **not tested**. A unit test returning `true` from a helper is insufficient proof of a visible gameplay effect.

## Functional tests: minimum matrix

| ID | Tests that must exist/run |
| --- | --- |
| T01 | 12-digit underlying ID preserved; prefix length 6 by default, uniqueness under forced collision (two full IDs same initial 6), offline peers and repeated gridded entities; stable after save/load, user sees/can search full ID; SPECIFIC target unchanged. |
| T02 | Named room first sorted; unnamed numbers; clear parent/child layout and indentation; collapsed state persists during simple refresh; search matches room/device/full ID/short ID; no cross-room leak. |
| T03 | 850×500, 1024×600, 1200×720, 1600×900; RU and EN; 100%/125% UI scaling when possible; palette/inspector/canvas nonzero widths; no overlap or clipped primary controls; long profile names readable without hover-only dependency. |
| T04 | `Шаблон:` and `Template:` localized near preset dropdown at min/full widths; clicking load still writes/updates expected graph, no misplaced legacy import. |
| T05 | `KiasIntegrationKit` Sprite exactly intended new scale, RSI frame/state valid, collision/pickup and Item size unchanged; compare floor/in hand in native client. |
| T06 | Native 32×64 core in wall/cable/grille/floor context: sprite not wrongly hidden/in front; selection/interaction align with chassis; appears expected after map load; online/offline tint still works. |
| T07 | SetChannel through verb and screwdriver; actual actor popup generated once on change; examine string includes selected channel+closed/open; no popup for unauthorized/unchanged; selected cable channel disconnected only when relay open; old network reconnected on channel switch. |
| T08 | Range 10→2→10, source target exits/enters range, 2 overlapping scanners, scanner unanchored/removed/powered off, Connector module removed/reinserted, target relocated to different grid, direct kit untouched, unrelated DeviceList unchanged, stable topology after 30 ticks no repeated invalidations. |
| T09 | Specific button→speaker pair reproduces original suggestion and now either disappears if semantically invalid or actually works; correct source/sink types; normal button→vanilla sink still works; emergency button→receiver still works; speaker full String message + Announce and ALL targeted speaker still works. |
| T10 | Manual verb, DeviceLink signal, and graph `Suppression.Trigger`; power/role/cartridge gating; consumed one on success/none on failure; water/foam object exists at target position, fire is actually suppressed under game simulation, no duplicate recharge; fire alarm event→preset→physical actuation if preset is supposed to trigger suppression. |

## Regression shield from previous KIAS passes

- KiasControllerCorrectionTests, KiasControllerLayoutTests, KiasControllerEditorTests, KiasControllerSelectorTests, KiasControllerBridgeTests, KiasControllerFeedbackTests, KiasControllerMigrationTests, KiasSpeakerTests, KiasControllerRuntimeTests.
- KiasDeviceConfigurationTests (named rooms, range), KiasRelayTests (three cable channels), KiasSpriteTests (32×64 core, rotations), KiasAccessTests, KiasPersistenceTests, KiasFleetTests, KiasControllerStressTests, KiasFaunaTests, KiasVentilationTests, KiasOutputMonitorTests, KiasUiStabilityTests.
- AirAlarm ordinary DeviceList configurator and `DeviceLink` mode both reachable; atmosphere real state transitions deliver graph events and recovery; card metadata after WRITE/eject/reinsert; ALL Lighting (direct) separate from LightController (groups); integrated light can OFF→ON; speaker Unicode Russian message actual speech; no global shortcut for KIAS roles.

## Testing commands (resolve environment-specific details)

```powershell
# Start at Monolith repo root. Capture output and exit codes.
git status --short
git diff --check
# Try focused tests first; no-restore only if restore completed and assets are available.
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter 'FullyQualifiedName~Kias' --logger 'console;verbosity=normal'
```

Run new specific fixtures before whole KIAS. For existing problems with test paths/config/release/runtime, use repo normal supported build commands. Do not infer passing counts from old `Docs/KIAS/VALIDATION.md`.

## True UI tests

Use real Robust UI windows when possible, not only static string asserts. Check `KiasControllerWindow`, `KiasGraphCanvas`, `KiasLocalWindow`, rack and management. Validate `Measure/Arrange`, `DesiredSize`, containment, actual row indent and text color, scrolling and tooltips; keyboard nav, click release, focus after BUI state refresh, hover tooltip correct lifetime.

- Cases: 0 nodes, 30 long named devices (across 5 rooms), many visible ANY/ALL profiles, one huge Cyrillic display name, 12+ nodes + wires, one selected SPECIFIC node.
- UI flow: expand/collapse, search room/ID, change preset, drag node, zoom/pan, open inspector, WRITE/Eject, close/reopen. Minimum resolution with all sections open.
- Visual acceptance: no unreadable palette main names, hierarchy should be distinguishable in actual screenshot, label «Шаблон:» truly visible, full tooltip readable and non-stale.
- Trigger `KiasUiStabilityTests` to guarantee rack button and port tooltip regressions stay fixed.

## Performance limits

- 200 KIAS-equipped grids model, many scanners and 20–200 attached targets on test grid; no N×M full-world scans each tick/UI refresh.
- Range changes/reconnections can do an **event-triggered** neighborhood enumeration or incremental index update. Deduplicate and only `_kias.Invalidate(grid)` when membership/connection truly changes. Bounded work; avoid infinite reentrant `RebuildCoverage` ↔ `ConnectRoom` loops. Measure changes in revision counts after 30 idle updates.
- Default aliases computed on change or at controlled UI state construction, not GUID regenerate per display. Avoid `OrderBy()/ToArray()/Regex` allocations in every entity `Update()`.
- Pressing relay tool should reflood only impacted cable node channels/tiles. Watch unanchored relay and inactive grid edge cases.
- UI tree should not rebuild 100s of Buttons each frame; save collapsed/search state and keep focus while state refreshes.

## Required exact evidence

Record all: test command, test names, pass/fail counts, `buildAllRelease.bat` terminal/exit (not generic dotnet build), `runQuickAll.bat` successful client/server and logs, 3 screenshots before/after when possible, 1–2 line observed actual output for popup, scanner detach, button/speaker and suppression. Mark any absent as NOT VERIFIED.
