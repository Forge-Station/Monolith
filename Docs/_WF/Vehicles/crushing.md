# Vehicle crushing

Owner decision of 2026-10-11: grid vehicles smash the Wolfgate equivalents of what RMC-14 marks `VehicleSmashable`, with
RMC's weight-class rules, and run over mobs as RMC does. This is how, and what to change to add or remove something.

## What RMC does

`VehicleSmashable` is a component on 33 RMC prototypes. The mover reads it for anything its collision probe meets:

- No weight classes (`minDestroyWeightClass` and `minContinueWeightClass` unset): the vehicle damages the thing, deletes it
  if `deleteOnHit`, slows down by `slowdownMultiplier` for `slowdownDuration` (not for a vehicle at or above
  `slowdownBelowWeightClass`) and drives on.
- `minDestroyWeightClass`: a vehicle below that class is stopped and the thing is left alone.
- `minContinueWeightClass`: a vehicle below that class destroys the thing and is stopped; at or above it drives on.
- `requiresDoorUnpowered` on a door: only an unpowered door is smashed. A powered door the driver has access to opens
  instead, and one the driver has no access to holds.

Weight classes in order: Weak, Light, Medium, Heavy. Mobs are not smashables: a standing mob is run over (8 Blunt, a
knockdown, `VehicleRunover`) at 1.5 tiles per second or more, and left alone below that.

## What Wolfgate does

Owner decision of 2026-10-17: small things never stop a vehicle. Lockers, barricades, grilles, hydroponics trays, fences,
windows, dumpsters and planters are smashed and the vehicle drives on (a light one slows down for lockers, barricades and
fences); only walls, doors, hull, boulders, trees for the weakest vehicles, fuel pumps and statues still hold one. Stumps,
including the one a Crocus tree leaves under the vehicle that knocked it down, are driven over by anything.

Wolfgate's prototypes are not edited. A `vehicleSmashRule` prototype (`Resources/Prototypes/_WF/Vehicles/Crushing/smash_rules.yml`)
carries the same settings and selects the entity prototypes that get them:

- `parents`: the listed prototypes and everything that inherits from them (abstract bases count).
- `whitelist`: components and tags, read off the prototype.
- `excludeParents` and `blacklist`: never these.
- All selectors a rule sets must match. Of two rules that match one prototype the higher `priority` wins; equal priorities
  are a data error and the data test fails on them.
- The settings are `VehicleSmashable`'s: `deleteOnHit`, `damageOnHit`, `slowdownMultiplier`, `slowdownDuration`,
  `smashSound`, `requiresDoorUnpowered`, `minDestroyWeightClass`, `minContinueWeightClass`, `slowdownBelowWeightClass`.

`VehicleSmashRuleSystem` resolves a prototype to its winning rule once and caches the answer by prototype id (cleared
when prototypes reload). `GridVehicleMoverSystem.TryGetSmashable` asks it for an entity that has no `VehicleSmashable`
component; the component wins when present. The system runs on both sides, so the client predicts the same smashes.

Built in, whatever a rule says: a prototype needs `Damageable` and `Destructible`, is not tagged `Wall`, and does not
inherit from `BaseWall`, `BaseWallIndestructible` or `BaseStructureIndestructible`. Walls always block.

### Ship hull: tanks only

Owner decision of 2026-10-11 ("For tanks only yeah."): the windows and airlocks that ships are built from stay breakable,
but only by a tank. Hull parts are reinforced, reinforced plasma and reinforced uranium windows (square, diagonal and
directional), shuttle windows, plastitanium windows, external airlocks, and shuttle and docking airlocks (the capital
ships' take 7000 damage to destroy, the shuttle windows 4000, a reinforced window 150). They get a rule
(`WFVehicleSmashHullWindows`, `WFVehicleSmashHullDoors`) whose `minDestroyWeightClass` is `Medium`, the class of the tank
family. A vehicle below Medium (vans, humvees, APCs, the Bison, civilian vehicles) is stopped by a hull part, does it no
damage, and the driver is told it is too light. A tank destroys it through its own thresholds in one hit and drives on
(a window) or stops where it broke it (a door, as every door, since `minContinueWeightClass` is `Heavy`).

Not hull, and not breakable by any vehicle, a tank included: walls, anything built indestructible, blast doors, vault and
secret doors (`HighSecDoor`), heavy glass airlocks, safes, safety deposit boxes and the machines of a ship (consoles,
thrusters, gyroscopes, generators, shields, armour). The outposts' own windows and docking airlocks (the `OUTPOST ONLY`
prototypes, 10 to 100 times tougher on purpose) are no ship and are left alone too.

