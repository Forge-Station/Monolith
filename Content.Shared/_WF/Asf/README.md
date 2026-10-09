# Asf

The Astraeus Solar Federation (ASF): a noblebright, multispecies federation with a minor presence in the
sector. It coexists with the TSF rather than replacing or fighting it. Purple is its colour, and its doctrine is
good shields, lasers first and a few self-loading torpedoes.

Players join at ASF Lantern Post (`WFAsfLantern`, `Resources/Prototypes/_WF/Asf/lantern.yml`) as Envoy, Enforcer,
Field Researcher or Colonist. It is a base ship that spawns as a mobile POI (no ForceAnchor) with gyroscopes and a
station anchor. Its plan is a set of rooms of different sizes and offsets, and the hull is generated around them, so
the outline is uneven and nothing is mirrored. From bow to stern:

- A tapered bridge on a wide ready-room collar (fire control, EVA gear), with a sensor boom off the starboard bow.
- A private north block: the envoy office, shipyard hall and quarters, then the post, the wardrobe room with the ASF
  drobe (`WFAsfDrobe`) and a commons that juts out to starboard, all off a short spine corridor. The post's west wall
  is its armoury: a rifle rack (`WFAsfGunRackFilled`, three coil rifles), a table with four slug boxes and a box each of rifle and pistol magazines, and a sidearm
  rack (`WFAsfPistolRackFilled`, four coil pistols). Three suit storage units (`WFAsfSuitStoragePathfinder`, ASF
  security access) along its north wall each hold a charged Pathfinder modsuit, a breath mask and tanks. Racks and suit
  units are in `Entities/armory.yml`.
- The public middle: consular office, atrium garden and clinic over the dock concourse. The concourse runs out into a
  long port docking arm (3 docks; a FuelVend and a FlatpackVend flank its bay) and a short starboard annex (2 docks)
  with the public cargo bay behind it: four sell pallets and a sale console, the ASF bounty console, the grant kiosk,
  two artifact containers and a hand labeler.
- Aft: the farm with a stores pod off its flank, the gyro room, R&D lab, power room, atmos, and an anomaly pair (an
  observation room looking through plasma glass into a test chamber with an artifact analyzer and an APE). The lab's
  material silo is mapped linked to all three lathes (protolathe, `WFAsfLathe`, circuit imprinter), so they share
  its materials.
- Outside the pressure hull: engine pylons of different lengths on lattice trusses (seven large thrusters in all,
  `WFAsfThrusterLarge`), a central engine block, gun pads on trusses and stubs, and hull plating filling the notches.

- Fleet (`fleet.yml`; civilian hulls are "ASF", armed ones "ASFN" like the TSF's "TSFN"), sold at `WFAsfComputerShipyard` in the shipyard hall (a purple recolour of the USSP
  shipyard screen). Prices and loadouts were set against other factions' hulls of similar tile count, scoring
  firepower by hardpoint size (light 1, medium 2, heavy 4):

  | Hull | Tiles | Firepower | Shield | Price |
  |---|---|---|---|---|
  | Gleaner (mining pod) | 33 | - | - | 25k |
  | Lamplighter (survey, research bay) | 131 | - | - | 38k |
  | Porter (hauler) | 130 | - | - | 40k |
  | Vigil (escort fighter) | 64 | 3: Hummingbird, Flutter | - | 40k |
  | Bulwark (gunship) | 99 | 4: Hummingbird, Serpent | small | 75k |
  | Guardian (corvette) | 237 | 12: Apollo, 2 Hummingbird, 2 Serpent | small | 120k |
  | Lodestar (flagship frigate) | 479 | 22: 2 Apollo, 4 Hummingbird, 2 Serpent, 2 Flutter | MS-250 Aegis | 250k, voucher only |

  Peers at those sizes: QJ340/Bazalt (fighters), QJ270/Strayk/Reaver (gunships), Paladin/Kite/Shiv (corvettes),
  Calypso/Senkusha/Neptune (voucher frigates). Each hull's gunnery server is sized to its guns.
