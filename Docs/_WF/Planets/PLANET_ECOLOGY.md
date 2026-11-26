# Kyphrus terrain and ambient wildlife

Planet-specific recipes live in `Resources/Prototypes/_WF/Planets/open_biomes.yml`;
ambient animal tables live in `fauna.yml`; `carcinoma.yml` defines the quarantined sixth world. The upstream biome templates and the dev
map's automatic DesertWorld are unchanged.

- Fervidus: basalt flats, narrow lava channels and isolated basalt ore outcrops.
  Native argocytes have heat-adapted variants; they still take normal combat damage.
- Merak: adapted from MonoDesertPermanentPlanet, retaining piluma/orakim plants,
  desert animals and richer sand ore. Ore walls occupy smaller patches.
- Asclepiu: grassy plains, patches of woodland, broad coastal water and snowy regions.
  Foxes, lizards, crabs and snakes populate land clearings.
- Aerumna: chromite clearings between shadow groves, sparse crystals and rock outcrops.
  Slurvas are common relative to the occasional hostile xeno.
- Thrascias: open snowfields and ice, occasional ridges and narrow liquid-plasma channels.
  Cold-tolerant argocytes and occasional space bears inhabit this fictional frozen surface.

- Carcinoma: flesh ground, static meat-wall outcrops and flesh clumps. Chimera beasts
  roam with rarer large horrors. The sector body is red/pink and the world is
  unsanctioned, so entering its orbit asks for confirmation.
  No self-spreading kudzu is generated.

Ambient animals are separate from any scripted attack waves. Biome markers
register candidate sites, coalesced into 8-tile cells with a global 2,048-site FIFO
limit. Discovery from orbit does not immediately consume the animal budget. Every
five seconds a bounded pass considers up to 64 candidate sites and admits at most
four animals, 24–64 tiles from surface visitors and outside a 24-tile exclusion
radius around every observer. Sites must still have loaded, unobstructed ground;
ships, holes and ungenerated terrain are rejected. Up to four animals can share a
32-tile neighborhood. Each successful site has a three-minute cooldown.

Ambient spawning remains capped at 32 living animals per origin planet and 128
across all planets. Dead/deleted animals release slots; captured animals still count.
Untouched wildlife on its original ground retires after two minutes more than 96
tiles from all observers. Any damage, player possession, or reparenting to a ship,
container or detached terrain protects that animal from retirement permanently.
Corpses, items and shipwrecks are never removed by this system. Admin spawns and
infection transformations have their separate existing behavior.

Chimera blood's biomass tile reaction is suppressed on planet layers and detached
terrain. Action-spawned hive biomass is also queued for deletion there before a
spread tick. On a planet's maps, and between them, a hull's biomass stops growing at
256 and stays on that hull, so it cannot cross a dock onto terrain. None of this alters chimera
biomass on ordinary station/space maps: there it is neither capped nor kept to one grid.

Carcinoma includes static flesh trees (tinted existing shadow-tree art), flesh
polyps, and dark-red rivers using the existing animated water sprite and ordinary
Blood reagent. They do not spread. Asclepiu/Aerumna have inland water channels;
Merak has sparse desert channels, Fervidus lava, Thrascias plasma, Carcinoma blood.
Orbital-marker WarpPoints are admin-only; radar/navigation markers are unaffected.

Surface ore comes only from the biomes' `MonoPlanetmapOre*` outcrops; planets have no
ore marker layers. Planet atmospheres and flight rules are unchanged.
These recipes use the existing generation system and existing art. No engine edits
or new outpost maps are required.

Use fresh planet networks for verification: already-generated terrain is not
retroactively replaced. PlanetEcologyTest samples three distant regions per world,
checks open-space and rock coverage, exercises real biome spawning, checks that
animals are mobile and climate-compatible, and reloads their chunk to catch duplicate
spawns. Planet/radar tests cover the existing network and terrain display paths.

## Performance scope

Planet networks create five maps each, but terrain/entities stream around players
and subscribed viewers (including views from orbit). Unvisited networks have no
loaded terrain chunks. Planet layers load the far part of a load area over a few passes
(wf.planet_terrain_load_budget) and unload terrain nobody has been near for three
minutes (wf.planet_terrain_unload), a few milliseconds a pass; modified
tiles and mobile animals can persist. NPC AI defaults to sleeping beyond 32 tiles
from players when npc.pause_when_no_players_in_range is enabled. Sleeping AI still
has entity/component overhead. The bounded ecology pass runs every five seconds, not every frame. It never loads
terrain or creates extra view subscriptions.

