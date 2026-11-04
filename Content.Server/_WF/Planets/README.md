# Planets

Planets you can fly to, land on and crash into. Each world of the sector's star system that has a Wolfgate surface is
a stack of CE z-levels: an orbit layer, air layers and the ground. Ships hop into orbit from the sector with the
shuttle console's orbit button (an unsanctioned world asks first), descend through the atmosphere on landing
thrusters, lift off again, and land hard, skid, break up or crash when lift is lost. Hulls that stop keeping station
fall out of orbit, and a world's gravity well pulls adrift hulls in from the sector.

The hop into and out of orbit is `WFPlanetHopSystem` (`Hop/`), its own state and not FTL: no hull in a hop ever has an
`FTLComponent`, and no drive, beacon, range or cooldown is asked or started, so a driveless hull or one with its drive
cooling down can enter and leave. A hop warms up for five seconds on the old map (ripple, spool-up sound, shields
down), then the hull and the grids docked to it wait five seconds on a private hold map (`WFPlanetHopMapComponent`,
where the approach overlay draws the planet), and land at the spot picked at the start, pushed inside the world's ring
and clear of other grids. The physical refusals are a hop or an FTL jump already under way, a protected or anchored
hull, a transit map, and whatever a system adds on `WFPlanetHopAttemptEvent` (docking clamps, the crash and Reaver
scenarios). Systems that cared about an FTL jump listen for `WFPlanetHopStartedEvent` and `WFPlanetHopCompletedEvent`.

The shuttle console's flight panel (`WFOrbitButton`, fed by `WFConsoleOrbitTargetComponent`) shows the flight
phase, lift and power against what flight needs, what holds a landed hull down, and how many thrusters to convert
or add (`CEZLevelsSystem.WfGetLiftAdvice`). Holding ascend on the ground lifts off and holding descend in orbit enters
the atmosphere when no confirm is needed; the buttons do the same. Ordinary thrusters give half thrust below orbit
and landing thrusters full thrust; power draw is the same as in space. The ship previewer estimates the strongest
gravity a vessel lifts off in (`ShipPreviewLift`). The guidebook page is Planet Flight, under Piloting.

Rideable vehicles use the same z-level physics as loose objects, so a hoverbike and its buckled rider leaving a
descending ship move onto the planet layers before the temporary flight map is deleted.

Worlds have day and night, phased weather with thunder, drawn and voiced by WolfWeather through each world's
`wolfWeathers` table (off until `wf.planet_weather` is set; admins can still force it), fauna, ambience, a map on the orbit radar (`PlanetMap`) and an approach
cinematic on the hop in; Carcinoma is a quarantined flesh world whose tendrils hold landed hulls down; its flesh ground has scabbed lake shores, long blood rivers with clotted banks, bone fields of bleached bone and calcified flesh, hive pockets in the tendon forest, a blood-pool meat marsh and dried crust with dead growth round the groves (`Resources/Prototypes/_WF/Planets/Biomes/carcinoma.yml`). Lower layers of a planet are drawn smaller
(`WFPlanetView`, the perspective Monolith turned off for other z-levels), except to someone inside a building, under a roof on the ground or a city's level, who sees the floors below full size. Over a world's layers the shuttle console's
strategic plot shows the world itself, drawn by the sector's planet shader close enough to fill it, instead of space
(`ShuttleMapControl.PlanetBackground`). A floor nobody built that breaks on a planet, pried up or blown apart, gives
the natural ground back instead of station plating and lattice (`WFPlanetTileBreakSystem`); a player's own floor over
the ground breaks the station way, down to the ground they built on. A blast never leaves plating or lattice on the
ground: natural ground holds or opens into the caverns, and a player's floor never breaks through. Jetpacks only
work in orbit and lattice goes straight onto natural ground. A room built on a planet holds its own air once it has a
floor: bare ground counts as outdoors and keeps the planet's air. A world whose air its own life couldn't breathe lists
that life in `WFPlanetNatives` on its network (wildlife tables, entities, slime breeds), and `WFPlanetNativesSystem`
gives each `BreathingImmunity` while it is there; Carcinoma's carbon dioxide (`_WF/PlanetVents`) is the one so far.
Players use landing thruster kits, parachutes and the
planet timepiece. Admins get the Planet Control panel in the Wolfgate admin tab (`planetcontrol`) for sanction, time
of day, weather and gravity, and `wfplanet` to build a star system's worlds.

Seasons and climate: a world's `wfPlanetWeather` lists `seasons`, looped in order from a random one each round (one
season is perpetual, none means no seasons). Asclepiu runs spring, summer, autumn and winter, 90 minutes each (a year is
six local days; winter snows and fogs, summer storms); Merak dry and dust seasons, Aerumna dry and monsoon, Thrascias a
perpetual deep winter, Fervidus and Carcinoma none. A season weights the world's storms or brings its own, stretches
clear spells and shifts the temperature. `WFPlanetWeatherSystem` rolls each world's weather three hours ahead
(`WFPlanetWeatherComponent.Upcoming`, `TryGetSchedule`), rolled again when an admin sets the weather or the season,
so the forecast is what happens. `WFPlanetClimateSystem` sets the outdoor air: the ground's air temperature plus the
season, the weather (`weatherTemperature`) and the hour (`dayTemperatureSwing` either side, warmest at 15:00, coldest
at 05:00), moving at most 0.1 K a second into the map air of the ground and air layers, which bare ground shares
(Planet Control snaps it to the target at once when an admin sets the hour, weather or season, so a set blizzard is cold
straight away); rooms keep their own air. Other code reads it with `WFPlanetClimateSystem.GetOutsideTemperature(map, at)` and
`GetSeason(map, at)`. The timepiece HUD shows the season and outside temperature, Planet Control shows and sets the
season (`planetcontrol <world> season <id>`), and the weather forecast computer (`WFComputerWeatherForecast`, its
board from the circuit imprinter; one on Caelestinus Central) lists a world's weather and temperature for the next
three hours, exact for the next change and vaguer further out.

Verda has wildlife and forage of its own (`Prototypes/_WF/Planets/Life/verda.yml` and `verda_mobs.yml`). Each region's
biome layer places its own `WFFaunaVerda*` spawner: meadows and woods get mutated deer herds that bolt from anyone close
(one deer in ten charges), foxes, and bees and snakes where the wildflowers and tall grass grow; lake and pond shores
frogs and crabs; the irradiated scars a two-headed deer, irradiated rats and now and then a rat king's pack; the
highlands penguins and frost bears. LivingWorld plants grow in the same regions: forest mushrooms (with a soup of their
own), berry bushes, lake reeds for plant fibre, shore sage and pond lotus to heal with, snapping brambles that lash and
give thorns to a knife, and glowroot that irradiates whoever comes close. The recoloured frost bear and two-headed deer
sprites are made by `Tools/_WF/Planets/gen_verda_life_art.py`.

