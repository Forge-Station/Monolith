# LivingWorld

Planet flora that does something. Two components make any plant prototype interactive, and the per-planet content
builds its plants from them:

- `WFReactiveFlora` makes a stationary plant react to living mobs within `range`: it lashes, grabs, spits, puffs a
  cloud, swells and bursts, irradiates or injects. It looks only when its timer runs out (`checkInterval` while idle,
  `cooldown` after reacting), so thousands of plants cost little. `ignoreFactions` spares a faction;
  `healInsteadFactions` tends one with `healDamage`/`healSolution` instead.
- `WFRegrowingHarvest` lets a plant be picked by hand (or with a `tool` quality) for a loot table, the first piece
  into the picker's hand. It stays standing, shows bare, and regrows after `regrowTime`.
- Wild produce bases in `Resources/Prototypes/_WF/LivingWorld/produce.yml`: `WFWildMushroomBase` (two
  `WFWildMushroom` and water in a big bowl cook into mushroom soup), `WFWildBerryBase`, `WFHealingHerbBase` (eat it
  for bicaridine and kelotane, or cut it into a `WFHerbPoultice`) and `WFPlantFibreBase` (cut into cotton).
- Carcinoma's `WFCarcinomaPimple` grows sparsely on the surface. When anyone outside the Chimera and aberrant flesh
  factions comes within 2 m it swells for 1.5 s, then bursts for 8 blunt and 7 slash within 1.5 m, leaving 20 u of
  blood and a 4 s tear gas spore cloud. Any hit bursts it at once, so shoot it from afar.
- Carcinoma's regions have their own life (Resources/Prototypes/_WF/Planets/Life/carcinoma.yml): grasping tendon vines hold a
  walker for 2 s in the tendon forest, weeping polyps on the scab shores mist blood and a thin toxin, vitae blisters in the
  marsh give a bead of vitae to a blade, bone piles give bone shards by hand, and meat-fruit bushes in the groves heal and
  seed a little mould. Bone burrowers, hive spiders, scab slurvas and marsh newborns join the usual flesh wildlife.
- Black Mesa sounds (`Tools/_WF/LivingWorld/import_bms.py`): plant sounds as `WFFlora*` sound collections in
  `sounds.yml`, and Xen and Black Mesa beds and accents added to every world's ambience in
  `Resources/Prototypes/_WF/Planets/planet_ambience.yml`.

A held mob sees the "Held fast" alert: clicking it struggles, taking `struggleStep` off the hold, and enough damage to
the plant (`cutFreeDamage`) cuts it free.

## Making a plant

Make a child of an existing flora prototype (the Decor Import ones the biomes place) and add the components:

```yaml
# Lashes whoever walks past it, and its pods can be picked.
- type: entity
  parent: GoonShrub
  id: WFFloraWhipvine
  name: whipvine
  components:
  - type: Appearance
  - type: WFReactiveFlora
    behaviour: Lash            # Lash, Grab, Spit, Cloud, Explode, Irradiate, Inject
    range: 1.5
    cooldown: 4
    sound: { collection: WFFloraLash }
    popup: wf-flora-whipvine-lash   # your own string; $plant is the plant
    damage: { types: { Slash: 6 } }
    knockback: 1.5
    ignoreFactions: [Xeno]
  - type: WFRegrowingHarvest
    regrowTime: 600
    harvestSound: { collection: WFFloraVineSnap }
    table: !type:AllSelector
      children:
      - id: WFPlantFibre
        amount: !type:RangeNumberSelector
          range: 1, 2
```

Per behaviour:

- `Lash`: `damage`, `knockback` (m) and `knockbackSpeed`, `stunTime`, `knockdownTime`; `maxTargets: 0` hits everyone.
- `Grab`: `holdTime`, `heldSpeed` (0 holds still), `damage` on the grab, `cutFreeDamage`, `struggleStep`,
  `releaseSound`.
- `Spit`: `projectile` (any projectile prototype) and `projectileSpeed`.
- `Cloud`: `solution`, `cloudPrototype` (`Smoke` or a foam), `cloudDuration`, `cloudSpread`. Tear gas blinds,
  space drugs or mindbreaker make you see things, toxin or mold poisons.
- `Explode`: `armDelay`, `armSound`, `armPopup`, then `sound`, `popup`, `burstRadius`, `burstDamage`, `burstSpill`,
  `burstSpawns`, a `solution` cloud, an optional `explosionType` (it never breaks tiles), and `deleteOnBurst` or
  `regrowTime`. `burstOnDamage` pops it when hit.
- `Irradiate`: `radsPerSecond` on every mob in range (`maxTargets: 0`), each `cooldown`.
- `Inject`: `solution` into the bloodstream, e.g. omnizine to heal or toxin to hurt.

