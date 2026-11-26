# NpcCrew handoff

For whoever (or whatever) picks up the NPC crew work. Everything below is true as of the `npc-crew` branch head.
Paste this whole file as the opening brief; it is written for an AI agent working in a fresh checkout.

## Where things are

- Repository `VanguardControl/Wolfgate`, branch `npc-crew` (an older copy of the same commits sits on
  `clanker/relaxed-lovelace-q1366b`; ignore it). Never commit to `main`. Keep current work local; never open
  a PR without explicit user permission. If authorized, target `VanguardControl/Wolfgate` and fill in
  `.github/PULL_REQUEST_TEMPLATE.md` with a `:cl:` changelog. Commit as
  `gandalf2k15 <9026500+Gandalf2k15@users.noreply.github.com>` without assistant attribution trailers.
- `AGENTS.md` at the repo root is the rulebook. Read it first, every session. The parts that bite:
  - All new code lives in module folders: `Content.{Server,Shared,Client}/_WF/NpcCrew`, tests in
    `Content.IntegrationTests/Tests/_WF/NpcCrew`, prototypes in `Resources/Prototypes/_WF/NpcCrew`, strings in
    `Resources/Locale/en-US/_WF/NpcCrew`. Cvars for Wolfgate modules go in `Content.Shared/_WF/CCVar/` (see
    `NpcCrewCVars.cs`).
  - Any edit to a file outside `_WF` needs a `// WOLFGATE(NpcCrew): reason` marker (or `START`/`END` block). Then run
    `python3 Tools/_WF/Ci/modules.py --write` and commit the regenerated READMEs. CI runs `--check` and
    `--pr-check`.
  - No license headers. `[Dependency] private X _x = default!;` without `readonly`. One-line `/// <summary>` on
    types and public members. Every player-facing string through Fluent. Prototype IDs start with `WF`.
  - Never edit `RobustToolbox`. Client and Shared code is sandbox-checked at client load (no `BinaryWriter`,
    `Process`, collection expressions into `List<T>`, `string += char`).
  - Only one system may subscribe a given (component, event) pair. Grep Content.Server and Content.Shared for an
    existing `SubscribeLocalEvent<ThatComponent, ThatEvent>` before adding a directed subscription on an upstream
    component; a duplicate compiles and crashes the server at startup. Broadcast subscriptions and subscriptions on
    our own `WF*` components are always safe.