Entry points: `WFPlanetNetworkSystem` and `WFPlanetRegistrySystem` (the z-level stack per world), `WFOrbitEntrySystem`
(hops in and out of orbit), `WFFlightSystem` with the `CEZLevelsSystem.WF*` partials (flight, liftoff, landing and
crashes), `WFOrbitDecaySystem`, `WFGravityWellSystem`, `WFPlanetWeatherSystem`, `WFParachuteSystem` and
`PlanetControlSystem`. Upstream systems are extended through partials in this module (`CEZLevelsSystem.WF*`,
`ThrusterSystem.WF*`, `ShuttleSystem.WFOrbit`). `WFTerrainAtmosphereSystem` gives every layer with ground a grid
atmosphere that `AtmosphereSystem.WFTerrain` keeps sparse: tiles the layer's biome lays (and what digging turns them
into) are bare ground, treated like tiles off the grid and never tracked until something is built beside them, so
loading terrain costs atmos nothing; `WFTerrainOpenTilesEvent` lets a module name more ground. A tile newly built
on any other grid on a planet layer, such as a city's upper level when it gets its atmosphere, starts with that
layer's air instead of a vacuum; orbit's air is space, so tiles there still start empty.
`BiomeSystem.WFUnload` unloads planet terrain nobody is near, in place of upstream's unloader, which let go of
almost none of it: untouched biome entities go with their chunk, and the ground under a hull, beside a build or
loaded by hand stays. Ground that is only unloaded still holds up whatever was left on it. `BiomeSystem.WFRoll`
makes a planet's rock spawner rolls in the loader, seeded from the tile, so surface rock unloads too and the same
rock comes back. `BiomeSystem.WFLoad` puts off the far part of a planet load area and loads it nearest first over
the next passes, so an arrival or a flight does not load it all in one tick. Round a player's body only the 16-tile
block it stands in and the blocks within `wf.planet_terrain_load_now_pad` (2) tiles load at once; the six by six chunks
round it, `wf.planet_terrain_load_now_ring` 2, was thirty-six chunks in a pass, over a tenth of a second in a cavern.
Unloaded ground is still ground to z-physics (`WfUnloadedGround`), so a body that lands on it does not fall. A hull that sets down flattens what is
under it as an FTL arrival does, breaks the trees and rock it would rest against (`WfClearLandingObstacles`), and
hurts a mob under it and shoves it clear instead of gibbing it (`ShuttleSystem.WFSetDown`). Only walls, windows and doors hold a landing off; trees, rocks, furniture, machines, tables and grilles under the footprint are flattened (`WFLandingSurveySystem.IsCrushable`). A hull is kept inside a world's circle by the middle of its bounds, not its origin, on every layer including orbit: it is pushed back in the last tiles and put back inside, docked grids and all, if it reaches the circle. One entering orbit from further out comes in over the edge on its own side, or further in if another ship is parked there. Every layer of a network has a name (`WFPlanetLayerNameSystem`) and the shuttle map labels sector space, surfaces and levels (`WFMapLabelSystem`).
Every world is a circle (`radius` on its `wfPlanetSurface`, 256 tiles unless the prototype or `wf.planet_radius` says
otherwise): `WFPlanetBoundsSystem` marks each layer with `WFPlanetBoundsComponent` when the network is built, the loader
never generates a chunk outside it (`BiomeSystem.WFBounds`), an impassable boundary rings the ground and the layers
below it, the radar draws nothing past it, a hull can't enter the atmosphere unless it is wholly inside, one adrift out
there is pulled back to the edge before it falls, and one flying out in the air is turned back (`WFPlanetDragSystem`).
`WFPlanetPreloadSystem` then generates the whole circle, nearest the centre first and `wf.planet_preload_budget` ms a
tick, one world at a time, and walls the outermost ring of tiles with the surface's `boundaryWall`; a preloaded ground
(`WFPlanetPreloadedComponent`) keeps its tiles and decals for good. Its trees, rocks and plants are stashed instead
(`BiomeSystem.WFStash`, `wf.planet_terrain_stash`): once everything that needed them in place has been placed, the
preload puts away the untouched biome entities of every chunk nobody is near, the loader lays them again from the
recipe as someone comes within its load area, and the unloader puts them away again once the chunk has been idle for
`wf.planet_terrain_unload_idle`. A touched one stays and pins its tile, as when terrain unloads, and a tile someone
stands on is left bare when the rest comes back. A planet layer's navmesh, and that of any grid standing on one
(a city's upper levels, wrecks), is built only within `wf.planet_pathfinding_range` of players and awake NPCs,
`wf.planet_pathfinding_chunk_budget` chunks an update (`PathfindingSystem.WFPlanetLazy`); upstream built a whole
world's in one tick and kept it. A path asked for across ground not built yet has the unbuilt chunks along its straight
line built first, nearest its start first, and waits for them up to ten seconds instead of finding no way
(`PathfindingSystem.WFPlanetPathWake`). Generation steps that can't be split and run past 100 ms are logged as
`Slow world generation step` (`WFPlanetPreloadSystem.Time`).
`WFPlanetPreloadStartingEvent` goes out the tick before,
for whatever must be placed while the ground is still unloaded, such as cavern mouths, and `WFPlanetPreloadedEvent`
once the ring is laid. Caverns stay streamed, inside the same circle, with their own ring laid onto bare pinned ground.
While the lobby is open, `WFPlanetRegistrySystem` builds the lobby preset's round-start worlds where its star system
will put them (`wf.planet_prebuild`), so the preload runs at `wf.planet_preload_budget_lobby` before anyone is in the
round; the body that spawns at a prebuilt centre under the same name takes that network over in `TryBuildNetwork`,
and any network no body takes is deleted a few seconds into the round.
The rest of a world's generation, its sites, cities, roads, wrecks and camps, the boundary ring and what is let loose
once the ground exists, is spread over ticks the same way: it awaits `WFPlanetPreloadSystem.Breathe`, which carries on
until `wf.planet_generation_budget` ms of the tick are spent (`wf.planet_generation_budget_lobby` in the lobby) and then
resumes next tick. Map files for sites and cities are parsed off the main thread. What can't be split still takes its
own tick: loading one map's entities, merging one site into the ground, one heavy spawn such as a crewed vehicle, and
the road planner. `WFPlanetPreloadedEvent` handlers put that kind of work in its `Work` list, and the ground is only
marked preloaded once it is done.
`WFWorldFormingSystem` sends every client how far each forming world has got, four times a second: the holds (sites,
roads, cavern mouths) report through `WFPlanetPreloadSystem.ReportProgress`, then the chunks, the ring and the work
after it fill the rest. The lobby shows it as a bar under the start time; a player on or over a world still forming
gets a small bar at the top of the screen. Stock weather loops off a planet follow the ambience slider and stop when the
player has no body (`WFWeatherAudioSystem`), since the weather system left them looping in the lobby after a respawn.
When the lobby countdown runs out while a prebuilt world is still forming, `WFPlanetRoundHoldSystem` holds the start
(a hook in `GameTicker.StartRound`, through `WFRoundStartHoldEvent`), tells the lobby the worlds are still forming and
starts the round itself once they are done, or after `wf.planet_round_hold_max` seconds (180) regardless. An admin's
start round waits the same way, under the same cap; a paused countdown calls a held start off. A world that is still
forming once the round is under way, because the cap ran out or it was built mid-round, refuses descent from orbit
with a popup until it is done (`WFOrbitEntrySystem.TryDropFromOrbit`).
`PlanetEntityCensusTest.Census` (run by hand) prints what a formed world holds by source and prototype, and
`PlanetFirstMinuteTest` times the minute after all six of Kyphrus's worlds form, with what each slow tick spent.
Settings are in `PlanetCVars` (`wf.planet_networks`, `wf.planet_terrain_atmos`, `wf.planet_terrain_unload`,
`wf.planet_terrain_load_budget`, `wf.planet_bounds`, `wf.planet_radius`, `wf.planet_preload`,
`wf.planet_preload_budget`, `wf.planet_generation_budget`, `wf.planet_round_hold_max`); ecology and
landing notes and the playtest checklist are in `Docs/_WF/Planets`. `WFBiomeNoiseCacheSystem` keeps one seeded copy
of each biome layer's noise for `SharedBiomeSystem.GetNoise`, which copied it for every tile planets and caverns
generate or sample.

Other modules build on it through `WFDetachedTerrainComponent` (a grid that is ground, not a hull),
`WFLiftoffAttemptEvent`, `WFGridLiftLoadEvent`, `WFPlanetGroundSpawnedEvent`, `WFPlanetLowerLayersEvent` and
`WFPlanetNetworkBuiltEvent` (maps below the ground, kept in `WFPlanetNetworkComponent.LowerLayers`, which
`CEZLevelsSystem.WfClosedToHulls` keeps every hull out of and where an orbital faller is maimed as on the ground) and
`WFOrbitLayerComponent.RadarScars`.

Thrascias is a frozen world, and its ground (`Resources/Prototypes/_WF/Planets/Biomes/thrascias.yml`) is built from the
imported Decor Import and RMC tiles through weather child tiles, so snow falls on all of it. Plain snow is broken by
rough and calm snow and bare frozen dirt, with ice grass, frostbelles, snow shrubs and snow rocks. Where the old ice
patches were there is pack ice (cracked, rough and clear ice) with clear frozen lakes in the middle, ice rocks and
short runs of ice cliff; the clear ice is slightly see-through, so the layer below shows through it, and the lakes and
the smooth clear ice are slippery as the old ice was. Pine pockets are a small
share of the snow. Every liquid plasma pool sits in a crystal field of frozen plasma, with crystal rocks and trees and a
few small crystal wall clusters. Its lakes are real water (`WFThrasciasLakeWater`) over the lake bed tile, so they freeze and thaw
by the air like every world's: at Thrascias's temperature they read frozen from the first tile, and warm the world and they
thaw. Roads and sites keep out of them as out of any lake. `ThrasciasBiomeTest` samples the whole surface and `LakeClimateTest`
drives each world's lakes through its air.

Jetpacks in the atmosphere: a gas jetpack refuses below a world's orbit layer and cuts out if carried down. The
atmospheric jetpack (`WFJetpackAtmospheric`, `WFAtmosphericJetpackComponent`) is the reverse: it burns welding fuel,
flies on the ground and air layers, climbs and descends half a layer a second on the jetpack's ascend and descend
keys, and refuses in orbit, where there is no air to burn. It holds 100 units, burns one a second hovering and two
moving, a quarter more above 1 g, and refuels from any welding fuel tank the way a welder does. Above 1.5 g it will
not lift anyone, so a 3 g world grounds it. An empty tank switches it off mid-air and the wearer falls; a parachute
takes it from there. It flies in a cavern too, and up or down through a hole in the ground, but nowhere else: the
ground overhead is a ceiling (the Caverns `WfSealedAbove`). `WFAtmosphericJetpackSystem` (shared) decides where it lights and refuels it,
`WFAtmosphericJetpackFuelSystem` burns the fuel. John Wolfgate stocks it.

A world's standing water comes as lakes and ponds a few dozen tiles across at most, never seas (`PlanetLakeTest`).
`BiomeSystem.WfIsWater` says whether the biome grows water on a tile from its recipe alone, before the ground exists, so
the PlanetPois placement keeps sites, roads and camps out of it.

Aerumna, the dark 3 g world that went wrong, is a patchwork of regions (`Resources/Prototypes/_WF/Planets/Biomes/aerumna.yml`,
pulled in by `WFShadowClearings` in `open_biomes.yml`). Chromite flats are cut by shrouded clay and patches of bare rock; a fungal
forest of giant mushrooms, glowshrooms and bioluminescent plants grows on shrouded ground; the old shadow groves stay on the flats;
boulder fields lie where the gravity dropped them; pale crystal spires and a few overgrown root-wall ruins are rare. The pools,
chromite ore and fauna are as before. Every Decor Import tile it lays is a `weather: true` child (`WFFloorAerumna*`), so rain falls on
all of it, and `AerumnaBiomeTest` samples the whole surface for the tile set, each region and the walkable share.

Aerumna is the xenonids' homeworld. About 8% of its surface is hive (`WFAerumnaHive`, laid last so its ground wins where regions
overlap): dark crust under wild weeds, with small resin chambers (a ring of resin walls with gaps and doors round a floor of weeds and
eggs), dormant eggs in clusters inside them, and thicker drone, runner and spitter wildlife than the rest of the planet
(`WFFaunaAerumnaHive`). The wild weeds (`WFXenoWeedsWild`, `_WF/Xenonids/Wild`) never spread and any hive's own weeds replace them. A wild
egg (`WFXenoEggWild`) comes grown and opens for whoever walks up to it, like any egg, so it is the real infection; it never turns fragile.
A small hive site, a hive core in a broken ring of resin with eggs and drones about it, is one of Aerumna's
camps (`WFCampXenoHive` in `WFPoisAerumna`). Mouths to the cavern may open on hive ground but never through a wall, door or egg.

Fervidus is black basalt cut by lava channels. Lava has a crust of cooled ash rock round it (the lava noise at a lower
threshold), and about a third of the surface is ashfields: ash flats, scrub ground, sparse ash flowers, shrooms, cacti,
dead scrub and burnt trees, with rare tar and sulfur pools a few tiles across and scattered volcanic rock piles. It
lives in `Resources/Prototypes/_WF/Planets/Biomes/fervidus.yml` (the weather child tiles and the `WFFervidusAshFields`
template) and `WFOpenBasalt`, and `FervidusBiomeTest` samples it. The pools are plain tiles and cosmetic: roads and
sites treat them as dry ground, and the cavern mouths never land on them.

Fervidus has living regions (`Resources/Prototypes/_WF/Planets/Life/fervidus.yml`, strings in `life_fervidus.ftl`, sampled by
`FervidusLifeTest`). Black basalt keeps the argocytes (`WFFaunaFervidus`). The ashfields add ashen laser raptors and slow ash
golems (`WFFaunaFervidusAshfields`). The lava crust, a band laid on the lava noise by the `WFFervidusLavaCrust` meta layer, adds
cinder slimes that set what they hit alight, a very rare lava hellspawn (`WFFaunaFervidusCrust`) and ember flowers that spit a
burning ember at anyone within five metres. All of them take the Xeno faction so the natives don't fight each other, and ignore
the heat and the lack of air. Plants are `LivingWorld` children of the old decor flora: fire-blossoms (burn whoever steps on
them; the bloom is an ingredient for blossom fizz), ash caps and ash leaves (mildly toxic raw, safe once cooked into ash mushroom
soup), cool cacti (a water pod) and ash lungs (a short weak toxin cloud when someone passes).

