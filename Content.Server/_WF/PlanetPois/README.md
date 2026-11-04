# PlanetPois

Randomized ground points of interest and connecting routes are generated before a planet preloads.
`WFPlanetPoiSystem` draws procedural dungeons or single-grid authored maps from `wfPlanetPoiTable`,
reserves their footprints and connects every non-isolated site before biome terrain loads. Asclepiu
and Merak use paved roads; the other worlds use routes suited to their terrain. A site that finds no
room, or whose entrances can't be reached, is skipped or left off the roads with a warning, and a route
that can't be laid costs the world its roads, never its sites; roads clear whatever the biome had grown
on unpinned ground, as the boundary ring does.
A priority site (`priority: true`; cargo hubs and fixed cities always) is routed first with a larger search; if it
still misses the roads the plan is retried with new seeds, then with its other entrances first, then with the nearest
ordinary sites left off the roads, before it is ever left off them itself.

The whole placement is spread over ticks: every site, road batch, piece of dressing, tunnel, wreck, camp, guard and
roamer awaits `WFPlanetPreloadSystem.Breathe`, so each tick does at most `wf.planet_generation_budget` ms of it (more in
the lobby), and site, wreck and camp maps are read and parsed on the thread pool (`WFPlanetPoiSystem.Loading`). A map's
bare entities, saved with nothing but where they stand and named by nothing else (walls, rock, cables, water), are
taken out of the load and spawned after it within the budget; a fixed city's are spawned straight onto the world once
the rest of its level is merged, so neither the load nor the merge carries twenty thousand of them in one tick. The
room atlases a site's dungeon draws on are loaded the same way before its dungeon job runs, which would otherwise load
each in one go (`WFPlanetPoiSystem.Atlases`). The road planner, seconds of work on a big world, runs on the thread pool
once the world's water has been found within the budget. A site of more than `wf.planet_merge_slice_tiles` (2048)
tiles has its ground laid a chunk at a time within the generation budget before its grid merge (`LayMergeTilesAsync`),
so the engine merge, which sets every tile in one call, is left only the entities to move; its roofs are set the same
way. Merged
entities stay paused until every one of them is map-initialised. The draws on the random stream come in the same order
as before; only the ticks they fall in differ. What waits for the ground, camp guards outside the hull, residents,
vehicles, patrols and roamers, goes out the same way after the preload, and the ground is marked preloaded once it has.

The roads join places (`WFPlanetRoadPlanner`). On a world with room for them, one or two gate roads run straight out of
the landing zone's edge, the first toward the site that matters most (a big one near), a second toward sites well round
the other side. Every site entrance then joins the nearest road, nearest first, so the roads branch like a tree and
meet at T and Y junctions, and an off-road site with a footpath gets a road out to within `reach` (64) tiles of its
door. A road runs on toward the world's edge through its emptiest stretch (`exits`), and a link closes the longest
detour between two sites (`loops`), but never into a ring round the landing zone. Roads run straight and bend 45° at a
time, keep a little off the landing zone and away from one another, and go round whatever is in the way (a site, a
cavern mouth, a lake). `gates`, `exits`, `loops` and `reach` are set on a table's `roads`; a small world gets just the
sites joined to one another.

