# Encounters

The Random Encounters System (RES). An encounter is a `wfEncounter` prototype: a set of ships, each with its hull
(or a list to pick from), side, crew profile, faction, security and disengage rules, cargo, warning and attack zones
and a queue of orders or a route to fly. `WFEncounterSystem` spawns the ships, loads the run's manifest into their
holds, crews them and gives them their orders through the `NpcCrew` module, resolves the encounter (expired, orders
completed, one side left, nothing left) and removes its ships once no player is near, or has them jump out.

`WFEncounterSchedulerSystem` is the storyteller: it works to a `wfEncounterPreset` (budget, pace, mix), places
round-start encounters once the stations exist, then starts weighted encounters that fit, in open space, beside a
station or on a circuit of several. It is off until `wf.encounters.enabled` is set. `WFEncounterVoteSystem` has the
lobby vote for the preset. `WFEncounterZoneSystem` warns player ships of another company off a ship's warning zone
and has its crew fire on them inside its attack zone; the hail and the call to arms are made once for a side, by
whichever of its ships speaks first, however many hold the zone, and the radar draws one pair of rings round a side
(its widest ranges plus the spread of its ships) rather than a ring per hull. Whose side a vessel is on is judged by
conduct as well as colours: `WFEncounterRewardSystem` answers the crews' friendly-fire query, vouching for a vessel
that the side took as an ally or that has hit the side's enemies at least twice as often as the side itself over the
last half minute, so a stray round in a melee is not an attack. `WFEncounterMarkerSystem` sends every ship of every visible
encounter to all players, each with its side and colour; the radar draws them, with their zones, sector-wide, and
ghosts find them on the Encounters tab of the orbit menu.
A ship's stores are stowed at spawn (`fuelStores` jars and stacks a generator, two by default). A persistent ship's crate
is checked every five minutes and topped up while its engineer lives, it is refuelled like a fighter once he is dead or a
machine of its plant has burned dry, and
`Judge` does not destroy a persistent encounter whose ships are out of the fight only for want of power with no hit on them.

Ships can arrive stranded (`stranded: Fuel | Thrusters | Random`) and pay their side's reward to whoever gets them
under way: thrust held for 20 s, and for a ship wrecked for thrusters, thrust that drives it ahead, not a side
thruster's nudge. Ships can lie in wait (`lurks`) until a player is within the start radius, and an order can target
`@player`. `announcer` names the ship that voices a radio announcement. With two or more sides each gets a radar
colour (`iffColor`, or `WFEncounterSystem.SideColors`).

Every crewed ship's highest in command (captain, then pilot, radio operator, gunner, engineer, marine, deckhand)
carries its deed (`deed`, `WFEncounterShipDeed` by default; null for none) and drops it when he dies.
`WFEncounterDeedSystem`: used in hand, the deed calls `WFEncounterSystem.Free`, which takes the ship out of its
encounter, cancels its crew's orders, frees its passengers, opens its clamps and makes it immune to cleanup, so it
never despawns. It gives no ownership; the encounter completes once it has no ships left.

