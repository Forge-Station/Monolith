# NPC vehicle crew: design

NPC-crewed ground vehicles for planets: a tank, APC, humvee or truck spawned with a driver, a gunner and troopers
aboard, driven and fought by the server. Module `NpcVehicleCrew` (`_WF/NpcVehicleCrew` in Shared, Server, Prototypes,
Locale, IntegrationTests). It builds on `Vehicles` (RMC/CMU grid vehicles) and `NpcCrew` (crew mobs, roles, profiles,
factions) and makes a few small public-API additions inside the `Vehicles` module (no marker needed: both are `_WF`).

## Owner's rules that apply (from the ship-crew playtests)

- NPC crew are clearly weaker than players: slow first shot, aim error, no rushing. They never pursue off the map.
- Fight only faction hostiles or whoever attacks them. Player ships are never targets.
- No spoken action lines. A guard vehicle warns with its horn, nothing else.
- Encounters end and clean up: a crewed vehicle with no living crew is just a vehicle; a wreck's crew dies with it or
  fights on foot nearby, it never spreads out over the planet.

## Shape

```
WFVehicleCrewPrototype (wfVehicleCrew, Shared)     what to spawn: vehicle, seats -> roles, profile, faction, behaviour, tuning
WFVehicleCrewComponent (Server, on the vehicle)    the live crew: members by seat, group, behaviour state, home, target
WFVehicleAutopilotComponent (Shared, on the vehicle)  the driver's current input, read by GridVehicleMoverSystem
WFVehicleCrewSystem (Server)                       spawn, assemble, tick the brain at 5 Hz, dismount, cleanup
  .Drive.cs       waypoint steering -> autopilot input, stuck recovery, pathfinding
  .Gunnery.cs     target pick, turret aim, trigger, hardpoint switching, ammo state
  .Behaviour.cs   Hold / Patrol / Hunt / Escort / Flee / Retreat / Bail state machine
WFVehicleCrewSpawnerComponent (Server, on a marker) spawns a wfVehicleCrew at MapInit (for mappers / planet POIs)
WFVehicleCrewCommand (Server)                      wf_vehiclecrew spawn|list|clear
```

### Prototype `wfVehicleCrew`

| field | type | meaning |
|---|---|---|
| `vehicle` | EntProtoId | the vehicle to spawn (an armed variant, e.g. `WFVehicleTankArmed`) |
| `driver` | ProtoId\<WFCrewRole\> | role for the driver seat (`WFVehicleDriver`) |
| `gunner` | ProtoId\<WFCrewRole\>? | role for the primary weapons seat; null for unarmed vehicles |
| `troopers` | List\<ProtoId\<WFCrewRole\>\> | passengers seated in any other seat / standing in the cabin; they dismount on Bail |
| `profile` | ProtoId\<WFCrewProfile\>? | bodies, names, loadouts per role (NpcCrew profiles: Pirate, Mercenary, Tsf, Pdv, Ussp, Hauler) |
| `faction` | ProtoId\<NpcFaction\> | the crew's faction (default `WFCrew`) |
| `behaviour` | WFVehicleBehaviour | default behaviour at spawn (Hold, Patrol, Hunt, Escort, Flee) |
| `engagement` | WFCrewEngagement | OnSight (faction hostiles) or WhenAttacked (only attackers of the vehicle or its crew) |
| `engageRange` | float | tiles; targets farther than this are ignored (tank 18, humvee 14, APC 16) |
| `standoff` | float | tiles; preferred fighting distance to the target (tank 10, humvee 6, APC 8) |
| `fireOnTheMove` | bool | humvee/APC true, tank false (a tank stops to shoot) |
| `patrolRadius` | float | tiles round home for generated patrol points (default 40) |
| `guardRadius` | float | Hold only: a non-hostile stranger inside it gets the horn, and `guardWarnTime` (15 s) to leave before he counts as hostile; 0 disables |
| `retreatIntegrity` | float 0..1 | frame integrity fraction below which the vehicle retreats home (0.35) |
| `bailIntegrity` | float 0..1 | fraction below which the crew abandons the vehicle (0.1); also when it can't run (no fuel, frame destroyed) or the driver is dead and nobody can replace him |
| `skill` | WFCrewSkill | first-shot delay and aim error (Green 2.5 s / 8°, Veteran 1.5 s / 4°, Elite 0.8 s / 2°; my picks) |

