# The Ashfall Reaver campaign

A round-long, automated threat that scales with time and with the sector danger vote, that every player can see on
the sector map and fight. Built on `_WF/SectorControl` (territory, activity, drawing) and `_WF/Encounters` (ships,
crews, zones, markers, rewards). Rounds run about six hours; every number below is tuned to that.

## Why

Today the Reavers are a dice roll: the storyteller starts a raider pack now and then, it is fought or it jumps out,
and nothing carries over. Nobody can look at the map and say "the pirates are over there, and they are getting
bolder". This gives them ground: strongholds that hold space, patrols that fly it, raids that leave it, and a threat
level that rises while nobody stops them and falls when crews do.

## Strongholds

A stronghold is an encounter (`start: Manual`, `lifetime: Persistent`, never pinned) holding station at a cell
centre: a **hub**. One prototype, `WFReaverStronghold`, whose lead is a pirate frigate with several docking ports and
a shield, and no guard ships. It gains levels as reinforcements dock (see Stronghold levels below). The lead holds and
does not evade (`evades: false`): a hub that ran from an alert would undock everything on it. Only the lead carries
zones (warn 1500 m, attack 900 m, `wf-encounter-pirate-zone` lines, `disengage: Deter` at 1200 m). The encounter has
`leash: 2000` so nobody chases a hauler across the sector. The lead's hold carries four crates of
`WFManifestContraband`: loot for whoever boards and wins. `icon: Flag`; `company: WFAshfallReavers`, `faction:
PirateNF`, profile `WFCrewProfilePirate`, `distress: false`.

**The hub hull.** The lead is a **Saintie**: 4 docking ports and a shield generator (an Aegis,
`ShieldGeneratorMedium`) already wired into its power plant. No other pirate hull with several ports carries one but
the Vulture (9 ports, a Sentinel, 3115 entities against Saintie's 1754), so the hub uses the Saintie and the
campaign only places a generator itself on a hull that has none: when a hub spawns, `EnsureShield` looks for a
`ShipShieldEmitter` on the hull and, if there is none, spawns `WFReaverHubShieldGenerator` (the Aegis) on a free
floor tile with floor on every side, within three tiles of a power provider (an APC or extension cable, the reach of
`ExtensionCableReceiver`), and anchors it there. The ship's power is already on from the encounter spawn, so the
field comes up a few seconds later. The shield covers the hub's own grid only; a docked reinforcement is outside it.

Founding claims the cell and its ring with the encounter entity as the source. A stronghold is **broken** when its
lead is out of the fight (`WFEncounterSystem.InFight` false): the campaign calls `Resolve(Destroyed)` itself, since
the Encounters judge waits for every ship. A lead is out of the fight when its crew are all dead, it is disabled
(`WFCrewShipStatusSystem`: abandoned, disarmed, crippled, or **wrecked**: under 60% of the hull tiles it was first seen
with for 10 s, which covers a hull shot to pieces or split into fragments although its crew live), or its helm has been
unmanned for **two minutes** (the queue's status is `no-pilot`, `pilot-away` or `awaiting-helm`: no living pilot, none
aboard, or none who reaches a helm). Patrols and raids break the same way when their lead goes, though a wing is left. On `WFEncounterResolvedEvent` with `Destroyed` the campaign pays the
bounty (`strongholdBounty[tier]` times one plus half the level), ends the stronghold's reinforcements, releases the
source (freeing every cell it alone supported) and announces. `Ended` (admin or round end) frees
and pays nothing.

**First stronghold.** `firstStrongholdDelay` after `StationsGeneratedEvent`, once the round is in progress, in a
quiet cell 14–24 km from the origin with a clear centre (`WFEncounterSchedulerSystem.IsClearSpace` with 600 m grid
clearance and 6 km station clearance, which also keeps off hidden stations and gravity wells), at least 8 km from any
living player. **Later strongholds** fly out as convoys (below). **Regroup.** With no stronghold standing for
`regroupDelay` and no convoy out, found a fresh light one as the first was, and announce the regrouping, so breaking
the only stronghold buys a real lull instead of ending the arc. The first and a regroup appear at once: nothing stands
to launch them from.

## Stronghold levels

A hub is founded at level 0 and grows one level for each reinforcement that docks with it, up to a cap set by the
threat tier. State is on `WFReaverStrongholdComponent` (`Level`, `NextReinforcement`, `Reinforcements`, `Inbound`,
`Scattered`) and the campaign prototype (`reinforcementInterval`, `maxLevelByTier`, `reinforcements`,
`levelBountyScale`).

| | Quiet | Standard | Dangerous |
|---|---|---|---|
| reinforcementInterval | 20 min | 12 min | 8 min |
| maxLevelByTier (0..5) | 0,1,1,2,2,2 | 0,1,2,2,3,4 | 0,1,2,3,4,4 |

| level | reinforcement | ship |
|---|---|---|
| 1, 2 | `WFReaverReinforcementGunship` | Kalisto, Metis or Bastion: pilot, gunner, captain, 2 hands |
| 3, 4 | `WFReaverReinforcementHeavy` | Saintie, Vulture or Ganymede: 3 hands, 2 guards, a full crew |

All are `start: Manual`, `lifetime: Persistent`, off budget, `evades: false` and fly the hub's company and faction.

**Launching.** A hub that has stood `reinforcementInterval` since its founding or its last level, with no reinforcement
on its way, no fight in progress (`Scattered`, or a hit from a player ship in the last two minutes) and `Level`
under the cap for the current tier, sends the next one
(`TryReinforce`; a hub that cannot send one tries again after a minute). The convoy spawns at a clear point 25 to
28 km from the origin on the side the hub is on (within about 20 degrees of the line from the origin through it), or
failing that at a clear point 3 to 5 km from the hub. The lead is ordered `GoTo` the hub (range 600 m), then `Dock`
on the hub's grid, and the sector watch says "Reaver reinforcements are inbound to K-7, ETA n min" (no chime; the
ETA is the distance at 30 m/s). It carries `WFReaverReinforcementComponent` (stronghold, level, phase, deadline).
There is one on the way per hub.

