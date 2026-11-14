# Contracts

A contract board. Stations post offers on the public board at the crossing of Caelestinus
Central's plaza; any character, Vagrants included, takes one there, pays its deposit if it has one, and has until its
deadline. Faction boards on the Halcyon (TSF) and Camelot (USSP) post work for their own members, paid in faction
credits on top. The Contracts PDA app (on every PDA) tracks the crew's accepted contracts: objectives, the clock, where
to go and coordinates for targets in space, with Abandon. Offers are taken at a board only.

Entry points: `WFContractSystem` (offers, accepting, payouts; partials for objectives, stations, crews, standing
persistence, markers and UI), `WFContractPrototype`, `WFContractorPrototype` and `WFContractFactionPrototype`
(`wfContract`, `wfContractor`, `wfContractFaction`), `WFContractBoardWindow` and `ContractsUi` on the client, and the
`contractpost` and `contractlist` admin commands. Content is in `Resources/Prototypes/_WF/Contracts`: `contractors.yml`,
a file of contracts per contractor under `Contracts/` (faction boards in `factions.yml`), `targets.yml` (bounty and
escort encounters), `factions.yml` (sides) and `entities.yml` (boards, cartridge, cargo).

## How it works

- **Contractors** (`wfContractor`) name the game maps they work from (`stations`, matched to the round's stations by the
  name the round gives them, the point of interest's or the map's). One whose station is not in the round posts
  nothing. Each keeps `offers` offers up; after one leaves the board it waits 90 s before posting again. Offers stay up
  20 to 35 minutes. Nothing is posted unless the public board stands on a station.
- **Boards**: a `WFContractBoard` shows the offers of contractors whose `board` matches its own (`Central`, `TSF`,
  `USSP`, `PDV`). A contractor with `companies` only deals with members of those companies. Central has a board at the
  plaza and one per dock arm; the Halcyon, Camelot and Helios have theirs by their docks (Helios also in the cargo bay).
  Each has a `WFContractCargoPad` beside it. A station only faction-board contractors call home (Helios) is never the
  far end of another board's run (`OpenTo`).
- **Employer**: each contractor's `employer` names who the work is really for, shown above every briefing.
- **Requirements**: `requires` lists items the taker must carry to accept (tows need a load-bearing rope coil and a
  tether installer); `excludePresets` keeps a template off the board in those game presets (the pirate cull in
  Contested Sector).
- **Offers** are rolled from a `wfContract` template when posted: reward, counts, goods, destinations and target are
  fixed then, so the board shows exactly what will be asked. The same template appears twice only when nothing else
  fits.
- **Pay** is the template's base roll plus its objective's extras (per package, per kilometre up to 12 km, supply goods
  at their premium up to `goodsCap` or the top base pay, per anomaly), rounded to 50. Deposits and faction credits are
  separate. `ContractRewardCeilingTest` keeps each tier's best case, shared by the contract's `minCrew`, under 37.5k,
  75k, 125k, 175k and 250k a head.
- **Crew pay**: `crewBonus` adds that share of the pay for each paid crewmate past `minCrew` (default 1), at most four
  (`CrewPool`); mining has 0.5, bounty, salvage and escort 0.25. `minCrew` locks a contract until the taker's crew has
  that many in the round; crew work is priced for that many. The board shows the viewer's crew pool and share.
- **Crews** (Crew Setup) share contracts. A crew holds one contract, and one more each time it doubles: 1, 2, 4, 8 and
  16 members in the round give 1 to 5 slots. A player outside a crew is a crew of one. Crewmates see each other's
  contracts; only the holder can abandon one.
- **Accepting** needs a character (mind with a name, not a ghost), a free crew slot, the crew `minCrew` asks and the
  standing the tier needs.
  The deposit comes from the bank.
