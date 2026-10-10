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
and has its crew fire on them inside its attack zone. `WFEncounterMarkerSystem` sends every ship of every visible
encounter to all players, each with its side and colour; the radar draws them, with their zones, sector-wide, and
ghosts find them on the Encounters tab of the orbit menu.

Ships can arrive stranded (`stranded: Fuel | Thrusters | Random`) and pay their side's reward to whoever gets them
under way: thrust held for 20 s, and for a ship wrecked for thrusters, thrust that drives it ahead, not a side
thruster's nudge. Ships can lie in wait (`lurks`) until a player is within the start radius, and an order can target
`@player`. `announcer` names the ship that voices a radio announcement. With two or more sides each gets a radar
colour (`iffColor`, or `WFEncounterSystem.SideColors`).

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

An encounter an admin starts, from the window or the command, is pinned unless told otherwise (the "Keep until ended"
box, or `jump` on the command): it has no clock, takes no slot under the scheduler's cap, never jumps out, and its
ships are never swept up however far away players are, until an admin ends it. Unpinned, it jumps out and is cleaned
up like a scheduled one. The window's search box filters both the start list and the running list by name,
prototype, ship or state.

Admins use the Encounters window in the Wolfgate admin tab, or `wf_encounter list | spawn <prototype> [distance] |
end <encounter> | schedule | pause | resume`. Other code calls `WFEncounterSystem.TrySpawn`, `Resolve`, `End` and
`Reveal` and listens for `WFEncounterStartedEvent` and `WFEncounterResolvedEvent`.

`Docs/_WF/Encounters/design.md` is the original design. Not built: mass scanner contacts and lazy loading (ships
are always real), freight value and payouts, follow-up encounters, and a real FTL jump on exit.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Encounters/Commands/WFEncounterCommand.cs`](Commands/WFEncounterCommand.cs)
- [`Content.Server/_WF/Encounters/Components/WFEncounterComponent.cs`](Components/WFEncounterComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFEncounterGridComponent.cs`](Components/WFEncounterGridComponent.cs)
- [`Content.Server/_WF/Encounters/Components/WFSalvageClaimComponent.cs`](Components/WFSalvageClaimComponent.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterAdminSystem.cs`](Systems/WFEncounterAdminSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterMarkerSystem.cs`](Systems/WFEncounterMarkerSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterRewardSystem.cs`](Systems/WFEncounterRewardSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSchedulerSystem.cs`](Systems/WFEncounterSchedulerSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Battle.cs`](Systems/WFEncounterSystem.Battle.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.cs`](Systems/WFEncounterSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Derelict.cs`](Systems/WFEncounterSystem.Derelict.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Hunt.cs`](Systems/WFEncounterSystem.Hunt.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Radar.cs`](Systems/WFEncounterSystem.Radar.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterSystem.Stranded.cs`](Systems/WFEncounterSystem.Stranded.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterVoteSystem.cs`](Systems/WFEncounterVoteSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFEncounterZoneSystem.cs`](Systems/WFEncounterZoneSystem.cs)
- [`Content.Server/_WF/Encounters/Systems/WFSalvageClaimSystem.cs`](Systems/WFSalvageClaimSystem.cs)
- [`Content.Server/_WF/Encounters/WFEncounterEvents.cs`](WFEncounterEvents.cs)

### Shared

- [`Content.Shared/_WF/Encounters/WFEncounterAdminMessages.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterAdminMessages.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterManifestPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterManifestPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterMarkers.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterMarkers.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterPresetPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterPresetPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFEncounterPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFEncounterPrototype.cs)
- [`Content.Shared/_WF/Encounters/WFStandingPrototype.cs`](../../../Content.Shared/_WF/Encounters/WFStandingPrototype.cs)

### Client

- [`Content.Client/_WF/Encounters/MapScreen.Encounters.cs`](../../../Content.Client/_WF/Encounters/MapScreen.Encounters.cs)
- [`Content.Client/_WF/Encounters/ShuttleNavControl.Encounters.cs`](../../../Content.Client/_WF/Encounters/ShuttleNavControl.Encounters.cs)
- [`Content.Client/_WF/Encounters/WFEncounterClientSystem.cs`](../../../Content.Client/_WF/Encounters/WFEncounterClientSystem.cs)
- [`Content.Client/_WF/Encounters/WFEncounterColors.cs`](../../../Content.Client/_WF/Encounters/WFEncounterColors.cs)
- [`Content.Client/_WF/Encounters/WFEncounterMarkerClientSystem.cs`](../../../Content.Client/_WF/Encounters/WFEncounterMarkerClientSystem.cs)
- [`Content.Client/_WF/Encounters/WFEncounterWindow.cs`](../../../Content.Client/_WF/Encounters/WFEncounterWindow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditLifecycle.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditLifecycle.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditZones.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.AuditZones.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.BatchCore.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.BatchCore.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Battle.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Battle.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Pinned.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Pinned.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Reactor.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Reactor.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Storyteller.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Storyteller.cs)
- [`Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Stranded.cs`](../../../Content.IntegrationTests/Tests/_WF/Encounters/WFEncounterTest.Stranded.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Encounters/crates.yml`](../../../Resources/Prototypes/_WF/Encounters/crates.yml)
- [`Resources/Prototypes/_WF/Encounters/dwellers.yml`](../../../Resources/Prototypes/_WF/Encounters/dwellers.yml)
- [`Resources/Prototypes/_WF/Encounters/encounters.yml`](../../../Resources/Prototypes/_WF/Encounters/encounters.yml)
- [`Resources/Prototypes/_WF/Encounters/manifests.yml`](../../../Resources/Prototypes/_WF/Encounters/manifests.yml)
- [`Resources/Prototypes/_WF/Encounters/presets.yml`](../../../Resources/Prototypes/_WF/Encounters/presets.yml)
- [`Resources/Prototypes/_WF/Encounters/standing.yml`](../../../Resources/Prototypes/_WF/Encounters/standing.yml)
- [`Resources/Prototypes/_WF/Encounters/vessels.yml`](../../../Resources/Prototypes/_WF/Encounters/vessels.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Encounters/encounters.ftl`](../../../Resources/Locale/en-US/_WF/Encounters/encounters.ftl)

### Textures

- [`Resources/Textures/_WF/Encounters/markers.rsi/`](../../../Resources/Textures/_WF/Encounters/markers.rsi/)

### Shared maps

- [`Resources/SharedMaps/_WF/Encounters/chengdu.yml`](../../../Resources/SharedMaps/_WF/Encounters/chengdu.yml)
- [`Resources/SharedMaps/_WF/Encounters/jupiter.yml`](../../../Resources/SharedMaps/_WF/Encounters/jupiter.yml)

### Docs

- [`Docs/_WF/Encounters/design.md`](../../../Docs/_WF/Encounters/design.md)

## Non-modular edits

- [`Content.Client/Shuttles/UI/MapScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/MapScreen.xaml.cs): list encounters among the sector objects
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): Sector markers for visible encounters.
- [`Content.Server/Cargo/Systems/PricingSystem.cs`](../../Cargo/Systems/PricingSystem.cs): a claimed hulk is worth a fraction of its parts

<!-- WOLFGATE-GENERATED END -->
