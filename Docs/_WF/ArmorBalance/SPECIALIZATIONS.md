# Armour specializations

Armour values make suits sidegrades on paper. Specializations make them feel different in a fight: each suit
changes what Wolfmed does to the body under it, not just how much damage gets through. Everything below is built:
the components live in the ArmorSpecialization module (plus SoftShield and ArmorDeflection), and the faction
assignments are applied as listed. How the code works, and how to add or port a specialization, is in
`Docs/_WF/ArmorSpecialization/GUIDE.md`.

## The toolbox

Each specialization is one small, reusable component on the suit. Most hook a point Wolfmed already has.

| Component | What it does in play | Hooks |
|---|---|---|
| **Soft shield** | Hits pass through dampened; no bleeding while up; burns ignore it | `WFSoftShield`, bleed gate |
| **Ricochet plating** | Light rounds bounce off and can hit others; AP and rifles get through | `WFArmorDeflect` |
| **Wound ward** | Refuses named wound rules outright (a "concussion ward" helmet never concusses) | a cancellable event before a rule matches, relayed to worn items, the same way the bleed gate works |
| **Bone plating** | Fractures, crush injuries and dislocations need more damage to happen | scales a rule's `minDamage` for covered parts |
| **Limb lock** | Covered limbs resist dismemberment: a higher threshold, or immune below a damage size | dismemberment threshold on the routed part |
| **Spall liner** | Rounds that would lodge or leave shrapnel make a bruise instead; no embedded objects to dig out | wound ward on the lodged-round and shrapnel rules, adds Blunt |
| **Trauma gel** | An arterial cut becomes an ordinary cut; bleed rate on covered parts is reduced | swaps the rule's wound, or a bleed-rate multiplier |
| **Auto-tourniquet** | The first arterial bleed on a limb is clamped automatically, once per charge | Wolfmed tourniquet treatment |
| **Pain dampener** | Pain from wounds is reduced, so the wearer stays up and functional longer | `WoundPainBehavior` multiplier |
| **Shock absorbers** | Blunt hits are less likely to concuss or bruise organs | wound ward or `minDamage` scale on concussion and organ contusion |
| **Sealed layer** | No chemical burns, no wound infection, frostbite resistance | wound ward on the caustic and frostbite rules, infection risk scale |
| **Insulation** | No internal burns from shock; synth wearers don't short circuit | wound ward on the internal-burn and short-circuit rules |
| **Ablative coat** | The first burn after a lull is soaked, then the coat needs time to recover | a small charge pool, like a soft shield for Heat |

Rules of thumb:
- A specialization costs budget like speed does. Strong ones, like limb lock or ricochet, go on high-tier or rule-of-cool suits.
- One headline specialization per suit, two at most. If a suit does everything, nothing feels special.
- Every specialization needs a counter, and the counter should be a weapon choice: AP rounds beat ricochet, lasers beat
  soft shields, and so on.

## By faction

Suits are grouped by faction, with the specialization that fits their role and identity. Values are the suit's
current armour (Blunt / Slash / Piercing / Heat, damage taken).

### TSF (TSFMC): disciplined, well-supplied military

| Suit | Current | Specialization | Why |
|---|---|---|---|
| M82c generalist | 0.6 / 0.6 / 0.6 / 0.7 | **Spall liner** | Standard issue that keeps marines out of surgery |
| M82b burn / environment | 0.7 / 0.65 / 0.75 / 0.4 | **Sealed layer** | Already the NBC suit; no chem burns or infection |
| M86 Mk.3 heavy | 0.5 / 0.5 / 0.5 / 0.6 | **Bone plating** | The slow tank shrugs off fractures and dislocations |
| M86 Mk.4 fast heavy | 0.65 / 0.6 / 0.6 / 0.65 | **Shock absorbers** | Moves fast, so it's built to take falls and blunt hits |
| M92-X | 0.7 / 0.7 / 0.7 / 0.6 | Soft shield | Already has its specialization |
| M92 colonel | 0.35 / 0.35 / 0.35 / 0.45 | **Wound ward (concussion)** | A commander who stays clear-headed |