Verda's ground (`Resources/Prototypes/_WF/Planets/Biomes/verda.yml`, `biomes.yml`, `open_biomes.yml`) is built from the
imported Decor Import and RMC floors and flora, each through a `weather: true` child tile. Plains run to wildflower
meadow and dark forest floor, with oak, hardwood and pine woods; every lake and pond sits in a ring of marsh, mud and
wet sand with reeds and lilies at the waterline; a few per cent of the surface is irradiated scar of dead trees, scrub and
bones; the snow highlands have rough snow, drifts, snow-capped rocks and shrubs. `VerdaBiomeTest` samples the whole world
for each region.

Merak's ground (`Resources/Prototypes/_WF/Planets/Biomes/merak.yml`, `MerakBiomeTest`) is dune seas of golden, tan and pale sand,
over which noise lays red-rock badlands (red desert sand and rock, dark flats, black boulders,
a few short orange mesa clusters), cactus fields (barrel cacti, agave, yucca, mesquite, desert grass), and quake-cracked
gravel flats (gravel, sandrock, cracked mud, rock spires, bones). Oases are rare, a handful on the whole world: each is a
pond of the old water layer ringed by dirt and grass with palms and reeds. Every tile is a `weather: true` child so dust storms draw;
the soft ones still dig through to the cavern. Ore, boulders and fauna are unchanged.

Thrascias is alive by region (`Resources/Prototypes/_WF/Planets/Life/thrascias.yml`, `ThrasciasLifeTest`): penguins and snow foxes on the tundra; frost bears and the rare yeti on the pack ice; crystal crabs, glowing crystal blooms and frost pods in the crystal fields; snow spiders and whipvines in the boreal pockets. Frostbelles can be picked and eaten for a painkiller that slows you, frozen fungus grows only in the snowdrifts and cooks into a warming soup, and ice crystals are chipped with a shovel for crystal shards. The natives don't breathe or feel the cold. The animals are recoloured vanilla sprites (`Tools/_WF/Planets/recolor_thrascias_life.py`).