The sprite can follow the plant: appearance key `enum.WFReactiveFloraVisuals.State` with values `Idle`, `Armed`
(swelling, holding, or just after a reaction for `activeTime`) and `Spent`, and `enum.WFRegrowingHarvestVisuals.Harvested`
(`True`/`False`). Use a `GenericVisualizer`, as `WFCarcinomaPimple` does.

To place it, add a `BiomeEntityLayer` to the planet's biome as `WFCarcinomaPimple` does in
`Resources/Prototypes/_WF/Planets/carcinoma.yml`, and add solid or dangerous ones to the world's cavern `avoid` list in
`Resources/Prototypes/_WF/Caverns/caverns.yml`. A planet's biome test may list the features it allows.

Entry points: `WFReactiveFloraSystem`, `WFRegrowingHarvestSystem`.

## Merak

Merak's regions each have their own wildlife and plants (`Resources/Prototypes/_WF/Planets/Life/merak.yml`; the sand
maws stay in `_WF/SandMaw`). Dunes: desert tarantulas (a sand-coloured space spider, slower and harder-hitting) and
tumbleweeds that rustle now and then. Badlands: lizards, cobras and spitting cacti that flick slow needles from 6 m.
Cactus fields: kangaroos, sting-nettles among the grass (a sting and a 1 s stun), barrel cacti picked by hand for
cactus water and agave cut for sugary sap. Gravel flats: ore crabs round the ore outcrops. Oases: goats and crabs, date
palms with pickable dates and, very rarely, a mimic posing as a crate.

## Sounds

The Black Mesa files come from the owner's copy of the mod's sounds, which the owner states are open source; the
attributions say `license: "Custom"` until the exact licence is confirmed. Rerun the script to re-import, and keep the
set to what a collection or ambience uses (`LivingWorldAudioTest` fails on an unused file or more than 60).

| Folder | Files | Collections |
|---|---|---|
| `lash` | tentacle strikes and swooshes | `WFFloraLash`, `WFFloraSwoosh` |
| `grab` | barnacle tongue pulls, gulp, bark | `WFFloraGrab`, `WFFloraGulp` |
| `spit` | bullsquid goo attacks, splat | `WFFloraSpit`, `WFFloraSplat` |
| `pods` | barrel cactus flinches and bursts, mold cyst, Xen pod breaks | `WFFloraPodSwell`, `WFFloraPodBurst`, `WFFloraSporePuff`, `WFFloraPodIdle` |
| `heal` | healing pool | `WFFloraHeal`, `WFFloraHealHum` |
| `light` | light plant extend and retract | `WFFloraLightExtend`, `WFFloraLightRetract` |
| `critters` | protozoan chirps, lily pad | `WFFloraChirp`, `WFFloraLilypad` |
| `hackertree` | attack, death, retract | `WFFloraTreeAttack`, `WFFloraTreeDie`, `WFFloraTreeRetract` |
| `vines` | vine and web breaks | `WFFloraVineSnap`, `WFFloraVineTear` |
| `crystal` | radiation crystal hum and charge | `WFFloraCrystalHum`, `WFFloraCrystalCharge` |
| `gore` | gore splats, gibbing | `WFFloraGore`, `WFFloraWetSquelch` |
| `ambience` | stereo, -20 LUFS | see below |

