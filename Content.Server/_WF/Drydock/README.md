# Drydock

Anvilhold Drydock is a POI station (`WFDrydock`, `Resources/Maps/_WF/Drydock/drydock.yml`) that repairs and refits
ships parked in its four berths, North, East, South and West. A pilot either pulls up to a berth console or hails the
dockmaster from their helm (Hailing module) and books a berth. Any ship can also build a mobile drydock, which pays in
materials instead of credits.

Entry points: `WFDrydockSystem` (berths, quotes, payment; `.Job.cs` for jobs and cranes, `.Booking.cs` for the hail
service, bookings and guides, `.Materials.cs` for material bills), `WFShipRefitSystem` and `WFPartRefitSystem` (refit
tiers), and the client `WFDrydockCraneVisualSystem` (crane animation and rig) and `WFDrydockConsoleWindow` (the
console deck).

## Berths and cranes

A berth is the set of cranes linked to a `WFComputerDrydock` with a multitool, from its sender port to their receiver
ports (`ports.yml`). A crane serves one console, so linking it elsewhere moves it. The work area is the union of the
cranes' reach, starting `BerthGap` tiles down each arm, and ships approach from the arms' direction. The ship in a
berth is the one with the most tiles in its area, so a hull bigger than the berth still counts; a booked ship has to
come in until the centre of its bounds is in the area.