PlanetPopulationTest exercises all six networks, verifies no eager terrain generation,
and saturates per-planet/global caps, including moving an animal and releasing a slot.
This is a correctness check, not an FPS or server-tick benchmark. A representative
multi-player round is still needed to measure tick time, pathfinding, networking,
loaded chunks, persistent objects and ship-impact spikes on the target server.

## Planet ambience

The maintainer's 24 recordings in Planet Sounds ambience are imported as stereo
44.1 kHz Vorbis under Audio/_WF/Planets/Worlds (about 9 MB total).
Asclepiu, Merak and Carcinoma choose day/night playlists; Aerumna, Fervidus and
Thrascias use all-day playlists. Day is 06:00-18:00 on the same local clock as the
watch. Numbered beds play in order and crossfade near their ends (normally four
seconds, shortened for short clips). Day/night changes use the same equal-power
crossfade. A single-track set loops until the local phase changes.

Aerumna and Carcinoma use the supplied random sounds as intermittent accents;
other planets retain their stock accents because no replacements were supplied.
Playback is client-local and bounded to two overlapping beds plus one accent.
The ambience slider, hull attenuation, air-layer attenuation, mute cleanup and
silent orbit remain. Transit maps inherit the lower layer's soundscape and clock.
Leaving the planet or detaching stops playback. Imported asset credits remain
marked TODO until the maintainer supplies the attribution details.

## Planet weather and timepiece

New networks alternate clear periods (3-7 minutes; Merak 5-10) with weather
lasting 2-4 minutes. Asclepiu gets rain/storms, Fervidus ashfall, Merak sandstorms,
Aerumna light rain/rain, Thrascias snow/blizzards, and Carcinoma red-tinted rain.
Blood rain is visual/audio only: no reagent deposits, damage or biomass growth.
Carcinoma uses WFFloorFlesh, an outdoor variant; indoor FloorFlesh is unchanged.
The existing weather renderer/sound system supplies fades and roof shelter.
Ground and air layers share the schedule; transit maps pick it up on the next
one-second sweep. Orbit never receives weather. The scheduler creates no terrain,
NPCs or precipitation entities and updates at most once per second.

Planet Timepiece (WFPlanetTimepiece), sold through Engineering cargo for 100 credits,
is a handheld readout: use or examine it for
the planet name, local HH:mm and actual surface weather. It works on ships, on the
surface, in atmospheric transit and in orbit; outside a planet network it reports
no planetary signal. Admin weather overrides are reflected in its surface report.
Each world's clock starts when its network is built and uses a configurable
60-real-minute day, displayed on a 24-hour clock. Initial hours differ by world.
This clock also drives the stock daylight and sun-shadow cycles: midnight is
dark, noon is bright, with gradual dawn/dusk. Randomized stock 30-minute lighting
is replaced by the same 60-minute day used by the watch and ambience. Ground,
air and transit stay in phase; orbit keeps its space lighting.

Changes require newly built planet networks. No engine edits are needed.


## Worn timepiece display

Wearing the Planet Timepiece in either arm slot adds a small digital HUD above
the body-part selector at bottom right, with the planet, local time and current
surface weather. Gloves remain available. Holding it or storing it in a bag does not display
the HUD. A worn watch outside a planet shows no signal. The map-scoped clock and
weather snapshot is shared by the watch and soundscape, updated once per second
and sent only when values change. Use/examine readouts remain available.

Regular ambient music fades out on planetary ground/air layers (including transit)
and resumes in orbit/space. Combat music, machines and weather are unchanged.
Planetary loops and intermittent accents were raised by 3 dB.

The first environmental accent plays 6-12 seconds after entering an audible
planetary layer; subsequent accents retain their per-world random intervals.
Client debug logs record soundscape/track changes and accent playback.

Audibility pass: all 17 supplied ambience beds are normalized to -20 LUFS
with -2 dB true-peak headroom. Bed mix is -8 dB; environmental accents gain
another 5 dB. This replaces the previous uneven bed levels, including night
recordings roughly 20 dB below daytime. Original source files are untouched.

Planet boundaries stop both beds and accents immediately; crossfades only apply
within one world. Client-owned streams are explicitly removed during player
state changes, avoiding orphaned loops after ghosting. Each weather scheduler
has its own random deadline; a change on one planet does not advance another.

Shipboard planet ambience is 3 dB louder: hull attenuation is -6 dB on the
five standard worlds and -8 dB on Carcinoma. Outdoor levels are unchanged.

Aerumna uses gravity 3 (three times standard), making it the high-gravity world.
Existing running planet networks retain their copied gravity until rebuilt.

The Planet Timepiece is available free in the shared Contractor Trinkets loadout
group (placed in the backpack), from AstroVend, and via the existing Engineering
cargo product. It is worn in either armband slot. AstroVend stocks five; POI
AstroVend follows its existing infinite-stock convention.