Ambience: Aerumna adds the Xen outdoors bed and far gargantua, boid and tower manta calls; Carcinoma the Xen swamp bed by
day; Merak desert wind after each bed; Thrascias the howling Nihilanth wind bed and a wind gust; Fervidus a fire bed and
a deep rumble; Asclepiu hawks and eagles by day.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/LivingWorld/WFFloraGrabbedComponent.cs`](WFFloraGrabbedComponent.cs)
- [`Content.Server/_WF/LivingWorld/WFReactiveFloraSystem.cs`](WFReactiveFloraSystem.cs)
- [`Content.Server/_WF/LivingWorld/WFRegrowingHarvestSystem.cs`](WFRegrowingHarvestSystem.cs)

### Shared

- [`Content.Shared/_WF/LivingWorld/WFReactiveFloraComponent.cs`](../../../Content.Shared/_WF/LivingWorld/WFReactiveFloraComponent.cs)
- [`Content.Shared/_WF/LivingWorld/WFRegrowingHarvestComponent.cs`](../../../Content.Shared/_WF/LivingWorld/WFRegrowingHarvestComponent.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/LivingWorld/CarcinomaPimpleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/LivingWorld/CarcinomaPimpleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/LivingWorld/LivingWorldAudioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/LivingWorld/LivingWorldAudioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/LivingWorld/ReactiveFloraTest.cs`](../../../Content.IntegrationTests/Tests/_WF/LivingWorld/ReactiveFloraTest.cs)
- [`Content.IntegrationTests/Tests/_WF/LivingWorld/RegrowingHarvestTest.cs`](../../../Content.IntegrationTests/Tests/_WF/LivingWorld/RegrowingHarvestTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/LivingWorld/alerts.yml`](../../../Resources/Prototypes/_WF/LivingWorld/alerts.yml)
- [`Resources/Prototypes/_WF/LivingWorld/carcinoma_pimple.yml`](../../../Resources/Prototypes/_WF/LivingWorld/carcinoma_pimple.yml)
- [`Resources/Prototypes/_WF/LivingWorld/produce.yml`](../../../Resources/Prototypes/_WF/LivingWorld/produce.yml)
- [`Resources/Prototypes/_WF/LivingWorld/recipes.yml`](../../../Resources/Prototypes/_WF/LivingWorld/recipes.yml)
- [`Resources/Prototypes/_WF/LivingWorld/sounds.yml`](../../../Resources/Prototypes/_WF/LivingWorld/sounds.yml)

### Localization

- [`Resources/Locale/en-US/_WF/LivingWorld/living_world.ftl`](../../../Resources/Locale/en-US/_WF/LivingWorld/living_world.ftl)

### Audio

- [`Resources/Audio/_WF/LivingWorld/ambience/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/ambience/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/ambience/boid_xen_medium01.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/boid_xen_medium01.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/eagle1.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/eagle1.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/garg_xen_far01.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/garg_xen_far01.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/hawk1.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/hawk1.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/o_ambience_disaster_fire1.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/o_ambience_disaster_fire1.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/o_ambience_disaster_rumble1.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/o_ambience_disaster_rumble1.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/towermantas_far01.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/towermantas_far01.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/wind1.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/wind1.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/wind_fall.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/wind_fall.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/xen_nih_wind1_mix.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/xen_nih_wind1_mix.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/xen_outdoor1_mix.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/xen_outdoor1_mix.ogg)
- [`Resources/Audio/_WF/LivingWorld/ambience/xen_swamp1_mix.ogg`](../../../Resources/Audio/_WF/LivingWorld/ambience/xen_swamp1_mix.ogg)
- [`Resources/Audio/_WF/LivingWorld/critters/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/critters/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/critters/chirp03.ogg`](../../../Resources/Audio/_WF/LivingWorld/critters/chirp03.ogg)
- [`Resources/Audio/_WF/LivingWorld/critters/chirp05.ogg`](../../../Resources/Audio/_WF/LivingWorld/critters/chirp05.ogg)
- [`Resources/Audio/_WF/LivingWorld/critters/lilypad_landing_soft01.ogg`](../../../Resources/Audio/_WF/LivingWorld/critters/lilypad_landing_soft01.ogg)
- [`Resources/Audio/_WF/LivingWorld/critters/lilypad_sinking01.ogg`](../../../Resources/Audio/_WF/LivingWorld/critters/lilypad_sinking01.ogg)
- [`Resources/Audio/_WF/LivingWorld/crystal/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/crystal/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/crystal/xen_crystal_radiation_charging01.ogg`](../../../Resources/Audio/_WF/LivingWorld/crystal/xen_crystal_radiation_charging01.ogg)
- [`Resources/Audio/_WF/LivingWorld/crystal/xen_crystal_radiation_hum01.ogg`](../../../Resources/Audio/_WF/LivingWorld/crystal/xen_crystal_radiation_hum01.ogg)
- [`Resources/Audio/_WF/LivingWorld/gore/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/gore/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/gore/gibbing1.ogg`](../../../Resources/Audio/_WF/LivingWorld/gore/gibbing1.ogg)
- [`Resources/Audio/_WF/LivingWorld/gore/gore1.ogg`](../../../Resources/Audio/_WF/LivingWorld/gore/gore1.ogg)
- [`Resources/Audio/_WF/LivingWorld/gore/gore_splat1.ogg`](../../../Resources/Audio/_WF/LivingWorld/gore/gore_splat1.ogg)
- [`Resources/Audio/_WF/LivingWorld/gore/gore_splat2.ogg`](../../../Resources/Audio/_WF/LivingWorld/gore/gore_splat2.ogg)
- [`Resources/Audio/_WF/LivingWorld/grab/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/grab/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/grab/barnacle_bark1.ogg`](../../../Resources/Audio/_WF/LivingWorld/grab/barnacle_bark1.ogg)
- [`Resources/Audio/_WF/LivingWorld/grab/barnacle_gulp1.ogg`](../../../Resources/Audio/_WF/LivingWorld/grab/barnacle_gulp1.ogg)
- [`Resources/Audio/_WF/LivingWorld/grab/barnacle_pull1.ogg`](../../../Resources/Audio/_WF/LivingWorld/grab/barnacle_pull1.ogg)
- [`Resources/Audio/_WF/LivingWorld/grab/barnacle_pull2.ogg`](../../../Resources/Audio/_WF/LivingWorld/grab/barnacle_pull2.ogg)
- [`Resources/Audio/_WF/LivingWorld/grab/barnacle_tongue_pull1.ogg`](../../../Resources/Audio/_WF/LivingWorld/grab/barnacle_tongue_pull1.ogg)
- [`Resources/Audio/_WF/LivingWorld/hackertree/attack1.ogg`](../../../Resources/Audio/_WF/LivingWorld/hackertree/attack1.ogg)
- [`Resources/Audio/_WF/LivingWorld/hackertree/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/hackertree/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/hackertree/die1.ogg`](../../../Resources/Audio/_WF/LivingWorld/hackertree/die1.ogg)
- [`Resources/Audio/_WF/LivingWorld/hackertree/retract_to_idle01.ogg`](../../../Resources/Audio/_WF/LivingWorld/hackertree/retract_to_idle01.ogg)
- [`Resources/Audio/_WF/LivingWorld/heal/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/heal/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/heal/healingpool_active01.ogg`](../../../Resources/Audio/_WF/LivingWorld/heal/healingpool_active01.ogg)
- [`Resources/Audio/_WF/LivingWorld/heal/healingpool_hum01.ogg`](../../../Resources/Audio/_WF/LivingWorld/heal/healingpool_hum01.ogg)
- [`Resources/Audio/_WF/LivingWorld/lash/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/lash/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/lash/tent_strike1.ogg`](../../../Resources/Audio/_WF/LivingWorld/lash/tent_strike1.ogg)
- [`Resources/Audio/_WF/LivingWorld/lash/tent_strike2.ogg`](../../../Resources/Audio/_WF/LivingWorld/lash/tent_strike2.ogg)
- [`Resources/Audio/_WF/LivingWorld/lash/tent_swoosh1.ogg`](../../../Resources/Audio/_WF/LivingWorld/lash/tent_swoosh1.ogg)
- [`Resources/Audio/_WF/LivingWorld/lash/tent_swoosh2.ogg`](../../../Resources/Audio/_WF/LivingWorld/lash/tent_swoosh2.ogg)
- [`Resources/Audio/_WF/LivingWorld/lash/tent_swoosh3.ogg`](../../../Resources/Audio/_WF/LivingWorld/lash/tent_swoosh3.ogg)
- [`Resources/Audio/_WF/LivingWorld/light/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/light/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/light/extend01.ogg`](../../../Resources/Audio/_WF/LivingWorld/light/extend01.ogg)
- [`Resources/Audio/_WF/LivingWorld/light/retract01.ogg`](../../../Resources/Audio/_WF/LivingWorld/light/retract01.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/pods/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/pods/explode01.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/explode01.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/explode02.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/explode02.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/flinch01.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/flinch01.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/flinch02.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/flinch02.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/moldcyst_idle.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/moldcyst_idle.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/moldcyst_pop.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/moldcyst_pop.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/xen_podbreak.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/xen_podbreak.ogg)
- [`Resources/Audio/_WF/LivingWorld/pods/xen_podbreak2.ogg`](../../../Resources/Audio/_WF/LivingWorld/pods/xen_podbreak2.ogg)
- [`Resources/Audio/_WF/LivingWorld/spit/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/spit/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/spit/goo_attack1.ogg`](../../../Resources/Audio/_WF/LivingWorld/spit/goo_attack1.ogg)
- [`Resources/Audio/_WF/LivingWorld/spit/goo_attack2.ogg`](../../../Resources/Audio/_WF/LivingWorld/spit/goo_attack2.ogg)
- [`Resources/Audio/_WF/LivingWorld/spit/splat1.ogg`](../../../Resources/Audio/_WF/LivingWorld/spit/splat1.ogg)
- [`Resources/Audio/_WF/LivingWorld/vines/attributions.yml`](../../../Resources/Audio/_WF/LivingWorld/vines/attributions.yml)
- [`Resources/Audio/_WF/LivingWorld/vines/int_vinebreak1.ogg`](../../../Resources/Audio/_WF/LivingWorld/vines/int_vinebreak1.ogg)
- [`Resources/Audio/_WF/LivingWorld/vines/int_vinebreak2.ogg`](../../../Resources/Audio/_WF/LivingWorld/vines/int_vinebreak2.ogg)
- [`Resources/Audio/_WF/LivingWorld/vines/web_break01.ogg`](../../../Resources/Audio/_WF/LivingWorld/vines/web_break01.ogg)
- [`Resources/Audio/_WF/LivingWorld/vines/web_largebreak01.ogg`](../../../Resources/Audio/_WF/LivingWorld/vines/web_largebreak01.ogg)

### Tools

- [`Tools/_WF/LivingWorld/import_bms.py`](../../../Tools/_WF/LivingWorld/import_bms.py)

## Non-modular edits

None.

<!-- WOLFGATE-GENERATED END -->