### The economy: not hull, and not crushed

Same decision: the economy's fixtures are not hull, so no vehicle breaks them, a tank included. The locker, crate and vending
machine rules leave out, by data:

- Lockers and crates with a `Lock` or an `AccessReader`: the secure lockers (personal, department, evidence, armoury, the
  materials locker) and the secure crates (armoury, ammunition, security, the NFSD and encounter vault
  crates). A crate with a `TradeCrate` or a `Store` (the surplus bundles) is out too.
- Vending machines that cannot be unanchored (`BaseStructureDisableAnchoring`): the trade station, depot and Baikal trade
  goods vendors, the traders' stock machines (`WFTraderStock*`), the points of interest's vendors and the weapon and faction
  vendors. The bounty vendors (`VendingMachineBountyVend`, its POI and Punk variants), the ASF grant kiosk, every vendor with
  an `AccessReader` or `CompanyAccessReader` and the uplinks and stores (`Store`) are out as well.

Ordinary lockers, wardrobes, crates and vending machines without a lock still crush, and the data cannot tell a trade
station's machine from an ordinary one that can be unanchored beyond these markers; where it cannot, the rule errs on the
protective side (a vendor with an access reader is never crushed, even a harmless drobe). `VehicleSmashRuleTest` fails on any
prototype of the three rules that carries one of those components or inherits from one of those families.

### Doors

A door opens for a driver with access whatever the vehicle and whatever the rules say. The mover asks the door system
whether the driver may open it, for any door that bumping into would open, and opens it; a smash rule only decides what
happens to a door the driver cannot open. A powered door with a smash rule is opened or holds (`requiresDoorUnpowered`),
and an unpowered one is smashed or holds by the weight class. A vehicle with nobody at the wheel opens nothing: the door
system lets a missing user through any door, so the mover does not ask for one. The answer is asked once a tick for a
vehicle and a door, because each yes is written to the door's access log.

### What shares a tile with hull

The grille a hull window is set on is a window frame in the rules and a light vehicle breaks one on its own, but not under a
hull window. When another breakable on the grille's tile would stop the vehicle without being smashed (the window, for
a vehicle below a tank), the mover leaves the grille alone as well: the vehicle is held in front of both, in whatever order
the broadphase hands them out, and breaks neither. A tank is not held by the window, so it breaks both. The check
(`IsShelteredFromSmash`) is made for the smashing steps only.

### Destruction

A smash deals Blunt damage, ignoring resistances. For a rule with `deleteOnHit`, the server (`VehicleCrushingSystem`)
sizes the hit to what the target still needs to reach its lowest threshold that destroys it, if that is less than
`damageOnHit`. So the target goes through its own thresholds: a table leaves its sheet of steel, a locker opens and spills
its contents, a vending machine drops what the thresholds drop. An overkill hit would run the highest threshold and skip
the leftovers. Only what is still standing after the damage is deleted outright (`DestroyEntity`, which also spills
storage); the data test rejects any rule that would need that for a prototype it matches.

A target smashed this tick and waiting for deletion is not smashed again, and the weight-class stop is decided from the
first hit. The client cannot destroy anything, so it predicts "gone" for any target a rule deletes.

### What the driver is told

When a vehicle is stopped by something it could have smashed, the server raises `VehicleSmashBlockedEvent` on it
(`TooLight` or `DoorPowered`) and the driver gets a popup (`crushing.ftl`), at most one every two seconds per vehicle.
Smash sounds are existing Wolfgate sound collections (`MetalCrunch` for doors and shutters, as RMC plays its metal crash
there); everything else relies on the target's own destruction sounds, as in RMC.

## Mapping