Encounter ships are simulated while nobody is near (`wf.encounters.simulate`, `WFEncounterSystem.Simulation.cs`). A
simulated ship is a stand-in entity carrying `WFEncounterGridComponent` and `WFCrewVirtualShipComponent` in place of its
hull; `WFCrewObjectiveSystem` flies its queue as a point at cruise speed (travel, timed holds, docking by reaching the
target), and campaigns, markers and judging read it like a ship that is in the fight. A living player or an admin ghost
within `materialize_range` (4 km, past radar range), or a jump plotted to an exit within it (`FTLStartedEvent`), spawns
its hull, crew and retrofit (seeded, so it looks the same each time), hands it the stand-in's orders and retargets any
orders aimed at it. A real ship nobody has boarded, docked with or fought, with its whole crew and no alert, goes back
to simulation after `dematerialize_delay` with every player past `dematerialize_range`. Spawns and despawns are queued
and run one at a time. A ship spawned from simulation is built over several ticks (`WFEncounterSystem.Build.cs`):
its hull is loaded onto a paused staging map, map-initialised 250 entities a tick, moved into place, refitted, crewed
three men a tick and powered up, and only then given the stand-in's orders; it is left out of judging, fights and
despawning meanwhile. Each build logs its time per stage and its worst tick. The hull load itself is one engine call
and the largest step left (a few hundred ms on a big hull in DebugOpt). Simulated docking (`DockedTo`, `RedockTo`, `AreDocked`) carries over both ways,
and a ship spawned on top of another is moved clear. Two simulated ships where one is ordered to attack the other
fight it out by a roll (`duel_mean_seconds`, weighted by hull worth, crew skill and refit); each round raises
`WFEncounterSimulatedHitEvent`, which a Reaver stronghold counts as a hit. A simulated extorter roams and picks its mark
off the map, then closes on it and is spawned on the way. A crew warrant stands on a simulated ship and marks its crew
when the hull spawns; a captain warrant names the man, so its encounter is always real. A ship hit by any ship weapon,
an NPC's included, never goes back to simulation. Tests run with simulation off (`PoolManager.Cvars.cs`).

Pirate and Reaver ships fly by threat tier (`tiers` on a ship: the band with the highest `minTier` at or below the
tier picks the hulls, crew `skills` and `retrofit`). The tier is the one passed to `TrySpawn`, else the Reaver
campaign's (`WFEncounterThreatTierEvent`), else 0; admins set it with `wf_encounter spawn ... tier=N` or the window's
tier picker. Tiers 0-2 fly retrofitted civilian hulls with Green crews, tier 3 mixes heavy civilian hulls and gunships,
tiers 4-5 fly the black-market line. Stronghold hubs draw from a pool of civilian hulls with docking ports and a fixed
gun count. `WFPirateRetrofitSystem` applies a `wfPirateRetrofit` (pirate retrofitting) as a hull spawns: it pulls mapped
guns its pool doesn't list, fills hardpoints at random from the pool within the fire control's processing power, and
wears the decks (worn tiles, rusted walls, grime decals, cobwebs, dead lights, litter). The encounter's rank (its tier,
a stronghold's founding tier plus level, three stars for the citadel) goes out on the markers and is drawn as chevrons
and stars beside the radar label and under the sector map glyph, with the threat and hull class on its card.