**Docking.** Each tick the campaign checks whether the reinforcement's grid is docked to the hub's
(`DockingSystem.AreGridsDocked`). On the dock (`CompleteDock`) the hub's level becomes the reinforcement's, it joins
`Reinforcements`, `NextReinforcement` restarts, the hub encounter is renamed "Reaver stronghold XX-123 (level n)",
which the map's hover card and the admin window read, and the legend gains the levels ("level 2", or "levels 3/1");
the sector watch says "The Reaver stronghold at K-7 has grown to level n" (no chime). A pilot that runs out of docking
attempts (`WFPilotDockFailedEvent`; a crowded face, a lane that is never clear, three failed attempts) flies the
reinforcement to 300 m of the hub and holds there (`Holding`), and it still counts: a dock that cannot be flown does
not stall the stronghold. There is no timer on a dock in progress, so a heavy still settling in is not cut off.
Hand-flown NPC docking of large hulls has not been proven, which is why the fallback exists.

**Deadline.** A reinforcement is due at the hub twice the reckoned trip (30 m/s) plus three minutes after it sets
out (`Overdue`). If its crew gave up the flight short of the hub (the queue ran out) it is ordered to dock again, at
most every 30 s. Past its deadline: within 900 m of the hub it is given five more minutes to finish docking; farther
out it is ended, which frees the hub (`Inbound`) and restarts the interval. A reinforcement on its way back that is
past its deadline is ordered to dock again instead.