### PDV (Phaethon Dynasty): raiders and boarders

| Suit | Current | Specialization | Why |
|---|---|---|---|
| SCAF burn | 0.7 / 0.7 / 0.65 / 0.45 | **Ablative coat** | Cheap, rough-and-ready laser protection |
| CV-32 anti-ballistic | 0.75 / 0.65 / 0.4 / 0.7 | **Spall liner** | Rounds stop in the plate instead of in you |
| CV-280 skirmisher | 0.7 / 0.65 / 0.6 / 0.75 | **Limb lock** (light) | A boarder who keeps their limbs in close quarters |
| CV-53 | 0.75 / 0.75 / 0.7 / 0.7 | Soft shield | Already has its specialization |
| CV-82 fast medic | 0.65 / 0.6 / 0.6 / 0.6 | **Auto-tourniquet** | The medic who doesn't bleed out while working |
| CV-67 Vizier (commander) | 0.35 / 0.35 / 0.27 / 0.4 | **Concussion ward** + **trauma gel** ("Commander's Weave") | A boarding-squad leader who stays upright and doesn't bleed out |
| Juggernaut | 0.2 across | Ricochet, plus **limb lock** | Rule of cool: nothing small gets through, nothing comes off |

### Viper Group (VG): mercenary blades and close quarters

The VG line is already strongly anti-slash (several suits take 0.34 Slash), which suits a melee identity.

| Suit | Current | Specialization | Why |
|---|---|---|---|
| BROOD mk.I initiate | 0.8 / 0.88 / 0.67 / 0.84 | none | Starter suit; its plainness makes the others read |
| JACKAL mk.II standard | 0.67 / 0.5 / 0.5 / 0.67 | **Trauma gel** | Arterial cuts become ordinary cuts |
| RIPPER mk.III medic | 0.67 / 0.34 / 0.67 / 0.4 | **Tendon ward** (wound ward on tendon cuts) | A blade-fighter's medic who keeps their hands working |
| IMP mk.IV juggernaut | 0.6 / 0.34 / 0.34 / 0.6 | **Limb lock** | A melee tank that can't be disarmed, literally |
| MEDUSA mk.VI shield | 0.67 / 0.34 / 0.67 / 0.34 | **Soft shield** (converted from a hard shield) | Follows the CV-53 and M92-X |

### USSP: cold-weather navy and commissars

| Suit | Current | Specialization | Why |
|---|---|---|---|
| L-10 / L-10-A lightsuits | 0.8 / 0.8 / 0.8 / 0.75 | **Sealed layer** (frostbite) | Light naval suits built for the cold |
| M-10 scoutsuit | 0.8 / 0.8 / 0.7 / 0.7 | **Pain dampener** | A scout who keeps moving when hit |
| L-27 tacsuit | 0.6 / 0.7 / 0.45 / 0.65 | **Spall liner** | The line infantry suit |
| L-30 / UF-16 commissar | 0.6 / 0.45 / 0.4 / 0.8 and 0.55 / 0.4 / 0.35 / 0.75 | **Wound ward (concussion)** + pain dampener | The commissar stays upright and in charge |

### Ullman Industries (UI): corporate security and engineering

| Suit | Current | Specialization | Why |
|---|---|---|---|
| Engineering hardsuit | 0.8 / 0.8 / 0.65 / 0.6 | **Insulation** + bone plating (crush) | Electrical and industrial accidents |
| Pilot voidsuit | 0.8 / 0.8 / 0.65 / 0.6 | **Shock absorbers** | Built for hard landings |
| ENFORCER | 0.5 / 0.45 / 0.4 / 0.65 | **Insulation** | Security that's ready for stun weapons and shock traps |
| ENFORCER MKII | 0.7 / 0.75 / 0.6 / 0.5 | **Soft shield** (converted from a hard shield) | Its hard shield is retired like the others |
| VK-1 experimental (Director) | 0.35 / 0.35 / 0.45 / 0.5 | **Ricochet plating** | A corporate prototype with rule-of-cool plating |