The encounter pirates fly as the Ashfall Reavers (`WFAshfallReavers`, a company nobody can pick, at war with both
navies in `standing.yml`) on scrapyard and black-market hulls, in pirate slops. The prototypes cover freight runs, TSF and Phaethon Dynasty patrols (one of each at round start, kept to different
stations and away from the other's home ports with `avoidStations`) and transports, wandering traders (with exotic dealers under mercenary escort), pirate raiders
and boarders, a hauler under attack, TSF and Dynasty backup requests, frigate and capital patrols (and admin-only supercapital ones), stranded freighters, a false mayday with pirates
lying in wait, skirmishes (TSF or Dynasty against raiders, corsairs or each other; plain, and hostile ones that also
fire on players who come close) and black market transports.

The TSF and the Dynasty are under a ceasefire (see `ColdWar`): `standing.yml` lists them under `declaredWar`, which
only counts while the sector's war level is hot. Until then their patrols warn each other's ships off and fire only
when fired on, and the encounters marked `requiresWar` (the navy against navy fights) are not scheduled.

Warships come in three tiers. Frigates (TSF Fujian, Altair, Andromeda; PDV Kortik, Europa, Saintie) are the common big
ships, under attack or on patrol. Capitals (TSF Flyssa, PDV Saturn) are rare scheduled encounters. Supercapitals (the
`WFChengdu` and `WFJupiter` vessels, grids in `Resources/SharedMaps/_WF/Encounters`) are admin-spawn only, as is the
capital fleet battle (gamma alert) and the supercapital one (epsilon alert). Every ship in a battle between sides
(the frigate, capital and supercapital under-attack encounters and both fleet battles) carries a hostile-only zone,
1500 m warning and attack alike, so its faction's enemies are fought on sight and dropped once driven 2000 m off. A
combatant whose attack is flown, or whose target is lost with all hands, while another side still fights is sent at
the nearest enemy left (`WFEncounterSystem.Battle.cs`); once a battle is decided the victors stay on the field with
their zones standing until cleanup, where every other resolved encounter's zones stand down.

A resolved ship is never removed while players are aboard, while it is docked with a player-owned ship (one with a
deed), or within `wf.encounters.cleanup_grace` (300 s) of the last time either was true, so stepping off a wreck to
fetch tools doesn't lose it.

An encounter an admin starts, from the window or the command, is pinned unless told otherwise (the "Keep until ended"
box, or `jump` on the command): it has no clock, takes no slot under the scheduler's cap, never jumps out, and its
ships are never swept up however far away players are, until an admin ends it. Unpinned, it jumps out and is cleaned
up like a scheduled one. The window's search box filters both the start list and the running list by name,
prototype, ship or state.

An encounter can carry a warrant (`bounty:`: a ship key, `target: Captain | Crew`, a base prize in spesos and the
Fluent title and description). When it starts, `WFEncounterBountySystem` posts the warrant on the PDA bounty program, in
the Warrants collection (`WFWarrants`, first tab, PDA notification). Players can read it and cannot post to it or delete
from it: the collection is restricted to the `CentralCommand` access level, because an empty list lets anyone through.
The system posts with no player behind it through a partial of `BountyContractSystem` (`BountyContractSystem.Warrants.cs`):
post, change a reward in place, remove. A warrant on a captain is named for one flagged crewman; on a crew, for the ship.
It is claimed when the crewman is dead (not just down) or gone, or for a crew when every hand is dead or the ship is
gone. The prize is shared equally, into bank accounts, by the players whose ships hit the encounter's ships at least
`MinimumHits` times and the players who hurt a target in person (dead or ghosted, they are still paid), and the living
players aboard the ship or within 200 m when it fell. A share that cannot be paid, and the odd spesos of the split, go
with the next share. The hourly cap on encounter rewards does not apply to a warrant, since the board promised the
number. Then the warrant comes off the board and the encounter resolves as destroyed. An encounter that expires,
completes or is ended, or whose ships are swept up first, takes its warrant down and pays nobody; its clock does not
run out while players are on the warrant (a ship alerted, or a player aboard, docked or within 200 m).
`WFEncounterBountySystem.AddToBounty` raises the prize of a live warrant and updates the board entry, for encounters
whose prize grows. With no bounty board up the warrant is not posted but still stands: it grows and pays the same.

Ships with `flees: <metres>` run from player ships: while one with a player aboard is within that range the ship flies
directly away from it, and with none near it goes back to its wander. It never starts a fight; `evades: false` keeps it
running while its guns answer an attacker, and the default turns it on the attacker. The fugitives (`fugitives.yml`) are
four tiers of this with a warrant on the captain: petty (unarmed shuttle, pilot, Green), wanted (light armed hull, two
guards, Regular), dangerous (corvette, hard crew, Veteran) and most wanted (pirate frigate, full crew, Elite, turns and
fights when attacked).

A ship with an `extort:` block is an extorter (`WFEncounterSystem.Extort.cs`, state in `WFExtortState`). It roams on
its wander legs, flown about the nearest player ship it has no business with yet. Every few minutes (`markMin` to
`markMax`) it picks the nearest ship with a player aboard inside `reach`, closes to `standoff` and keeps station, and
hails it from its own helm through the `Hailing` module's automated path, its captain or pilot speaking. A hail that
rings out, is declined or is cut off from the mark's end is one of three; after the third it says so on the radio and
boards. A ship alongside that cannot be rung (no powered console, running dark, line busy) gets no way out of it: a
line dead for as long as a hail rings is one of the three too, with a warning on the radio. A mark that gets out of
reach between hails is chased again with its count kept, and left alone for 15 minutes only once it has outrun the
extorter for 5. Once answered it names its `toll` and offers two answers, pay or refuse, with 45 seconds to pick.
Paying takes the whole toll from the bank account of whoever picked it, adds it to the encounter's warrant
(`AddToBounty`), and the ship is left alone for 30 minutes while the extorter flies clear and roams. Refusing, being
short of the toll, picking nothing in time or closing the channel gets the ship boarded: the mark is handed to the hunt
(`Hunt(ship, free: false)`), which runs the boarders' raid on that ship alone and, flown, given up or lost, hands the
ship back to roam; a ship it has not got a port on in 10 minutes has outrun it. The raided ship is left alone for 20
minutes. Hands still off the ship are left behind, and taken off the warrant while anyone lives aboard the extorter;
with nobody left aboard they are its crew and stay on it. Extorters share what they know: a ship one of them is
leaving alone, has as its mark or is boarding is not gone for by another. Extorter encounters are
`lifetime: Persistent`: no clock, no jump out, no share of the budget, and the scheduler's one-of-a-kind rule keeps at
most one of each prototype alive. `extorters.yml` has three tiers flying as the Reavers with a warrant on the crew: light
(Hazel, Green, 6000 toll, 15000 base), medium (Metis or Mudskipper, Regular, 15000 toll, 40000 base) and heavy (Kalisto
or Scallywag, Veteran, four guards, 40000 toll, 90000 base). The admin window and `wf_encounter list` show what each
extorter is doing (roaming, closing on a ship, hailing with the attempt, waiting for an answer, boarding) and what any
warrant pays now.

A ship with `zoneTargets: Interrogate` (the ADS) fires on nobody until asked: `WFEncounterInterrogationSystem`
hails each crewed player ship in its warning zone from its helm and asks `{zoneLines}-hail-question` with YES and NO.
YES puts the ship in `Declared` on every ship of the encounter, and the zone warns and fires on it as any intruder;
NO clears it for 15 minutes, no answer for 3. A ship that fires on it is engaged without a question.

Traders (any ship with `passengers`) answer hails: `WFEncounterTraderHailSystem` picks up a hail ringing at the helm
after 2 s and offers "hold position" and "never mind". Holding pauses its orders for 5 minutes
(`WFEncounterSystem.HoldPosition`), long enough to come alongside; once docked or aboard, the usual serve pause keeps it
there. A trader whose crew are alerted declines every hail.

A ship docked to a station (a station member without a deed) is in sanctuary (`DockedAtStation`): hunts, extorters,
`@player` objectives, the Reaver raids and intercepts and the zones never pick it, and a hunter, extorter, raid or
patrol already on it lets it go when it berths.

Boarders and extorters are stealth ships (`stealth: true`): their IFF label is off for good, so a scope shows an
unknown contact, and their encounter never comes onto the sector markers unless an admin reveals it. Every hunting
ship carries docking clamps (`WFDockingClampSystem`): the ship it docks with cannot undock from it at its own helm or
jump until someone uses Release docking clamps on the raider's shuttle console (they then stay open ten minutes) or
the raider has no powered helm left. When they bite, both ships hear them slam home, everyone on the prey gets a
screen jolt, a large popup and a PA (or chat) notice, and carries a "Docking clamps engaged" alert while held; the
helms and the clamped port say so on examine. The docking screen shows a pulsing DOCKING CLAMPS ENGAGED band naming
the raider, the clamped port's undock button reads CLAMPED, and pressing it flashes the refusal. A prey that breaks away while the raider is still looting or holding alongside
is gone after again. A warrant with `broadcast` set is read out to the whole sector when it is posted, as the
fugitives' are, and every PDA carries the bounty board program so the Warrants tab is there to read.

Admins use the Encounters window in the Wolfgate admin tab, or `wf_encounter list | spawn <prototype> [distance] |
end <encounter> | schedule | pause | resume`. The window's Running tab lists the encounters beside the one picked, with
its ships (simulated, spawning or spawned) and an order panel: pick a ship, an order and, where it needs one, a player
ship, and `WFEncounterSystem.AdminOrder` has it extort or board that ship, attack or follow it, come to the admin, hold,
resume its own orders, spawn its hull now, or send the encounter away. The Start and Scheduler tabs hold the rest. Other code calls `WFEncounterSystem.TrySpawn`, `Resolve`, `End` and
`Reveal` and listens for `WFEncounterStartedEvent` and `WFEncounterResolvedEvent`.

Systems that run encounters of their own (the Reaver campaign, sector control) have more entry points. `TrySpawn(..., offBudget)`
keeps a transient encounter out of the cap and budget. `SetOrders(encounter, key, queue)` gives one ship a fresh queue
as its own orders. `WFEncounterRewardSystem.PayBounty` pays a beaten encounter's bounty to every player who hit its ships
or downed one of its crewmen in person (crews carry a `KillTrackerComponent`; kills and assists are kept in `Kills`),
split per attacking ship and then per head, a boarding party counting as one more ship, with a fifth for the players aboard any supporter grids, and returns how many
were paid, and its optional `extraCredits` are further rewards of which only the faction credits are handed out, so one
bounty can pay several navies, and its optional `helperMobs` collects everyone it was shared among; `GetHelpers` is
the helper rule it shares with the Decided payout. The scheduler's `IsClearSpace` and `Stations` give placement checks, and
the broadcast by-ref `WFEncounterCollectMarkersEvent` (add markers of your own, as the ShipPa SOS does), `WFEncounterWeightEvent` (change a candidate's weight) and `WFEncounterStationsEvent` (drop stations
from a map's list) let other modules shape what it picks and where.

`Docs/_WF/Encounters/design.md` is the original design. Not built: mass scanner contacts and lazy loading (ships
are always real), freight value and payouts, follow-up encounters, and a real FTL jump on exit.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Encounters/Commands/WFEncounterCommand.cs`](Commands/WFEncounterCommand.cs)
- [`Content.Server/_WF/Encounters/Components/WFDockingClampComponent.cs`](Components/WFDockingClampComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFEncounterBountyComponent.cs`](Components/WFEncounterBountyComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFEncounterComponent.cs`](Components/WFEncounterComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFEncounterDeedComponent.cs`](Components/WFEncounterDeedComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFEncounterGridComponent.cs`](Components/WFEncounterGridComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFExtortState.cs`](Components/WFExtortState.cs)
- [`Content.Server/_WF/Encounters/Components/WFSalvageClaimComponent.cs`](Components/WFSalvageClaimComponent.cs)
- [`Content.Server/_WF/Encounters/Systems/BountyContractSystem.Warrants.cs`](Systems/BountyContractSystem.Warrants.cs)
- [`Content.Server/_WF/Encounters/Systems/DockingSystem.Clamps.cs`](Systems/DockingSystem.Clamps.cs)
- [`Content.Server/_WF/Encounters/Systems/ShuttleConsoleSystem.Clamps.cs`](Systems/ShuttleConsoleSystem.Clamps.cs)
- [`Content.Server/_WF/Encounters/Systems/WFDockingClampSystem.cs`](Systems/WFDockingClampSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterAdminSystem.cs`](Systems/WFEncounterAdminSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterBountySystem.cs`](Systems/WFEncounterBountySystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterDeedSystem.cs`](Systems/WFEncounterDeedSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterInterrogationSystem.cs`](Systems/WFEncounterInterrogationSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterMarkerSystem.cs`](Systems/WFEncounterMarkerSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterRewardSystem.cs`](Systems/WFEncounterRewardSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSchedulerSystem.cs`](Systems/WFEncounterSchedulerSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Admin.cs`](Systems/WFEncounterSystem.Admin.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Battle.cs`](Systems/WFEncounterSystem.Battle.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Build.cs`](Systems/WFEncounterSystem.Build.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.cs`](Systems/WFEncounterSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Deed.cs`](Systems/WFEncounterSystem.Deed.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Derelict.cs`](Systems/WFEncounterSystem.Derelict.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Extort.cs`](Systems/WFEncounterSystem.Extort.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Fugitive.cs`](Systems/WFEncounterSystem.Fugitive.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Hunt.cs`](Systems/WFEncounterSystem.Hunt.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Radar.cs`](Systems/WFEncounterSystem.Radar.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Simulation.cs`](Systems/WFEncounterSystem.Simulation.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Stranded.cs`](Systems/WFEncounterSystem.Stranded.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterTraderHailSystem.cs`](Systems/WFEncounterTraderHailSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterVoteSystem.cs`](Systems/WFEncounterVoteSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterZoneSystem.cs`](Systems/WFEncounterZoneSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFPirateRetrofitSystem.cs`](Systems/WFPirateRetrofitSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFSalvageClaimSystem.cs`](Systems/WFSalvageClaimSystem.cs)
- [`Content.Server/_WF/Encounters/WFEncounterEvents.cs`](WFEncounterEvents.cs)

### Shared

- [`Content.Shared/_WF/Encounters/WFEncounterAdminMessages.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterAdminMessages.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterManifestPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterManifestPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterMarkers.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterMarkers.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterPresetPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterPresetPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFPirateRetrofitPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFPirateRetrofitPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFStandingPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFStandingPrototype.cs)

### Client

- [`Content.Client/_WF/Encounters/MapScreen.Encounters.cs`](../../../Content.Client/_WF/Encounters/MapScreen.Encounters.cs)
- [`Content.Client/_WF/Encounters/ShuttleDockControl.Clamps.cs`](../../../Content.Client/_WF/Encounters/ShuttleDockControl.Clamps.cs)
- [`Content.Client/_WF/Encounters/ShuttleNavControl.Encounters.cs`](../../../Content.Client/_WF/Encounters/ShuttleNavControl.Encounters.cs)
- [`Content.Client/_WF/Encounters/WFEncounterClientSystem.cs`](../../../Content.Client/_WF/Encounters/WFEncounterClientSystem.cs)
- [`Content.Client/_WF/Encounters/WFEncounterColors.cs`](../../../Content.Client/_WF/Encounters/WFEncounterColors.cs)
- [`Content.Client/_WF/Encounters/WFEncounterMarkerClientSystem.cs`](../../../Content.Client/_WF/Encounters/WFEncounterMarkerClientSystem.cs)
- [`Content.Client/_WF/Encounters/WFEncounterRanks.cs`](../../../Content.Client/_WF/Encounters/WFEncounterRanks.cs)
- [`Content.Client/_WF/Encounters/WFEncounterWindow.cs`](../../../Content.Client/_WF/Encounters/WFEncounterWindow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AdminOrders.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AdminOrders.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditLifecycle.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditLifecycle.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditZones.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditZones.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.BatchCore.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.BatchCore.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Battle.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Battle.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Bounty.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Bounty.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Cargo.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Cargo.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Clamps.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Clamps.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Cleanup.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Cleanup.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Conduct.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Conduct.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Deed.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Deed.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Extort.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Extort.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.FriendlyFire.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.FriendlyFire.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Fuel.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Fuel.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.MapRoot.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.MapRoot.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Mechs.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Mechs.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.NeutralZones.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.NeutralZones.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Pinned.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Pinned.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Placement.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Placement.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Reactor.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Reactor.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Retrofit.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Retrofit.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Sanctuary.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Sanctuary.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Simulation.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Simulation.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Stealth.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Stealth.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Storyteller.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Storyteller.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Stranded.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Stranded.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.TraderHail.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.TraderHail.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterVehicleInteriorTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterVehicleInteriorTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Encounters/alerts.yml`](../../../Resources/Prototypes/_WF/Encounters/alerts.yml)
- [`Resources/Prototypes/_WF/Encounters/crates.yml`](../../../Resources/Prototypes/_WF/Encounters/crates.yml)
- [`Resources/Prototypes/_WF/Encounters/deed.yml`](../../../Resources/Prototypes/_WF/Encounters/deed.yml)
- [`Resources/Prototypes/_WF/Encounters/dwellers.yml`](../../../Resources/Prototypes/_WF/Encounters/dwellers.yml)
- [`Resources/Prototypes/_WF/Encounters/encounters.yml`](../../../Resources/Prototypes/_WF/Encounters/encounters.yml)
- [`Resources/Prototypes/_WF/Encounters/extorters.yml`](../../../Resources/Prototypes/_WF/Encounters/extorters.yml)
- [`Resources/Prototypes/_WF/Encounters/fugitives.yml`](../../../Resources/Prototypes/_WF/Encounters/fugitives.yml)
- [`Resources/Prototypes/_WF/Encounters/manifests.yml`](../../../Resources/Prototypes/_WF/Encounters/manifests.yml)
- [`Resources/Prototypes/_WF/Encounters/presets.yml`](../../../Resources/Prototypes/_WF/Encounters/presets.yml)
- [`Resources/Prototypes/_WF/Encounters/retrofits.yml`](../../../Resources/Prototypes/_WF/Encounters/retrofits.yml)
- [`Resources/Prototypes/_WF/Encounters/standing.yml`](../../../Resources/Prototypes/_WF/Encounters/standing.yml)
- [`Resources/Prototypes/_WF/Encounters/vessels.yml`](../../../Resources/Prototypes/_WF/Encounters/vessels.yml)
- [`Resources/Prototypes/_WF/Encounters/warrants.yml`](../../../Resources/Prototypes/_WF/Encounters/warrants.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Encounters/bounties.ftl`](../../../Resources/Locale/en-US/_WF/Encounters/bounties.ftl)
- [`Resources/Locale/en-US/_WF/Encounters/encounters.ftl`](../../../Resources/Locale/en-US/_WF/Encounters/encounters.ftl)
- [`Resources/Locale/en-US/_WF/Encounters/extorters.ftl`](../../../Resources/Locale/en-US/_WF/Encounters/extorters.ftl)

### Textures

- [`Resources/Textures/_WF/Encounters/markers.rsi/`](../../../Resources/Textures/_WF/Encounters/markers.rsi/)
- [`Resources/Textures/_WF/Encounters/ranks.rsi/`](../../../Resources/Textures/_WF/Encounters/ranks.rsi/)

### Shared maps

- [`Resources/SharedMaps/_WF/Encounters/chengdu.yml`](../../../Resources/SharedMaps/_WF/Encounters/chengdu.yml)
- [`Resources/SharedMaps/_WF/Encounters/jupiter.yml`](../../../Resources/SharedMaps/_WF/Encounters/jupiter.yml)

### Docs

- [`Docs/_WF/Encounters/design.md`](../../../Docs/_WF/Encounters/design.md)

## Non-modular edits

- [`Content.Client/Shuttles/UI/DockingScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/DockingScreen.xaml.cs): flash the clamp refusal on the plot.
- [`Content.Client/Shuttles/UI/MapScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/MapScreen.xaml.cs): list encounters among the sector objects
- [`Content.Client/Shuttles/UI/ShuttleDockControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleDockControl.xaml.cs)
  - flag a ship held by docking clamps on the plot.
  - a clamped port's undock says so.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): Sector markers for visible encounters.
- [`Content.IntegrationTests/PoolManager.Cvars.cs`](../../../Content.IntegrationTests/PoolManager.Cvars.cs): tests spawn real ships unless they turn simulation on
- [`Content.Server/Cargo/Systems/PricingSystem.cs`](../../Cargo/Systems/PricingSystem.cs): a claimed hulk is worth a fraction of its parts
- [`Content.Server/Shuttles/Systems/DockingSystem.cs`](../../Shuttles/Systems/DockingSystem.cs): a raider's docking clamps hold the ship until released at its helm.
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs): docking screens show clamped ports.
- [`Content.Shared/Shuttles/BUIStates/DockingPortState.cs`](../../../Content.Shared/Shuttles/BUIStates/DockingPortState.cs): the raider grid whose docking clamps hold this port's joint.
- [`Resources/Prototypes/_Goobstation/Entities/Objects/Devices/pda.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Objects/Devices/pda.yml): warrants reach every PDA
- [`Resources/Prototypes/_Mono/Entities/Objects/Devices/pda.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Devices/pda.yml): warrants reach every PDA
- [`Resources/Prototypes/Entities/Objects/Devices/pda.yml`](../../../Resources/Prototypes/Entities/Objects/Devices/pda.yml): warrants reach every PDA

<!-- WOLFGATE-GENERATED END -->
