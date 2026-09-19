# Tractor beams

Place `WFTractorBeamEmitter` on an arrestor's high-voltage cable and
`WFComputerTractorBeam` on the same grid with APC power. The console discovers
anchored dishes automatically. Select a dish and radar contact. **Hold target**
captures the current distance. Once the beam is active, enter a desired separation
in metres (center to center) and press **Set range** to reel inward at the configured
rate, then hold at that range. The display shows the current and ordered separation
and the hull-safe minimum. Range orders cannot acquire new targets, push outward,
overlap hulls, or renew a failing lock's power allowance. Once the ships have stopped,
**Lock in place** captures their separation and relative angle and actively holds
the target against translation and rotation. Resistance consumes beam power even
when the target remains still. Hold/Set range leave this mode; **Release** stops the whole field.
Each dish holds one target; several dishes or ships can hold the same target.
Reopening the console restores the selected dish's captured target. Disabled pin
and range controls explain the required action in hover tooltips. Pinning requires
both ships below 0.2 m/s and 0.05 rad/s; its power check tolerates small allocation
fluctuations, while restraint still uses only the power actually supplied.
The console board is `WFTractorBeamComputerCircuitboard`.
Unanchor, rotate, and reanchor the dish to change its installed facing.

The dish faces along its local +Y axis (north in its default placement) and operates
inside a 90-degree forward sector, out to 200 m (80 m for the small dish). Turn the arrestor or mount its dish
to bring a contact into that sector before capturing it. Captures release when the
target's center leaves either the angular boundary or the maximum range, including
when the arrestor turns away. The control display shows the selected dish's sector
and warns during the outer 15% of its angle or range, before the lock is lost.

Hold resists moving closer, farther away, sideways, and rotating, using damped
springs about the distance, source-relative bearing, and relative angle captured at lock. A range
order shortens that held distance while retaining the other restraints.
Turning the arrestor carries the target around its center of mass on the held arm,
including the target's tangential velocity and relative hull orientation. Sideways
resistance applies a lever-arm torque back to the arrestor, so either ship can turn
the coupled pair; inertia, powered thrusters, beam strength, and electricity still
limit authority. Turning too fast can overload the beam or lose cone coverage.
Equal and opposite impulses and reaction torques act on the two ships. Inward, outward,
sideways, and rotational resistance all share the same force and power limit.
Distance adds electrical strain even without resistance: field strain is
`0.6 × (distance / maxRange)²`, measured from the dish to the target's center.
It uses 15% strain / 385 kW at 100 m and 60% / 1.24 MW at 200 m for a stationary
target. Mechanical strain fills the remaining headroom up to the 2 MW limit.
The field-maintenance cost is paid before mechanical restraint: insufficient
power weakens the beam, with no force available below that cost. A powered dish
may retain an energized capture while waiting for that supply; the existing
base-power loss and range-release rules still apply. Distance alone causes no
physical force or hull creaks. Reeling closer reduces the distance load.
Lock in place instead uses a firm powered position and rotation
restraint, with equal reaction on the arrestor. Insufficient beam power or force
allows the target to slip; insufficient arrestor thrust lets the arrestor be dragged.

While a powered beam is active, the source automatically brakes through its real
directional thrusters and gyroscope, even with nobody at the helm. Pilot movement
overrides automatic linear braking; pilot rotation overrides automatic angular
braking. Available thrust and power still limit its authority. A light or poorly
powered source can therefore be dragged by its target; cooperating ships contribute
their own forces. There is no mass-threshold freeze or free space anchor.

While any mode is active, the cone also collects free-floating objects
and other movable grids. Its width at the target matches the target hull's projected
width across the beam, including its rotation, rather than widening with distance.
Its side boundaries match the drawing and end at the selected target. The visible
effect fades out ahead of a rounded envelope around the target hull, keeping it
clear of the ship; this visual clearance does not prevent nearby debris collection.
Objects are selected by their center, grids by intersection with their
world bounds; both must also have their center inside the dish's operating sector.
Cargo aboard ships, held/contained items, anchored machinery, static
grids, and the source's own docked group are excluded. A dish shares one force and
power budget across its primary target and everything it collects, with reaction
forces on its source ship. Secondary objects and grids continuously accelerate
toward the dish, with no collection speed cap or arrival brake. The dish's offset
also applies rotational recoil to the arrestor. Debris can strike and damage
equipment through normal collision and impact behavior; this is an intentional risk
of collecting the wrong objects. The selected vessel uses the separate range control.

## Test vessel

Choose **TRACTOR TEST** (`WFTractorTest`) in the Wolfgate ship spawn menu. It is a
small, flyable arrestor test craft with a foredeck dish, both control computers,
thrusters, gyroscope, breathable cabin, port airlock, and a wired 2 MW test supply.
The administrative vessel is not sold by normal shipyards. The power source is
deliberately fuel-free for repeatable tests. Spawn another ship or grid ahead,
lock it, and put loose objects between the dish and target to test collection.