- Purple LPCs redeem only at the ASF console: civilian (Gleaner, Porter, Lamplighter), escort (Vigil, Bulwark) and
  Lodestar. Only the Envoy starts with one (escort), like the VG Commander's Wolf voucher. The rest are earned like
  other factions' LPCs: research `WFAsfLPCCivilian`, `WFAsfLPCEscort` and `WFAsfLPCLodestar` (tier 2) on the lab's
  `WFAsfResearchServer` (its own `WFAsf` discipline), then print them at `WFAsfLathe` (`research.yml`). The
  fabricator also prints universal shipgun ammo.
- Astraeus Reward Grants (ARG, `grants.yml`, `bounties.yml`): the cargo bay's bounty console offers only ASF
  bounties, which pay ARG tickets (`WFAsfRewardGrant`, a purple recolour of the MMC ticket) instead of cash. They ask
  for monolithic crystals by size (tagged `WFMonolithicCrystal*` in Mono's `loot.yml`; about 1.25 ARG per 1,000 of
  sale value), for artifact fragments (six, a little over the four that craft an artifact) or for alien artifacts, either
  any artifact or one with a lasting utility effect (holds items, intercom,
  instrument, holds liquid, wanders, speed, power, drill, multitool, gun). Like other bounties, items handed in are
  paid by the reward only. ASF crew spend ARG at the grant kiosk (`WFAsfGrantKiosk`, the MMC requisition kiosk in
  purple): hull vouchers (civilian 30, escort 60, Lodestar 250), coil arms and ammo, both modsuits and
  xenoarchaeology tools. ARG has no cash payout. Checked by `AsfGrantsTest`.
- Lantern Post clears drones within 1.5 km and keeps world-gen debris out of 800 m, like Hokkaido and Camelot.
- Access (`faction.yml`): every role has `WFAsf` (private rooms), all but the Colonist have `WFAsfBridge`, the
  Enforcer and Envoy have `WFAsfSecurity` (the post and its lockers), and only the Envoy has `WFAsfCommand` (the
  envoy office). The shipyard console checks the ASF company, not ID access, since the buyer's ID sits in its
  slot; `AsfAccessTest` covers this.
- Each role has a purple PDA with its own trim and an ID card whose symbol copies its job icon.
- Official clothing: `WFAsfClothingUniformService`, `WFAsfClothingHeadBeret`, `WFAsfClothingOuterGreatcoat`.
  `Entities/clothing_wardrobe.yml` adds 19 more pieces, including the backpack, satchel and duffel the drobe sells.
