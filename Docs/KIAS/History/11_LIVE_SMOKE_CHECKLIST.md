# KIAS in-game smoke checklist (real user path)

**Set-up:** local test map/server via built Release client/server, KIAS core + DATA topology, Atmosphere and Crew roles/servers, controller rack + programmer + service multitool, 2 rooms, 2 speakers, 2 scanners with Connector modules, relay, ordinary switches, one suppression module + valid cartridge, air sensor, FireAlarm/AirAlarm, vents and scrubbers, real source/sinks for DeviceLink. Keep unrelated saves safe. Prefer sandbox test map.

For every item write `PASS / FAIL / NOT VERIFIED`, screenshot/log path, actual observation, and relevant test name. «Button clicked» isn't enough; observe the world action.

## Programmer visual route

- [ ] Open at standard 1200×720, RU. `Конкретные устройства` clear and hierarchical; `▾ Мостик` grey name adjacent to arrow, device rows indented twice, readable name, short `#abcdef`-style alias. Capture screenshot.
- [ ] Several same-name scanners and speakers: differentiate by short ID. Click Diagnose on one, full persistent identifier available, match correct physical device.
- [ ] Search room name, short prefix and full 12-character ID; expand/collapse and return; no wrong specific target. A named room before unnamed room; room with no name shows `Помещение N`.
- [ ] A long `ALL/ANY` and SPECIFIC profile name is readable **without waiting for hover**. Search focus stays after a BUI update.
- [ ] `Шаблон:` visible next to dropdown. Select, load preset, edit graph, WRITE, eject/reinsert; inspector/canvas not clipped. Repeat at 850×500 and 1600×900, screenshots.
- [ ] Speaker graph: StringConstant Russian phrase → Speaker.Message, event pulse→Speaker.Announce; actual speech heard/read. ALL reaches 2, not excluded third; no duplicate speech.

## Hardware visual route

- [ ] Integration kit physically ~20% smaller than previous 0.75 sprite scale; pickup/craft unaffected; screenshot in world/in hand.
- [ ] KIAS 32×64 core looks like a standalone full-height machine beside wall/grille/power/data cables; no accidental horizontal overlay masking body; top and lower chassis are interactable, correct tint online/offline. Screenshot from original camera placement plus control configuration.

## Relay functional route

- [ ] Select relay channel via screwdriver; get local popup naming **new** channel. Shift+click inspect shows new channel and open/closed.
- [ ] Alternative verb change gets correct feedback. Channel isolation actually switches only selected network (DATA/MV/LV/HV depending real enum), other channels stay unchanged.
- [ ] Open and close relay; test powerless/unauthorized: no switch and no success popup. Tooltip remains readable if underfloor/partially hidden.

## Scanner functional route

- [ ] Start Range 10: several anchored valid integrated devices in zone. Diagnose membership and available controls, pick physical target.
- [ ] Change Range to 2: devices outside now **not connected** and no longer respond to KIAS; previously connected stale names do not remain falsely online. Nearby devices remain.
- [ ] Increase Range to 10 again: eligible devices reconnect and commands work. Two overlapping scanners: A→B handoff after shrink; deleting B removes auto assignment.
- [ ] Remove Connector module, unanchor scanner, power off, move target to another grid: reachability/membership truth; direct kit never gets overwritten; normal AirAlarm sensor/vent/scrubber DeviceList remains usable.
- [ ] Unanchored AirAlarm beside scanner: no endless topology/UI offline flicker across ~30 server ticks. Rack buttons remain clickable under BUI refresh.

## Button / speaker route

- [ ] Put the exact player-reported «button/switch» beside speaker, inspect multitool default link list. If invalid, no misleading speaker recommendation; if valid, complete link and observe actual meaning/event with nonblank message.
- [ ] Legacy switch→valid vanilla sink works. KIAS emergency button→receiver/graph still works. Graph Speaker stays functional and scoped to selected target(s).

## Fire suppression route

- [ ] Inspect installed suppression cartridge. Place module online with Atmosphere role, functional power, DATA and valid configuration.
- [ ] Ignite **real** controlled small fire. Trigger module manually: visible foam/reagent and actual fire reduction/extinguish through native atmos simulation, cartridge consumed once, event/log feedback real.
- [ ] Trigger new cartridge via `KiasSuppress` port, again via `Suppression.Trigger` on controller; if preset should fire, induce actual FireAlarm transition and see preset action (no direct fake Trigger shortcut).
- [ ] Empty cartridge/power loss/role missing/Testing: no success, no reagent spawned, no cartridge consumed; user gets actionable reason when interacting manually. No multiple spawns on one pulse.

## Overall negative/regression smoke

- [ ] Native air alarm DeviceList (sensor+vent+scrubber) and port-link mode both selectable. Real `AtmosDanger` and clear still trigger graph preset.
- [ ] Existing fauna detection signal, Russian speaker text, 2–4 position rotary mapping, external scanner direction, DATA cable masks, ALL Lighting/groups, integrated lights OFF→ON, card name WRITE, rack refresh stability work as previously.
- [ ] Final release build and launched processes correspond to **this source revision**, not cached stale binaries. Capture git HEAD/diff and logs.

## If physical GUI automation is unavailable

Do not report these as PASS. Run native headless client UI interaction + real server integration tests for all reachable cases, record their limits, leave a short specific list for a human tester. Start the client's run script anyway if allowed, and report accurately whether the game reached MainScreen.
