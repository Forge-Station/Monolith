# Risk register — proactively break fragile things before the player does

Severity: C = critical functional/regression; H = high; M = medium. Every row needs an explicit check or a justified NOT VERIFIED.

| Risk | Severity | Why it matters | Required proof / defensive action |
| --- | --- | --- | --- |
| R01: short-prefix collision | H | Two devices show indistinguishable `#ABC123` | Forced deterministic colliding identifiers; expand just colliding aliases; SPECIFIC binding still unique. |
| R02: shorter ID accidentally saved | C | Save/load controller program points to wrong device | Compare persisted raw identifier + NetEntity bindings before/after; no `ShortId` serialized as identity. |
| R03: room index/cache stale | M | Devices in wrong named room after moving doors/scanners | Test rename/door/tile movement, 2-sec cache and sorting; do not invoke expensive flood fill each UI frame. |
| R04: min-width collapse | H | Wider palette crushes canvas/inspector at 850×500 | Actual layout test & screenshot for min width, long RU labels, tooltip and preset toolbar. |
| R05: UI BUI redraw eats input | H | Rack button/palette click loses focus when state refresh arrives | Re-run `KiasUiStabilityTests` button-down/update/button-up and popup closure; ensure no wholesale BUI recreate. |
| R06: enum iteration assumptions | H | Relay tool wraps to invalid `CableType` member; popup lies | Enumerate actual supported cable channels; after successful transition report real enum; test each channel with open relay. |
| R07: unauthorized relay interaction | H | Hidden or off-grid actor changes cable topology | Server-side CanOperate, distance/access/tool quality; no silent permission bypass or actorless popup. |
| R08: stale auto-scanner ownership | C | Device remains visibly connected even after range shrink | 10→2→10, target moves, scanner drops/reanchors; inspect actual KiasIntegrated scanner reference and KIAS online status. |
| R09: invalidation loop | C | Scanner auto-connect causes endless grid topology rebuild | Idle topology revision stable; reproduce past unanchored AirAlarm bug, ensure diff before mutation. |
| R10: overlapping scanners | H | One scanner erases another's valid connection | Deterministic tie-break, candidate validity, stepwise handoff; test A/B remove, add and power state. |
| R11: direct kit overwritten | C | Explicit manual integration lost due to auto-scanner | Never change `Direct=true`; direct device remains connected after all scanner range edits. |
| R12: native DeviceList disturbed | C | Fire/Air alarm no longer sees sensors or vents | Re-test normal NetworkConfigurator list/port modes, real `AtmosDanger` preset and air alarm danger/clear chain. |
| R13: speaker accidental global link | H | KIAS button sends blanks to all speakers / bypasses policy | Confirm actual DefaultLinks pair and resolve via real native sink or remove misleading suggestion; normal vanilla signals unaffected. |
| R14: fire suppress consumes with no effect | C | User loses cartridge yet fire stays intact | Real fire, native simulation, reagent/foam test, impact within intended radius; prevent success message if creation fails. |
| R15: fire suppress never actuates | C | Hardcode path exists but role/power/port wrong | Actual manual verb → cartridge, DeviceLink path, graph path, physical fire alarm event path; inspect prerequisite failures. |
| R16: duplicated action | H | Multiple profile sources use cartridge twice | One-shot assertions + ALL selector overlap; no double event/consumption. |
| R17: misleading sprite offset | M | 32×64 core overlaps walls/cables or selection point | Before/after world screenshots with wall/floor/grille; compare other native 2-tile machines, test tint/anchor state. |
| R18: release script false-green | C | Build from wrong directory or `pause` never finishes | Execute from `Scripts\bat`, capture exit code, target output and post-build binary timestamps. |
| R19: launcher false-green | C | `.bat` exits but client/server never start | Confirm child processes/game MainScreen/server listens; capture stderr/exception logs. |
| R20: legacy controllers/presets broken | C | Saved user graphs/ANY/ALL, light groups, speakers silently change | Run old migration/integration/voice/lighting/fleet tests; no persisted enum/profile changes. |
| R21: simulation load spike | H | 200-grid KIAS deployment slows every tick | Benchmark topology + scanner update workload before/after, no unbounded per-frame entity scanning or allocations. |
| R22: CI passes but UI unreadable | H | Layout tests don't see actual appearance | Real native client screenshot/interaction; label and hierarchy proof for standard/minimum size. |
| R23: camera-relative rotations | M | Scanners/core visibly rotate with client camera | Native world rotation tests; core fixed, directional units world-facing, no camera coupling. |
| R24: map load stale references | H | Saved scanner `EntityUid` references missing/deleted on new map | Reload map, clean orphaned refs, no crash, scanner reconnects after proper startup. |

## Red-team scenarios

1. Spawn ~50 devices with long Cyrillic names, force two to share 6-char prefix, put them into two unnamed and three named rooms. Use palette search and add SPECIFIC nodes to each; swap room name; verify correct target remains.
2. Configure two connector scanners with overlapping coverage of an air alarm; set A range to 0, remove B module, then add B back, delete A; repeat 30 ticks with an unanchored AirAlarm nearby; network must stabilize.
3. Put relay across 3 real cable channels with core/servers downstream, open, change channel via tool, inspect current, close; alternative verbs and Shift-examine should be truthful while power/access changes.
4. Link an ordinary SignalButton to recommended KIAS Speaker, and a normal door sink; test the real chosen pair before/after. If «Speaker» is not a valid default, it must not appear; graph string→Announce remains intact.
5. Put suppression module in a working atmos grid with cartridge, ignite controlled fire through native gameplay, activate by manual/graph. Verify actual hazard change, visual effect, one cartridge consumed, status message. Repeat without cartridge, offline server, unconnected DATA and during Testing.
6. Place 32×64 core next to wall/grille/pipes, rotate camera, inspect online/offline tint, click chassis top/bottom, compare before/after captures.

## Anti-pattern tripwires

Do not fix by: hardcoding `Kias` branches inside global speech/power systems; disabling access checks; spawning proxy ECS entities per device/node; wiping `DeviceList`; calling `ConnectRoom` every frame after an unconditional `_kias.Invalidate`; assuming all enum values 0..3; removing all default links globally; changing `KiasCore` into a 32×32 image contrary to request; simply printing «fire suppressed» without fire effect.
