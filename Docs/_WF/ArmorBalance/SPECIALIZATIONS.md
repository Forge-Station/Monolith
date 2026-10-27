# Armour specializations

Armour values make suits sidegrades on paper. Specializations make them feel different in a fight: each suit
changes what Wolfmed does to the body under it, not just how much damage gets through. Everything below is built:
the components live in the ArmorSpecialization module (plus SoftShield and ArmorDeflection), and the faction
assignments are applied as listed.

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
| MEDUSA mk.VI shield | 0.67 / 0.34 / 0.67 / 0.34, hard shield | **Convert to soft shield** | Still a hard shield; it should follow the CV-53 and M92-X |

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
| ENFORCER MKII | 0.7 / 0.75 / 0.6 / 0.5, hard shield | **Convert to soft shield** | Another hard shield to retire |
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

## Notes

- The M92-X, CV-53 and the converted MEDUSA and ENFORCER MKII have their soft shield as their specialization.
- The sealed layer blocks chemical burns and frostbite; it doesn't change infection risk.
- Trauma gel blocks arterial cuts; it doesn't lower bleed rates on ordinary cuts.
- Wolfmed's armour events and the coverage helper are in `Content.Shared/_WF/Wolfmed/Wounds/WolfmedArmorEvents.cs`.