### Smaller factions

| Faction / suit | Current | Specialization | Why |
|---|---|---|---|
| MMC mining suit | 0.6 / 0.4 / 0.8 / 0.8 | **Bone plating** (crush, organ contusion) | Cave-ins and falling rock |
| MMC Kurogane tacsuit | 0.6 / 0.6 / 0.55 / 0.7 | **Tendon ward** | A blade culture's combat suit |
| Underground Godfather | 0.6 / 0.6 / 0.4 / 0.25 | **Ablative coat** | Already the best burn suit in its band |
| Underground Warlord | 0.65 / 0.6 / 0.6 / 0.65 | **Pain dampener** | A warlord who fights through it |
| UNSA armored EVA | 0.9 / 0.85 / 0.65 / 0.9 | **Sealed layer** | A vacuum suit first, armour second |
| Armed Trauma T-23 / T-53 | 0.7 / 0.7 / 0.7 / 0.8 and 0.6 / 0.6 / 0.4 / 0.55 | **Auto-tourniquet** (T-23), **trauma gel** (T-53) | Rescue teams that keep themselves alive |
| Asakim exoskeleton | 0.35 / 0.85 / 0.3 / 0.4 | **Limb lock** | An exoskeleton holds the frame together |
| Mercenary SCAF | 0.5 / 0.5 / 0.45 / 0.55 | **Spall liner** | Practical contractor gear |

## Civilian and utility suits

Every step of the build order below is built. Combat specializations decide how a suit survives a fight; civilian
ones decide what the suit is *for*: surviving the job, the environment and the accidents. They're free utility rather
than budget-costed armor, since civilian suits already have weak armor. Two passives at most, except medics; generic
and baseline suits stay light so job suits feel like an upgrade.

### New passives

| Passive | What it does | Hooks |
|---|---|---|
| **Fire-Rated** | A large burn-soak pool for Heat only, so you can work through fires | The ablative coat tuned to Heat, plus the existing `FireProtection` |
| **Rad Sink** | Takes half of incoming radiation and sheds absorbed radiation over time | `WFRadSink` |
| **Anti-Slip Treads** | You can't slip on spills, ice or banana peels | Existing `NoSlip`; slips are already relayed to worn suits |
| **EMP-Hardened** | EMPs don't touch the wearer or anything they carry | `WFEmpHardened`, checked by the EMP system before it pulses anything |
| **Wildlife Padding** | Creatures (carp, goliaths, spiders) deal much less damage: half, or 60% less for the strong version | `WFWildlifePadding`; a creature is a mob that isn't a person or a borg |
| **Emergency Stasis Weave** | Going critical holds all bleeding for 20 seconds; recharges over 5 minutes | `WFStasisWeave`, using Wolfmed's stasis hold |
| **Impact Dampers** | Throws, wall slams and ship collisions can't dislocate joints or concuss you | The wound ward, including the thrown-dislocation rule |
| **Plush Lining** | Comfort padding that dulls pain | The pain dampener |
| **Steady Hands** | Your treatments (bandaging, splinting, suturing, tourniquets) aren't interrupted by light hits | `WFSteadyHands`: triples the line in `WolfmedDoAfterInterruptSystem` |
| **Sterile Weave** | Wounds under the suit can't get infected, and wounds the wearer treats start clean | `WFSterileWeave`, answering Wolfmed's infection tick and treatment |
| **Plasma Reserve** | A refillable 90u store of universal synthetic plasma for patients, never the wearer. A "Transfuse from suit" verb runs a line to a patient within a tile, like a portable IV, shown as a silver line with an IV Connected alert. The line breaks if either moves away; one patient at a time; refilled from blood packs used on the suit | `WFPlasmaReserve`, sharing the IV drip's pack rules |
| **Ore Sense** | A built-in pulse that shows nearby ore | Existing ore scanner component |
| **Haul Frame** | Dragging bodies, crates and machines doesn't slow you down | `WFHaulFrame`: undoes the pull-speed penalty |
| **Servo Assist** | Tool work (welding, prying, unbolting) goes about 25% faster | `WFServoAssist`, through a hook in the tool system |
| **Decon Coating** | Chemicals splashed, sprayed or spilled on you don't soak in, nor smoke or foam; showers and antiseptic still work | `WFDeconCoating`, checked by the reaction system for every touch; showers and antiseptic bypass it |
| **Blast Liner** | Explosions can't take your limbs, bruise your organs or cause internal bleeding, though they still hurt | `WFBlastLiner`, answering the sever and wound-rule events for explosions only |
| **Emergency Thrusters** | One thruster burst in zero-G, the way you face; recharges over 90 seconds | `WFSuitThrusters`, an action granted while worn |