**Lost on the way.** A reinforcement whose lead is out of the fight is resolved `Destroyed` by the campaign: it
founds no level, pays the patrol bounty to those who hit it, marks them, and is announced "Reaver reinforcements bound
for K-7 are destroyed" with the paid line; the hub tries again after the interval. One lost after docking is dropped
from the list the same way and the level stays. Killing a reinforcement never breaks the stronghold.

**Break-apart.** When a hub takes fresh hits from player ships (the `Hits` count, as for patrol recall), every
reinforcement that has joined it (docked, holding or on its way back) is given `Undock` then `Attack` on the player
ship that is hitting it: of the ships whose hit total grew since the last look, the one with most hits. Reinforcements
already out turn on a different attacker the same way. The hub holds under its shield. A reinforcement that docks
while the fight is on (`Scattered`, or a hit in the last two minutes) goes straight out, onto the last attacker if it
is within 5 km of the hub, else the top attacker near the hub, else the top attacker anywhere. Three minutes after the last hit, with none of the ships that hit the hub within 2.5 km of it, they
fly `GoTo` the hub (600 m) and `Dock` again, with the same deadline and dock-failure rules.

**Breaking the hub.** When the hub's lead is out of the fight the stronghold resolves `Destroyed`; its reinforcements
are ended with it, and the hits players landed on them are added to the hub's so that those who only shot a
reinforcement are paid. The bounty is `strongholdBounty[tier]` times `1 + levelBountyScale * level` (credits scale the
same way), so a level 4 hub pays three times a level 0 one. Ended by an admin or the round, it pays nothing.

## Founding convoys

A later stronghold is not placed: it sets out from one that stands. When held cells ≥ `cellsPerStronghold` ×
strongholds, `foundInterval` has passed since the last completed founding, strongholds plus convoys out are under
`maxStrongholds` and no convoy is out (one founding at a time), the campaign picks the target as before (a held cell on
the edge of held space at least two cells from every stronghold and convoy target, nearer the core preferred, with a
clear centre) and `TryLaunchConvoy` spawns the hub prototype (shield placed as at any founding) at a clear point
600–1000 m from the lead of the standing stronghold nearest the target (off budget, not pinned), with
`WFReaverConvoyComponent` (target cell and point, source stronghold, lead key, launch time, and an overdue time of twice
the reckoned trip plus the slack). Orders by `SetOrders`: the
lead `GoTo` the target centre (range 300, nudged clear as patrol route points are). The
encounter's leash is off while it flies. The target cell is contested for the faction with the convoy as the source
and a deadline of the reckoned arrival (distance / 30 m/s) plus 3 min, moved with `SetContestDeadline` as the convoy
goes, so the framework never takes it first. The sector watch says so without a chime: "A Reaver convoy is under way to
K-7, 4.2 km bearing 045 from Outpost Lambda. Stop it before it digs in." If no stronghold has a clear point beside it,
the founding is tried again after a minute.

**Arrival.** Each tick a convoy whose lead is within 400 m of the target point, or is in the target cell and has flown
its orders or is overdue, is completed by `CompleteFounding`: the convoy component goes,
`WFReaverStrongholdComponent` is added as a founding sets it, the contest is withdrawn, the cell and ring are claimed,
the lead holds as a level 0 hub, the leash comes back around the point it stopped at (the target point, or where a lead that stopped short
in the cell stands), the founding is counted (`LastFounded` from now) and announced as any founding. Hits the convoy
took on the way are carried over (`Hits`, `AttackerHits`, `Recalled` set, `LastHit` out of the support window) and the
ships that landed them are marked once, so they do not read as a fresh attack that recalls a patrol.

**Stuck.** A lead whose crew gave up the flight (`Flown`) short of its cell is given its `GoTo` again. A convoy still
short of its cell once overdue, such as a lead with its thrusters shot out that can still fire, is ended: it founds
nothing, pays nothing, and the one-at-a-time gate opens for the next founding. A new cell size re-aims every convoy at
the cell its target point is in, and the next tick contests that one.
Once strongholds have map assets, `CompleteFounding` is where the station grid is placed and the convoy becomes just
the ship that brings it.