### Behaviours

- **Hold**: parked at home. Turret tracks and engages; a vehicle whose selected gun can't bear (fixed arc) turns the
  hull on the spot instead. Returns home if displaced more than 3 tiles.
- **Patrol**: loops through waypoints (given, else N=4..6 random points within `patrolRadius` of home that the
  pathfinder can reach), pauses 10-20 s at each. A road patrol (`patrolPauseMax` <= 2 s) instead rolls through each
  point: the driver's goal is the point with `NextGoal` the one after it, the look-ahead runs on into that leg, there
  is no approach slowdown, and the leg after the goal is pre-planned (`NextRoute`) and taken up unchanged when the
  goal advances. A turnaround (the next leg doubles back) and the last point still brake and pause. On contact: `fireOnTheMove` vehicles keep rolling and shoot; a tank
  stops, fights, then resumes. Loses contact after 30 s without line of sight -> resume.
- **Hunt**: closes on the target to `standoff` (advance past 1.3x, back off under 0.6x, otherwise hold and shoot).
  Without line of sight for 10 s it drives to the target's last seen position; nothing there after 60 s -> back to
  the previous behaviour. Hunt is entered from Hold/Patrol when the engagement rule gives a target and
  `pursues` is true (default true for Patrol, false for Hold: a guard holds its post, per the owner's "defensive crews
  never leave their ship").
- **Escort**: follows `Lead` (an entity) at 4-6 tiles behind, engages only what attacks the lead or itself, never
  strays more than `engageRange` from the lead.
- **Flee**: unarmed vehicles. Drives away from the attacker at full speed toward home (or 40 tiles away), stops when
  nothing has hit it for 30 s.
- **Retreat**: automatic when integrity < `retreatIntegrity` or every gun is out of ammo; drives home, turret still
  returns fire. Recovers to the previous behaviour only when repaired above `retreatIntegrity` + 0.2 (never in v1 unless
  a player repairs it; fine).
- **Bail**: automatic when integrity < `bailIntegrity`, the vehicle can't run, or the driver is dead with nobody to
  take over (a trooper takes the wheel first: `TryReseat`). Everyone exits through the vehicle's exits
  (`VehicleInteriorSystem.TryExitVehicle`), the vehicle keeps `WFVehicleCrewComponent` but `Bailed = true` (brain off,
  autopilot removed, engine off). Dismounted crew keep their `WFCrew` with `Duty = Guard`, `Post` = where they stand,
  `Pursues = false`, `Engagement` as the crew's: troopers fight whoever comes, drivers/gunners (WhenAttacked) only
  defend themselves. Cowards (profile) are handled by NpcCrew already.

### Crew mobs

Seated crew are ordinary NpcCrew crewmen (`WFCrewSystem.SpawnCrewman(role, seatCoords, group, loadout, body, name)`)
with their HTN asleep (`NPCSystem.SleepNPC`) while mounted, so they neither wander nor unbuckle. The brain drives and
shoots *as* them: the autopilot input counts only while the driver (the vehicle's `RMCVehicleComponent.Operator`) is
alive and conscious (`GetMoverInput` already requires this), and the gunner fires through
`SharedGunSystem.AttemptShoot(gunner, gun, coords)` where the gun comes from `MannedTurretGetGunEvent` on the
seated `VehicleWeaponsOperatorComponent`. Kill the driver and the tank stops; kill the gunner and the turret goes
quiet. A dead crewman is unbuckled and left where he sat. When the driver dies and a trooper lives, the trooper is
reseated into the driver seat after 5 s (he was unbuckled, teleported to the seat coords on the interior grid and
buckled: no walking inside the cabin in v1).

Roles (new `wfCrewRole` prototypes, this module's `roles.yml`): `WFVehicleDriver` (officer mob, duty Guard,
WhenAttacked), `WFVehicleGunner` (gunner mob, WhenAttacked), `WFVehicleTrooper` (marine mob, OnSight). Profiles'
`loadouts` are keyed by role, so each new role gets loadout pools in a `profiles.yml` *patch*: NpcCrew profiles cannot
be extended from another file, so this module defines its own `wfCrewProfile`s (`WFVehicleCrewProfilePirate`,
`...Mercenary`, `...Tsf`, `...Pdv`, `...Ussp`, `...Civilian`) that reuse the NpcCrew gear prototypes.

### Driving

`GridVehicleMoverSystem.GetMoverInput` (Vehicles module, Shared) gets one branch: if the vehicle has
`WFVehicleAutopilotComponent` with `Active`, and the operator passes the existing alive/conscious checks, the
autopilot's `Direction` (four-way drive), `Throttle` and `Steering` (free steering, -1..1) are returned instead of the
operator's keys. All roster vehicles use free steering; the four-way path is for the test chassis.

The driver brain (5 Hz): current waypoint -> heading error in the vehicle's frame (`GridVehicleMoverComponent.Heading`
is grid-local; convert the target into the grid's frame). Steering = clamp(error / 30°, -1, 1); throttle = 1 when
|error| < 60°, 0.4 while turning harder, reverse with opposite steering when the waypoint is behind and within 3
tiles. Arrive within 1.5 tiles -> next waypoint. Stuck: throttle held and |CurrentSpeed| < 0.2 for 2 s -> reverse
1.5 s with full steering the other way, then re-path; three stucks on one leg -> skip the leg.

Paths: `PathfindingSystem.GetPathSafe(vehicle, start, end, range, cancel)` on the vehicle's grid when the grid has
`GridPathfindingComponent` (planets do: every grid gets one); the result's `Path` is a queue of coordinates, which the
driver follows as waypoints (the vehicle is wider than a mob, so bumps are expected and the stuck recovery handles
them). No path -> straight line. Repath every 5 s while hunting a moving target.

Engine: NPC vehicles start with `WFVehicleEngineSystem.TryStartForNpc(vehicle)` (new public method in the Vehicles
module): puts the interior's spawned key (`SpawnVehicleInteriorKey` puts one at the driver seat when the interior
loads) into the ignition slot, sets Electrics and Running when fuel and battery allow. The brain calls it whenever the
engine is off and the vehicle wants to move; out of fuel -> Bail. `ParkingBrake` off.

### Gunnery

Every 0.5 s per armed vehicle with a living gunner:

1. Candidates: `NpcFactionSystem.GetNearbyHostiles(gunner, engageRange)` plus occupied hostile vehicles
   (`WFOccupiedVehicleComponent` + faction member) within range; WhenAttacked crews consider only entries in
   `Attackers` (filled from `DamageChangedEvent` origins on the vehicle and on any crewman, kept 60 s). Ignore anything
   on another map than the vehicle, anything dead, anything a player's ship grid.
2. Line of sight from the vehicle's position: `ExamineSystemShared.InRangeUnOccluded(vehicleCoords, targetCoords, range)`.
   Prefer the current target while it stays valid (sticky 5 s), else nearest.
3. Hardpoint: `VehicleWeaponsSystem.TrySelectHardpointForNpc(vehicle, gunner, weapon)` (new public wrapper around
   `TrySelectHardpoint`): pick the mounted gun with rounds (`BallisticAmmoProvider.Count` + `VehicleHardpointAmmo`
   reserve), preferring the one the prototype lists first; none -> `OutOfAmmo = true` (Retreat).
4. Aim: `VehicleTurretSystem.TryAimAtTarget(turret, target)` every tick with a target; fire only when
   `TryGetBarrelWorldRotation` is within 8° of the bearing, after the skill's first-shot delay since acquisition, and
   with the skill's aim error applied to the coordinates. Fire: ensure `CombatModeComponent` on the gunner is in combat
   mode, then `SharedGunSystem.AttemptShoot(gunner, gun, aimCoords)`. Fixed-arc guns (no turret): the driver turns the
   hull toward the target when stopped.
5. Burst discipline: autocannon/minigun fire for 2 s, rest 1.5 s; cannon fires on cooldown. (My picks.)

Troopers never shoot from inside (no port guns in v1).

### Spawning

- `wf_vehiclecrew spawn <wfVehicleCrew> [behaviour] [distance=6]` beside the caller on their grid (admin, `Admin` flag).
  `wf_vehiclecrew list`, `wf_vehiclecrew clear [group]`. Also `WFVehicleCrewSystem.TrySpawn(proto, coords, out vehicle,
  behaviour?, group?)` for encounters and planet POIs to call later.
- Marker entities `WFVehicleCrewSpawner*` (parent `MarkerBase`, `WFVehicleCrewSpawner { crew: <id>, behaviour }`),
  one per roster entry, in entity category `WFVehicles` so they sit in the Wolfgate spawn menu's vehicle group.
- Spawn: spawn the vehicle at the coords; load the interior (`VehicleInteriorSystem.TryLoadInterior(vehicle)`, new
  public wrapper around `EnsureInterior`); enumerate seats on the interior grid (`VehicleDriverSeatComponent`,
  `VehicleWeaponsSeatComponent` with `IsPrimaryOperatorSeat`, then any other `StrapComponent` seat); spawn each crewman
  at his seat and `SharedBuckleSystem.TryBuckle(mob, null, seat)`; troopers without a seat stand in the cabin at the
  entry coords. Set each crewman's faction to the prototype's (`ClearFactions` + `AddFaction`), `KeepActive`, sleep
  the HTN. Fill fuel. Start the engine. Register with `VehicleOccupantFactionSystem` (already automatic).

### Roster (my picks, for the owner to change)

| id | vehicle | crew | faction / profile | behaviour |
|---|---|---|---|---|
| `WFVehicleCrewPirateHumvee` | `VehicleHumvee` (armed cannon) | driver, gunner, 2 troopers | PirateNF | Patrol, fireOnTheMove |
| `WFVehicleCrewPirateTank` | `WFVehicleTankArmed` | driver, gunner | PirateNF | Patrol |
| `WFVehicleCrewPirateApc` | `WFVehicleAPCArmed` | driver, gunner, 4 troopers | PirateNF | Patrol |
| `WFVehicleCrewMercenaryTank` | `WFVehicleTankPMCArmed` | driver, gunner | WFCrew (MercenariesExpeditionNF is hostile to all) | Hold, guardRadius 12 |
| `WFVehicleCrewTsfTank` | `WFVehicleTankTWEArmed` | driver, gunner | TSFMC | Hold, guardRadius 12 |
| `WFVehicleCrewTsfApc` | `WFVehicleAPCArmed` | driver, gunner, 4 troopers | TSFMC | Patrol |
| `WFVehicleCrewUsspApc` | `VehicleSPPAPC` | driver, gunner, 4 troopers | USSP | Patrol |
| `WFVehicleCrewUsspTank` | `VehicleSPPTank` | driver, gunner | USSP | Hold |
| `WFVehicleCrewCivilianTruck` | `AU14VehicleCivTruck` | driver | hauler/civilian (`WFCrew`) | Patrol, Flee when attacked |
| `WFVehicleCrewCivilianVan` | `AU14VehicleCivVan` | driver | hauler | Patrol, Flee |

Faction ids come from `Resources/Prototypes/_WF/NpcCrew/ai_factions.yml` and the encounter presets; the agent
building the prototypes must grep them rather than guess.

### Tests (`Content.IntegrationTests/Tests/_WF/NpcVehicleCrew/WFVehicleCrewTest.cs`)

Test prototypes on `WFInteriorTestVehicle` (`/Maps/_WF/Vehicles/Interiors/wf_test_interior.yml` has a driver seat and
a weapons seat; check) plus a test gun hardpoint from `VehicleArmedWeaponsTest`. `NPCPauseWhenNoPlayersInRange` off.

1. `SpawnSeatsStartsAndDrives`: spawn a crew; driver is the operator, gunner has `VehicleWeaponsOperatorComponent`,
   engine running; give a waypoint 8 tiles away; after 10 s the vehicle is within 2 tiles of it.
2. `GunnerShootsAHostileAndHoldsFireOnFriends`: spawn a hostile mob 8 tiles away in sight; within 6 s a projectile
   from the gun exists / the mob or the plating took damage; a same-faction mob is never fired at.
3. `BailsWhenTheFrameBreaks`: set `HardpointIntegrity.Integrity = 0`; within 3 s every crewman is on the outer map,
   has `WFCrew` with Duty Guard, and the vehicle has no autopilot.
4. `ADeadDriverStopsTheVehicle`: kill the driver mid-drive; speed falls to 0 and stays.

No `Destructive` pool settings.

### Out of scope for v1

Troopers fighting from port guns; crew walking inside the cabin; refuelling and repair by the crew; convoys that
wait for each other; planet POI and encounter integration (the API `TrySpawn` is the hook); turret-less fixed guns
on moving vehicles beyond "turn the hull when stopped"; z-level changes.