### Engineering and atmos

| Suit | Passives |
|---|---|
| Engineering hardsuit | Servo Assist + Rad Sink |
| Engineer EVA | Insulation |
| Sapper modsuit | Servo Assist + Insulation |
| Chief engineer's hardsuit | Servo Assist + EMP-Hardened |
| Atmos hardsuit | Fire-Rated + Sealed Layer |
| Atmos EVA | Fire-Rated |
| Aeolus modsuit | Fire-Rated + Sealed Layer |

### Salvage and mining

| Suit | Passives |
|---|---|
| Mining hardsuit | Ore Sense + Cave-In Rated |
| Salvage EVA | Ore Sense |
| Goliath hardsuit | Wildlife Padding (strong) + Haul Frame |
| Salvager Maxim | Fire-Rated + Blast Liner |
| Experimental salvager | Fire-Rated + Ore Sense |
| Luxury mining hardsuit | Plush Lining + Ore Sense |
| Spationaut hardsuit | Impact Dampers + Emergency Thrusters |

### Science

| Suit | Passives |
|---|---|
| Experimental research hardsuit | EMP-Hardened + Insulation |
| Minerva modsuit | EMP-Hardened + Decon Coating |
| Scientist EVA | Decon Coating |

### Medical

Every medic suit gets **Plasma Reserve**, on top of what it already has. Medics are the exception to "two
specializations at most": their extras are tools for treating others, not more protection.

| Suit | Passives |
|---|---|
| CMO hardsuit, Sanitäter modsuit | Steady Hands + Emergency Stasis Weave + Plasma Reserve |
| Paramedic voidsuit | Steady Hands + Plasma Reserve |
| Medic EVA | Sterile Weave + Plasma Reserve |
| Brigmedic hardsuit | Auto-Tourniquet + Steady Hands + Plasma Reserve |
| PDV CV-82 (faction) | Auto-Tourniquet (existing) + Steady Hands + Plasma Reserve |
| VG RIPPER (faction) | Tendon Guards (existing) + Steady Hands + Plasma Reserve |
| Armed Trauma T-23 (faction) | Auto-Tourniquet (existing) + Steady Hands + Plasma Reserve |
| Armed Trauma T-53 (faction) | Trauma Gel (existing) + Emergency Stasis Weave + Plasma Reserve |
| TSF M82b corpsman (faction) | Sealed Spall Liner (existing) + Sterile Weave + Plasma Reserve |
| Blood-red medic hardsuit | Auto-Tourniquet + Steady Hands + Plasma Reserve |

### Security

| Suit | Passives |
|---|---|
| Judge's hardsuit, Soldat modsuit, private security EVA | Spall Liner |
| Overseer's hardsuit | Spall Liner + Concussion Ward |
| Warden's hardsuit | Shock Absorbers |
| Bulwark modsuit | Bone Plating + Blast Liner |
| TSFMC EVA | Spall Liner (light) |
| Overseer EVA | Concussion Ward |

### Service and command

| Suit | Passives |
|---|---|
| Janitor EVA | Anti-Slip Treads + Decon Coating |
| Botanist EVA | Decon Coating + Tendon Guards |
| Captain's armored spacesuit | Concussion Ward + Plush Lining |
| Captain EVA | Plush Lining |
| Magnate modsuit | Concussion Ward + EMP-Hardened |
| Pilot hardsuit | Impact Dampers + Emergency Thrusters |