| RMC prototype | Wolfgate rule | Matches (bases) | Settings |
|---|---|---|---|
| `CMTable`, `CMRack` | `WFVehicleSmashTables` | `TableBase`, `N14FreeTableBase`, `Rack` | defaults |
| `CMLockerBase` | `WFVehicleSmashLockers` | `ClosetBase`, not `GunSafe`, not `N14ClosetBase`, not locked or access-locked | slowdown below Medium |
| `RMCCrateOpenableBase` | `WFVehicleSmashCrates` | `CrateGeneric`, not locked, access-locked, trade or store crates | defaults |
| `RMCBarrelBase` | `WFVehicleSmashBarrels` | `BaseBarrel`, `N14BaseBarrel` | defaults |
| `CMBed` | `WFVehicleSmashBeds` | `Bed`, not `OperatingTable` | defaults |
| `RMCCouchMid` | `WFVehicleSmashSofas` | sofas and comfortable benches | defaults |
| `CMVendor*` | `WFVehicleSmashVendingMachines` | components `VendingMachine`, not wall mounted, fixed (`BaseStructureDisableAnchoring`), bounty, access-locked or store vendors | defaults |
| `CMFence` | `WFVehicleSmashFences` | `BaseFenceMetal`, `BaseFenceWood` | slowdown below Medium |
| `CMBarricade*` | `WFVehicleSmashBarricades` | `BaseBarricade`, `InflatableWall`, not the N14 wooden wall frame (a girder) | slowdown below Medium |
| `CMBaseWindow` | `WFVehicleSmashWindows` | `Window`, `WindowDirectional`, not hull windows | defaults |
| `CMBaseWindow` (hull) | `WFVehicleSmashHullWindows` | reinforced, reinforced plasma and uranium, shuttle and plastitanium windows, not outpost ones | minDestroy Medium |
| `RMCBaseWindowFrame` | `WFVehicleSmashWindowFrames` | `Grille` | defaults |
| `CMBaseWindoor` | `WFVehicleSmashWindoors` | `BaseWindoor` | requiresDoorUnpowered, minContinue Heavy |
| `CMBaseDoor` | `WFVehicleSmashDoors` | components `Airlock`, `Firelock`, not windoors, hull doors, vault doors | requiresDoorUnpowered, minContinue Heavy, crunch |
| `CMBaseDoor` (hull) | `WFVehicleSmashHullDoors` | `AirlockExternal`, `AirlockShuttle`, not the outposts' docking airlocks | requiresDoorUnpowered, minDestroy Medium, minContinue Heavy, crunch |
| `RMCShutterAlmayer`, `RMCPodDoor` | `WFVehicleSmashShutters` | `BaseShutter`, not `BlastDoor` | minContinue Heavy, crunch |
| `RMCBaseTree` | `WFVehicleSmashTrees` | `BaseTree`, crocus trees | minDestroy Light, slowdown below Medium |
| (none) | `WFVehicleSmashStumps` | `CrocusStump`, `FloraTreeStump`, `FloraTreeStumpConifer`, priority 1 | defaults |
| `RMCBoulderLarge` | `WFVehicleSmashBoulders` | `BaseRock`, `N14DecorStalagmite1` | minDestroy Medium |
| `CMBarricadeSandbag*` | `WFVehicleSmashSandbags` | `N14BarricadeSandbagSingle`, `N14BarricadeWindow`, not the tank traps | slowdown below Medium |
| (none) | `WFVehicleSmashClutter` | N14 pallet stacks, junk and shelves, dressers, railings, broken fences, scrap, robot showcases, targets (not the strange one), instruments, kegs, plastic flaps (not airtight), airlock assemblies | slowdown below Medium |
| `CMHydroponicsTray` | `WFVehicleSmashHydroponicsTrays` | `hydroponicsTray` | defaults |
| `CMDisposalUnit` | `WFVehicleSmashDisposalUnits` | `DisposalUnit`, `MailingUnit` | defaults |

Left out, with the reason:

- RMC floodlights: Wolfgate's floodlight is an item, and the mover ignores loose items.
- Window frames: Wolfgate has none; the grille a window is set on takes RMC's frame settings.
- Hospital dividers, platforms, space heaters and fridge lockers: not on the owner's list.
- Material doors (wood, metal, gold), curtains: they have no power state, so "unpowered" has no meaning for them.
- Blast doors and vault doors (`HighSecDoor`), heavy glass airlocks, and the outposts' own windows and docking airlocks:
  armour, or no ship. Gun safes, deposit boxes, trade crates and anything built on `BaseStructureIndestructible`.
- Secure, locked and trade lockers and crates, and the trade station, trader, bounty, access-locked and store vendors (see
  the economy above): not hull, and no vehicle's to break.
- Walls, girders (the N14 wooden wall frame is one, and is excluded by name), thrusters, gyroscopes, consoles, generators and
  shields match nothing: no rule names them.

## Tests