Cranes come in three sizes, `WFDrydockCrane` (36-tile arm), `WFDrydockCraneLarge` (60) and `WFDrydockCraneHuge` (80,
the station's), each with a `Mirrored` variant. They are 2x2 HV machines whose speed scales with the power they get.
Below 25% a crane stalls, and starting work needs every crane at half power. A job whose cranes stay stalled for
`powerAbortTime` (60 s) aborts and refunds its unfinished work.

Each arm lane is scanned every 2 s, gap included. A solid entity there (walls, windows, machines; not cables, pipes
or decals) blocks the crane: it takes no work, and stalls its slot if it blocks mid-job. The boom also stops short of
a solid entity on its row, so items beyond it are out of reach.

## Bookings and jobs

The dockmaster, Hammer Ironstrike (`WFDrydockDockmaster`, a dwarf with HailResponder), mans
`WFComputerDrydockTraffic` and answers hails. His booking window is a per-caller hail service (`WFDrydockOperator`).
A booking takes the smallest free berth that fits the ship, for 5 minutes, and gives the ship a guide frame and
approach lights (`WFDrydockGuideComponent`, drawn by `WFDrydockGuideOverlay`). Work starts once the ship has held
still in the berth for 2 s and is paid then; a booking that lapses or is cancelled costs nothing. Walk-in consoles
sell work on the ship in their berth directly, and their berth lights button shows the same guide for
`BerthLightsTime` seconds.

A job restores missing hull tiles and entities and heals damage, from the ship's repair snapshot
(`ShipRepairDataComponent`). Cranes take the nearest item and skip any they can't reach within 10 s, refunding it. At
full power a repair takes at most 6 minutes (`maxDuration`) and a refit 2 (`maxRefitDuration`), crane deploy and
travel included; longer work is sped up to fit, and lost power adds the time it costs. When a job ends, the console
announces it at the console and to a payer elsewhere; it can also use a radio channel (`noticeChannel`), which no
console sets.

## Pricing

A hull tile costs 120 cr. Rebuilding an entity costs 60 cr labor plus the more of twice its estimated price and 35% of
its construction materials' value (`WFDrydockSystem.Construction.cs`), so a plastitanium wall, priced low but built
from four 600 cr sheets, comes to about 960 cr. Healing costs the rebuild price times the share of health lost. Work
is capped at 50% of the ship's shipyard price (`VesselComponent`), or 40% of its full rebuild price for ships not
bought at one. A fee of 2.5% of that value (at least 1,500 cr) comes on top and is never refunded.

## Refits

A ship owns refits (`WFShipRefitsComponent`, saved with it): a tier of a refit line (`wfShipRefit`, `refits.yml`) and
the units it paid for there. A line's `effect` is its kind, and a server system implements each kind by handling its
survey, prospect and apply events (`WFShipRefitEffect`); `WFShipRefitSystem` holds ownership and pricing. New kinds,
such as action modules or armor, are a new effect and its system. The thruster, gyroscope and power lines are
`WFPartRatingRefit` (`WFPartRefitSystem`): each tier swaps the line's machine parts for higher-rated stock parts, priced
per part: 1,000 cr for Mk II, 4,000 for Mk III and 10,000 for Mk IV.

The atmospheric thruster line is `WFLandingThrusterRefit` (`WFLandingThrusterRefitSystem`): one tier that converts
every linear thruster the way a landing thruster kit does (Planets module), at 800 cr per thruster. A converted
thruster keeps full thrust and its normal power draw below orbit, where an unconverted one gives half thrust for
three times the power, so a fully converted ship hovers on the power it already flies on. It keeps the thruster's
parts, so it stacks with the thruster line. A single-tier line can name its tier with `mark` (the keys' "to Mk II")
and `lamp` (the lamp's numeral).

A refit buys the next tier any unit falls short of, for every unit of the line, and is recorded once its job finishes
with nothing skipped. A repair then brings each owned line back to tier, rebuilt machines included: units the ship paid
for cost `refitRepairShare` (20%) of their tiers, and units past that coverage, such as machines added later, pay in
full. Refit costs sit outside the repair cap. The ship repair tool rebuilds machines stock.

## Console

The console window is an instrument deck in the cockpit style (CombatConsole's instrument kit). A live berth plot
(`WFDrydockBerthPlot`) draws the bay, the station around it, the ship's hull, the work left on it (missing hull red,
missing parts as red diamonds, damage amber) and each crane as the client animates it. A ship outside the plot gets
an edge pointer with its range. Open consoles refresh once a second.

## Mobile drydock

`WFComputerMobileDrydock` (board `WFComputerMobileDrydockCircuitboard`) and `WFMobileDrydockCrane` (board
`WFMobileDrydockCraneMachineCircuitboard`, MV power, at stock parts a 26-tile berth starting 3 tiles past the machine, 13 tiles
across). Both boards print at the circuit imprinter after Shuttlecraft research. To mirror a mobile crane, unbolt it,
open its panel and cut with wirecutters for 2 s (`WFDrydockCraneMirrorSystem`). It works walk-in only and never on its
own grid.

The mobile console charges no fee and pays in materials (`WFDrydockMaterialPaymentComponent`), from its cranes' own
storage or their linked material silo. Work is billed at material units per credit; refits use their tier's
`materials` per part instead. Rates are pegged to a T2 ship voucher (100 steel, 50 plasteel, 50 glass and 10
plastitanium sheets for a 150k to 500k ship): 25k cr of work takes about 14 steel sheets, and `maxBill` caps a repair
at about 60% of the voucher for a 250k ship, scaled by ship price. A rebuild also takes 1% of any construction material
the rates don't cover, so a plastitanium wall adds 4 units of plastitanium. Refunds go back into the job's first crane.

A mobile crane also links a parts silo and a chemical silo (Lathe module), from the silo's window like a lathe, and
parts silos then take any machine part (`parts` on its `FabricationSiloClient`). A refit unit billed in full takes the
stock part it installs from the parts silo, if there is one, instead of its tier materials
(`WFDrydockSystem.Supplies.cs`). Only that exact prototype counts, so trade goods that are machine parts, such as
`CapacitorEconomy2`, are never spent. Units restored at the repair share pay in materials, so a spare part isn't spent
on a 20% bill. Unfinished work's parts go back to the silo, or onto the nearest free floor beside the crane. The console
lists everything the parts silos hold and the chemicals in the chemical silos; nothing uses chemicals yet. Bills (the
repair quote, the salvage preview and each refit's cost) list one row per material, red where the cranes hold too
little.

### Hull breaking

A mobile console also breaks up the ship in its berth for materials (`WFDrydockSystem.Break.cs`), behind a confirm on
its Break hull key. Anything but a station can be broken: outposts and POIs (station members with no `ShuttleDeed`) and
the drydock's own grid are refused, while player ships, encounter and event ships are fair game. Breaking a crewed
ship is an attack on it, reported on start and with every tile through `WFCrewAlertSystem.ReportBreaking`, so its
crew, escorts and allies respond. Nobody aboard stops the job.

The hull is peeled tile by tile from its edges inward: each crane takes the nearest tile it reaches that has an open
side and whose loss leaves the hull in one piece, so the grid never splits. Tiles out of reach are skipped, and only
tiles that are the last link between parts out of reach are left with them. A hull being broken only has to overlap
the berth, since peeling moves its centre. A crane strips everything anchored on the tile, 0.45 s per repairable entity plus 0.1 s for the tile,
divided by crane speed and capped at 10 minutes (`maxBreakDuration`), so a Caracara is about 3 crane-minutes of work. People and loose things drift off as their tiles
go, mobs are taken out of stripped containers, and anything still aboard is set adrift before the last tile, which
takes the grid with it.

Each finished tile credits the slot's crane silo or crane (`breakDefaultYield`, `breakYield` and `breakTileYield` on
the payment component): 25% of every material in what it strips, but only 5% of the plastitanium and composite, plus 25
steel units per tile, and 2 chunks of scrap per tile (`breakTileSalvage`, uncapped, and never billed to rebuild the
tile). An abort pays nothing more. The caps grow with each tile the job lifts (`breakCap`: 1 plastitanium or composite
sheet per 20 tiles, plasteel per 2, steel per tile), and yield past them waits for later tiles, so the outer walls that
come off first are paid as the peel goes on. A hull broken whole or in parts, aborted and restarted, pays the same.

A stripped machine's board and parts, and a computer's board, go to the crane's parts silo, or onto the nearest free
floor beside the crane when the silo doesn't take them. Guns on hardpoints come off whole as a `WFSalvagedFlatpack`, put
in the parts silo or left at the crane the same way. It unpacks at half the damage that would destroy the gun and sells
for a quarter of its price.

The hull keeps its repair snapshot, so a repair can rebuild what was broken, but `WFHulkComponent` records the broken
tiles and rebuilding a tile or part on one bills its full materials, outside the repair's limits: a part's construction
materials and a tile's plating. Breaking returns at most a share of that, so break and rebuild never pays. An encounter
leaves a hull alone for 5 minutes after its last broken tile.

### Crane upgrades

A mobile crane takes machine part upgrades (`WFDrydockCraneUpgradesComponent`, applied in `WFDrydockSystem.Upgrades.cs`
on `RefreshPartsEvent`). Each part type sets one axis by its tier, as the drydock refits count them (Mk I stock, Mk II at
rating 3, Mk III at 4, Mk IV at 6): manipulators set speed, matter bins set reach and capacitors trim the draw. Speed
scales crane travel and work time and adds to power; a crane at half power still works at half its upgraded speed.

| Tier | Speed | Berth (length x width) | Speed and reach draw | Capacitor | Full set |
|---|---|---|---|---|---|
| Mk I | 1x | 26 x 13 | 1x each | 1x | 6 kW |
| Mk II | 1.5x | 34 x 17 | 1.9x each | 0.9x | 19.5 kW |
| Mk III | 2.25x | 44 x 21 | 3.1x each | 0.85x | 49 kW |
| Mk IV | 3x | 60 x 26 | 5x each | 0.8x | 120 kW |

The draw is pegged to ship generation. Small ships run one Super PACMAN (30 kW) and medium ships two PACMANs, a
CR-Pinch or a one-core AME (60 to 120 kW), so a pair of Mk II cranes browns out a small ship and a Mk III pair strains
most medium ones. A Mk IV pair (240 kW) needs a two-core AME or better and a second substation, since a substation passes
150 kW; a Mk IV crane without its Mk IV capacitor draws that much on its own. Mixed parts only pay for the axes upgraded:
Mk IV manipulators on stock bins draw 30 kW.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Drydock/WFDrydockBerth.cs`](WFDrydockBerth.cs)
- [`Content.Server/_WF/Drydock/WFDrydockBookingComponent.cs`](WFDrydockBookingComponent.cs)
- [`Content.Server/_WF/Drydock/WFDrydockCraneUpgradesComponent.cs`](WFDrydockCraneUpgradesComponent.cs)
- [`Content.Server/_WF/Drydock/WFDrydockJobComponent.cs`](WFDrydockJobComponent.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.BerthLights.cs`](WFDrydockSystem.BerthLights.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Blockers.cs`](WFDrydockSystem.Blockers.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Booking.cs`](WFDrydockSystem.Booking.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Break.cs`](WFDrydockSystem.Break.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Construction.cs`](WFDrydockSystem.Construction.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.cs`](WFDrydockSystem.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Job.cs`](WFDrydockSystem.Job.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Linking.cs`](WFDrydockSystem.Linking.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Materials.cs`](WFDrydockSystem.Materials.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Refits.cs`](WFDrydockSystem.Refits.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Supplies.cs`](WFDrydockSystem.Supplies.cs)
- [`Content.Server/_WF/Drydock/WFDrydockSystem.Upgrades.cs`](WFDrydockSystem.Upgrades.cs)
- [`Content.Server/_WF/Drydock/WFDrydockWorkItem.cs`](WFDrydockWorkItem.cs)
- [`Content.Server/_WF/Drydock/WFHulkComponent.cs`](WFHulkComponent.cs)
- [`Content.Server/_WF/Drydock/WFLandingThrusterRefitSystem.cs`](WFLandingThrusterRefitSystem.cs)
- [`Content.Server/_WF/Drydock/WFPartRefitSystem.cs`](WFPartRefitSystem.cs)
- [`Content.Server/_WF/Drydock/WFSalvagedHardwareComponent.cs`](WFSalvagedHardwareComponent.cs)
- [`Content.Server/_WF/Drydock/WFShipRefitsComponent.cs`](WFShipRefitsComponent.cs)
- [`Content.Server/_WF/Drydock/WFShipRefitSystem.cs`](WFShipRefitSystem.cs)

### Shared

- [`Content.Shared/_WF/Drydock/SharedFlatpackSystem.Salvage.cs`](../../../Content.Shared/_WF/Drydock/SharedFlatpackSystem.Salvage.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockConsoleComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockConsoleComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockConsoleUi.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockConsoleUi.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneExamineSystem.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneExamineSystem.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneMirrorDoAfterEvent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneMirrorDoAfterEvent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockCraneMirrorSystem.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockCraneMirrorSystem.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockGuideComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockGuideComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockMaterialPaymentComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockMaterialPaymentComponent.cs)
- [`Content.Shared/_WF/Drydock/WFDrydockOperatorComponent.cs`](../../../Content.Shared/_WF/Drydock/WFDrydockOperatorComponent.cs)
- [`Content.Shared/_WF/Drydock/WFFlatpackUnpackedEvent.cs`](../../../Content.Shared/_WF/Drydock/WFFlatpackUnpackedEvent.cs)
- [`Content.Shared/_WF/Drydock/WFLandingThrusterRefit.cs`](../../../Content.Shared/_WF/Drydock/WFLandingThrusterRefit.cs)
- [`Content.Shared/_WF/Drydock/WFPartRatingRefit.cs`](../../../Content.Shared/_WF/Drydock/WFPartRatingRefit.cs)
- [`Content.Shared/_WF/Drydock/WFShipRefitEffect.cs`](../../../Content.Shared/_WF/Drydock/WFShipRefitEffect.cs)
- [`Content.Shared/_WF/Drydock/WFShipRefitPrototype.cs`](../../../Content.Shared/_WF/Drydock/WFShipRefitPrototype.cs)

### Client

- [`Content.Client/_WF/Drydock/WFDrydockBerthPlot.cs`](../../../Content.Client/_WF/Drydock/WFDrydockBerthPlot.cs)
- [`Content.Client/_WF/Drydock/WFDrydockConsoleBoundUserInterface.cs`](../../../Content.Client/_WF/Drydock/WFDrydockConsoleBoundUserInterface.cs)
- [`Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml`](../../../Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml)
- [`Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml.cs`](../../../Content.Client/_WF/Drydock/WFDrydockConsoleWindow.xaml.cs)
- [`Content.Client/_WF/Drydock/WFDrydockCraneVisualSystem.cs`](../../../Content.Client/_WF/Drydock/WFDrydockCraneVisualSystem.cs)
- [`Content.Client/_WF/Drydock/WFDrydockGuideOverlay.cs`](../../../Content.Client/_WF/Drydock/WFDrydockGuideOverlay.cs)
- [`Content.Client/_WF/Drydock/WFDrydockGuideSystem.cs`](../../../Content.Client/_WF/Drydock/WFDrydockGuideSystem.cs)
- [`Content.Client/_WF/Drydock/WFDrydockReadout.cs`](../../../Content.Client/_WF/Drydock/WFDrydockReadout.cs)
- [`Content.Client/_WF/Drydock/WFDrydockTierPips.cs`](../../../Content.Client/_WF/Drydock/WFDrydockTierPips.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Drydock/DrydockHailTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/DrydockHailTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/DrydockTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/DrydockTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockBerthTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockBerthTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockBreakTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockBreakTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockClientTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockClientTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockSiloTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockSiloTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockUpgradeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Drydock/MobileDrydockUpgradeTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Drydock/consoles.yml`](../../../Resources/Prototypes/_WF/Drydock/consoles.yml)
- [`Resources/Prototypes/_WF/Drydock/cranes.yml`](../../../Resources/Prototypes/_WF/Drydock/cranes.yml)
- [`Resources/Prototypes/_WF/Drydock/dockmaster.yml`](../../../Resources/Prototypes/_WF/Drydock/dockmaster.yml)
- [`Resources/Prototypes/_WF/Drydock/lathe_recipes.yml`](../../../Resources/Prototypes/_WF/Drydock/lathe_recipes.yml)
- [`Resources/Prototypes/_WF/Drydock/mobile.yml`](../../../Resources/Prototypes/_WF/Drydock/mobile.yml)
- [`Resources/Prototypes/_WF/Drydock/poi.yml`](../../../Resources/Prototypes/_WF/Drydock/poi.yml)
- [`Resources/Prototypes/_WF/Drydock/ports.yml`](../../../Resources/Prototypes/_WF/Drydock/ports.yml)
- [`Resources/Prototypes/_WF/Drydock/refits.yml`](../../../Resources/Prototypes/_WF/Drydock/refits.yml)
- [`Resources/Prototypes/_WF/Drydock/salvage.yml`](../../../Resources/Prototypes/_WF/Drydock/salvage.yml)
- [`Resources/Prototypes/_WF/Drydock/sounds.yml`](../../../Resources/Prototypes/_WF/Drydock/sounds.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Drydock/drydock-booking.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-booking.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-console-ui.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-console-ui.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-console.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-console.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-crane.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-crane.ftl)
- [`Resources/Locale/en-US/_WF/Drydock/drydock-dockmaster.ftl`](../../../Resources/Locale/en-US/_WF/Drydock/drydock-dockmaster.ftl)

### Textures

- [`Resources/Textures/_WF/Drydock/crane.rsi/`](../../../Resources/Textures/_WF/Drydock/crane.rsi/)
- [`Resources/Textures/_WF/Drydock/crane_machine.rsi/`](../../../Resources/Textures/_WF/Drydock/crane_machine.rsi/)

### Maps

- [`Resources/Maps/_WF/Drydock/drydock.yml`](../../../Resources/Maps/_WF/Drydock/drydock.yml)

## Non-modular edits

- [`Content.Shared/Construction/SharedFlatpackSystem.cs`](../../../Content.Shared/Construction/SharedFlatpackSystem.cs): salvaged hardware unpacks damaged
- [`Resources/Prototypes/_Mono/Entities/Objects/Devices/ship_vouchers.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Devices/ship_vouchers.yml): single use, so a drydock repair undercuts printing another card
- [`Resources/Prototypes/Recipes/Lathes/Packs/science.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/science.yml): mobile drydock boards
- [`Resources/Prototypes/Research/industrial.yml`](../../../Resources/Prototypes/Research/industrial.yml): mobile drydock boards

<!-- WOLFGATE-GENERATED END -->