No site, road, footpath, camp, wreck, parked vehicle or roamer is put down in standing water
(`BiomeSystem.WfIsWater`, which reads the biome's recipe before the ground exists). A footprint over 48 tiles square may
cover the edge of a pond, which is drained where it is laid. Every world's water comes as lakes and ponds, not seas.

A network that names a `wfRoadDressing` is furnished as it is painted (`WFPlanetPoiSystem.Dressing`,
laid out by `WFRoadDressingPlanner`): the shoulder becomes kerbed pavement facing the road, a dashed
line runs down the middle of the straight stretches, street lights stand on alternating sides, signals
on the corners where roads meet, and parked and wrecked vehicles, barriers, bins and litter are
scattered along it from the Streets and Vehicles props. Blocking pieces keep off the middle of the road
and apart from one another, so a road is never shut, and nothing is placed inside a site. Asclepiu and
Merak have one each (`road_dressing.yml`).

A network's `tunnels` (`WFPlanetPoiSystem.Tunnels`) wall one or two straight stretches of road in the world's rock and roof
them over: dark, with webs and bones along the edges and lurkers inside (`tunnels.yml`: giant spiders and the world's
risen wreck crew). A tunnel keeps off the sites and clear of bends and junctions, and a footpath meeting the road
inside one leaves a gap in its wall. Every road world has them.

A table's `wrecks` (`WFPlanetPoiSystem.Wrecks`, themes in `wrecks.yml`) crash a few vessels off the roads, torn open
and reclaimed by the world, with its wildlife and what's left of the crew inside: spacers dressed for that world who
rise as zombies when spawned (`WFZombifyOnSpawn`, `wreck_crew.yml`). About half the wrecks still have something in the hold
(`wreck_loot.yml`): a crate with a ship gun flatpack and the hardpoint it mounts on, or a crate of gunnery co-processor
chips (`_WF/GunneryChips`).

A table's `camps` (`WFPlanetPoiSystem.Camps`, `camps.yml`) set a ship down intact and square to the world beside a
road, just off the shoulder of a straight stretch and nose to it, where whoever drives by sees it (out in the open
instead when no stretch has room, or for a camp with `roadside: false`), with an armed crew from an NpcCrew profile
guarding it: two or three on the deck at once, more round the hull behind a
ring of cover once the ground there exists. Every prop claims the ground it covers (`footprint` on a camp prop, a single
tile when unset; a tent is 3 by 4 tiles), so tents, fires, cover and guards never lie on each other. Dwelling sites (`dwelling: true`, such as the houses) may get a resident. Every world has a Reaver encampment (Ashfall Reaver raiders) and an ADS
control site (ADS automatons); the hold has loot of the same kind as the wrecks'.

Once the ground exists, a table's `vehicles` (`WFPlanetPoiSystem.Vehicles`, `vehicles.yml`) park a few working
trucks, vans and the odd humvee by the roads and out in the open, part-fuelled, with the key on the driver's seat; in
about a third a squatter (`WFStrangerSquatter`) has holed up with the key in his hand, attacks whoever comes in and
drops it when he dies. A table's `roadPatrols` send an NPC-crewed vehicle up and down a stretch of road, there and back
along points traced down the centreline: Verda (Asclepiu) and Merak each have a Reaver APC
(`WFVehicleCrewReaverRoadPatrol`) that leaves people be unless shot at. They are the only worlds with APCs.

An authored site's `below` is a level under it, such as a basement: it is laid on the world's level under the ground
(the cavern, or the rock a city adds) in the site's frame and turn, clearing the rock there, and the site's floor is
opened over its stairs (`WFPlanetPoiSystem.LayBelow`). With no level under the ground the site goes without it.

An isolated site with `trail: true` stands off the roads and gets a footpath instead (`WFPlanetPoiSystem.Trails`,
routed by `WFPlanetTrailPlanner`): a wandering track of the network's `trailTile`, a tile or two wide, from just
outside its first socket (a door) to the nearest road or earlier footpath. A site more than `trailLength` (240) steps from
any road goes without. `Tools/_WF/PlanetPois/roadside_map.py <map> <name> --trail` turns a mapper's grid into one.

Other modules add sites of their own for a round through `WFPlanetPoiExtraSitesEvent`, raised as a world draws its
sites; theirs are placed first (the planetside cargo hubs, `PlanetCargoHubs`).

Players discover orbit signals by approaching sites on the ground. Content authors add maps, entry
limits and road sockets through the table prototypes; see [MapperGuide](../../../Docs/_WF/PlanetPois/MapperGuide.md)
for the map contract and content roadmap. The authored maps are the mappers' own: Verda's houses,
gas station and bandit hideout, the greenhouse on Verda and Merak, the ancient temple (a shrine over stairs down to
halls in the cavern, guarded by skeletons with swords and holding relics, `temple.yml`) on every world but Carcinoma,
and Carcinoma's shrine, alongside the expedition dungeons. The gas
station's fuel merchant sells usable fuel items through Traders; it does not refuel docked ships. The mechanic's
garage and the roadside inn sit on their own stretch of road (always on Verda, sometimes on Merak): the mechanic
repairs a vehicle parked out front and sells delivery beacons and repair boxes, and the innkeeper takes organs as
payment, sells a few, and eats whoever is left dead in his inn.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/PlanetPois/WFPlanetPoiComponent.cs`](WFPlanetPoiComponent.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiExtraSitesEvent.cs`](WFPlanetPoiExtraSitesEvent.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiPlacementPlanner.cs`](WFPlanetPoiPlacementPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Atlases.cs`](WFPlanetPoiSystem.Atlases.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Camps.cs`](WFPlanetPoiSystem.Camps.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.cs`](WFPlanetPoiSystem.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Dressing.cs`](WFPlanetPoiSystem.Dressing.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Loading.cs`](WFPlanetPoiSystem.Loading.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.MergeSlices.cs`](WFPlanetPoiSystem.MergeSlices.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Network.cs`](WFPlanetPoiSystem.Network.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Roamers.cs`](WFPlanetPoiSystem.Roamers.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Rotation.cs`](WFPlanetPoiSystem.Rotation.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Trails.cs`](WFPlanetPoiSystem.Trails.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Tunnels.cs`](WFPlanetPoiSystem.Tunnels.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Vehicles.cs`](WFPlanetPoiSystem.Vehicles.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetPoiSystem.Wrecks.cs`](WFPlanetPoiSystem.Wrecks.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetRoadPlanner.cs`](WFPlanetRoadPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFPlanetTrailPlanner.cs`](WFPlanetTrailPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFRoadDressingPlanner.cs`](WFRoadDressingPlanner.cs)
- [`Content.Server/_WF/PlanetPois/WFZombifyOnSpawnSystem.cs`](WFZombifyOnSpawnSystem.cs)

### Shared

- [`Content.Shared/_WF/PlanetPois/WFPlanetCampPrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFPlanetCampPrototype.cs)
- [`Content.Shared/_WF/PlanetPois/WFPlanetPoiTablePrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFPlanetPoiTablePrototype.cs)
- [`Content.Shared/_WF/PlanetPois/WFRoadDressingPrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFRoadDressingPrototype.cs)
- [`Content.Shared/_WF/PlanetPois/WFWreckThemePrototype.cs`](../../../Content.Shared/_WF/PlanetPois/WFWreckThemePrototype.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/PlanetPois/AncientTempleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/AncientTempleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/CampDeckTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/CampDeckTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/CarcinomaPoiTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/CarcinomaPoiTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/GreenhouseTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/GreenhouseTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PdvCacheLootTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PdvCacheLootTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetCampTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetCampTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetHiveCampTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetHiveCampTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiContentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiContentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiLegacyNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiLegacyNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiPlacementPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiPlacementPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiProceduralAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiProceduralAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiRotationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiRotationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiSiteFailureTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiSiteFailureTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiTileTurnTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetPoiTileTurnTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadDressingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetRoadPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetTrailPlannerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetTrailPlannerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetVehicleSpawnTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetVehicleSpawnTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetWreckTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/PlanetWreckTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/RoadsideSitesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/RoadsideSitesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetPois/SettledHullLiftoffTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetPois/SettledHullLiftoffTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/PlanetPois/camps.yml`](../../../Resources/Prototypes/_WF/PlanetPois/camps.yml)
- [`Resources/Prototypes/_WF/PlanetPois/dungeons.yml`](../../../Resources/Prototypes/_WF/PlanetPois/dungeons.yml)
- [`Resources/Prototypes/_WF/PlanetPois/entities.yml`](../../../Resources/Prototypes/_WF/PlanetPois/entities.yml)
- [`Resources/Prototypes/_WF/PlanetPois/pdv_cache.yml`](../../../Resources/Prototypes/_WF/PlanetPois/pdv_cache.yml)
- [`Resources/Prototypes/_WF/PlanetPois/road_dressing.yml`](../../../Resources/Prototypes/_WF/PlanetPois/road_dressing.yml)
- [`Resources/Prototypes/_WF/PlanetPois/road_props.yml`](../../../Resources/Prototypes/_WF/PlanetPois/road_props.yml)
- [`Resources/Prototypes/_WF/PlanetPois/tables.yml`](../../../Resources/Prototypes/_WF/PlanetPois/tables.yml)
- [`Resources/Prototypes/_WF/PlanetPois/temple.yml`](../../../Resources/Prototypes/_WF/PlanetPois/temple.yml)
- [`Resources/Prototypes/_WF/PlanetPois/traders.yml`](../../../Resources/Prototypes/_WF/PlanetPois/traders.yml)
- [`Resources/Prototypes/_WF/PlanetPois/tunnels.yml`](../../../Resources/Prototypes/_WF/PlanetPois/tunnels.yml)
- [`Resources/Prototypes/_WF/PlanetPois/vehicles.yml`](../../../Resources/Prototypes/_WF/PlanetPois/vehicles.yml)
- [`Resources/Prototypes/_WF/PlanetPois/wreck_crew.yml`](../../../Resources/Prototypes/_WF/PlanetPois/wreck_crew.yml)
- [`Resources/Prototypes/_WF/PlanetPois/wreck_loot.yml`](../../../Resources/Prototypes/_WF/PlanetPois/wreck_loot.yml)
- [`Resources/Prototypes/_WF/PlanetPois/wrecks.yml`](../../../Resources/Prototypes/_WF/PlanetPois/wrecks.yml)

### Localization

- [`Resources/Locale/en-US/_WF/PlanetPois/pois.ftl`](../../../Resources/Locale/en-US/_WF/PlanetPois/pois.ftl)
- [`Resources/Locale/en-US/_WF/PlanetPois/roadside.ftl`](../../../Resources/Locale/en-US/_WF/PlanetPois/roadside.ftl)
- [`Resources/Locale/en-US/_WF/PlanetPois/settlements.ftl`](../../../Resources/Locale/en-US/_WF/PlanetPois/settlements.ftl)

### Maps

- [`Resources/Maps/_WF/PlanetPois/abandoned_bandit_hideout.yml`](../../../Resources/Maps/_WF/PlanetPois/abandoned_bandit_hideout.yml)
- [`Resources/Maps/_WF/PlanetPois/ancient_temple.yml`](../../../Resources/Maps/_WF/PlanetPois/ancient_temple.yml)
- [`Resources/Maps/_WF/PlanetPois/ancient_temple_below.yml`](../../../Resources/Maps/_WF/PlanetPois/ancient_temple_below.yml)
- [`Resources/Maps/_WF/PlanetPois/atalis_house.yml`](../../../Resources/Maps/_WF/PlanetPois/atalis_house.yml)
- [`Resources/Maps/_WF/PlanetPois/atalis_house_below.yml`](../../../Resources/Maps/_WF/PlanetPois/atalis_house_below.yml)
- [`Resources/Maps/_WF/PlanetPois/carcinoma_fountain_of_life.yml`](../../../Resources/Maps/_WF/PlanetPois/carcinoma_fountain_of_life.yml)
- [`Resources/Maps/_WF/PlanetPois/greenhouse.yml`](../../../Resources/Maps/_WF/PlanetPois/greenhouse.yml)
- [`Resources/Maps/_WF/PlanetPois/mechanic_shop.yml`](../../../Resources/Maps/_WF/PlanetPois/mechanic_shop.yml)
- [`Resources/Maps/_WF/PlanetPois/planetary_hospital.yml`](../../../Resources/Maps/_WF/PlanetPois/planetary_hospital.yml)
- [`Resources/Maps/_WF/PlanetPois/planetary_vault.yml`](../../../Resources/Maps/_WF/PlanetPois/planetary_vault.yml)
- [`Resources/Maps/_WF/PlanetPois/roadside_inn.yml`](../../../Resources/Maps/_WF/PlanetPois/roadside_inn.yml)
- [`Resources/Maps/_WF/PlanetPois/Tests/through_service.yml`](../../../Resources/Maps/_WF/PlanetPois/Tests/through_service.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_corner_house.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_corner_house.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_gas_station.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_gas_station.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_house_1.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_house_1.yml)
- [`Resources/Maps/_WF/PlanetPois/verda_small_house.yml`](../../../Resources/Maps/_WF/PlanetPois/verda_small_house.yml)

### Tools

- [`Tools/_WF/PlanetPois/map_author.py`](../../../Tools/_WF/PlanetPois/map_author.py)
- [`Tools/_WF/PlanetPois/roadside_map.py`](../../../Tools/_WF/PlanetPois/roadside_map.py)

### Docs

- [`Docs/_WF/PlanetPois/MapperGuide.md`](../../../Docs/_WF/PlanetPois/MapperGuide.md)

## Non-modular edits

- [`Content.Server/Zombies/ZombieSystem.cs`](../../Zombies/ZombieSystem.cs): an inert zombie's kills do not rise

<!-- WOLFGATE-GENERATED END -->
