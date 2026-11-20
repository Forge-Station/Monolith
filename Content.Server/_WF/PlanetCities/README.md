# PlanetCities

Cities on planet surfaces, in two kinds.

**Fixed cities** are hand-made multi-level maps stamped onto a world every round at a random spot, as a
`wfFixedCity` prototype. Crocus (Monolith's Jianghui Industrial, `WFCityCrocus`) is the first, on Asclepiu. The
city's game map gives everything: its main map goes on the ground, its maps above (`CEStationZLevels`) on the
world's flight layers as static grids held up like mapped islands, and its maps below on the cavern, or on empty
layers this module adds when the world has no cavern. Each level is loaded as a plain grid and merged in by the POI
site code, so Monolith's map files are used as they are; it loads whole, since a level's buildings don't touch and a grid
free to split would lose all but one of them. The street's holes over stairs on the level below stay open and pinned, as
stairwells, so the biome doesn't grow over them. The game map's station is created over the city's grids
(the whole ground included), so it is joinable with the game map's jobs and spawn points. The prototype lists
prototypes to swap on load (Crocus's indestructible rock and walls become ordinary ones, its observer spawns go)
and road entrances cut from the city's streets to the edge of its footprint, where the world's roads join. A
fixed city is placed before the world's other sites; one that finds no room is left out with a warning. Bare rock on
the levels below the street (`terrain`) is dropped before loading where the layer under the world grows its own: the
cavern, or the cavern's rock without its mouths on a world with caverns off. The city is built a level per tick,
during the world's preload. Whatever has become of it, its `residents` (Crocus: 24 to 36 civilians with the odd dealer or
conspiracy nut, `WFCityResidents`) live in its rooms: standing on bare roofed floor on every level, the ground floor, the
upper levels and the underground each (`ground`/`upper`/`lower`), one on every level that has room and the rest in
proportion to each level's floor, a few tiles apart and clear of the spawn points. They wake like any NPC, when a player is
near on their map, so the ones in the towers wait for someone on that flight layer. Each round it also rolls one of its
`fates`: left alone, or overrun by groups of something (Crocus: zombies, spiders, crazy camps or aberrant flesh), kept
clear of its spawn points. A fate's `indoorShare` of its groups settle on roofed floor instead of the open streets, upstairs
too with `upperLevels`; only groups in the open get the fate's `gathering` props.

**Generated cities** will be a city boundary cut into blocks by a street grid, blocks filled wall to wall with
mapped plots, up to two floors above the street and a basement on the sewer level, and a sewer network under every
street. Only the mapping side exists so far: the plot and sewer templates, the markers the generator will read, and
a test that holds every city map to the rules below. The generator itself is not written yet.

Templates are written by `python Tools/_WF/PlanetCities/templates.py` into
`Resources/Maps/_WF/PlanetCities/Templates`; `--preview DIR` also draws them as PNGs. Copy one to start a new plot;
don't edit the templates in place, the script overwrites them.

## Plots

A plot is one building, one map file per floor, in `Resources/Maps/_WF/PlanetCities/Plots/<district>/`:
`<name>_ground.yml` (required), `<name>_upper1.yml`, `<name>_upper2.yml` and `<name>_basement.yml`. Upper floors go on
the planet's two flight layers above the ground and the basement on the cavern layer below, where the sewers are.

- **Size.** 8 to 32 tiles a side in steps of 4. The templates cover 8x8, 8x12, 12x12, 12x16, 16x16 and 16x24. Every
  floor of a plot has the same size and lines up with the ground floor.
- **Full rectangle.** Every tile of the rectangle has a floor, outside walls included. Nothing outside it.
- **Facing.** The street is south (the bottom row). The generator rotates the plot to face its street.
- **Front door.** At least one door on the bottom row of the ground floor, with a `WFCityFrontDoor` marker on the same
  tile. Storefronts can be windows and glass doors along the whole row.
- **Back door.** Optional, on the top row of the ground floor, marked `WFCityBackDoor`. Used when the plot backs onto
  an alley.
- **Shared walls.** The outside walls sit on the rectangle's edge. Neighbouring plots stand wall to wall; the
  generator keeps one wall where two meet, so don't leave the side edges open.
- **Stairs.** `WFCityStairs` marks the foot of stairs on a floor; the floor above has `WFCityStairwell` on the same
  tile, which the generator opens. The ground floor's stairs go up to `upper1`; a basement's stairs come up into the
  ground floor. Keep the tiles in front of them clear. The templates use the back-left corner for going up and the
  back-right corner for the basement.
- **Basement to sewer.** Optional: a door in a basement's outside wall marked `WFCitySewerLink` opens onto the sewer
  tunnel behind it if one runs there.
- **District.** The folder name is the district: `shops`, `homes`, `bars`, `clinics`, `police`, `industrial`,
  `warehouses`, `civic`, `lots` (empty lots, parks). The generator mixes districts by these.
- **Who's there.** Leave NPCs and loot as spawners, not placed mobs, so the generator can make a block lived-in,
  abandoned or hostile.
- **Roofs.** Every tile of every floor is roofed in the templates; keep it, it's what makes the inside indoors.

## Sewers

Sewer pieces are 8x8 cells (16x16 for a chamber) in `Resources/Maps/_WF/PlanetCities/Sewers/`, laid under the
streets and rotated to fit. A 6-wide tunnel with a two-tile channel of moving water (`CEWaterMoving`, half speed to
wade) and catwalks where it turns.

- Every opening has a `WFCitySewerLink` marker on its middle edge tile; pieces join where links meet.
- `WFCityManhole` marks a walkway tile with a ladder up to the street.
- Templates: straight, corner, tee, cross, dead end, manhole straight and a 16x16 chamber. More kinds (outfall, pump
  room, collapsed tunnel, gang den) just need the same rules.

`CityTemplateTest` checks all of this for every map under `Resources/Maps/_WF/PlanetCities`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/PlanetCities/WFPlanetCitiesComponent.cs`](WFPlanetCitiesComponent.cs)
- [`Content.Server/_WF/PlanetCities/WFPlanetCitySystem.cs`](WFPlanetCitySystem.cs)
- [`Content.Server/_WF/PlanetCities/WFPlanetPoiSystem.Cities.cs`](WFPlanetPoiSystem.Cities.cs)
- [`Content.Server/_WF/PlanetCities/WFPlanetPoiSystem.CityFates.cs`](WFPlanetPoiSystem.CityFates.cs)
- [`Content.Server/_WF/PlanetCities/WFPlanetPoiSystem.CityPeople.cs`](WFPlanetPoiSystem.CityPeople.cs)

### Shared

- [`Content.Shared/_WF/PlanetCities/WFFixedCityPrototype.cs`](../../../Content.Shared/_WF/PlanetCities/WFFixedCityPrototype.cs)
- [`Content.Shared/_WF/PlanetCities/WFPlanetCityGridComponent.cs`](../../../Content.Shared/_WF/PlanetCities/WFPlanetCityGridComponent.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/PlanetCities/CityTemplateTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetCities/CityTemplateTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetCities/CrocusAirTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetCities/CrocusAirTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetCities/CrocusLevelsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetCities/CrocusLevelsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/PlanetCities/FixedCityTest.cs`](../../../Content.IntegrationTests/Tests/_WF/PlanetCities/FixedCityTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/PlanetCities/crocus.yml`](../../../Resources/Prototypes/_WF/PlanetCities/crocus.yml)
- [`Resources/Prototypes/_WF/PlanetCities/markers.yml`](../../../Resources/Prototypes/_WF/PlanetCities/markers.yml)
- [`Resources/Prototypes/_WF/PlanetCities/residents.yml`](../../../Resources/Prototypes/_WF/PlanetCities/residents.yml)
- [`Resources/Prototypes/_WF/PlanetCities/turrets.yml`](../../../Resources/Prototypes/_WF/PlanetCities/turrets.yml)

### Localization

- [`Resources/Locale/en-US/_WF/PlanetCities/cities.ftl`](../../../Resources/Locale/en-US/_WF/PlanetCities/cities.ftl)

### Maps

- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_basement.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_basement.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_ground.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_ground.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_upper1.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_upper1.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_upper2.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x12_upper2.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_basement.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_basement.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_ground.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_ground.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_upper1.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_upper1.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_upper2.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_12x16_upper2.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_basement.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_basement.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_ground.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_ground.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_upper1.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_upper1.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_upper2.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x16_upper2.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_basement.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_basement.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_ground.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_ground.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_upper1.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_upper1.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_upper2.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_16x24_upper2.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_basement.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_basement.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_ground.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_ground.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_upper1.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_upper1.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_upper2.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x12_upper2.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_basement.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_basement.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_ground.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_ground.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_upper1.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_upper1.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_upper2.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Plots/plot_8x8_upper2.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_chamber.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_chamber.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_corner.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_corner.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_cross.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_cross.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_end.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_end.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_manhole.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_manhole.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_straight.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_straight.yml)
- [`Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_tee.yml`](../../../Resources/Maps/_WF/PlanetCities/Templates/Sewers/sewer_tee.yml)

### Tools

- [`Tools/_WF/PlanetCities/templates.py`](../../../Tools/_WF/PlanetCities/templates.py)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs`](../../../Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelShadowOverlay.cs): a city's upper floors are buildings, roofed already, not a ship overhead.

<!-- WOLFGATE-GENERATED END -->