- **Ending**: done splits the reward evenly among the holder and their crewmates in the round, returns the deposit and
  any faction `credits` to the holder, raises each one's standing (1, or 2 from tier 3) and lowers it with every
  contractor in `harms`. Failing (deadline, cargo or escort lost, target escaped) or abandoning forfeits the deposit and
  costs the holder 1 standing. A contract called off through no fault of its holder (the target vanished, an admin
  ended the encounter) returns the deposit. Pay that cannot reach a bank is owed and paid when they next open a board
  or the app.
- **Standing** is stored per character (player and character name) and contractor in `wolfgate_contract_standing`,
  loaded when the character spawns. The first time it loads in a round it drifts toward 0 by the contractor's `drift`
  (1). Ranks: Hostile (-6 and below) won't deal at all, Distrusted (-5 to -1) offers tier 1 only, Applicant (0) tier 2,
  then Junior (1) tier 3, Contractor (3) tier 4 and Experienced (6) tier 5; Senior is 10. Standing stays between -20
  and 40.
- **Claims**: a bounty's ships and a salvage wreck carry `[claimed: crew or holder]` in their name on every radar. The
  holder's crew get a waypoint for the target (or cargo to collect) on any player ship they are aboard, moved as it
  moves; one they delete is not put back. When a claimed bounty ends, every player outside the holder's crew whose ship
  hit it loses 2 standing with the contractor, unless they share a side with the holder or the contractor: a
  `wfContractFaction` (TSF, PDV or USSP and their civilians) or the same company. Contracts and pay owed last the
  round.

## Objectives

- **Cargo placement**: a board leaves cargo on the nearest `WFContractCargoPad` (a `WFContractDrop`) within 8 m on its
  grid, else in front of it. A station start end uses its drop, else its warp point, else its centre. From there the
  cargo goes on the nearest clear floor, walking out tile by tile without crossing walls in a fixed order, so the same
  place always gives the same tile; tables, machines, racks and crates count as blocked. All of a contract's packages
  share one tile. Wreck contents (finds, anomalies, mobs) go on random clear floor of the wreck.