- Design docs: `Docs/_WF/NpcCrew/design.md` (this module's brief, with an "as built" note per section) and
  `Docs/_WF/Encounters/design.md` (the Random Encounters System that consumes crews). Module overview:
  `Content.Server/_WF/NpcCrew/README.md` (the generated section lists every file).

## Toolchain

```
# .NET 10 SDK (the environment's network must allow builds.dotnet.microsoft.com and download.visualstudio.microsoft.com)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir $HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
git submodule update --init --recursive --depth 1 RobustToolbox

# Build everything the tests need (about 2.5 min); only errors shown
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug -v q -nologo 2>&1 | grep -E "error CS|error RA|Error\(s\)" | sort -u

# The module's tests (212 cases with the ship shield tests, about 4 minutes)
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug --no-build --filter "FullyQualifiedName~_WF.NpcCrew|FullyQualifiedName~_WF.ShipShields" --logger "console;verbosity=detailed" -nologo 2>&1 | grep -vE "warning RA|warning CS" > /tmp/wfcrew-tests.log
grep -E "^\s*(Passed|Failed) |Error Message|Passed!|Failed!" -A3 /tmp/wfcrew-tests.log | head -40

# Prototype linter (about 4 min, builds Release); must end with "No errors found"
dotnet run --project Content.YAMLLinter -c Release -v q -nologo 2>&1 | grep -vE "warning|^\s*$" | tail -15

# Module docs; the only acceptable complaint is the pre-existing fork_point one in a shallow clone
python3 Tools/_WF/Ci/modules.py --write
```

Never run two dotnet builds at once. `Content.Server` treats nullable warnings as errors. Say plainly in every
report what you built, what you ran and the exact counts; never call something working without running it.

## What is built

Every crew NPC runs one HTN root, `WFCrewCompound` (`Resources/Prototypes/_WF/NpcCrew/htn.yml`): **fight** first
(gated by `WFCrewMayFightPrecondition`), then one branch per **duty** selected by the `WFCrewDuty` blackboard key,
then **idle**. The planner swaps to an earlier branch as soon as it is valid, so a fight interrupts a duty and the
duty resumes afterwards. The module README describes each system; `navigation.md` covers flight profiles.

Entry points for other modules (Encounters): `WFCrewSetupSystem.TrySpawn(grid, posts, mission, out crew)`,
`WFCrewSetupSystem.TryApplyMission`, `WFCrewObjectiveSystem.SetQueue/Control/Cancel/Snapshot`,
`WFCrewAlertSystem.ReportShipThreat(grid, group, attacker)`, `WFPilotDutySystem.SetNavigation`.

Last verified on the merge of `main` at `633c05cc4e` (2026-10-03): Debug integration build 0 errors; all 240
crew and ship shield integration cases passed; module `--check` and `--pr-check origin/main` passed.

## Not yet verified

- The empty admin/verb menus and blank Ghost Warp seen in playtests were traced to the Debug build (see below).
  The original sequence (admin ghost, spawn as human, fight, die) has not been retested under sustained combat
  with the `Tools` launcher.
- The final Crew Setup layout has not been checked by eye in a real client.
- Docking and escort flight are tested on the Dredger only; other production hulls need a manual pass.
- Crew stay awake with no player nearby (`KeepActive`, default true). The cost of several crews on a live server
  has not been measured.
- Open questions 1 to 4 in `design.md` were answered by assumption.

## Behaviour that is easy to get wrong

- Station operators (Pilot and Gunnery duties, Captain and RadioOperator roles) fight only their own remembered
  attackers, whatever the engagement or boarding rule says.
- Infantry never receive a ship as a combat target. Hull hits and absorbed shield damage go to the radio, the
  captain, the gunner and the escort formation; weapon locks provoke nothing.
- Attack authorizes the named grid even across friendly factions. Defensive gunnery excludes friendly factions
  unless that vessel actually fired, for sixty seconds after its last hit. Hostile docking policy is tracked
  separately and stops when the policy changes.
- Crew never scavenge loose weapons, open containers, fabricate supplies or create ammunition. Reload uses
  detachable magazines from equipment slots only.
- Spawn posts are checked for safe air at spawn time only; later breaches are not handled.
- Objective queues need a pilot, are round-local and are cancelled by an immediate setup order.
- Boarding, injury and casualty reports need personal sight or a headset report actually received; a switched-off
  headset shares nothing. Check `Enabled` as well as `ActiveRadio`, which is removed at the end of the tick.
- PAIs, borg brains and station AI entities are `Alive` but are not boarders; use the shared boarding-candidate
  check.
- "Same crew" means same home grid (`WFCrewSystem.HomeGrid`: the post's grid, else the current one) and same
  group. The group label alone is never an ally test; every mission defaults to the label "crew".
- Ships whose crews share a `Battlegroup` (a mission setting) are one formation: no friendly fire, and an attack
  on one alerts all. `WFCrewEscortSystem` builds the formations once per tick for escorts and battlegroups alike.
  A formation partner that fires a ship weapon at a member is that member's attacker for 60 seconds.
- Protection from crew fire ends for anyone who attacked the crew, whatever their company or faction.
- Ship-level alerts come from ship weapons only (projectile or hitscan). A handheld shot at a shield alerts nobody.
  Each attacking vessel has its own 60 second expiry.
- A ship is out of the fight (`WFCrewShipStatusSystem`) after 10 seconds without a break of any of: everyone who
  was aboard is dead or gone (abandoned); it had ship weapons and none is powered (disarmed); or it has no powered
  weapon and under a quarter of the thrust it was seen with (crippled); or it holds under 60% of the hull tiles it was
  seen with (wrecked: shot to pieces or split apart, crew or no). Each test is against what the ship was seen
  with, so a hull that never had crew, weapons or thrust is never judged for lacking them.
- When a crew stops attacking a ship is its own mission setting, `Disengage`: `Destroy` stops only for an abandoned
  ship, `Disable` (default) for any of the four, `Deter` also once the ship is beyond `DisengageRange`. It decides
  the threat list, gunner fire and when an Attack task completes (`ShouldDisengage`; the range part is skipped for
  the Attack task, which starts out of range). A ship that has fired on a deterring crew is still answered beyond
  `DisengageRange`, by the captain and the gunners alike, but only out to three times it.
- A pilot looking for a helm takes the nearest console, one square to the hull before any that is turned: cruise
  flight faces the helm's north while the thrust is laid in the hull's frame, so a turned console flies the ship
  stern first (Saintie's aft console is turned 180 degrees).
- Each crewman has a `Skill` (Green, Regular, Veteran, Elite; `WFCrewSkills`), set per crew by the mission. It sets
  hand weapon aim error and time to first shot, how far off and how well led the ship's guns are laid, thrust and
  turn rate at the helm, how far ahead the pilot looks, and whether he dodges incoming fire. Veteran is the crew
  as tuned.
- A crew profile (`wfCrewProfile`, the mission's `Profile`) rolls each crewman's body from a species pool and his
  loadout from his role's pool, one skill for the crew, and may make roles passive. A profile's body is bare: it
  gets `WFCrewKit` by inheritance and its role's kit (`wfCrewRole.kit`) by `AddComponents`, so role kits can't be
  abstract. A mob that inherits a kit lists it first, because the first parent wins a shared component.
- `Never` engagement crew don't fight. On an alert or a boarding incident they run for the pilot's post
  (`WFCrewShelterSystem`) and go back a minute after the last alarm.
- With no captain aboard, or once the captain is down, the pilot evades attackers on their own
  (`WFPilotDutyComponent.ReactToAttacks`). Evasion re-targets every second and always ends in the saved course or,
  if its target is gone, a hold.
- Undocking waits at most `AbsentCrewWait` (60 s) for crew who are off the ship, and not at all when evading.
- Crew sleep with no player within 64 m unless they have something to do: not yet at their station, away from
  the ship, alerted, threatened, on a work job or in bad air (`WFCrewSleepSystem`, cvar `wf.crew.sleep_idle`).
  Flying, gunnery, radio, alerts and boarding detection run in their own systems and don't need the HTN awake.
- Crew set up ship access only on grids that aren't station members; stations keep the mapper's door access.
- One operator speaks per crew (the radio officer, else the captain), and one line goes out per incident however
  many crew witnessed it.
- A failed work target is skipped for the rest of the order; the order blocks only when every target has failed.
- Admins see crew tags and battlegroup lines on any radar; the client polls crews only while one is open.
- Work orders rebuild only what the ship's saved repair snapshot holds and the ordinary SRD whitelist allows.

## Playtesting

`Tools/_WF/NpcCrew/Start-TestPair.ps1` builds Server then Client in the `Tools` configuration into sibling
`bin/NpcCrewPlaytest.Server` and `bin/NpcCrewPlaytest.Client` folders and starts a pair on `127.0.0.1:1221` with
client sandboxing and `wf.crew.ui_diagnostics` on, and fake lag and mob pushing off. It refuses an occupied port
or a live output folder and never stops an existing session. `-SkipBuild`, `-Port`, `-DataDirectory` and
`-BuildName` are available.

Use it instead of a Debug pair. In Debug the engine broadcasts every physics ray (`MsgRay`) on the same reliable
ordered channel as verb and Ghost Warp replies; with the development preset's simulated lag and loss, NPC
visibility checks fill its 64-message window and replies arrive seconds late, after the menu has closed. World
state uses another channel and keeps updating, so it looks like a UI bug. `wf.crew.ui_diagnostics` logs each
setup request, reply and any reply missing after five seconds.

Server and Client must build into separate output folders (one shared folder mixes dependency versions and the
server fails to load), each directly under `bin/` (the engine finds resources by folder depth).

## Test traps

- Run builds and test processes one at a time. Two vstest processes contend on Robust's gravestone file and
  poison the pool.
- Test decks must sit within about 10 m of the origin unless the crew has `KeepActive`; tests that put crew to
  sleep must turn `KeepActive` off.
- Procedural test grids need explicit tile air; map atmosphere alone leaves their tiles in vacuum.
- Test attacker grids need real tiles, or a weapon's and shooter's `GridUid` is null.
- `ProjectileComponent.Shooter` is rewritten after impact; snapshot attribution before the collision.
- Stop combat and let effects drain before deleting combat actors, or the client's `MeleeWeaponSystem` throws on
  a missing transform. Tests with combat also disable client room echo (`SharedAudioSystem.SetAuxiliary` assert).
- A target can be deleted between HTN planning and the first task; operators revalidate when they start.
- Test factions can't be declared in `[TestPrototypes]`; the faction system caches its table first. The hostile
  test mob uses the production `SimpleHostile` faction.
- The engine's hard-collision helper reads the first body's fixtures twice and reports closed doors as free
  space; crew steering compares both bodies' masks itself.
- The native orbit halves a commanded radius through its range tolerance and cuts inside with a 30 degree
  look-ahead; crew orbits use a zero-width band and 10 degrees.

## Additional engine traps

- Robust requires every subscription one system makes to the same event type to use identical ordering constraints
  (`after:`/`before:`), or the server fails to start with "uses different ordering constraints".
- `SharedHandsSystem.TrySelectEmptyHand` is always false in this fork (it asks `IsHolding` about a null entity). Use
  `TryPickupAnyHand` then `TrySelect(uid, item)`.
- `MapInitEvent`, `MobStateChangedEvent`, `DamageChangedEvent` are by-value events: `(EntityUid, TComp, TEvent)`
  handlers. Check each event for `[ByRefEvent]`; our own events are all `[ByRefEvent] readonly record struct`, raised
  with `RaiseLocalEvent(uid, ref ev, true)`.
- Local functions can't see a null check on a captured variable; copy to a non-nullable local first (nullable
  warnings are errors).
- `LocalizedCommands.Loc` is an instance member; command helpers that use it can't be static.
- `ShipSteeringSystem.Stop` only removes the steering component; `Status` refreshes only when the ship asks for
  input, so set `Status = Moving` after re-steering. `FinishOnCollide` defaults to true. `InRangeRotation` applies
  only once in range; hold a heading during an approach with `AlwaysFaceTarget` plus a `TargetRotation` offset.
- `DockingSystem.GetDocks` clears a set `UndockDocks` still iterates; don't call it from an undock handler.
- A test deck is a bare grid; add `ShuttleComponent` for steering, `GravitySystem.EnableGravity` for walking, and a
  helm prototype with `ApcPowerReceiver needsPower: false` to count as powered without an APC.
  Its Powered state still initializes asynchronously after docking; wait for `TryFindHelm` instead of fixed ticks.
- Cannon tests must use a closed bolt and a projectile that can reach the target before its lifetime expires.
  Damage probes against walls need `ignoreResistances: true` so flat armor reductions cannot mask a failed control.
- Blackboard YAML: `Key: !type:Bool` on one line, the value on the next (see `mobs.yml`).

## Upstream API cheat sheet (verified in this tree)

- `ShuttleConsoleSystem.AddPilot(EntityUid console, EntityUid mob, ShuttleConsoleComponent)` needs `PilotComponent`
  on the mob first; `RemovePilot(EntityUid mob)`; a pilot cannot move and is dropped each tick it can't interact.
- `ShipSteeringSystem.Steer(Entity<ShipSteererComponent?> mob, EntityCoordinates)` → component or null (grid needs
  `ShuttleComponent`); fields `Coordinates`, `Status` (`Moving`/`InRange`), `Mode` (`GoToRange`/`Orbit`/`OrbitCW`),
  `Range`, `InRangeMaxSpeed`, `InRangeRotation`, `AvoidCollisions`, `FinishOnCollide`, `AlwaysFaceTarget`,
  `TargetRotation`. Input reaches the ship through `GetShuttleInputsEvent`; `PilotComponent` and the steerer coexist.
- `DockingSystem.GetDockingConfig(shuttle, targetGrid, priorityTag?, DockType)`, `CanDock(a, b)` (1.2 m, 15°),
  `Dock(a, b)`, `Undock(dock)`, `CanUndock`. A dock's outward normal is its rotation applied to (0, -1).
- `RadioSystem.SendRadioMessage(source, text, ProtoId<RadioChannelPrototype>, radioSource, ...)`; channels `Traffic`
  ("Shortband", 1500 m), `Common` ("Broadband"), `Nfsd` (TSF), `Ussp`. Identical text already in flight is dropped.
- `NpcFactionSystem.GetNearbyHostiles(mob, range)`, `AggroEntity`, `IsEntityFriendly`; factions in
  `Resources/Prototypes/{,_NF/,_Mono/}ai_factions.yml`. NPC vision is the `VisionRadius` blackboard key (10 tiles).
- `NPCSystem.SetBlackboard(uid, key, value, htn?)`, `NPCSystem.IsAwake(uid, htn)`, `HTNSystem.Replan(htn)`;
  `HTNComponent.Plan?.CurrentOperator`. `MoveToOperator` keys: `targetKey` (EntityCoordinates), `rangeKey`,
  `removeKeyOnFinish`. Blackboard defaults: `IdleRange` 7, `InteractRange`, `MovementRange` 1.5, `MeleeRange` 1.
- Grid maths: `SharedMapSystem.GetAllTilesEnumerator(grid, comp)`, `TileIndicesFor`, `GridTileToLocal`,
  `TurfSystem.IsTileBlocked(grid, tile, CollisionGroup.MobMask, comp)`.
- Players and purchased grids carry Monolith's `CompanyComponent` (TSF, PDV, USSP, MMC...); NPC factions are
  separate (`TSFMC`, `USSP`, `VG`, `MMC`). There is no "Rogue".