The descent warning raises `WFLiftWarningEvent` with the rigid set's hull and cargo mass, so a module that can restore
lift (CargoLoad, by dropping crates) adds its hint from the same set lift weighs, and `WFGridLiftLoadEvent` carries
cargo weight into lift and gravgen support. The flight panel shows a cargo row with the rigid set's load
(`WFConsoleOrbitTargetComponent.CargoLoad`).

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Planets/Administration/PlanetControlCommand.cs`](Administration/PlanetControlCommand.cs)
- [`Content.Server/_WF/Planets/Administration/PlanetControlSystem.cs`](Administration/PlanetControlSystem.cs)
- [`Content.Server/_WF/Planets/Atmosphere/AtmosphereSystem.WFTerrain.cs`](Atmosphere/AtmosphereSystem.WFTerrain.cs)
- [`Content.Server/_WF/Planets/Atmosphere/WFTerrainAtmosphereComponent.cs`](Atmosphere/WFTerrainAtmosphereComponent.cs)
- [`Content.Server/_WF/Planets/Atmosphere/WFTerrainAtmosphereSystem.cs`](Atmosphere/WFTerrainAtmosphereSystem.cs)
- [`Content.Server/_WF/Planets/Atmosphere/WFTerrainOpenTilesEvent.cs`](Atmosphere/WFTerrainOpenTilesEvent.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFLoad.cs`](BiomeSystem.WFLoad.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFRoll.cs`](BiomeSystem.WFRoll.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFStash.cs`](BiomeSystem.WFStash.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFUnload.cs`](BiomeSystem.WFUnload.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.WFWater.cs`](BiomeSystem.WFWater.cs)
- [`Content.Server/_WF/Planets/BiomeSystem.Wolfgate.cs`](BiomeSystem.Wolfgate.cs)
- [`Content.Server/_WF/Planets/Bounds/BiomeSystem.WFBounds.cs`](Bounds/BiomeSystem.WFBounds.cs)
- [`Content.Server/_WF/Planets/Bounds/WFPlanetBoundsSystem.cs`](Bounds/WFPlanetBoundsSystem.cs)
- [`Content.Server/_WF/Planets/Bounds/WFPlanetPreloadEvents.cs`](Bounds/WFPlanetPreloadEvents.cs)
- [`Content.Server/_WF/Planets/Bounds/WFPlanetPreloadSystem.Budget.cs`](Bounds/WFPlanetPreloadSystem.Budget.cs)
- [`Content.Server/_WF/Planets/Bounds/WFPlanetPreloadSystem.cs`](Bounds/WFPlanetPreloadSystem.cs)
- [`Content.Server/_WF/Planets/Bounds/WFPlanetPreloadSystem.Progress.cs`](Bounds/WFPlanetPreloadSystem.Progress.cs)
- [`Content.Server/_WF/Planets/CEZLevelsSystem.WFTerrain.cs`](CEZLevelsSystem.WFTerrain.cs)
- [`Content.Server/_WF/Planets/CEZLevelsSystem.Wolfgate.cs`](CEZLevelsSystem.Wolfgate.cs)
- [`Content.Server/_WF/Planets/Commands/WFPlanetCommand.cs`](Commands/WFPlanetCommand.cs)
- [`Content.Server/_WF/Planets/ExplosionSystem.WFPlanets.cs`](ExplosionSystem.WFPlanets.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFFlight.cs`](Flight/CEZLevelsSystem.WFFlight.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFGravityCache.cs`](Flight/CEZLevelsSystem.WFGravityCache.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFLandingClearance.cs`](Flight/CEZLevelsSystem.WFLandingClearance.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFLandingRefusal.cs`](Flight/CEZLevelsSystem.WFLandingRefusal.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFLiftAdvice.cs`](Flight/CEZLevelsSystem.WFLiftAdvice.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFLiftoff.cs`](Flight/CEZLevelsSystem.WFLiftoff.cs)
- [`Content.Server/_WF/Planets/Flight/CEZLevelsSystem.WFVirtualMass.cs`](Flight/CEZLevelsSystem.WFVirtualMass.cs)
- [`Content.Server/_WF/Planets/Flight/MoverController.WFAtmosphere.cs`](Flight/MoverController.WFAtmosphere.cs)
- [`Content.Server/_WF/Planets/Flight/ShuttleSystem.WFSetDown.cs`](Flight/ShuttleSystem.WFSetDown.cs)
- [`Content.Server/_WF/Planets/Flight/ThrusterSystem.WFAtmosphere.cs`](Flight/ThrusterSystem.WFAtmosphere.cs)
- [`Content.Server/_WF/Planets/Flight/ThrusterSystem.WFCrashThrust.cs`](Flight/ThrusterSystem.WFCrashThrust.cs)
- [`Content.Server/_WF/Planets/Flight/ThrusterSystem.WFPowerPulses.cs`](Flight/ThrusterSystem.WFPowerPulses.cs)
- [`Content.Server/_WF/Planets/Flight/WFAtmosphereThrusterComponent.cs`](Flight/WFAtmosphereThrusterComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashApcFaultComponent.cs`](Flight/WFCrashApcFaultComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashApcFaultSystem.cs`](Flight/WFCrashApcFaultSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashFractures.cs`](Flight/WFCrashFractures.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashImpactComponent.cs`](Flight/WFCrashImpactComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFCrashThrustComponent.cs`](Flight/WFCrashThrustComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightAmbienceComponent.cs`](Flight/WFFlightAmbienceComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightAmbienceSystem.cs`](Flight/WFFlightAmbienceSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Breakup.cs`](Flight/WFFlightSystem.Breakup.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.cs`](Flight/WFFlightSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.GroundContact.cs`](Flight/WFFlightSystem.GroundContact.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Lattice.cs`](Flight/WFFlightSystem.Lattice.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Scar.cs`](Flight/WFFlightSystem.Scar.cs)
- [`Content.Server/_WF/Planets/Flight/WFFlightSystem.Skid.cs`](Flight/WFFlightSystem.Skid.cs)
- [`Content.Server/_WF/Planets/Flight/WFGridLiftLoadEvent.cs`](Flight/WFGridLiftLoadEvent.cs)
- [`Content.Server/_WF/Planets/Flight/WFLandingThrusterKitComponent.cs`](Flight/WFLandingThrusterKitComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFLandingThrusterKitSystem.cs`](Flight/WFLandingThrusterKitSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFLiftoffComponent.cs`](Flight/WFLiftoffComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFOrbitalMobFallSystem.cs`](Flight/WFOrbitalMobFallSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFPlanetDragComponent.cs`](Flight/WFPlanetDragComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFPlanetDragSystem.cs`](Flight/WFPlanetDragSystem.cs)
- [`Content.Server/_WF/Planets/Flight/WFSkidComponent.cs`](Flight/WFSkidComponent.cs)
- [`Content.Server/_WF/Planets/Flight/WFThrustAmbienceSystem.cs`](Flight/WFThrustAmbienceSystem.cs)
- [`Content.Server/_WF/Planets/Forecast/WFWeatherForecastConsoleSystem.cs`](Forecast/WFWeatherForecastConsoleSystem.cs)
- [`Content.Server/_WF/Planets/GameTicker.WFRoundHold.cs`](GameTicker.WFRoundHold.cs)
- [`Content.Server/_WF/Planets/Hop/WFPlanetHopComponent.cs`](Hop/WFPlanetHopComponent.cs)
- [`Content.Server/_WF/Planets/Hop/WFPlanetHopEvents.cs`](Hop/WFPlanetHopEvents.cs)
- [`Content.Server/_WF/Planets/Hop/WFPlanetHopSystem.cs`](Hop/WFPlanetHopSystem.cs)
- [`Content.Server/_WF/Planets/Hop/WFPlanetHopSystem.Effects.cs`](Hop/WFPlanetHopSystem.Effects.cs)
- [`Content.Server/_WF/Planets/Hop/WFPlanetHopSystem.Spot.cs`](Hop/WFPlanetHopSystem.Spot.cs)
- [`Content.Server/_WF/Planets/Hop/WFPlanetHopSystem.Transit.cs`](Hop/WFPlanetHopSystem.Transit.cs)
- [`Content.Server/_WF/Planets/Infestation/WFCarcinomaInfestationComponent.cs`](Infestation/WFCarcinomaInfestationComponent.cs)
- [`Content.Server/_WF/Planets/Infestation/WFCarcinomaInfestationSystem.cs`](Infestation/WFCarcinomaInfestationSystem.cs)
- [`Content.Server/_WF/Planets/Jetpack/WFAtmosphericJetpackFuelSystem.cs`](Jetpack/WFAtmosphericJetpackFuelSystem.cs)
- [`Content.Server/_WF/Planets/Natives/WFPlanetNativesComponent.cs`](Natives/WFPlanetNativesComponent.cs)
- [`Content.Server/_WF/Planets/Natives/WFPlanetNativesSystem.cs`](Natives/WFPlanetNativesSystem.cs)
- [`Content.Server/_WF/Planets/PathfindingSystem.WFPlanetLazy.cs`](PathfindingSystem.WFPlanetLazy.cs)
- [`Content.Server/_WF/Planets/PathfindingSystem.WFPlanetPathWake.cs`](PathfindingSystem.WFPlanetPathWake.cs)
- [`Content.Server/_WF/Planets/ShuttleSystem.WFOrbit.cs`](ShuttleSystem.WFOrbit.cs)
- [`Content.Server/_WF/Planets/StarSystemMapSystem.Wolfgate.cs`](StarSystemMapSystem.Wolfgate.cs)
- [`Content.Server/_WF/Planets/WFBiomeGrownComponent.cs`](WFBiomeGrownComponent.cs)
- [`Content.Server/_WF/Planets/WFGravityWellSystem.cs`](WFGravityWellSystem.cs)
- [`Content.Server/_WF/Planets/WFGridAudienceSystem.cs`](WFGridAudienceSystem.cs)
- [`Content.Server/_WF/Planets/WFLooseDebrisComponent.cs`](WFLooseDebrisComponent.cs)
- [`Content.Server/_WF/Planets/WFLooseDebrisSystem.cs`](WFLooseDebrisSystem.cs)
- [`Content.Server/_WF/Planets/WFOrbitDecayComponent.cs`](WFOrbitDecayComponent.cs)
- [`Content.Server/_WF/Planets/WFOrbitDecaySystem.cs`](WFOrbitDecaySystem.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.Atmosphere.cs`](WFOrbitEntrySystem.Atmosphere.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.cs`](WFOrbitEntrySystem.cs)
- [`Content.Server/_WF/Planets/WFOrbitEntrySystem.Liftoff.cs`](WFOrbitEntrySystem.Liftoff.cs)
- [`Content.Server/_WF/Planets/WFPlanetBiomassSystem.cs`](WFPlanetBiomassSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetClimateSystem.cs`](WFPlanetClimateSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetClimateSystem.History.cs`](WFPlanetClimateSystem.History.cs)
- [`Content.Server/_WF/Planets/WFPlanetFaunaSpawnerComponent.cs`](WFPlanetFaunaSpawnerComponent.cs)
- [`Content.Server/_WF/Planets/WFPlanetFaunaSystem.cs`](WFPlanetFaunaSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetGroundSpawnedEvent.cs`](WFPlanetGroundSpawnedEvent.cs)
- [`Content.Server/_WF/Planets/WFPlanetLayerNameSystem.cs`](WFPlanetLayerNameSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetLowerLayersEvent.cs`](WFPlanetLowerLayersEvent.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkBuiltEvent.cs`](WFPlanetNetworkBuiltEvent.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkComponent.cs`](WFPlanetNetworkComponent.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkSystem.cs`](WFPlanetNetworkSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetNetworkSystem.Prebuild.cs`](WFPlanetNetworkSystem.Prebuild.cs)
- [`Content.Server/_WF/Planets/WFPlanetRegistrySystem.cs`](WFPlanetRegistrySystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetRegistrySystem.Lobby.cs`](WFPlanetRegistrySystem.Lobby.cs)
- [`Content.Server/_WF/Planets/WFPlanetRoundHoldSystem.cs`](WFPlanetRoundHoldSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetTileBreakSystem.cs`](WFPlanetTileBreakSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetTimepieceSystem.cs`](WFPlanetTimepieceSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherComponent.cs`](WFPlanetWeatherComponent.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Admin.cs`](WFPlanetWeatherSystem.Admin.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.cs`](WFPlanetWeatherSystem.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Daylight.cs`](WFPlanetWeatherSystem.Daylight.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Seasons.cs`](WFPlanetWeatherSystem.Seasons.cs)
- [`Content.Server/_WF/Planets/WFPlanetWeatherSystem.Storms.cs`](WFPlanetWeatherSystem.Storms.cs)
- [`Content.Server/_WF/Planets/WFPlanetWildlifeComponent.cs`](WFPlanetWildlifeComponent.cs)
- [`Content.Server/_WF/Planets/WFRoundStartHoldEvent.cs`](WFRoundStartHoldEvent.cs)
- [`Content.Server/_WF/Planets/WFWorldFormingSystem.cs`](WFWorldFormingSystem.cs)

### Shared

- [`Content.Shared/_WF/Planets/Administration/PlanetControlEvents.cs`](../../../Content.Shared/_WF/Planets/Administration/PlanetControlEvents.cs)
- [`Content.Shared/_WF/Planets/CESharedZLevelsSystem.Ghosts.cs`](../../../Content.Shared/_WF/Planets/CESharedZLevelsSystem.Ghosts.cs)
- [`Content.Shared/_WF/Planets/CESharedZLevelsSystem.Roofs.cs`](../../../Content.Shared/_WF/Planets/CESharedZLevelsSystem.Roofs.cs)
- [`Content.Shared/_WF/Planets/CESharedZLevelsSystem.Terrain.cs`](../../../Content.Shared/_WF/Planets/CESharedZLevelsSystem.Terrain.cs)
- [`Content.Shared/_WF/Planets/Flight/WFApcRepairDoAfterEvent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFApcRepairDoAfterEvent.cs)
- [`Content.Shared/_WF/Planets/Flight/WFEnterAtmosphereMessage.cs`](../../../Content.Shared/_WF/Planets/Flight/WFEnterAtmosphereMessage.cs)
- [`Content.Shared/_WF/Planets/Flight/WFLandingThrusterComponent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFLandingThrusterComponent.cs)
- [`Content.Shared/_WF/Planets/Flight/WFLiftLostComponent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFLiftLostComponent.cs)
- [`Content.Shared/_WF/Planets/Flight/WFLiftoffAttemptEvent.cs`](../../../Content.Shared/_WF/Planets/Flight/WFLiftoffAttemptEvent.cs)
- [`Content.Shared/_WF/Planets/Forecast/WFWeatherForecast.cs`](../../../Content.Shared/_WF/Planets/Forecast/WFWeatherForecast.cs)
- [`Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackComponent.cs`](../../../Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackComponent.cs)
- [`Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackSystem.cs`](../../../Content.Shared/_WF/Planets/Jetpack/WFAtmosphericJetpackSystem.cs)
- [`Content.Shared/_WF/Planets/Parachute/WFParachuteComponents.cs`](../../../Content.Shared/_WF/Planets/Parachute/WFParachuteComponents.cs)
- [`Content.Shared/_WF/Planets/Parachute/WFParachuteSystem.cs`](../../../Content.Shared/_WF/Planets/Parachute/WFParachuteSystem.cs)
- [`Content.Shared/_WF/Planets/SharedBiomeSystem.Wolfgate.cs`](../../../Content.Shared/_WF/Planets/SharedBiomeSystem.Wolfgate.cs)
- [`Content.Shared/_WF/Planets/SharedShuttleSystem.Wolfgate.cs`](../../../Content.Shared/_WF/Planets/SharedShuttleSystem.Wolfgate.cs)
- [`Content.Shared/_WF/Planets/SharedWeatherSystem.Planets.cs`](../../../Content.Shared/_WF/Planets/SharedWeatherSystem.Planets.cs)
- [`Content.Shared/_WF/Planets/WFBiomeKeepComponent.cs`](../../../Content.Shared/_WF/Planets/WFBiomeKeepComponent.cs)
- [`Content.Shared/_WF/Planets/WFBiomeNoiseCacheSystem.cs`](../../../Content.Shared/_WF/Planets/WFBiomeNoiseCacheSystem.cs)
- [`Content.Shared/_WF/Planets/WFConsoleOrbitTargetComponent.cs`](../../../Content.Shared/_WF/Planets/WFConsoleOrbitTargetComponent.cs)
- [`Content.Shared/_WF/Planets/WFDetachedTerrainComponent.cs`](../../../Content.Shared/_WF/Planets/WFDetachedTerrainComponent.cs)
- [`Content.Shared/_WF/Planets/WFMapLabelSystem.cs`](../../../Content.Shared/_WF/Planets/WFMapLabelSystem.cs)
- [`Content.Shared/_WF/Planets/WFOrbitLayerComponent.cs`](../../../Content.Shared/_WF/Planets/WFOrbitLayerComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetAmbiencePrototype.cs`](../../../Content.Shared/_WF/Planets/WFPlanetAmbiencePrototype.cs)
- [`Content.Shared/_WF/Planets/WFPlanetApproachComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetApproachComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetBoundsComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetBoundsComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetBuiltTilesComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetBuiltTilesComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetEnvironmentComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetEnvironmentComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetHopMapComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetHopMapComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetLayerComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetLayerComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetOrbitMessages.cs`](../../../Content.Shared/_WF/Planets/WFPlanetOrbitMessages.cs)
- [`Content.Shared/_WF/Planets/WFPlanetRadarSystem.cs`](../../../Content.Shared/_WF/Planets/WFPlanetRadarSystem.cs)
- [`Content.Shared/_WF/Planets/WFPlanetSurfacePrototype.cs`](../../../Content.Shared/_WF/Planets/WFPlanetSurfacePrototype.cs)
- [`Content.Shared/_WF/Planets/WFPlanetTimepieceComponent.cs`](../../../Content.Shared/_WF/Planets/WFPlanetTimepieceComponent.cs)
- [`Content.Shared/_WF/Planets/WFPlanetView.cs`](../../../Content.Shared/_WF/Planets/WFPlanetView.cs)
- [`Content.Shared/_WF/Planets/WFPlanetWeatherPrototype.cs`](../../../Content.Shared/_WF/Planets/WFPlanetWeatherPrototype.cs)
- [`Content.Shared/_WF/Planets/WFSectorPlanetComponent.cs`](../../../Content.Shared/_WF/Planets/WFSectorPlanetComponent.cs)
- [`Content.Shared/_WF/Planets/WFWorldFormingEvents.cs`](../../../Content.Shared/_WF/Planets/WFWorldFormingEvents.cs)

### Client

- [`Content.Client/_WF/Planets/Administration/PlanetControlSystem.cs`](../../../Content.Client/_WF/Planets/Administration/PlanetControlSystem.cs)
- [`Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml`](../../../Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml)
- [`Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml.cs`](../../../Content.Client/_WF/Planets/Administration/PlanetControlWindow.xaml.cs)
- [`Content.Client/_WF/Planets/ContentAudioSystem.Planets.cs`](../../../Content.Client/_WF/Planets/ContentAudioSystem.Planets.cs)
- [`Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml`](../../../Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml)
- [`Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml.cs`](../../../Content.Client/_WF/Planets/Flight/WFEnterAtmosphereConfirmWindow.xaml.cs)
- [`Content.Client/_WF/Planets/Forecast/WFWeatherForecastBoundUserInterface.cs`](../../../Content.Client/_WF/Planets/Forecast/WFWeatherForecastBoundUserInterface.cs)
- [`Content.Client/_WF/Planets/Forecast/WFWeatherForecastWindow.cs`](../../../Content.Client/_WF/Planets/Forecast/WFWeatherForecastWindow.cs)
- [`Content.Client/_WF/Planets/Parachute/WFParachuteVisualsSystem.cs`](../../../Content.Client/_WF/Planets/Parachute/WFParachuteVisualsSystem.cs)
- [`Content.Client/_WF/Planets/ScalingViewport.Planets.cs`](../../../Content.Client/_WF/Planets/ScalingViewport.Planets.cs)
- [`Content.Client/_WF/Planets/ShuttleMapControl.PlanetBackground.cs`](../../../Content.Client/_WF/Planets/ShuttleMapControl.PlanetBackground.cs)
- [`Content.Client/_WF/Planets/ShuttleNavControl.Terrain.cs`](../../../Content.Client/_WF/Planets/ShuttleNavControl.Terrain.cs)
- [`Content.Client/_WF/Planets/StencilOverlay.Planets.cs`](../../../Content.Client/_WF/Planets/StencilOverlay.Planets.cs)
- [`Content.Client/_WF/Planets/WFHudPlacement.cs`](../../../Content.Client/_WF/Planets/WFHudPlacement.cs)
- [`Content.Client/_WF/Planets/WFOrbitButton.cs`](../../../Content.Client/_WF/Planets/WFOrbitButton.cs)
- [`Content.Client/_WF/Planets/WFPlanetAmbienceSystem.cs`](../../../Content.Client/_WF/Planets/WFPlanetAmbienceSystem.cs)
- [`Content.Client/_WF/Planets/WFPlanetApproachOverlay.cs`](../../../Content.Client/_WF/Planets/WFPlanetApproachOverlay.cs)
- [`Content.Client/_WF/Planets/WFPlanetApproachSystem.cs`](../../../Content.Client/_WF/Planets/WFPlanetApproachSystem.cs)
- [`Content.Client/_WF/Planets/WFPlanetTimepieceHud.cs`](../../../Content.Client/_WF/Planets/WFPlanetTimepieceHud.cs)
- [`Content.Client/_WF/Planets/WFPlanetTimepieceHudSystem.cs`](../../../Content.Client/_WF/Planets/WFPlanetTimepieceHudSystem.cs)
- [`Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml`](../../../Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml)
- [`Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml.cs`](../../../Content.Client/_WF/Planets/WFUnsanctionedOrbitConfirmWindow.xaml.cs)
- [`Content.Client/_WF/Planets/WFWeatherAudioSystem.cs`](../../../Content.Client/_WF/Planets/WFWeatherAudioSystem.cs)
- [`Content.Client/_WF/Planets/WFWorldFormingBar.cs`](../../../Content.Client/_WF/Planets/WFWorldFormingBar.cs)
- [`Content.Client/_WF/Planets/WFWorldFormingClientSystem.cs`](../../../Content.Client/_WF/Planets/WFWorldFormingClientSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Planets/AirLayerAtmosphereTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AirLayerAtmosphereTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/AtmosphereThrusterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AtmosphereThrusterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/AtmosphericJetpackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AtmosphericJetpackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/AudioExhaustionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/AudioExhaustionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/BiomeNoiseCacheTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/BiomeNoiseCacheTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/AerumnaBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/AerumnaBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/AerumnaLifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/AerumnaLifeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/CarcinomaBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/CarcinomaBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/CarcinomaLifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/CarcinomaLifeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/CarcinomaPlantsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/CarcinomaPlantsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/FervidusBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/FervidusBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/FervidusLifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/FervidusLifeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/MerakBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/MerakBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/MerakLifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/MerakLifeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/ThrasciasBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/ThrasciasBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/ThrasciasLifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/ThrasciasLifeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/VerdaBiomeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/VerdaBiomeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/Biomes/VerdaLifeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/Biomes/VerdaLifeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CarcinomaInfestationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CarcinomaInfestationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CarcinomaTendrilAttackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CarcinomaTendrilAttackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashApcFaultTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashApcFaultTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashAudioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashAudioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashBreakupFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashBreakupFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashThrustTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashThrustTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/CrashVehicleFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/CrashVehicleFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightAudioLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightAudioLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightReadinessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightReadinessTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightSafetyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightSafetyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/FlightTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/FlightTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/GroundContactTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/GroundContactTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/KyphrusPlanetsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/KyphrusPlanetsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/LandingCrushTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/LandingCrushTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/LandingHoldTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/LandingHoldTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/LiftoffTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/LiftoffTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/MerakHeatTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/MerakHeatTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitalMobFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitalMobFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitArrivalTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitArrivalTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitBeaconPinTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitBeaconPinTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitDecayTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitDecayTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitEntryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitEntryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/OrbitEntryTest.Hop.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/OrbitEntryTest.Hop.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/ParachuteTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/ParachuteTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePlaybackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePlaybackTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePrototypeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetAmbiencePrototypeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetArrivalTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetArrivalTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetBoundsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetBoundsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetDragTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetDragTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetDroneBeltTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetDroneBeltTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyLifecycleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyLifecycleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetEcologyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetEntityCensusTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetEntityCensusTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetExplosionGroundTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetExplosionGroundTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetFirstMinuteTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetFirstMinuteTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetFixture.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetFixture.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetLakeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetLakeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetLayerNamesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetLayerNamesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetNetworkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetNetworkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetOreMarkerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetOreMarkerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetPathWakeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetPathWakeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetPopulationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetPopulationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetPrebuildTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetPrebuildTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetPrototypeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetPrototypeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarDrawingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarDrawingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetRadarTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetRoofFallTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetRoofFallTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetRoundHoldTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetRoundHoldTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetSeasonsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetSeasonsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetStashTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetStashTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetTileBreakTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetTileBreakTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetTimepieceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetTimepieceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetViewTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetViewTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetWeatherRoofTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetWeatherRoofTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PlanetWeatherTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PlanetWeatherTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/PullPointerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/PullPointerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/SectorBodyPinTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/SectorBodyPinTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/SetDownTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/SetDownTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/StructuralCrashTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/StructuralCrashTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainArrivalLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainArrivalLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainAtmosphereTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainAtmosphereTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/TerrainUnloadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/TerrainUnloadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/ThrustAmbienceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/ThrustAmbienceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/WeatherAudioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/WeatherAudioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/WorldFormingProgressTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/WorldFormingProgressTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Planets/ZLevelJumpTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Planets/ZLevelJumpTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Planets/biomes.yml`](../../../Resources/Prototypes/_WF/Planets/biomes.yml)
- [`Resources/Prototypes/_WF/Planets/Biomes/aerumna.yml`](../../../Resources/Prototypes/_WF/Planets/Biomes/aerumna.yml)
- [`Resources/Prototypes/_WF/Planets/Biomes/carcinoma.yml`](../../../Resources/Prototypes/_WF/Planets/Biomes/carcinoma.yml)
- [`Resources/Prototypes/_WF/Planets/Biomes/fervidus.yml`](../../../Resources/Prototypes/_WF/Planets/Biomes/fervidus.yml)
- [`Resources/Prototypes/_WF/Planets/Biomes/merak.yml`](../../../Resources/Prototypes/_WF/Planets/Biomes/merak.yml)
- [`Resources/Prototypes/_WF/Planets/Biomes/thrascias.yml`](../../../Resources/Prototypes/_WF/Planets/Biomes/thrascias.yml)
- [`Resources/Prototypes/_WF/Planets/Biomes/verda.yml`](../../../Resources/Prototypes/_WF/Planets/Biomes/verda.yml)
- [`Resources/Prototypes/_WF/Planets/bounds.yml`](../../../Resources/Prototypes/_WF/Planets/bounds.yml)
- [`Resources/Prototypes/_WF/Planets/carcinoma.yml`](../../../Resources/Prototypes/_WF/Planets/carcinoma.yml)
- [`Resources/Prototypes/_WF/Planets/carcinoma_regions.yml`](../../../Resources/Prototypes/_WF/Planets/carcinoma_regions.yml)
- [`Resources/Prototypes/_WF/Planets/crash_effects.yml`](../../../Resources/Prototypes/_WF/Planets/crash_effects.yml)
- [`Resources/Prototypes/_WF/Planets/entities.yml`](../../../Resources/Prototypes/_WF/Planets/entities.yml)
- [`Resources/Prototypes/_WF/Planets/fauna.yml`](../../../Resources/Prototypes/_WF/Planets/fauna.yml)
- [`Resources/Prototypes/_WF/Planets/flight.yml`](../../../Resources/Prototypes/_WF/Planets/flight.yml)
- [`Resources/Prototypes/_WF/Planets/flight_alerts.yml`](../../../Resources/Prototypes/_WF/Planets/flight_alerts.yml)
- [`Resources/Prototypes/_WF/Planets/forecast_console.yml`](../../../Resources/Prototypes/_WF/Planets/forecast_console.yml)
- [`Resources/Prototypes/_WF/Planets/guidebook.yml`](../../../Resources/Prototypes/_WF/Planets/guidebook.yml)
- [`Resources/Prototypes/_WF/Planets/infestation.yml`](../../../Resources/Prototypes/_WF/Planets/infestation.yml)
- [`Resources/Prototypes/_WF/Planets/jetpack.yml`](../../../Resources/Prototypes/_WF/Planets/jetpack.yml)
- [`Resources/Prototypes/_WF/Planets/kyphrus_planets.yml`](../../../Resources/Prototypes/_WF/Planets/kyphrus_planets.yml)
- [`Resources/Prototypes/_WF/Planets/lathe_recipes.yml`](../../../Resources/Prototypes/_WF/Planets/lathe_recipes.yml)
- [`Resources/Prototypes/_WF/Planets/Life/aerumna.yml`](../../../Resources/Prototypes/_WF/Planets/Life/aerumna.yml)
- [`Resources/Prototypes/_WF/Planets/Life/carcinoma.yml`](../../../Resources/Prototypes/_WF/Planets/Life/carcinoma.yml)
- [`Resources/Prototypes/_WF/Planets/Life/fervidus.yml`](../../../Resources/Prototypes/_WF/Planets/Life/fervidus.yml)
- [`Resources/Prototypes/_WF/Planets/Life/merak.yml`](../../../Resources/Prototypes/_WF/Planets/Life/merak.yml)
- [`Resources/Prototypes/_WF/Planets/Life/thrascias.yml`](../../../Resources/Prototypes/_WF/Planets/Life/thrascias.yml)
- [`Resources/Prototypes/_WF/Planets/Life/verda.yml`](../../../Resources/Prototypes/_WF/Planets/Life/verda.yml)
- [`Resources/Prototypes/_WF/Planets/Life/verda_mobs.yml`](../../../Resources/Prototypes/_WF/Planets/Life/verda_mobs.yml)
- [`Resources/Prototypes/_WF/Planets/open_biomes.yml`](../../../Resources/Prototypes/_WF/Planets/open_biomes.yml)
- [`Resources/Prototypes/_WF/Planets/parachute.yml`](../../../Resources/Prototypes/_WF/Planets/parachute.yml)
- [`Resources/Prototypes/_WF/Planets/planet_ambience.yml`](../../../Resources/Prototypes/_WF/Planets/planet_ambience.yml)
- [`Resources/Prototypes/_WF/Planets/planet_surfaces.yml`](../../../Resources/Prototypes/_WF/Planets/planet_surfaces.yml)
- [`Resources/Prototypes/_WF/Planets/planet_weather.yml`](../../../Resources/Prototypes/_WF/Planets/planet_weather.yml)
- [`Resources/Prototypes/_WF/Planets/planets.yml`](../../../Resources/Prototypes/_WF/Planets/planets.yml)
- [`Resources/Prototypes/_WF/Planets/timepiece.yml`](../../../Resources/Prototypes/_WF/Planets/timepiece.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Planets/aerumna_life.ftl`](../../../Resources/Locale/en-US/_WF/Planets/aerumna_life.ftl)
- [`Resources/Locale/en-US/_WF/Planets/carcinoma.ftl`](../../../Resources/Locale/en-US/_WF/Planets/carcinoma.ftl)
- [`Resources/Locale/en-US/_WF/Planets/carcinoma_life.ftl`](../../../Resources/Locale/en-US/_WF/Planets/carcinoma_life.ftl)
- [`Resources/Locale/en-US/_WF/Planets/climate.ftl`](../../../Resources/Locale/en-US/_WF/Planets/climate.ftl)
- [`Resources/Locale/en-US/_WF/Planets/flight.ftl`](../../../Resources/Locale/en-US/_WF/Planets/flight.ftl)
- [`Resources/Locale/en-US/_WF/Planets/guidebook.ftl`](../../../Resources/Locale/en-US/_WF/Planets/guidebook.ftl)
- [`Resources/Locale/en-US/_WF/Planets/infestation.ftl`](../../../Resources/Locale/en-US/_WF/Planets/infestation.ftl)
- [`Resources/Locale/en-US/_WF/Planets/jetpack.ftl`](../../../Resources/Locale/en-US/_WF/Planets/jetpack.ftl)
- [`Resources/Locale/en-US/_WF/Planets/kyphrus.ftl`](../../../Resources/Locale/en-US/_WF/Planets/kyphrus.ftl)
- [`Resources/Locale/en-US/_WF/Planets/life_fervidus.ftl`](../../../Resources/Locale/en-US/_WF/Planets/life_fervidus.ftl)
- [`Resources/Locale/en-US/_WF/Planets/life_merak.ftl`](../../../Resources/Locale/en-US/_WF/Planets/life_merak.ftl)
- [`Resources/Locale/en-US/_WF/Planets/life_thrascias.ftl`](../../../Resources/Locale/en-US/_WF/Planets/life_thrascias.ftl)
- [`Resources/Locale/en-US/_WF/Planets/life_verda.ftl`](../../../Resources/Locale/en-US/_WF/Planets/life_verda.ftl)
- [`Resources/Locale/en-US/_WF/Planets/liftoff.ftl`](../../../Resources/Locale/en-US/_WF/Planets/liftoff.ftl)
- [`Resources/Locale/en-US/_WF/Planets/parachute.ftl`](../../../Resources/Locale/en-US/_WF/Planets/parachute.ftl)
- [`Resources/Locale/en-US/_WF/Planets/planets.ftl`](../../../Resources/Locale/en-US/_WF/Planets/planets.ftl)
- [`Resources/Locale/en-US/_WF/Planets/timepiece.ftl`](../../../Resources/Locale/en-US/_WF/Planets/timepiece.ftl)
- [`Resources/Locale/en-US/_WF/Planets/weather.ftl`](../../../Resources/Locale/en-US/_WF/Planets/weather.ftl)
- [`Resources/Locale/en-US/_WF/Planets/world_forming.ftl`](../../../Resources/Locale/en-US/_WF/Planets/world_forming.ftl)

### Textures

- [`Resources/Textures/_WF/Planets/BloodWater/blood-lake.rsi/`](../../../Resources/Textures/_WF/Planets/BloodWater/blood-lake.rsi/)
- [`Resources/Textures/_WF/Planets/BloodWater/blood-river.rsi/`](../../../Resources/Textures/_WF/Planets/BloodWater/blood-river.rsi/)
- [`Resources/Textures/_WF/Planets/BoneField/attributions.yml`](../../../Resources/Textures/_WF/Planets/BoneField/attributions.yml)
- [`Resources/Textures/_WF/Planets/BoneField/bleached_bone.png`](../../../Resources/Textures/_WF/Planets/BoneField/bleached_bone.png)
- [`Resources/Textures/_WF/Planets/BoneField/bones.rsi/`](../../../Resources/Textures/_WF/Planets/BoneField/bones.rsi/)
- [`Resources/Textures/_WF/Planets/BoneField/bones_64x64.rsi/`](../../../Resources/Textures/_WF/Planets/BoneField/bones_64x64.rsi/)
- [`Resources/Textures/_WF/Planets/BoneField/calcified_flesh.png`](../../../Resources/Textures/_WF/Planets/BoneField/calcified_flesh.png)
- [`Resources/Textures/_WF/Planets/BoneField/cartilage.png`](../../../Resources/Textures/_WF/Planets/BoneField/cartilage.png)
- [`Resources/Textures/_WF/Planets/BoneField/clotted_blood.png`](../../../Resources/Textures/_WF/Planets/BoneField/clotted_blood.png)
- [`Resources/Textures/_WF/Planets/BoneField/dried_crust.png`](../../../Resources/Textures/_WF/Planets/BoneField/dried_crust.png)
- [`Resources/Textures/_WF/Planets/BoneField/lumps.rsi/`](../../../Resources/Textures/_WF/Planets/BoneField/lumps.rsi/)
- [`Resources/Textures/_WF/Planets/BoneField/marsh_mud.png`](../../../Resources/Textures/_WF/Planets/BoneField/marsh_mud.png)
- [`Resources/Textures/_WF/Planets/BoneField/marsh_scab.png`](../../../Resources/Textures/_WF/Planets/BoneField/marsh_scab.png)
- [`Resources/Textures/_WF/Planets/BoneField/scab_shore.png`](../../../Resources/Textures/_WF/Planets/BoneField/scab_shore.png)
- [`Resources/Textures/_WF/Planets/Effects/parachute_canopy.rsi/`](../../../Resources/Textures/_WF/Planets/Effects/parachute_canopy.rsi/)
- [`Resources/Textures/_WF/Planets/Effects/thunderbolt.rsi/`](../../../Resources/Textures/_WF/Planets/Effects/thunderbolt.rsi/)
- [`Resources/Textures/_WF/Planets/Effects/weather_tg.rsi/`](../../../Resources/Textures/_WF/Planets/Effects/weather_tg.rsi/)
- [`Resources/Textures/_WF/Planets/Life/merak/tarantula.rsi/`](../../../Resources/Textures/_WF/Planets/Life/merak/tarantula.rsi/)
- [`Resources/Textures/_WF/Planets/Life/Thrascias/crystal_crab.rsi/`](../../../Resources/Textures/_WF/Planets/Life/Thrascias/crystal_crab.rsi/)
- [`Resources/Textures/_WF/Planets/Life/Thrascias/frost_bear.rsi/`](../../../Resources/Textures/_WF/Planets/Life/Thrascias/frost_bear.rsi/)
- [`Resources/Textures/_WF/Planets/Life/Thrascias/snow_fox.rsi/`](../../../Resources/Textures/_WF/Planets/Life/Thrascias/snow_fox.rsi/)
- [`Resources/Textures/_WF/Planets/Life/Thrascias/snow_spider.rsi/`](../../../Resources/Textures/_WF/Planets/Life/Thrascias/snow_spider.rsi/)
- [`Resources/Textures/_WF/Planets/Life/Thrascias/yeti.rsi/`](../../../Resources/Textures/_WF/Planets/Life/Thrascias/yeti.rsi/)
- [`Resources/Textures/_WF/Planets/Life/verda/frost_bear.rsi/`](../../../Resources/Textures/_WF/Planets/Life/verda/frost_bear.rsi/)
- [`Resources/Textures/_WF/Planets/Life/verda/mutant_deer_buck.rsi/`](../../../Resources/Textures/_WF/Planets/Life/verda/mutant_deer_buck.rsi/)
- [`Resources/Textures/_WF/Planets/Objects/jetpack_atmospheric.rsi/`](../../../Resources/Textures/_WF/Planets/Objects/jetpack_atmospheric.rsi/)
- [`Resources/Textures/_WF/Planets/Objects/parachute.rsi/`](../../../Resources/Textures/_WF/Planets/Objects/parachute.rsi/)
- [`Resources/Textures/_WF/Planets/Structures/flesh_trees.rsi/`](../../../Resources/Textures/_WF/Planets/Structures/flesh_trees.rsi/)

### Audio

- [`Resources/Audio/_WF/Planets/Flight/atmo_wind.ogg`](../../../Resources/Audio/_WF/Planets/Flight/atmo_wind.ogg)
- [`Resources/Audio/_WF/Planets/Flight/attributions.yml`](../../../Resources/Audio/_WF/Planets/Flight/attributions.yml)
- [`Resources/Audio/_WF/Planets/Flight/crash10.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash10.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash11.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash11.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash12.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash12.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash3.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash3.ogg)
- [`Resources/Audio/_WF/Planets/Flight/crash4.ogg`](../../../Resources/Audio/_WF/Planets/Flight/crash4.ogg)
- [`Resources/Audio/_WF/Planets/Flight/dont_sink.ogg`](../../../Resources/Audio/_WF/Planets/Flight/dont_sink.ogg)
- [`Resources/Audio/_WF/Planets/Flight/fall_rumble.ogg`](../../../Resources/Audio/_WF/Planets/Flight/fall_rumble.ogg)
- [`Resources/Audio/_WF/Planets/Flight/ground_grind_loop.ogg`](../../../Resources/Audio/_WF/Planets/Flight/ground_grind_loop.ogg)
- [`Resources/Audio/_WF/Planets/Flight/hard_landing.ogg`](../../../Resources/Audio/_WF/Planets/Flight/hard_landing.ogg)
- [`Resources/Audio/_WF/Planets/Flight/lift_lost.ogg`](../../../Resources/Audio/_WF/Planets/Flight/lift_lost.ogg)
- [`Resources/Audio/_WF/Planets/Flight/pull_up.ogg`](../../../Resources/Audio/_WF/Planets/Flight/pull_up.ogg)
- [`Resources/Audio/_WF/Planets/Flight/sink_rate.ogg`](../../../Resources/Audio/_WF/Planets/Flight/sink_rate.ogg)
- [`Resources/Audio/_WF/Planets/Flight/skid.ogg`](../../../Resources/Audio/_WF/Planets/Flight/skid.ogg)
- [`Resources/Audio/_WF/Planets/Flight/takeoff.ogg`](../../../Resources/Audio/_WF/Planets/Flight/takeoff.ogg)
- [`Resources/Audio/_WF/Planets/Flight/terrain.ogg`](../../../Resources/Audio/_WF/Planets/Flight/terrain.ogg)
- [`Resources/Audio/_WF/Planets/Flight/thrust_loop.ogg`](../../../Resources/Audio/_WF/Planets/Flight/thrust_loop.ogg)
- [`Resources/Audio/_WF/Planets/Flight/too_low_terrain.ogg`](../../../Resources/Audio/_WF/Planets/Flight/too_low_terrain.ogg)
- [`Resources/Audio/_WF/Planets/Weather/attributions.yml`](../../../Resources/Audio/_WF/Planets/Weather/attributions.yml)
- [`Resources/Audio/_WF/Planets/Weather/rain_end.ogg`](../../../Resources/Audio/_WF/Planets/Weather/rain_end.ogg)
- [`Resources/Audio/_WF/Planets/Weather/rain_mid.ogg`](../../../Resources/Audio/_WF/Planets/Weather/rain_mid.ogg)
- [`Resources/Audio/_WF/Planets/Weather/rain_start.ogg`](../../../Resources/Audio/_WF/Planets/Weather/rain_start.ogg)
- [`Resources/Audio/_WF/Planets/Weather/thunder_1.ogg`](../../../Resources/Audio/_WF/Planets/Weather/thunder_1.ogg)
- [`Resources/Audio/_WF/Planets/Weather/thunder_2.ogg`](../../../Resources/Audio/_WF/Planets/Weather/thunder_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_4.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_ambience_loop_4.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_4.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/aerumna_random_sound_4.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/aerumna/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/aerumna/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_day_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_day_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_night_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/asclepiu/asclepiu_ambience_night_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/asclepiu/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/asclepiu/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/anchoring_tendril_loop.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/anchoring_tendril_loop.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_day_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_day_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_ambience_night_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/carcinoma_random_sound_night_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/carcinoma/tendril_deploy_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_2.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_3.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_3.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_4.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/fervidus/fervidus_ambience_loop_4.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/merak/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/merak/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_day_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_day_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_night_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/merak/merak_ambience_night_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/thrascias/attributions.yml`](../../../Resources/Audio/_WF/Planets/Worlds/thrascias/attributions.yml)
- [`Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_1.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_1.ogg)
- [`Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_2.ogg`](../../../Resources/Audio/_WF/Planets/Worlds/thrascias/thracias_ambience_loop_2.ogg)