**Stopped.** A convoy whose lead is out of the fight is resolved `Destroyed` by the campaign, as a stronghold is. On
`WFEncounterResolvedEvent` the contest is withdrawn; `Destroyed` pays half the current tier's stronghold bounty (and
half each navy's stronghold credits) to those who hit it, marks them, counts it for the round summary and announces
"The Reaver convoy bound for K-7 is destroyed." with the paid line as other bounties. `Ended` pays nothing.
`wf_reavers status` with no stronghold standing and a convoy out says the next founding waits for it. A convoy is
neither a stronghold nor a patrol: it sends no patrols, does not score, supports no cells and is no raid source.

## Spread

Every `spreadInterval` the campaign claims one frontier cell: unowned, adjacent to a held cell, within
`supportRange` rings of a stronghold (which becomes its source), quiet for `quietMinutes`, and not vetoed. The pick is
weighted toward the origin, so the front creeps inward. A frontier cell that holds a station is not claimed at once:
it is **contested** with a 10 min deadline and announced as a blockade warning naming the station; the framework
claims it at the deadline only if the cell stayed quiet. A Reaver-held station is cut off from NPC freight
(`blockades: true` on the faction) and is a raid target.

**Disputed cells.** The campaign reports the cell of every campaign ship (hubs, reinforcements, patrols, raids,
convoys) to sector control each tick. A cell that also holds a crewed player ship is disputed, and the campaign holds
its clocks there: a stronghold's `NextReinforcement` (and, once hit, its return countdown), a reinforcement's or
convoy's overdue deadline and a convoy's contest deadline are pushed forward by the time elapsed. Spread never picks
a disputed cell. When the dispute ends the clocks run on; if the Reaver side died (a patrol destroyed) the existing
rules free the cell as usual.

## Patrols

Each stronghold keeps up to `patrols[tier]` packs out, under a campaign cap of `clamp(players / 5, 1, 6)` packs.
`WFReaverPatrol`: lead Kalisto, Metis, Bastion or Garm with a pilot, gunner, captain and two hands; wing Carrion,
Hazel, Horsefly or Tick with a pilot and gunner on `Escort`. Transient, off budget, no leash, warn 900 m, attack
450 m, `disengage: Deter` 1200 m, `icon: Skull`. Spawned at a clear point 600–1000 m from its stronghold, then given
its route with `WFEncounterSystem.SetOrders`: `GoTo` (range 200) three to five held-cell centres as a random walk
through held space, each checked clear (a point that is not is nudged or skipped), then `GoTo` home. Home with the
route flown, it resolves `Completed`, jumps out and is swept; the stronghold sends another after `patrolInterval`.