- Coilguns (`Entities/weapons.yml`): the Type-3 coil rifle (`WFAsfWeaponRifleCoil`) and Type-2 coil pistol
  (`WFAsfWeaponPistolCoil`), white frames after the Star Trek Type-3B phaser rifle. They are ballistic, but they fire
  caseless 4mm ferrite slugs from a coil barrel: nothing is ejected (`deleteOnSpawn`) and there is no bolt
  (`boltClosed: null`). Both share one slug (20 piercing, 0.15 AP). The rifle's longer coil gives x1.4 damage (28), speed 70,
  semi or fast 3-round bursts (about 210 dps sustained against the TSF Lecter's 192), 24 rounds; the pistol gives x1.1
  (22) at 5 shots a second, 15 rounds (about 110 dps against the TSF Mk58's 100). Faction gear tier 2 and 1, allowed for the ASF
  department. Research `WFAsfCoilArms` and print guns and magazines at `WFAsfLathe`. The Enforcer starts with a pistol
  and a spare magazine.
  Loose slugs come in 60-round boxes (`WFAsfBoxCoilSlug`), printed under the same research.
- Field modsuits (`Entities/modsuit.yml`), recolours of Goob's Minerva (RD) modsuit with a visor
  added (the Minerva's face is a plain plate). The Pathfinder
  (`WFAsfClothingModsuitField`, research `WFAsfFieldModsuit`, tier 1) is pearl over plum with a dark visor and has the
  TSF M82c's armour (0.65 blunt and slash, 0.6 piercing, 0.95 speed) with better radiation cover; its chestplate takes
  armour plates like any modsuit, and it covers harpy and IPC wings (`HarpyHideWings`) as hardsuits do. The Aegis (`WFAsfClothingModsuitAegis`, `WFAsfAegisModsuit`, tier 2) is dark violet
  with glowing hardlight seams and a lit visor, keeps that armour and adds an M86 Mk.4-class personal shield (250
  charge, violet). The shield sits on the control unit (back slot), not the chestplate: a chestplate's item toggle also
  drives its seal, so toggling a shield there would drop the suit's pressure protection. `ShieldRequiresSeal`
  (`ShieldRequiresSealSystem`, the module's only C#) keeps the shield down until the suit is sealed and drops it when
  the suit unseals.

## Ported from Hullrot: Eclipsion

The wardrobe sprites and the seven hulls come from
[Eclipsion](https://github.com/eclipsion-team/eclipsion)'s DSM faction, renamed and rewritten for the ASF.

- Only assets whose `meta.json` gives an open licence (CC-BY-SA-3.0, Shiptest, tgstation and Baystation derived)
  were taken. Anything marked "all rights reserved" or under the Eclipsion Restricted Asset License was left out;
  don't port those without written permission from their team.
- The hulls (DSM Peasant, Impel, Gnosis, Redemptor, Imparator, Dragoon and Praxis; AGPL-3.0) were converted with
  prototypes Wolfgate lacks swapped for local ones and each DSM mount armed.
- The converted hulls were then fixed by hand: diagonal corners
  turned to fit the hull (Eclipsion's corner-specific diagonals all came in unrotated), cable nets pruned to loop-free
  trees that still reach every machine, and air supply added where the DSM layouts had none or too little (Porter,
  Bulwark, Lodestar, Guardian's gun bay, Lamplighter's engine room). Vigil and Gleaner stay without vents, like
  other fighters and pods their size. The Lamplighter also gained a research bay between the bridge corridor and the
  engine room (R&D server, protolathe, circuit imprinter, R&D and analysis consoles, an artifact analyzer pad and two
  extra APCs for the pad's 12 kW). The Bulwark's Serpent and the Guardian's Apollo sat on DSM mounts inside the
  hull behind blast doors, where ship rounds hit their own hull; they were moved to the Bulwark's starboard pad (3,2)
  and the Guardian's bow tip (8,20).
- The DSM thruster sprite was dropped: its fore and aft frames are 3/4 views, so no plume could leave the nozzle.
  `WFAsfThruster` and `WFAsfThrusterLarge` are the plan-view vanilla (Baystation) and Mono large thrusters recoloured
  to the ASF palette.

## Lore

- The ASF spans the solar system and a collection of alien species. Humanity is one member, not the driver.
- The moth fleet ended a human conflict and let humanity join.
- The ASF comes from our SS13 server, so its history overlaps the TSF's. In-game the two are not rivals.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Shared

- [`Content.Shared/_WF/Asf/ShieldRequiresSealComponent.cs`](ShieldRequiresSealComponent.cs)
- [`Content.Shared/_WF/Asf/ShieldRequiresSealSystem.cs`](ShieldRequiresSealSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Asf/AsfAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfCoilgunTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfCoilgunTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfFleetTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfFleetTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfGrantsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfGrantsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfLanternTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfLanternTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfModsuitTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfModsuitTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfRolesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfRolesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Asf/AsfShipWeaponsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Asf/AsfShipWeaponsTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Asf/bounties.yml`](../../../Resources/Prototypes/_WF/Asf/bounties.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/armory.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/armory.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/banners.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/banners.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/clothing.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/clothing.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/clothing_wardrobe.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/clothing_wardrobe.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/devices.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/devices.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/drobe.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/drobe.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/modsuit.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/modsuit.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/ship_weapons.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/ship_weapons.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/vouchers.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/vouchers.yml)
- [`Resources/Prototypes/_WF/Asf/Entities/weapons.yml`](../../../Resources/Prototypes/_WF/Asf/Entities/weapons.yml)
- [`Resources/Prototypes/_WF/Asf/faction.yml`](../../../Resources/Prototypes/_WF/Asf/faction.yml)
- [`Resources/Prototypes/_WF/Asf/fleet.yml`](../../../Resources/Prototypes/_WF/Asf/fleet.yml)
- [`Resources/Prototypes/_WF/Asf/grants.yml`](../../../Resources/Prototypes/_WF/Asf/grants.yml)
- [`Resources/Prototypes/_WF/Asf/guidebook.yml`](../../../Resources/Prototypes/_WF/Asf/guidebook.yml)
- [`Resources/Prototypes/_WF/Asf/lantern.yml`](../../../Resources/Prototypes/_WF/Asf/lantern.yml)
- [`Resources/Prototypes/_WF/Asf/research.yml`](../../../Resources/Prototypes/_WF/Asf/research.yml)
- [`Resources/Prototypes/_WF/Asf/Roles/jobs.yml`](../../../Resources/Prototypes/_WF/Asf/Roles/jobs.yml)
- [`Resources/Prototypes/_WF/Asf/Roles/loadouts.yml`](../../../Resources/Prototypes/_WF/Asf/Roles/loadouts.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Asf/faction.ftl`](../../../Resources/Locale/en-US/_WF/Asf/faction.ftl)
- [`Resources/Locale/en-US/_WF/Asf/grants.ftl`](../../../Resources/Locale/en-US/_WF/Asf/grants.ftl)
- [`Resources/Locale/en-US/_WF/Asf/modsuit.ftl`](../../../Resources/Locale/en-US/_WF/Asf/modsuit.ftl)
- [`Resources/Locale/en-US/_WF/Asf/roles.ftl`](../../../Resources/Locale/en-US/_WF/Asf/roles.ftl)
- [`Resources/Locale/en-US/_WF/Asf/wardrobe.ftl`](../../../Resources/Locale/en-US/_WF/Asf/wardrobe.ftl)
- [`Resources/Locale/en-US/_WF/Asf/weapons.ftl`](../../../Resources/Locale/en-US/_WF/Asf/weapons.ftl)

### Textures

- [`Resources/Textures/_WF/Asf/Clothing/Back/backpack.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Back/backpack.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Back/duffel.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Back/duffel.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Back/modsuit.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Back/modsuit.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Back/modsuit_aegis.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Back/modsuit_aegis.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Back/satchel.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Back/satchel.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Hands/gloves.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Hands/gloves.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Hands/modsuit.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Hands/modsuit.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Hands/modsuit_aegis.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Hands/modsuit_aegis.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/beret.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/beret.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/beret_plumed.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/beret_plumed.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/cap.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/cap.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/eva.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/eva.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/hat.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/hat.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/modsuit.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/modsuit.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Head/modsuit_aegis.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Head/modsuit_aegis.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Neck/cape.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Neck/cape.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Neck/capelet.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Neck/capelet.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/eva.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/eva.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/greatcoat.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/greatcoat.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/labcoat.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/labcoat.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/modsuit.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/modsuit.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/modsuit_aegis.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/modsuit_aegis.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/parade.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/parade.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/OuterClothing/robe.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/OuterClothing/robe.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Shoes/boots.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Shoes/boots.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Shoes/modsuit.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Shoes/modsuit.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Shoes/modsuit_aegis.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Shoes/modsuit_aegis.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Uniforms/casual.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Uniforms/casual.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Uniforms/colonist.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Uniforms/colonist.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Uniforms/formal.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Uniforms/formal.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Uniforms/medic.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Uniforms/medic.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Uniforms/researcher.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Uniforms/researcher.rsi/)
- [`Resources/Textures/_WF/Asf/Clothing/Uniforms/service.rsi/`](../../../Resources/Textures/_WF/Asf/Clothing/Uniforms/service.rsi/)
- [`Resources/Textures/_WF/Asf/Effects/ship_beams.rsi/`](../../../Resources/Textures/_WF/Asf/Effects/ship_beams.rsi/)
- [`Resources/Textures/_WF/Asf/Interface/job_icons.rsi/`](../../../Resources/Textures/_WF/Asf/Interface/job_icons.rsi/)
- [`Resources/Textures/_WF/Asf/Interface/research_disciplines.rsi/`](../../../Resources/Textures/_WF/Asf/Interface/research_disciplines.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Devices/pda.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Devices/pda.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/grant_ticket.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/grant_ticket.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Misc/id_cards.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Misc/id_cards.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/ShuttleWeapons/flutter.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/ShuttleWeapons/flutter.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/ShuttleWeapons/hummingbird.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/ShuttleWeapons/hummingbird.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_pistol_mag.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_pistol_mag.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_rifle_mag.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_rifle_mag.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_slug.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_slug.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_slug_box.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/Ammunition/coil_slug_box.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/coil_pistol.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/coil_pistol.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/coil_rifle.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/coil_rifle.rsi/)
- [`Resources/Textures/_WF/Asf/Objects/Weapons/Guns/coil_rifle_inhands_32x.rsi/`](../../../Resources/Textures/_WF/Asf/Objects/Weapons/Guns/coil_rifle_inhands_32x.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/Banners/federation.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/Banners/federation.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/Banners/marine.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/Banners/marine.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/drobe.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/drobe.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/fabricator.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/fabricator.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/grant_kiosk.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/grant_kiosk.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/shipyard_screen.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/shipyard_screen.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/thruster.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/thruster.rsi/)
- [`Resources/Textures/_WF/Asf/Structures/thruster_large.rsi/`](../../../Resources/Textures/_WF/Asf/Structures/thruster_large.rsi/)

### Maps

- [`Resources/Maps/_WF/Asf/lantern.yml`](../../../Resources/Maps/_WF/Asf/lantern.yml)

### Shared maps

- [`Resources/SharedMaps/_WF/Asf/bulwark.yml`](../../../Resources/SharedMaps/_WF/Asf/bulwark.yml)
- [`Resources/SharedMaps/_WF/Asf/gleaner.yml`](../../../Resources/SharedMaps/_WF/Asf/gleaner.yml)
- [`Resources/SharedMaps/_WF/Asf/guardian.yml`](../../../Resources/SharedMaps/_WF/Asf/guardian.yml)
- [`Resources/SharedMaps/_WF/Asf/lamplighter.yml`](../../../Resources/SharedMaps/_WF/Asf/lamplighter.yml)
- [`Resources/SharedMaps/_WF/Asf/lodestar.yml`](../../../Resources/SharedMaps/_WF/Asf/lodestar.yml)
- [`Resources/SharedMaps/_WF/Asf/porter.yml`](../../../Resources/SharedMaps/_WF/Asf/porter.yml)
- [`Resources/SharedMaps/_WF/Asf/vigil.yml`](../../../Resources/SharedMaps/_WF/Asf/vigil.yml)

### Guidebook

- [`Resources/ServerInfo/_WF/Asf/Guidebook/Factions/Asf.xml`](../../../Resources/ServerInfo/_WF/Asf/Guidebook/Factions/Asf.xml)

## Non-modular edits

- [`Resources/Prototypes/_Mono/Entities/Objects/Misc/loot.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Misc/loot.yml): ASF crystal bounties
- [`Resources/Prototypes/_Mono/Guidebook/factions.yml`](../../../Resources/Prototypes/_Mono/Guidebook/factions.yml)

<!-- WOLFGATE-GENERATED END -->