### Guidebook

- [`Resources/ServerInfo/_WF/Planets/Guidebook/PlanetFlight.xml`](../../../Resources/ServerInfo/_WF/Planets/Guidebook/PlanetFlight.xml)

### Tools

- [`Tools/_WF/Planets/gen_blood_water.py`](../../../Tools/_WF/Planets/gen_blood_water.py)
- [`Tools/_WF/Planets/gen_carcinoma_scab.py`](../../../Tools/_WF/Planets/gen_carcinoma_scab.py)
- [`Tools/_WF/Planets/gen_flight_placeholders.py`](../../../Tools/_WF/Planets/gen_flight_placeholders.py)
- [`Tools/_WF/Planets/gen_verda_life_art.py`](../../../Tools/_WF/Planets/gen_verda_life_art.py)
- [`Tools/_WF/Planets/recolor_thrascias_life.py`](../../../Tools/_WF/Planets/recolor_thrascias_life.py)

### Docs

- [`Docs/_WF/Planets/PLANET_ECOLOGY.md`](../../../Docs/_WF/Planets/PLANET_ECOLOGY.md)
- [`Docs/_WF/Planets/PLAYTEST_CHECKLIST.md`](../../../Docs/_WF/Planets/PLAYTEST_CHECKLIST.md)
- [`Docs/_WF/Planets/SHIP_LANDING_ASSESSMENT.md`](../../../Docs/_WF/Planets/SHIP_LANDING_ASSESSMENT.md)
- [`Docs/_WF/Planets/ship_landing_measurements.json`](../../../Docs/_WF/Planets/ship_landing_measurements.json)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs`](../../../Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs): orbit is above the atmosphere, not a storey up.
- [`Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs`](../../../Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs)
  - an empty air layer under orbit is skipped, each pass being a whole render
  - a planet's layers keep the perspective Monolith turned off, aboard a ship
- [`Content.Client/_FarHorizons/StarSystem/PlanetOverlay.cs`](../../../Content.Client/_FarHorizons/StarSystem/PlanetOverlay.cs): public (was private), so the planet approach can draw the very same body over the orbit hop.
- [`Content.Client/_Mono/Audio/AudioEchoSystem.cs`](../../../Content.Client/_Mono/Audio/AudioEchoSystem.cs)
  - a ray from a non-finite position is not cast, since a NaN crashes the client.
  - never hand MathF.Sign a NaN.
- [`Content.Client/Audio/ContentAudioSystem.AmbientMusic.cs`](../../../Content.Client/Audio/ContentAudioSystem.AmbientMusic.cs)
  - planet soundscapes replace ordinary ambient music.
  - no ordinary ambient music on a planet, whether a biome, grid, timer or combat-exit request asked.
- [`Content.Client/Light/RoofOverlay.cs`](../../../Content.Client/Light/RoofOverlay.cs)
  - the open-grating check below reads tile definitions.
  - open grating is no roof.
- [`Content.Client/Movement/Systems/FloorOcclusionSystem.cs`](../../../Content.Client/Movement/Systems/FloorOcclusionSystem.cs): a new entity's state lands before its sprite initialises; startup sets the shader.
- [`Content.Client/Overlays/StencilOverlay.Weather.cs`](../../../Content.Client/Overlays/StencilOverlay.Weather.cs): no rain down a stairwell onto an indoor floor
- [`Content.Client/Shuttles/UI/MapScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/MapScreen.xaml.cs): sector space, surfaces and levels say what they are
- [`Content.Client/Shuttles/UI/NavScreen.xaml`](../../../Content.Client/Shuttles/UI/NavScreen.xaml): enter/leave planet orbit, which needs no FTL drive (xmlns:wf above).
- [`Content.Client/Shuttles/UI/NavScreen.xaml.cs`](../../../Content.Client/Shuttles/UI/NavScreen.xaml.cs)
  - the orbit button reads its state off the console entity.
  - the flight panel's phase replaces the coarse status over a planet.