Default per-dish tuning (prototype fields):

- `maxRange`: 200 m, measured from the dish to the target's center of mass.
- `rangeStrainAtMaxRange`: 0.6, stationary electrical strain at the range limit.
- `coneHalfAngle`: 0.7853982 radians (45 degrees either side of the dish's facing).
- `maxForce`: 10,000 force units (50 standard thrusters).
- `frequency`: 0.7 Hz; `dampingRatio`: 1.
- `idlePower`: 5 kW; `holdingPower`: 100 kW; `maxPower`: 2 MW.
- `reelSpeed`: 2 m/s; `collectionAcceleration`: 2 m/s², sustained while in the field.
- `looseCollectionMultiplier`: 50. Free-floating objects and mobs request 100 m/s²,
  while secondary grids retain 2 m/s². All still share the dish's force/power budget.
- `collectionStandOff`: 1 m of clearance when reeling the primary vessel; its minimum range also accounts for both hull sizes.

Strain is the square root of demanded force divided by maximum force, making even
moderate resistance significant. Power rises with that strain: 25% of the force
limit means 50% strain and a 1.05 MW demand. After paying the holding overhead,
half the requested additional power supplies only a quarter of the requested force.
The supply may take one
second to establish a new lock, during which an underpowered beam applies no
force. After that, supply below holding power drops the lock. Strain measures the
current resistance rather than hull damage. Two continuous seconds at 100% displayed
strain break the capture, clear its effect, and return the dish to idle power, even
with a full power supply. Dropping below 100% resets this overload timer; the duration
is configurable with `overloadDuration`. Any release (manual, overload, power loss,
or invalid target) starts a 12-second dish cooldown (`restartCooldown`). The console
shows its countdown and capture is rejected server-side until it expires. Repeated
release commands on an idle dish do not extend the cooldown. Rotational restraint in every mode
also shares the dish's force and power budget with translation and collected debris.

Server validation rejects foreign emitters, self-targets, hidden/undetected
contacts, static grids, targets outside the operating sector and the source's own docked group.
Locks release on power loss, console loss, unanchoring, range or firing-arc loss, grid splitting,
target deletion, source-grid reassignment or FTL. Tractor beams do not inhibit FTL.
Anchored or static grids, station-anchor-disabled shuttles, and ships docked to such
grids cannot be selected or collected by the beam. Existing locks release if either
ship becomes fixed. Loose anchored objects remain excluded from debris collection.
Active shields prevent acquiring a new primary lock. Raising shields after capture
does not release the tether or prevent its existing pin/range controls; reacquiring
after release requires lowering the shields again.

The dish uses the supplied 128×128 artwork at 1.5× scale (a six-tile canvas), centered
on the emitter with its open face pointing forward (+Y), and a matching enlarged
collision footprint with four times its original fixture mass, and draws a
faint animated fan with a soft rounded hull cutout. Its color progresses from cyan
through amber to red as strain rises, with irregular red flickering above 85% strain.
The captured ship's helm shows the names of the ships holding it, across console
views, and clears the warning when the last active beam releases.

The user-supplied engage, loop, and disengage sounds play across both participating hulls.
The small dish (`WFTractorBeamEmitterSmall`) uses the same controls, operating cone,
restraint and collection physics. Its centered three-tile sprite has a 2.8 × 1 tile
collision footprint at density 300. It reaches 80 m with 6,000 N maximum force,
10 kW idle draw, 30 kW holding draw, and 150 kW maximum draw. It connects to MV cable
under its center tile; the full-size dish still uses HV. Distance still adds
strain; at maximum range an unresisted capture draws 102 kW. Its visual fan is half
the normal target width and its engage, loop and disengage clips are 6 dB quieter.
The loop starts alongside engagement at half playback gain, without waiting for the intro.
The three supplied hull creaks play intermittently while the beam meets resistance,
including attempts to translate or rotate a held ship; an idle hold does not creak.
Screen distortion,
obstacle occlusion and research unlocks are not implemented. Ship class is controlled by map/loadout
placement; the component is not hard-coded to a vessel class.

Validation:

```text
dotnet test Content.Tests/Content.Tests.csproj --filter FullyQualifiedName~TractorBeam
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter FullyQualifiedName~TractorBeam
```

Integration tests use real grids and machine prototypes. They inject the power
network's received-power result to isolate the tractor system from unrelated
station machinery. The test-vessel check additionally loads the real map and
verifies its shared 2 MW supply approaches maximum dish demand while powering the ship.
Manual multiplayer testing should cover generator load changes,
piloted thrust/braking, crowded battles and the appearance of long beams.