- `WFContractCourier` (Delivery, Hauling): packages spawn at the start end (see cargo placement) and count as delivered the moment
  they are anywhere on the destination station's grids, carried or not. Ends are `Board`, `Contractor` or `Other`
  (another contractor's station, from `destinations` or any). Pay adds `perPackage` and `perKilometre`.
- `WFContractSupply` (Supply, Mining): a manifest crate spawns beside the board; the goods are rolled from `goods`
  (`pick` lines at random, or all). A line matches a stack type, an entity prototype or its children, a tag or a
  component. The crate is collected when it is closed on the contractor's station with the whole manifest inside;
  closing it short there says what is missing, and examining it shows progress. Pay adds `premium` times the goods'
  market value. A crate from a failed contract is the holder's to keep.
- `WFContractBounty` (Bounty): accepting starts one of `encounters` (Encounters module, `start: Manual`, no warrant)
  in open sector space `minDistance` to `maxDistance` from the contractor's station, at least `stationClearance`
  (2.5 km) from every station and clear of planet wells. It is done when the encounter resolves Destroyed; expired or
  flown off fails it. The target shows on the sector radar like any encounter and leaves a claimable deed.
- `WFContractRecovery` (Salvage): accepting loads a wreck from `wrecks` in open space, placed as bounties are, and puts
  `item` on a free tile, with any `infestation` mobs; bringing it aboard the destination station completes it. The wreck
  is removed when the contract ends unless a player is within 300 m.
- `WFContractSurvey` (Science): loads a wreck like a recovery with `count` anomalies from `anomalies` (started at
  severity 0.3 at most) and its `infestation`. Done once every anomaly is an anomaly scanner's `ScannedAnomaly`, read by
  polling so the scanner's own events stay upstream's; fails if one is gone unscanned. Pay adds `perAnomaly`.
- `WFContractTow` (Salvage): loads a wreck like a recovery; done when the hull's bounds come within `range` (150 m) of
  the contractor station's largest grid; fails if the hull is destroyed.
- `WFContractEscort` (Escort): accepting starts one of `encounters` 1.2 to 2 km out from the contractor's station with
  the destination station as its one stop. Its `ship` (a `flyRoute` ship) waits there until a player comes within the
  encounter's `startRadius`, then flies to the stop and away; raiders in the encounter lie hidden, hold a few minutes
  and attack it. Done when the ship has flown its route; failed if it is lost.
- `WFContractMailRun` (Delivery): `stops` stations (never the board's, never a faction-only station) each get one
  parcel addressed to them, all handed out at the board; a parcel counts only at its own station. Pay adds `perStop`
  and `perKilometre` over the whole route.
- `WFContractSweep` (Science): `points` spots in open space round the contractor's station, at least 800 m apart; one
  is logged when the holder or a crewmate comes within `range`. The next unlogged point is the crew's waypoint.
- `WFContractClear` (Bounty): a wreck with `targets` (carp nests, which keep spawning carp) and `infestation`; done once
  every target is destroyed.
- `WFContractRepair` (Salvage): a wreck with a damaged `device` (the nav beacon); done once it is welded whole and has a
  power cell in `cell_slot`. The wreck is then deleted after `departure` (10 min) once no player is aboard.
- `WFContractDownload` (Salvage): a wreck with a `terminal`; progress banks every check while the holder or a crewmate
  is within `range`, and from the first moment someone is, `waves` spawn aboard every `waveInterval`.
- `WFContractInspect` (Bounty): a stopped ship (a wreck map) with hostile `crew` and suspect `cargo`; the holder's crew
  get an Inspect cargo verb on it that completes the contract.
- `WFContractOperation` (Salvage): one wreck that needs all three: its anomalies scanned, its `item` brought aboard the
  contractor's station, and the hull towed within `range`.
- Surveys take `derelicts` (twin surveys load a second derelict 160 m away and spread the anomalies over both).
  Recoveries take `perUnlockedNode`, pay per unlocked node of a delivered xenoarchaeology artifact. A template's
  `followUp` is offered only to the crew that finished it, for 15 minutes; chain links have `weight: 0` and are never
  rolled on their own. `ForceAccept` gives an offer with no checks or deposit, for admins and tests.
- **Planetary** is a category with no objective yet. A planetary objective is a new `WFContractObjective` subclass
  with its own roll, start, check and cleanup cases, and `WFContractSite.Planet` for the UI.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Contracts/ServerDbManager.Contracts.cs`](ServerDbManager.Contracts.cs)
- [`Content.Server/_WF/Contracts/WFContract.cs`](WFContract.cs)
- [`Content.Server/_WF/Contracts/WFContractCommands.cs`](WFContractCommands.cs)
- [`Content.Server/_WF/Contracts/WFContractComponents.cs`](WFContractComponents.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Crew.cs`](WFContractSystem.Crew.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.cs`](WFContractSystem.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Markers.cs`](WFContractSystem.Markers.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Objectives.cs`](WFContractSystem.Objectives.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Operations.cs`](WFContractSystem.Operations.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Persistence.cs`](WFContractSystem.Persistence.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Requirements.cs`](WFContractSystem.Requirements.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Stations.cs`](WFContractSystem.Stations.cs)
- [`Content.Server/_WF/Contracts/WFContractSystem.Ui.cs`](WFContractSystem.Ui.cs)

### Shared

- [`Content.Shared/_WF/Contracts/WFContractObjectives.cs`](../../../Content.Shared/_WF/Contracts/WFContractObjectives.cs)
- [`Content.Shared/_WF/Contracts/WFContractPrototype.cs`](../../../Content.Shared/_WF/Contracts/WFContractPrototype.cs)
- [`Content.Shared/_WF/Contracts/WFContractUi.cs`](../../../Content.Shared/_WF/Contracts/WFContractUi.cs)

### Client

- [`Content.Client/_WF/Contracts/ContractsUi.cs`](../../../Content.Client/_WF/Contracts/ContractsUi.cs)
- [`Content.Client/_WF/Contracts/WFContractBoardBoundUserInterface.cs`](../../../Content.Client/_WF/Contracts/WFContractBoardBoundUserInterface.cs)
- [`Content.Client/_WF/Contracts/WFContractBoardWindow.cs`](../../../Content.Client/_WF/Contracts/WFContractBoardWindow.cs)
- [`Content.Client/_WF/Contracts/WFContractControls.cs`](../../../Content.Client/_WF/Contracts/WFContractControls.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Contracts/ContractFlowTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Contracts/ContractFlowTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Contracts/ContractOperationsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Contracts/ContractOperationsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Contracts/ContractPrototypeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Contracts/ContractPrototypeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Contracts/ContractRewardCeilingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Contracts/ContractRewardCeilingTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Contracts/contractors.yml`](../../../Resources/Prototypes/_WF/Contracts/contractors.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/anomalous_lab.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/anomalous_lab.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/caelestinus.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/caelestinus.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/camelot.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/camelot.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/factions.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/factions.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/halcyon.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/halcyon.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/hokkaido.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/hokkaido.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/sevastopol.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/sevastopol.yml)
- [`Resources/Prototypes/_WF/Contracts/Contracts/stellar_blessing.yml`](../../../Resources/Prototypes/_WF/Contracts/Contracts/stellar_blessing.yml)
- [`Resources/Prototypes/_WF/Contracts/entities.yml`](../../../Resources/Prototypes/_WF/Contracts/entities.yml)
- [`Resources/Prototypes/_WF/Contracts/factions.yml`](../../../Resources/Prototypes/_WF/Contracts/factions.yml)
- [`Resources/Prototypes/_WF/Contracts/guidebook.yml`](../../../Resources/Prototypes/_WF/Contracts/guidebook.yml)
- [`Resources/Prototypes/_WF/Contracts/targets.yml`](../../../Resources/Prototypes/_WF/Contracts/targets.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Contracts/content.ftl`](../../../Resources/Locale/en-US/_WF/Contracts/content.ftl)
- [`Resources/Locale/en-US/_WF/Contracts/contracts.ftl`](../../../Resources/Locale/en-US/_WF/Contracts/contracts.ftl)

### Guidebook

- [`Resources/ServerInfo/_WF/Contracts/Guidebook/Contracts.xml`](../../../Resources/ServerInfo/_WF/Contracts/Guidebook/Contracts.xml)

## Non-modular edits

- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - standing with contractors
  - one standing per character and contractor
  - a character's standing with one contractor of the contract board
- [`Resources/Maps/_Mono/Outpost/caelestinus_central.yml`](../../../Resources/Maps/_Mono/Outpost/caelestinus_central.yml): Contract boards with pickup pads at the central plaza and beside each of the six dock arms.
- [`Resources/Maps/_Mono/POI/camelot.yml`](../../../Resources/Maps/_Mono/POI/camelot.yml): USSP faction contract boards with pickup pads off the warp corridor and by the north-west and south-west docks.
- [`Resources/Maps/_Mono/POI/pdvhelios.yml`](../../../Resources/Maps/_Mono/POI/pdvhelios.yml): PDV faction contract boards with pickup pads in the civilian arm by the west docks and in the cargo bay.
- [`Resources/Maps/_Mono/POI/tsfmchalcyon.yml`](../../../Resources/Maps/_Mono/POI/tsfmchalcyon.yml): TSF faction contract boards with pickup pads in the arrival hall, by both west public docks and the navy dock hall.
- [`Resources/Prototypes/_NF/Guidebook/nf14.yml`](../../../Resources/Prototypes/_NF/Guidebook/nf14.yml): contract board guide

<!-- WOLFGATE-GENERATED END -->