- [`Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs): over a world, the world fills the plot instead of space.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs)
  - retain the moving origin while panned.
  - a map-pinned radar must follow travel to another map.
  - terrain toggle overlays the radar, not the settings column.
  - a grid's map can change between BUI updates (FTL / planet layer travel).
  - cached planetary terrain beneath radar contacts.
- [`Content.Server/_CE/ZLevels/Core/CEZGroundFrictionController.cs`](../../_CE/ZLevels/Core/CEZGroundFrictionController.cs)
  - crash skids slide before regaining ordinary parked-hull grip.
  - detached crash sections can yaw while skidding, then regain normal parked grip.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs)
  - over a planet only landing thrusters lift.
  - landing thrusters are the planet-side lift.
  - grids parked on a planet orbit layer never fall.
  - sets level with each other met on an earlier tick.
  - partial landing-thruster lift slows the sink, and this is where lift lost begins.
  - the sink uses the lift-adjusted gravity.
  - only a held descend sets a hull down on a planet's ground.
  - no setting down on walls or rock.
  - a gentle set-down onto walls or rock holds just above them; a crash still comes down.
  - planetary landings can clear, land hard, break up or skid instead of only crashing.
  - leave clearance around planetary impact wrecks.
  - a lift-lost hull that touched down slowly enough lands hard and skids instead of exploding.
  - survivable ship breakup.
  - a crash with planar speed left ploughs on instead of stopping dead.
  - one crash, one bang.
  - a comma for the added silent argument.
  - only one blast of a crash plays its sound.
  - cargo virtual mass counts against pooled lift.
  - same virtual mass the lift check uses, so the readout agrees.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.GroundFriction.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.GroundFriction.cs): a crash skid scales down the landed body's tile friction instead of adding drag.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.PilotControl.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.PilotControl.cs)
  - the liftoff latch needs the pilot.
  - grounded ascend engages liftoff; airborne input keeps CE's normal control.
  - descend from orbit enters the atmosphere only when no confirm is needed.
  - a console latch feeds the same CE takeoff spool and flight integrator.
  - the climb is scaled by the maneuvering factor below.
  - only thrust left after hovering can climb.
  - planetary lift replaces, rather than supplements, the station gravgen gate.
  - pilots can't descend below a planet's ground.
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs)
  - a grid on a planet orbit layer holds its height instead of sinking.
  - hulls leave orbit through transit and never hop below ground.
  - riders' contacts with the old map must not survive the move.
  - hulls never descend below a planet's ground.
  - a held climb pops out into a planet's orbit layer instead of pinning under it.
  - a descending convoy lands on the ground instead of hopping below it.
  - a hull that sets down breaks the trees and rocks it would rest against
  - whatever else the hull came down wedged on goes too, however it got there
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.View.cs): a planet's lower layers are drawn smaller, so their eyes see wider
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.WallCollision.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.WallCollision.cs)
  - a skidding hull flattens obstacles instead of bouncing.
  - pinned between walls an axis has no way out; pushing by the difference swaps the overlaps and shakes the hull forever.
- [`Content.Server/_FarHorizons/StarSystem/StarSystemMapSystem.cs`](../../_FarHorizons/StarSystem/StarSystemMapSystem.cs)
  - keep the star off hulls that arrive on top of it.
  - register sector bodies that have a Wolfgate surface.
- [`Content.Server/_Mono/FireControl/FireControlSystem.cs`](../../_Mono/FireControl/FireControlSystem.cs): no firing mid-hop
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs`](../../Atmos/EntitySystems/AtmosphereSystem.API.cs): bare planet ground nobody built on isn't tracked
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Processing.cs)
  - bare planet ground keeps no map tile alive
  - bare planet ground shares the map's air, like a tile off the grid
  - a tile built on a planet starts with the air around it, not a vacuum
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Utils.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Utils.cs): bare planet ground is off the atmos grid but still carries walls
- [`Content.Server/Chemistry/TileReactions/CreateEntityTileReaction.cs`](../../Chemistry/TileReactions/CreateEntityTileReaction.cs): spilled chimera blood cannot seed an unbounded planetary hive.
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.cs`](../../Explosion/EntitySystems/ExplosionSystem.cs)
  - silent parameter, see QueuedExplosion.Silent.
  - one audible contributor is enough to make the merged blast audible.
  - carries the silent flag into the queued explosion.
  - a silent blast still carves its crater, it just does not shake, push or sound.
  - upstream sound, re-indented under the silent guard above.
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs`](../../Explosion/EntitySystems/ExplosionSystem.Processing.cs)
  - a blast leaves a planet's ground, not station plating, lattice or a hole under a player's floor.
  - silent flag, so a crash's storm of blasts makes one bang.