`VehicleSmashRuleTest` reads every entity prototype and fails when a rule matches a wall, a machine of a ship, a safe, an
item, an outpost fixture, a prototype that cannot be broken, needs more than 1000 damage to destroy (10000 for hull, which
only a tank breaks), would be deleted outright, or deletes its contents when destroyed, when two rules match one prototype
at the same priority, or when a hull part is matched by a rule a vehicle lighter than a tank can use. It also checks that
every rule is well formed and matches something, that named prototypes get the rule they should and others none, and that
a component wins over a rule. `VehicleCrushingTest` drives vehicles of each weight class at the targets and checks the
outcome (a light vehicle is stopped by hull and leaves it undamaged, a tank breaks it, a driverless vehicle opens no
door, a door's access log is not filled by one pass), that a locker spills its contents, that a table leaves its sheet,
and that a mob is run over.

## Adding a rule

Add a `vehicleSmashRule` entry with the narrowest selector that works: the base prototype of the family, not a component
half the game has. Run `VehicleSmashRuleTest` and read what it reports. Never edit the upstream prototype; a prototype that
needs settings of its own can carry a `VehicleSmashable` component in the module instead.

## Machines

Owner, 2026-10-13: vehicles were getting stuck on computers and fax machines, so `WFVehicleSmashMachines` lets any vehicle
drive through an ordinary machine or computer (`BaseMachine`, `BaseStructureComputer`). Its blacklist keeps what a ship
cannot fly, fight, power or pay without (thrusters, gyroscopes, shuttle consoles, FTL drives, shields, ship guns,
generators and batteries, bank, deposit and shipyard machines), the machines that hold a person or a hazard (cryo pods,
cloning pods, the AI core, containment and engine parts), wall-mounted and access-locked machines and vending machines,
and `BaseStructureDisableAnchoring` keeps the fixtures of a station, outpost or point of interest. Some of those
components exist only on the server, so a client may predict driving through one and be put back.

## The plow

Owner decision of 2026-10-13: plow vehicles break walls. A hardpoint with `VehiclePlow` (the humvee snowplow and the two
tank snowplows) is a plow while it has integrity left. When the vehicle is stopped by something and that something lies
off the hull's front face (`GridVehicleMotionSimulator.IsFrontImpact`), the plow damages it, on the server only:

- A ram: `speed squared x plowRamDamage x the plow's multiplier (1.5) x the chassis' multiplier`, if the contact is at
  `plowRamMinSpeed` or faster (tank 3.5, APC 6, humvee and van 10, AEV 0.1; never below 0.5). `plowRamDamage` is 14 on
  the vehicles (the component's own default of 75 is CMU's, for CMU's speeds), so a humvee at its top speed of 14 does
  about 4000, which is any wall but an outpost's, and a tank at 7 about 1000.
- A grind, for a vehicle with `VehiclePlowChassis` and a `poweredDemolitionDamagePerSecond` (the AEV: 2000, after half a
  second of unbroken contact): that much per second for as long as the driver keeps pushing, with a popup and a creak.

One target is hit at most once per `plowRamCooldown` (half a second). The vehicle is stopped either way and drives on
when the wall is gone; a plain wall leaves a girder, which a grind clears and a ram needs a second run-up for.

What a plow may damage is `VehicleSmashRuleSystem.IsPlowable`: a wall (the `Wall` tag), a girder, or anything a smash
rule names, provided it has `Damageable` and `Destructible` and inherits from none of `PlowNeverParents` (the
indestructible bases and `BaseWallOutpost`). So a plow goes through walls and through the hull windows a light vehicle
is otherwise stopped by, and not through an outpost's walls, a ship's machines, safes, deposit boxes or anything else
no rule names. Doors, vehicles and mobs are left to their own rules. A grinding plow tells its driver once when a wall
will not give.

CMU's own ramming model, in which every vehicle damages structures by its impact energy and takes damage back, is not
ported; without a plow a vehicle breaks exactly what the rules above say.

## Street furniture

Owner, 2026-10-13: street furnishings break when driven through. The planet cities' furniture comes from the Streets port
(`Tools/_WF/Streets/port.yml`, its `set` rules), which gives the RMC and CMU pieces a `VehicleSmashable` component
rather than a rule: many have no `Destructible`, and a rule never matches those. Lights, traffic lights, benches, bins,
traffic directors, phone boxes, tents, shop shelves and slot machines go down under any vehicle, which drives on; a light
breaks into its broken twin, which goes next. Dumpsters and planters go down the same way; the fuel pump and the concrete
statue take a tank. Hydrants, signs and the rest that nothing collides with
are driven over as before. `StreetFurnitureSmashTest` checks a piece of each kind.
