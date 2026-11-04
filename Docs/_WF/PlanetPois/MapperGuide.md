# Planet POI mapper guide

## What exists now

The world builds its randomized POI layout and routes before terrain preloading. All non-isolated
sites belong to one connected network. Production tables combine the mappers' authored maps with the
existing procedural expedition dungeons.

The network's shape: an inner ring road round the landing zone, an outer ring near the world's edge and
straight trunks between them, with each connected site joined to the nearest road by a short spur. A
roadside or through site's sockets face whichever road it ends up nearest, so give a site sockets on the
sides a road may come from. Expect roads to pass close to a site's yard on the way round it.

Nothing is placed in water. A site's footprint, its socket approaches and a footpath all keep out of
lakes and ponds; a footprint over 48 × 48 may cover up to 3% water, the edge of a pond, which is
drained under it. Water is any biome entity with `FloorOccluder` (lakes, ponds, the blood lakes);
lava and liquid plasma are not water and roads still cross them.

## Shipped maps

- Asclepiu (Verda): Crocus (Jianghui Industrial, a fixed city: see `Content.Server/_WF/PlanetCities/README.md`),
  `verda_house_1` (the owner's roadside house), `verda_gas_station` (by the road, with a fuel merchant),
  and off the roads with footpaths: `verda_corner_house`, `verda_small_house` and `abandoned_bandit_hideout`.
- Merak: `abandoned_bandit_hideout`.
- Carcinoma: `carcinoma_fountain_of_life`, the shrine.
- Verda and Merak: `atalis_house`, a small house with a basement (`atalis_house_below`), off the roads.
- Every world: `planetary_vault`, a rock-cut bunker guarded by PDV troops and turrets, off the roads with a footpath to
  its blast doors (up to one a world).

The houses and the hideout are `dwelling` sites, where a stranger may live.

A site with `rotate: true` is put down at a random quarter turn, and everything on it turns with it except what never
rotates (`noRot`) and walls, fences and the like whose `IconSmooth` mode is `CardinalFlags` or `Bitmask`: their
sprites only join up square to the grid, so they are set back to no rotation.

### A level below

A site can have a basement: map it on the z-level under the site in mapping mode, with stairs down to it under a gap in
the site's floor, and save both levels. `Tools/_WF/PlanetPois/roadside_map.py <site.yml> <name> --trail --below
<level under.yml>` writes `<name>_below.yml` in the site's frame (the offset comes from where the stairs meet the gap;
`--offset x,y` sets it by hand) and adds `below` to the entry. In game the basement goes on the level under the ground,
turned with the site, and the floor is opened over its stairs. A world with no level under its ground (caverns off and no
city) gets the house without the basement, its floor whole.

## Content roadmap

What the worlds could use next, for mappers to build by hand.

1. **Asclepiu city district, 288 × 144.** One per world; two to four clear road exits, with a complete
   authored internal street network. Add a civic square, clinic, housing, small industry and trader
   frontage. Use maintained asphalt/concrete, grass and shoreline vegetation. The lore describes a
   stable breathable biosphere, nutrient-rich shorelines and old nuclear survey signatures; ruins
   and research fit beside a living settlement. Keep the arrival zone separate.
2. **Asclepiu through villages, 64 × 48 and 80 × 56 variants.** Opposite-side road sockets, a main
   street, several buildings and an outdoor meeting space. Produce at least two layouts before
   adding more weighted copies, so repeated settlements are visibly different.
3. **Habitable-world trader posts, 40 × 32.** One roadside driveway and a turning yard, covered
   shop/counter and storage. Use the existing trader system and make stock/services an explicit
   content decision. On Merak, use portable structures, dust shields and freight storage.
4. **Habitable-world fuel/service stops, 48 × 40.** One roadside socket; pumps, parking and repair
   space off the through route. Choose and verify actual usable fuel supplies and repair equipment,
   rather than relying on decorative pumps. Add sheltered stock and a trader only where intended.
5. **Merak caravan town/district, up to 288 × 144; through camps, 64 × 48.** Paved main road with
   concrete shoulders, dusty forecourts, water storage and flexible modular buildings. Lore says
   permanent settlements were ruled out by severe earthquakes: make this a temporary logistics
   town or abandoned settlement rather than an unexplained permanent metropolis. Two or more
   exits must join a traversable authored street.
6. **Fervidus extraction/refuge stations, 48 × 40 to 80 × 56.** Dark pavement tracks, basalt scenery,
   enclosed work rooms, cooling and airlocks. Two sockets for transit depots, one for a roadside
   mining station. Avoid open residential villages: heat, lava and toxic atmosphere dominate.
7. **Thrascias research shelters, 48 × 40 to 64 × 48.** Cleared-snow routes, insulated labs, thermal
   equipment and pressure-safe entrances. The lore is a Frezon gas giant; Wolfgate deliberately
   implements a frozen ground layer. Treat these as that gameplay abstraction, not a claim that
   the original lore has a natural solid surface.
8. **Aerumna expeditions, 40 × 32 to 64 × 48.** Chromite paths and protected industrial/cave access.
   Current Wolfgate terrain is chromite with cave clearings and 3g gravity, while the older guide
   describes Chimera biomass. Resolve that discrepancy before building a settled city theme.
9. **Carcinoma salvage sites, 40 × 32 to 64 × 48.** Dirt tracks, flesh encroachment, abandoned
   infrastructure and quarantine barriers. Roadside or Through when connected; explicitly
   Isolated only for intentionally unreachable horror sites. This world is unsanctioned.

## Fixture and acceptance

`/Maps/_WF/PlanetPois/Tests/through_service.yml` is a **test-only geometry fixture**, not a village
or service station. It contains a 16 × 12 concrete rectangle, an asphalt through strip at y 5..7,
West `[0,6]` and East `[15,6]` sockets, and roofing only at x 5..7/y 1..3 beside the street. It has
one anchored wall at [6,2] and a road marking at [8,6], but no trader, pumps, furniture or production entry. It exists to exercise stamping, sockets
and selective roof copying with known coordinates.

Before enabling a finished map: inspect it in the mapper, walk through every socket and internal
route, verify roofs and weather exposure, test doors/power/air/traders/fuel, and place multiple
variants in a randomized world. Confirm streets do not overwrite structures or protected terrain
and that all non-isolated signals can be reached along routes. Start a headless server and inspect
errors, run the Release YAML linter, and check the PR's CI results. For changed Client/Shared
behavior, run a real client and check for sandbox violations. A successful YAML parse alone does
not establish playability or service functionality.