**Patrol killed.** When a patrol resolves `Destroyed`, the cell its lead died in is freed unless a stronghold stands
in it, the patrol bounty is paid, and the sector watch says so without a chime ("Reaver patrol destroyed at K-7, the
cell is clear, bounty paid"). A patrol whose lead is out of the fight (above) is broken at once, whatever its wing is
doing: the encounter resolves `Destroyed` (cell freed, bounty paid, cap freed) and the wing turns for the point it set out
from. **Leash.** A patrol's lead outside held space and the ring of cells round it for 10 s is called back: its orders
become the rest of its route and home (its recall, if it was recalled), which also breaks a captain's evasion chase, and
the ship it was chasing goes on cooldown. **Stronghold under attack.** Each fresh hit from a player ship makes a hub
recall every patrol within two cells (`GoTo` the stronghold at 600 m, then `Circle` it) and, when it has no reinforcement
docked, no sortie out, the campaign running, the ship within 5 km and five minutes since the last, send a **sortie pack**:
a fresh offBudget patrol with orders `GoTo` the ship, `Attack` it, then home. The pack gives the chase up and flies home
when it is more than 6 km from the hub or its target has been out of held space for 20 s. A hub's gunners also answer a
ship that fires on it from beyond its 1200 m deterrence range, out to three times that, and its zones stay up while its
own reinforcement is docked. A reinforcement that broke away to attack is called back past 6 km.

**Retaken cells.** Sector control frees a held cell a player ship has sat in unopposed for a while (`WFSectorRetakeAttemptEvent`
asks, `WFSectorCellRetakenEvent` follows); the campaign vetoes a cell with a standing stronghold in it, which is freed only
by breaking the hub, and announces the others, with no chime: "K-7 retaken from the Reavers." A ship out of the fight no
longer counts as the Reavers' presence in a cell.

**Freed space and the ending.** Any Reaver cell that is freed (a broken hub, a retake, `clear`) is clearing for
`wf.sector.clearing_minutes`: no drones, and Sector Watch says so with no chime, one line per batch freed in the same
frame: "Reaver space at K-7, K-8 is clearing. Drone activity returns in 10 minutes. Clear the area." When the last
standing stronghold breaks with no convoy out, a chimed line follows the stronghold's own: "The Ashfall Reavers are
broken. Their strongholds are gone and the sector is clear." The legend shows "broken, no strongholds standing" until a
regroup founds a new hub (its own announcement, as before), and the round summary counts the times they were broken.
Nothing extra is said when the round ends with the campaign rule.

## Raids

One campaign-wide raid clock from tier 3, interval `raidInterval[tier]` divided by nothing (not per stronghold),
concurrency capped at `clamp(players / 8, 1, 4)`, skipped when nothing qualifies. Targets come from activity data: a
crewed player ship in or beside held space (a marked ship anywhere on the map, below), not docked, not raided in the
last 45 min, weighted by crew count (three times that for a marked ship); every
third raid targets instead the nearest non-protected station to held space (a core station from tier 4), where the
navies' persistent patrols meet it. Three prototypes: `WFReaverRaidPack` (raider + wing, orders `GoTo` the target at
600 m then `Attack` it), `WFReaverRaidBoarders` (`hunt: true`, as the storyteller's boarders) and
`WFReaverRaidStation` (raider + wing, `GoTo` the station standoff, `wander: 4` there). Launched from the stronghold
nearest the target; the target ship gets a warning with bearing and distance over its PA and in chat to those aboard
(`WFSectorNoticeSystem.Tell`), and the common channel hears it too. The
storyteller's own raiders keep spawning everywhere: the campaign answers `WFEncounterWeightEvent` and scales the
weight of `WFEncounterPirateRaiders` and `WFEncounterPirateBoarders` by `threatScale[tier]`, capped at 1.5.

## Intercepts and marks

Staying in Reaver space gets a ship hunted. The campaign tick times every crewed player ship (the activity system's
`CrewedShips`) in a cell the faction holds; leaving held space resets its clock. Once a ship has been inside for
`interceptDwell` and is off its cooldown, a patrol is turned on it, within a campaign-wide cap of simultaneous
intercepts by tier (`intercepts`): the nearest patrol that is out, not already intercepting and not recalled, within
three cells of the ship; failing that, a fresh patrol from the nearest stronghold if the patrol caps allow. Its orders
(`SetOrders`): `GoTo` where the ship is (range 600 m), `Attack` the ship, then the route points it had still to call
at and home, so a patrol whose target is disabled flies on instead of going idle. The ship gets "Reaver patrol turning
to intercept Hauler, bearing 045, 3.2 km." over its PA and in chat to those aboard, and the common channel hears it,
with bearing and range from the ship to the patrol. A ship no patrol could be sent after is tried again in 30 s.

An intercept ends when its target is disabled (`WFCrewShipStatusSystem.IsDisabled`, the test the `Attack` task ends
on), deleted or off the sector map, has been out of held space for 20 s, or has been more than three cells from the
patrol for 20 s, or the patrol's lead has strayed out of held space and the ring round it (the leash above). The patrol gets the rest of its route and home again, and the target goes on `interceptCooldown`. A
patrol recalled to its stronghold, or one that resolves, drops its intercept the same way. The state is
`InsideSince` and `InterceptCooldowns` on `WFReaverCampaignComponent`, and `Intercepting`, `InterceptSince`, `Resume`
and the two give-up clocks on `WFReaverPatrolComponent`.

**Marks.** Every player ship that lands a hit on a stronghold, or hit a patrol that is destroyed (the encounter's
`ShipHits`), gets `WFReaverMarkComponent` for 30 min, refreshed by each new hit or kill. Raids weigh a marked ship
three times as heavy and take it as a target anywhere on the sector map, not only in or beside held space; its warning
reads "Reaver raiders are hunting Hauler, bearing 045, 3.2 km." Marks run out in the tick.

## Threat

Score = strongholds × 10 + held cells + 20 × (1 − distance of the nearest held cell to the origin / 27 000). Tier
thresholds are the campaign's (`tierThresholds`, five numbers). Tier names: None, Scattered, Organised, Entrenched,
Dominant, Overrun. A rise is announced at once; a fall only after the score has stayed under the threshold for 10
min, and never within 10 min of the last tier announcement. The announcement comes from the sector watch
(`wf-reaver-watch`), with `/Audio/Misc/redalert.ogg` and a red colour rising, `/Audio/Misc/notice1.ogg` and a teal
colour falling, and names the tier, the strongholds and cells held and the current stronghold bounty. Founding,
breaking and regrouping are announced the same way with the cell callsign and the bearing and range from the nearest
station. The legend line sent to the framework reads "Ashfall Reavers: Entrenched, 3 strongholds, 21 cells, bounty
180,000".

## Rewards

`WFEncounterRewardSystem.PayBounty(encounter, reward, deed, supporters)` pays everyone who hit the encounter's ships
(`MinimumHits` across all sides, alive, not of the losers' company) a share of the spesos, split first per ship then
per head, under the hourly cap, with each navy's own faction credits (the campaign's `credits` list: one credit entity,
companies and stronghold and patrol amounts per navy; the first grant rides on the main reward and the rest as
`extraCredits`, rewards of no spesos); and pays a
20 % support pool to the supporters: crewed player ships that spent 90 s within 2500 m of the stronghold while it was
under attack, meaning within two minutes of the last hit on it, without hitting it (medics, tenders, scouts). The
campaign tracks supporters on `WFReaverStrongholdComponent`. `PayBounty` returns how many players were paid; the
announcement says the bounty was paid only when it is above zero. Stronghold bounty: `strongholdBounty[tier]` (Standard 60k, 90k, 120k, 160k, 200k,
250k) times one plus half the stronghold's level; patrol bounty `patrolBounty` (20k). Paid on `Destroyed` only.

## The game mode

The Reavers are their own game mode, "Ashfall" (preset `MonoAshfall`, alias `ashfall`, shown in the gamemode vote). It has
the same rules as Standard plus the game rule `WFReaverCampaignRule`, and the same points of interest spawn. The campaign
is on only while that rule runs; `wf.reavers.enabled` (default false) is an admin override that forces it on without
the rule, and `addgamerule WFReaverCampaignRule` starts it by hand. While it is off, `wf_reavers status` says the game
mode is not running and names both ways in.

The lobby's sector danger vote picks the campaign: each `wfReaverCampaign` names the `wfEncounterPreset` it serves.
The campaign runs only when `wf.encounters.enabled` and `wf.sector.enabled` are on and the Ashfall rule runs (or the override is set); a
preset with no campaign runs none (logged once).

| | Quiet | Standard | Dangerous |
|---|---|---|---|
| firstStrongholdDelay | 60 min | 25 min | 20 min |
| regroupDelay | 45 min | 30 min | 20 min |
| spreadInterval | 20 min | 12 min | 7 min |
| quietMinutes | 15 | 10 | 6 |
| foundInterval | 90 min | 60 min | 40 min |
| cellsPerStronghold | 8 | 7 | 6 |
| maxStrongholds | 2 | 4 | 5 |
| supportRange | 2 | 2 | 3 |
| patrols by tier (0–5) | 0,1,1,1,1,2 | 0,1,1,2,2,2 | 0,1,2,2,3,3 |
| patrolInterval | 15 min | 10 min | 7 min |
| interceptDwell | 60 s | 60 s | 60 s |
| interceptCooldown | 10 min | 10 min | 10 min |
| intercepts by tier (0–5) | 0,1,1,1,1,2 | 0,1,1,2,2,2 | 0,1,2,2,3,3 |
| raidInterval by tier (3–5) | 60, 50, 40 min | 40, 32, 26 min | 30, 24, 18 min |
| threatScale by tier | 1,1,1,1.1,1.2,1.3 | 1,1,1.1,1.2,1.35,1.5 | 1,1.1,1.2,1.3,1.4,1.5 |
| tierThresholds | 10,22,40,60,80 | 12,30,55,80,105 | 15,35,60,90,120 |

Standard, unopposed: Scattered at the first founding (25 min), Organised about 2.5 h, Entrenched about 3.5 h,
Dominant near the fifth hour, the front in the inner ring by the end. Any crew can set that back at any time.

## Everyone can take part

- Guns: hunt patrols (frees a cell at once, paid), break strongholds (collapses a region, paid, loot aboard). Either
  marks the ship for raids for half an hour.
- No guns: cross held space quickly (a minute inside brings a patrol); fly a lane to keep it quiet (a blockade counts moving ships only, so docking or loitering does not stop it);
  stand by an assault for the support pool; haul and salvage.
- Navies: the Halcyon's cell is never taken, but a front at the doorstep is theirs, and each navy's faction credits
  (TSF and PDV alike) flow through the bounty.
- Ghosts: strongholds, patrols and raids on the Encounters tab of the orbit menu as any encounter.
- Admins: `wf_reavers status | found [x y] | convoy | reinforce <stronghold uid> | tier <n> | spread | patrol | raid | intercept <grid uid> |
  pause | resume | clear` (`found` founds a level 0 hub at once as an admin shortcut, `convoy` sends one out as the campaign does, `reinforce`
  sends a stronghold its next reinforcement now, within the tier cap; status lists each stronghold's level and
  reinforcements, convoys with their target and distance left, reinforcements with their phase and range, intercepts
  and marks), and every campaign encounter in
  the Encounters admin window.
- Round end: a summary line (peak cells held, strongholds founded and broken, patrols destroyed, convoys stopped)
  through `RoundEndTextAppendEvent`.

## Module layout (`ReaverCampaign`)

- `Content.Shared/_WF/ReaverCampaign`: `WFReaverCampaignPrototype.cs` (`wfReaverCampaign`: `preset`, every number
  above, the prototype ids, bounties, sounds and colours), `ReaverCampaignCVars.cs` (`wf.reavers.enabled`).
- `Content.Server/_WF/ReaverCampaign`: `README.md`, `Components/WFReaverStrongholdComponent.cs` (cell, tier at
  founding, hits seen, supporters), `Components/WFReaverConvoyComponent.cs` (target, source, lead, launch time),
  `Components/WFReaverReinforcementComponent.cs` (hub, level, phase, arrival time),
  `Components/WFReaverPatrolComponent.cs` (stronghold, route),
  `Components/WFReaverRaidComponent.cs` (target), `Components/WFReaverMarkComponent.cs` (until when a ship is
  marked), `Components/WFReaverCampaignComponent.cs` (campaign state on the sector map entity: tier, score, clocks,
  intercept clocks, counters for the round summary), `Systems/WFReaverCampaignSystem.cs` with partials
  `.Strongholds.cs`, `.Hub.cs` (shield placement, levels), `.Reinforcements.cs`, `.Convoys.cs`, `.Spread.cs`, `.Patrols.cs`, `.Intercepts.cs`, `.Response.cs` (sorties), `.Retakes.cs`, `.Raids.cs`, `.Threat.cs`, `.Announce.cs`,
  `.Rewards.cs`, `Commands/WFReaverCommand.cs`.
- `Resources/Prototypes/_WF/ReaverCampaign/campaigns.yml`, `encounters.yml`; `Resources/Locale/en-US/_WF/ReaverCampaign/reavers.ftl`;
  `Resources/ServerInfo/_WF/ReaverCampaign/Reavers.xml` guidebook page if the guidebook takes module pages without
  an upstream edit (follow an existing `_WF` guidebook page; otherwise leave it out and say so).
- Tests: `Content.IntegrationTests/Tests/_WF/ReaverCampaign/` (founding claims ring, lead down breaks and pays,
  patrol kill frees a cell, spread respects quiet and veto, contest claims at deadline, tier rises and falls with
  hysteresis, regroup, every votable preset has exactly one campaign, intercept past the dwell and not outside held
  space, route restored and cooldown held, marks from a patrol kill and a stronghold hit steering a raid, the Reaver
  border notice, convoys launched, arriving and stopped). Every step is a one-shot method the test can call
  (`TryFound`, `TryLaunchConvoy`, `CheckConvoys`, `CompleteFounding`, `TryReinforce`, `CheckReinforcements`,
  `CompleteDock`, `CheckStrongholds`, `ScatterReinforcements`, `RegroupReinforcements`, `TrySpreadOnce`, `TryLaunchPatrol`,
  `TryLaunchRaid`, `TryIntercept`, `CheckIntercepts`, `SetTier`), with time from `IGameTiming`.

Edits inside `_WF/Encounters` (same fork, no markers; the README overview gains a paragraph): `TrySpawn(...,
bool offBudget = false)`; public `SetOrders(encounter, key, queue)` that sets `HasOrders`/`Flown`; public
`PayBounty` in the reward system with the helper selection pulled out of `OnResolved`; `WFEncounterWeightEvent`
(by ref) raised in the scheduler's `Pick` at the weight line; public `IsClearSpace(map, point, gridClearance,
stationClearance)` and `Stations(map)` on the scheduler.

## Decisions

- Strongholds are encounters, not stations: crews, zones, markers, the admin window, the ghost menu and cleanup
  come for free, and no map file is needed.
- Lead down is broken. The judge waits for every ship; the campaign does not. The same goes for a convoy, and a
  reinforcement lost, docked or not, breaks nothing.
- Later strongholds fly out from a standing one: the expansion is seen coming and can be stopped on the way.
- Killing a patrol frees its cell at once. Slow decay would hide the feedback.
- One raid clock for the campaign, capped by population. Per-stronghold clocks reached 36 raids an hour.
- Start small and grow. A hub is a shielded frigate with nothing beside it; the wall builds as it ages, capped by the
  threat tier, so a quiet round never meets a fully grown one.
- Only tiers, strongholds and patrol kills are announced. Cell churn shows on the map.
- Staying in held space, not crossing it, is what brings a patrol, and intercepts use the patrols already out first,
  so the downside of Reaver space is a fight, not a toll.

## Later

- A medevac tender beside a stronghold under assault (`WFEncounterMedicTrader` via `TryStartAt`).
- A broken stronghold's lead as a claimable hulk (the derelict machinery).
- Relief freight to a liberated station; a traffic boost at low tiers.
- A Wolfgate admin-tab window for the campaign.
- Storyteller raider placement restricted to held and adjacent cells.