- [`Content.Server/GameTicking/GameTicker.RoundFlow.cs`](../../GameTicking/GameTicker.RoundFlow.cs): round-start worlds still forming hold the start
- [`Content.Server/Movement/Systems/JetpackSystem.cs`](../../Movement/Systems/JetpackSystem.cs)
  - an atmospheric pack burns welding fuel from a solution, not gas from a tank.
  - a wearer carried below orbit on a hull never changes parent, so the pack is cut here.
- [`Content.Server/Movement/Systems/PullController.cs`](../../Movement/Systems/PullController.cs): a pointer on another map has no distance to cap; capping it made a NaN position.
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.cs`](../../NPC/Pathfinding/PathfindingSystem.cs)
  - requests across unbuilt planet ground wait for it
  - held until the planet ground on its way is built
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.Grid.cs`](../../NPC/Pathfinding/PathfindingSystem.Grid.cs)
  - a planet layer builds only the dirty chunks near someone, a slice an update
  - what wasn't built this update stays dirty
- [`Content.Server/Parallax/BiomeSystem.ChunkLoader.cs`](../../Parallax/BiomeSystem.ChunkLoader.cs)
  - nobody standing in a chunk as it loads is walled in
  - a planet's rock spawner is rolled here from its tile, so the rock unloads and comes back the same