### Mercenaries, pirates and other factions

| Suit | Passives |
|---|---|
| Mercenary hardsuit, mercenary EVA | Spall Liner |
| Mercenary modsuit | Spall Liner + Blast Liner |
| Blood-red hardsuit | Spall Liner |
| Elite hardsuit (blood-red) | Ablative Coat |
| Commander hardsuit (blood-red) | Commander's Weave |
| Pirate captain's hardsuit | Concussion Ward + Plush Lining |
| Elite pirate hardsuit | Limb Lock (breaks at 40) |
| CCTC Century | Spall Liner |
| CCTC Centurion | Bone Plating + Blast Liner |
| RX-01 modsuit (PDV) | Spall Liner |
| Volta modsuit | Insulation |
| USSP Varyag modsuit | Bone Plating |
| USSP Zastavnik modsuit | Spall Liner |
| DME, LVHI, Arcadia, FSB EVAs | Sealed Layer (light) |

### ERT and special

| Suit | Passives |
|---|---|
| ERT leader | Concussion Ward + Steady Hands |
| ERT engineer | Servo Assist + Insulation |
| ERT medic | Steady Hands + Plasma Reserve |
| ERT security | Spall Liner + Blast Liner |
| ERT janitor | Anti-Slip Treads + Decon Coating |
| CBURN exosuit | Decon Coating + Sealed Layer |
| Death squad | Limb Lock (total) + Spall Liner |

### Baseline and rule of cool

| Suit | Passives |
|---|---|
| Basic hardsuit, deep space EVA, standard modsuit | Sealed Layer (frostbite only) |
| Clown hardsuit | **Comedic Padding** (blunt can't concuss or dislocate; hits honk) + Anti-Slip Treads |
| Mime hardsuit | **Vow of Silence** (no pain noises, muffled footsteps) |
| Santa's hardsuit | Sealed Layer (frostbite) + Plush Lining |
| Organic space suit | **Living Tissue** (small bleeds close on their own) |
| Wizard hardsuit | **Arcane Weave** (light-round ricochet) |
| Tactical maid hardsuit | **Spotless**: Decon Coating + Anti-Slip Treads + Tendon Guards |

### Build order

1. YAML-only assignments from existing components: wards, ablative, pain dampener, tourniquet, ricochet,
   `NoSlip` and the ore scanner.
2. Plasma Reserve, including treating others, since every medic suit waits on it.
3. Steady Hands, Sterile Weave and Emergency Stasis Weave.
4. EMP-Hardened, Emergency Thrusters, Servo Assist, Haul Frame, Decon Coating, Blast Liner, Wildlife Padding, Rad
   Sink and Living Tissue.
5. The clown's honk and the mime's silence.

## Notes

- The M92-X, CV-53 and the converted MEDUSA and ENFORCER MKII have their soft shield as their specialization.
- The sealed layer blocks chemical burns and frostbite; it doesn't change infection risk.
- Trauma gel blocks arterial cuts; it doesn't lower bleed rates on ordinary cuts.
- Wolfmed's armour events and the coverage helper are in `Content.Shared/_WF/Wolfmed/Wounds/WolfmedArmorEvents.cs`.
- Some suits inherit passives from their parent: the ERT medic gets the blood-red medic's set (including its
  auto-tourniquet), the ERT engineer, ERT janitor and ERT mail carrier get CBURN's decon coating (the last two its
  sealed layer as well), the experimental salvager gets the Maxim's blast liner, the ASF field modsuit gets the
  Minerva's EMP hardening and decon coating, and the Godfather, reverse-engineered blood-red and private security
  hardsuits get a spall liner.
- The mime hardsuit used to inherit from the clown hardsuit. It now inherits from the base hardsuit with the clown's
  protection copied over, so it doesn't get comedic padding, anti-slip treads or the honk.
