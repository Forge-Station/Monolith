# Observations from branch `KIAS` — code-grounded, as of `e32aecc197fed90b5947354bbad7b2c24add7911`

These are **static findings, not proof of in-game reproduction**. Agent must re-check current source and turn hypotheses into observed tests before assigning blame.

| Item | Observed current code | Inference / uncertainty |
| --- | --- | --- |
| ID | `KiasDeviceIdentitySystem.Identifier`: `Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()` + dictionary uniqueness; `Label` puts full `#{Identifier}` in text | Stable data likely fine; only presentation too long. Do not shorten underlying ID or SPECIFIC binding. |
| Rooms | `Rooms()` uses flood-fill (max 16384 steps), 2-second cache; `KiasControllerWindow.RebuildPalette` groups by `.Room` with `CollapsibleHeading`, body and raw `PaletteItem` | Missing explicit secondary hierarchy/padding; collapsed heading exists; do not reimplement room discovery. |
| Palette | `KiasControllerWindow` left `MinWidth=215, MaxWidth=250`; right inspector 300–340; window min 850×500 | Widen carefully with canvas floor and min-size tests. |
| Preset bar | `_presets` inserted directly into `templates`, no adjacent label | Simple localized UI change. |
| Kit | `devices.yml` `KiasIntegrationKit` Sprite scale 0.75 | -20% from current => ~0.60. |
| Core | `kias.yml` `KiasCore` uses 32×64 RSI and offset `(0, 0.5)` on inherited `BaseKiasServer` | Screenshot clearly demands real occlusion/placement examination; cause may be tile overlay/drawdepth; cannot confirm from still. |
| Relay | `KiasRelaySystem.SetChannel` refreshes indexes and topology. `OnTool` changes `(channel + 1) % 4`, without `PopupSystem` notification. `OnVerbs` offers channels. | No visible native examined-channel handler in this file. Need inspect inherited generic examine logic; add KIAS information narrowly. Enum safety required. |
| Scanner | `KiasCrewSystem.RebuildCoverage` rebuilds coverage then calls `KiasIntegrationSystem.ConnectRoom` for connector module scanners. `ConnectRoom` loops nearby anchored targets, `Integrate(target,scanner)` for eligible entities; no explicit inverse unlink pass in that function. | Potential stale `.Scanner` references after shrinking/removing module; must reproduce and implement two-way reconciliation without invalidation loop. |
| Button | `KiasEmergencyButton` inherits `SignalButton`, has `SignalSwitch`, source port `KiasEmergencyPressed`. `KiasDeviceSystem.OnButton` respects `SignalSwitchSystem`. DeviceLink generic graph bridge lives in `KiasControllerIoSystem` and `KiasControllerIoSystem.Commands`. | Exact misleading suggestion **not yet proven**. Inspect prototype `sourcePort.defaultLinks`, inherited SignalButton ports, and particular switch user uses. Do not invent a Speaker sink port. |
| Suppression | `KiasActuatorSystem.Suppress`: requires online, Atmosphere role, non-Testing, cartridge; `Foam` + `StartSmoke` Water + consume + safety publish. `KiasControllerIoSystem.Commands` profile `Suppression.Trigger` calls it; `KiasSuppression` sink `KiasSuppress` calls it. | Source path exists, but no proof foam puts out fire or preset actually triggers in real room. Need test physical effect/trigger conditions rather than duplicate logic. |

## Regression-prone facts from prior implemented work

- `Docs/KIAS/VALIDATION.md` reports first 58, then 71 passing tests for earlier patches; those are **historic claims, not a pass in this run**.
- `Docs/KIAS/UI_STABILITY.md`: previously fixed feedback loop when an unanchored AirAlarm was repeatedly auto-integrated; rack-button focus loss during live BUI refresh; stale tooltip. A new `ConnectRoom` fix MUST NOT revive these.
- `Docs/KIAS/CORRECTION_REPORT.md`: previous work retained legacy group `LightController` ID, separated direct `Lighting`, fixed Speaker ALL gating, AirAlarm DeviceList and `AtmosDanger` chain. These are protected invariants.
- Existing tests in `Content.IntegrationTests/Tests/_Forge/KIAS` cover room naming, relay cable channels, sprite dimensions/rotation, scanner range validity, many controller paths. Extend closest fixture.
- `Scripts/bat/buildAllRelease.bat` exists and contains `cd ../../`, `git submodule update --init --recursive`, `dotnet build -c Release`, `pause`. `Scripts/bat/runQuickAll.bat` exists and starts server/client scripts. CWD is important; process launch != successful smoke.

## Key questions for the implementing agent

1. What exact event and prototype cause the switch→speaker recommendation? Is a pair shown with NO compatible sink or handler?
2. Does losing scanner coverage remove `KiasIntegrated.Scanner`, or simply mark `CanControl` false while old selection persists? What counts as «connected» in UI?
3. Are Relay channels all valid enumerated CableType members? Does an unreadable Shift+click result come from missing ExaminedEvent or from SubFloorHide/Visibility layer?
4. For fire suppression, does a real cartridge spawn, get found by `_containers.TryGetContainer`, and does `StartSmoke` function as intended with `Water` when game atmos/fire actually runs?
5. Core image: what exactly is world tile and render order? Compare base server machine and native 32×64 machinery with same placement.