- [`Content.Server/Parallax/BiomeSystem.ConfigManager.cs`](../../Parallax/BiomeSystem.ConfigManager.cs): the loader's spawner rolls follow reloaded entity prototypes
- [`Content.Server/Parallax/BiomeSystem.cs`](../../Parallax/BiomeSystem.cs)
  - a chunk's own entities need no bookkeeping as its unload deletes them
  - planet layers unload on their own schedule, see BiomeSystem.WFUnload.cs
  - a planet layer is left to its own unloader
  - the far part of a planet layer's load area is put off
  - what was put off loads nearest first, within the pass's budget
- [`Content.Server/Parallax/BiomeSystem.PlayerTracker.cs`](../../Parallax/BiomeSystem.PlayerTracker.cs)
  - a planet layer loads nearest its loaders first
  - less loads at once round an eye than round a body
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs): planet lift and cargo load scale thrust
- [`Content.Server/Pinpointer/PinpointerSystem.cs`](../../Pinpointer/PinpointerSystem.cs)
  - a hop moves the pinpointer's map like a jump
  - pinpointers find their target again after a hop
- [`Content.Server/Shuttles/Systems/ShuttleSystem.FasterThanLight.cs`](../../Shuttles/Systems/ShuttleSystem.FasterThanLight.cs)
  - a hull mid-hop can't jump, nor can a ship docked to one
  - the docking branch never calls TrySetupFTL, so it asks the same gate.
  - a planet is left from orbit, never from the surface, the air or mid-transit.
  - a hop leaves NoFTL entities behind too
  - a hull coming down on a planet hurts a mob under it and shoves it clear, and gibs nobody
- [`Content.Server/Shuttles/Systems/ThrusterSystem.cs`](../../Shuttles/Systems/ThrusterSystem.cs)
  - show atmospheric rating and conversion status.
  - atmospheric overload recovery must survive power-change callbacks.
  - atmospheric efficiency and continuous power demand.
  - severed engines retain their last firing command while powered.
  - preserve upgraded rating across atmosphere transitions.
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Movement.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Movement.cs)
  - a roof one layer down is something to stand on, not to drop through.
  - planet terrain that isn't loaded is still ground
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Update.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Update.cs)
  - z-motion carried off the z-network is cleared, not kept for the next planet.
  - a ghost never sticks to stairs or stays between levels
  - a thrown body stays InAir until the throw lands, or friction eats a jump at once.
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs): retain the contacted obstacle for crash ploughing.
- [`Content.Shared/_CE/ZLevels/Throwing/CEZLevelThrowingSystem.cs`](../../../Content.Shared/_CE/ZLevels/Throwing/CEZLevelThrowingSystem.cs): a throw only has an arc where there are levels to arc through.
- [`Content.Shared/_Mono/ForceParent/ForceParentSystem.cs`](../../../Content.Shared/_Mono/ForceParent/ForceParentSystem.cs): the pin isn't networked, so a client applying a body's state must not re-parent it to nothing.
- [`Content.Shared/CCVar/CCVars.Game.cs`](../../../Content.Shared/CCVar/CCVars.Game.cs): defaults to MonoStandard (was nfpirate), whose round start spawns the star system.
- [`Content.Shared/Maps/TileSystem.cs`](../../../Content.Shared/Maps/TileSystem.cs): lattice laid on a planet's ground gives the ground back, not a hole through the world.
- [`Content.Shared/Movement/Systems/SharedJetpackSystem.cs`](../../../Content.Shared/Movement/Systems/SharedJetpackSystem.cs)
  - the condition continues on the next line.
  - a jetpack cuts out below a planet's orbit layer.
  - an atmosphere refusal is said plainly, rather than the gravity line, which is not why it failed.
  - jetpacks fly on a planet's orbit layer but not in its atmosphere.
  - no jetpack below a planet's orbit layer.
- [`Content.Shared/Movement/Systems/SharedMoverController.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.cs): every species can move in air for z-levels, so a thrown mob (a jump) must not, or ground friction stops it.
- [`Content.Shared/Movement/Systems/SharedMoverController.Input.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.Input.cs): a planet surface is a map that is also a grid, and its tiles are ground, not space.
- [`Content.Shared/Parallax/Biomes/SharedBiomeSystem.cs`](../../../Content.Shared/Parallax/Biomes/SharedBiomeSystem.cs)
  - reuse one copy per source noise and seed, since callers only read it
  - keep the copy for later calls
- [`Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs`](../../../Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs): you only FTL out of a planet network from orbit, and never FTL into an orbit layer.
- [`Content.Shared/Tiles/FloorTileSystem.cs`](../../../Content.Shared/Tiles/FloorTileSystem.cs)
  - reads the planet ground under a tile.
  - a planet's natural ground takes lattice directly, and remembers what is under it.
  - lattice also goes straight onto planet ground.
  - the ground under lattice is kept so cutting it gives the ground back.
  - only a planet's untouched biome ground takes lattice directly.
- [`Content.Shared/Weather/SharedWeatherSystem.cs`](../../../Content.Shared/Weather/SharedWeatherSystem.cs): on a planet, roofs decide rather than the tile, and a vessel is always under cover.
- [`Resources/ConfigPresets/Build/development.toml`](../../../Resources/ConfigPresets/Build/development.toml): planet networks are on in development builds.
- [`Resources/Locale/en-US/_Mono/guidebook/planets.ftl`](../../../Resources/Locale/en-US/_Mono/guidebook/planets.ftl): Asclepiu is called Verda
- [`Resources/Maps/_Mono/Outpost/caelestinus_central.yml`](../../../Resources/Maps/_Mono/Outpost/caelestinus_central.yml): A weather forecast computer beside the station radar console.
- [`Resources/Prototypes/_DV/Entities/Mobs/Species/harpy.yml`](../../../Resources/Prototypes/_DV/Entities/Mobs/Species/harpy.yml): z-level flight is back on, which Monolith#4812 turned off
- [`Resources/Prototypes/_FarHorizons/Space/planets.yml`](../../../Resources/Prototypes/_FarHorizons/Space/planets.yml): Asclepiu is called Verda
- [`Resources/Prototypes/_FarHorizons/Space/systems.yml`](../../../Resources/Prototypes/_FarHorizons/Space/systems.yml)
  - orbit well kept out of lock-on range of Monolith's drone belt.
  - orbit well moved out of Monolith's drone belt, a hazard world at its edge.
  - quarantined biothreat world, unsanctioned.
- [`Resources/Prototypes/_Mono/Entities/Mobs/Chimera/biomass.yml`](../../../Resources/Prototypes/_Mono/Entities/Mobs/Chimera/biomass.yml): prevent action-spawned biomass from colonizing planets, too.
- [`Resources/Prototypes/_NF/Guidebook/nf14.yml`](../../../Resources/Prototypes/_NF/Guidebook/nf14.yml): planet flight guide under piloting
- [`Resources/Prototypes/_NF/Loadouts/contractor_loadout_groups.yml`](../../../Resources/Prototypes/_NF/Loadouts/contractor_loadout_groups.yml): planetary clock and weather HUD.
- [`Resources/Prototypes/Entities/Mobs/Species/moth.yml`](../../../Resources/Prototypes/Entities/Mobs/Species/moth.yml): z-level flight is back on, which Monolith#4812 turned off
- [`Resources/Prototypes/Entities/Objects/Vehicles/buckleable.yml`](../../../Resources/Prototypes/Entities/Objects/Vehicles/buckleable.yml): vehicles and their riders leave temporary flight maps through normal z-level physics.
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml): landing thruster kits and parachutes.
- [`Resources/Prototypes/Recipes/Lathes/Packs/engineering.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/engineering.yml): weather forecast console board
- [`Resources/ServerInfo/_Mono/Guidebook/StarSystem/Asclepiu.xml`](../../../Resources/ServerInfo/_Mono/Guidebook/StarSystem/Asclepiu.xml): Asclepiu is called Verda
- [`Resources/ServerInfo/_Mono/Guidebook/StarSystem/Kyphrus.xml`](../../../Resources/ServerInfo/_Mono/Guidebook/StarSystem/Kyphrus.xml): Asclepiu is called Verda

<!-- WOLFGATE-GENERATED END -->
