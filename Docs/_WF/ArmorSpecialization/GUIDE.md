# Armour specializations: developer guide

How suit specializations are built, how to add one, how to put one on a suit and how to port the system to another
codebase. The design (which suit gets what, and why) is in `Docs/_WF/ArmorBalance/SPECIALIZATIONS.md`; this file is
about the code.

## What a specialization is

A specialization is one small component on a worn suit that changes what happens to the wearer. It doesn't change
armour values. Most of them answer an event: Wolfmed asks worn items whether a wound, a lost limb or an infection may
happen, the engine asks them to modify damage or speed, and a few upstream systems ask before they act. A few run on a
timer instead. Every specialization:

- works only while the suit is worn in a clothing slot, not carried;
- respects the suit's armour coverage when it protects a body part (`WolfmedArmorCoverage.Covers`; an item with no
  coverage list covers every part);
- adds exactly one examine line, `[color=#8fb7d6]Name:[/color] what it does.`, from
  `Resources/Locale/en-US/_WF/ArmorSpecialization/armor-specialization.ftl`;
- keeps its numbers in data fields, so a suit can tune them in YAML.

## Where the code lives

| File | What it holds |
|---|---|
| `Content.Shared/_WF/ArmorSpecialization/WFArmorSpecializationComponents.cs` | Every component, plus `WFPlasmaLineDoAfterEvent` and `WFToolSpeedEvent` |
| `Content.Shared/_WF/ArmorSpecialization/WFArmorSpecializationSystem.cs` | Wound wards, limb locks, pain dampeners, the ablative coat, and the shared `GetWearer` helper |
| `Content.Shared/_WF/ArmorSpecialization/WFCivilianSpecializationSystem.cs` | Steady hands, sterile weave, servo assist, rad sink, wildlife padding, haul frame, blast liner, vow of silence, thrusters, EMP hardening and decon coating |
| `Content.Server/_WF/ArmorSpecialization/WFAutoTourniquetSystem.cs` | The auto-tourniquet |
| `Content.Server/_WF/ArmorSpecialization/WFPlasmaReserveSystem.cs` | The plasma reserve and its line to a patient |
| `Content.Server/_WF/ArmorSpecialization/WFSuitUpkeepSystem.cs` | Timed passives: stasis weave, rad sink shedding, living tissue, the clown's honk |
| `Content.Server/_WF/Wolfmed/Medical/WolfmedIvLineSystem.cs` | The visible IV line and the IV Connected alert, shared by IV drips and the plasma reserve |
| `Content.Shared/_WF/Wolfmed/Wounds/WolfmedArmorEvents.cs` | The Wolfmed events worn items answer, and `WolfmedArmorCoverage` |
| `Resources/Prototypes/_WF/ArmorSpecialization/actions.yml` | The thrusters action |
| `Content.IntegrationTests/Tests/_WF/ArmorSpecialization/WFArmorSpecializationTest.cs` | Tests for every specialization |

Soft shields (`Content.Shared/_WF/SoftShield`) and ricochet plating (`Content.Shared/_WF/ArmorDeflection`) are
specializations too, but are their own modules with their own READMEs.

## How a specialization takes effect

There are four patterns. Pick the first one that works.

### 1. Answer a relayed event

The body raises an event; `InventorySystem` relays it to every worn item; the suit's component answers it. This needs
no upstream edit if the event is already relayed.

```csharp
SubscribeLocalEvent<WFWoundWardComponent, InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent>>(OnWoundRuleAttempt);

private void OnWoundRuleAttempt(Entity<WFWoundWardComponent> ent, ref InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent> args)
{
    if (!WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
        return;

    if (ent.Comp.Blocks.Contains(args.Args.Wound))
        args.Args.Cancelled = true;
}
```

The relayed events in use:

| Event | Raised by | Used by |
|---|---|---|
| `WolfmedWoundRuleAttemptEvent` | `WolfmedWoundRuleSystem`, before a rule makes a wound | Wound ward, blast liner |
| `WolfmedSeverAttemptEvent` | `AmputationSystem`, before a finishing hit or blast takes a limb | Limb lock, blast liner |
| `WolfmedArmorPainReliefEvent` | `WolfmedPainReliefSystem` | Pain dampener |
| `WolfmedSteadyHandsEvent` | `WolfmedDoAfterInterruptSystem`, when a hit lands mid-treatment | Steady hands |
| `WolfmedSterileEvent` | `WolfmedInfectionSystem` (tick and treatment) | Sterile weave |
| `WolfmedPainNoiseAttemptEvent` | `PainSystem` and `EmoteOnDamageSystem`, before a pain scream | Vow of silence |
| `DamageModifyEvent` | `DamageableSystem` | Rad sink, wildlife padding, the honk, the ablative coat on non-wound hosts |
| `PartDamageModifyEvent` | `WoundDamageRoutingSystem`, per body part | The ablative coat on wound hosts |
| `RefreshMovementSpeedModifiersEvent` | `MovementSpeedModifierSystem` | Haul frame |
| `WolfmedOrganBlastAttemptEvent` | `OrganDamageSystem`, before an explosion damages a part's organs | Blast liner |
| `WFToolSpeedEvent` | `SharedToolSystem.UseTool`, `PryingSystem.StartPry` | Servo assist |

Two traps:

- **One subscriber per pair.** Only one system may subscribe a given (component, event) pair; a duplicate compiles and
  then crashes the server at startup. A new component gets its own pair, so this only bites when two features share
  one component.
- **`DamageModifyEvent` on wound hosts.** For a Wolfmed body, `DamageModifyEvent` carries the whole hit once, with its
  origin, before it is split across parts. Scale it there for anything body-wide (radiation, creature damage). Part
  armour belongs in `PartDamageModifyEvent`, and nothing should soak in both, or the hit is soaked twice.

### 2. A new Wolfmed event

When Wolfmed has no event for the moment you need, add a `[ByRefEvent] record struct ... : IInventoryRelayEvent` to
`WolfmedArmorEvents.cs` with `TargetSlots => SlotFlags.WITHOUT_POCKET`, relay it in
`Content.Shared/Inventory/InventorySystem.Relay.cs` (inside the existing marked Wolfmed block), and raise it on the
body from the Wolfmed system. Wolfmed code is under `_WF`, so that edit needs no marker. Give the event a field for
the answer (`Cancelled`, `Multiplier`, `Sterile`) and let each handler fold its answer in, `MathF.Max` for
multipliers, so two items never fight.

### 3. A marked hook in an upstream system

When the moment belongs to an upstream system with no event, add the smallest hook that asks a public method on a
`_WF` system, inside a `WOLFGATE(ArmorSpecialization)` block:

| Upstream file | Hook | Asks |
|---|---|---|
| `Content.Shared/Tools/Systems/SharedToolSystem.cs` | Raises `WFToolSpeedEvent` on the user before a tool do-after | Servo assist |
| `Content.Shared/Prying/Systems/PryingSystem.cs` | Raises `WFToolSpeedEvent` on the user before prying with a tool | Servo assist |
| `Content.Shared/Slippery/SlipperySystem.cs` | Relays `SlipAttemptEvent` to the outer clothing slot as well as the feet | Anti-slip treads |
| `Content.Server/_Onyx/Wounds/OrganDamageSystem.cs` | Raises `WolfmedOrganBlastAttemptEvent` before an explosion damages organs | Blast liner |
| `Content.Shared/Emp/SharedEmpSystem.cs` | `TryEmpEffects` returns early | `WFCivilianSpecializationSystem.ShieldsFromEmp` |
| `Content.Shared/Chemistry/ReactiveSystem.cs` | Touch reactions return early | `WFCivilianSpecializationSystem.BlocksTouch` |
| `Content.Shared/_Onyx/Wounds/AmputationSystem.cs` | Passes `IsExplosion` to `WolfmedSeverAttemptEvent` | Blast liner |
| `Content.Shared/_Onyx/Wounds/PainSystem.cs`, `Content.Server/_Onyx/Chat/EmoteOnDamageSystem.PainSounds.cs` | Raise `WolfmedPainNoiseAttemptEvent` before a scream | Vow of silence |

A check like `BlocksTouch` should leave room for deliberate exceptions. Decon coating lets reagents through that put
out fires, and showers and antiseptic spray call `BypassDecon(() => ...)` so they still wash the wearer.

### 4. A timed passive

Effects that happen over time (shedding radiation, closing small bleeds, firing on going critical) run in a server
system's `Update`, throttled to once a second. They find the wearer with `WFArmorSpecializationSystem.GetWearer(suit)`,
which returns null for a suit carried in hand. Fields that change at runtime and show in examine are
`[AutoNetworkedField]` and get `Dirty`; a `TimeSpan` cooldown is a `TimeOffsetSerializer` field with
`[AutoPausedField]`.

## Component reference

Defaults are in brackets.

| Component | Effect | Fields | Pattern |
|---|---|---|---|
| `WFWoundWard` | Refuses wounds or wound rules on covered parts, or makes a rule need a bigger hit | `blocks`, `blockRules`, `hardens` (wound to multiplier), `examine` | Relayed Wolfmed event |
| `WFLimbLock` | Covered limbs can't be severed | `breakthrough` [0: never] | Relayed Wolfmed event |
| `WFPainDampener` | Pain relief, like a mild painkiller | `relief` [15], `examine` | Relayed Wolfmed event |
| `WFAblativeCoat` | Soaks a share of one damage group or type from a recharging pool | `damageGroup` [Burn], `damageType`, `reduction` [0.5], `maxCharge` [40], `regenRate` [8], `recoveryDelay` [6s], `examine` | Relayed engine event |
| `WFAutoTourniquet` | Ties a real tourniquet on a covered limb with an arterial bleed | `maxCharges` [2], `rechargeTime` [2 min] | Timed |
| `WFPlasmaReserve` | Feeds one patient over a line; never the wearer | `maxReserve` [90], `lineRate` [3 u/s], `range` [1.5], `connectTime` [2s] | Verb, do-after, timed |
| `WFSteadyHands` | Treatment breaks only past the interrupt line times `multiplier` | `multiplier` [3] | New Wolfmed event |
| `WFSterileWeave` | Covered wounds can't go bad; wounds the wearer treats are cleaned | none | New Wolfmed event |
| `WFStasisWeave` | Holds every bleed when the wearer goes critical | `duration` [20s], `recharge` [5 min] | Timed |
| `WFServoAssist` | Tool work is faster | `multiplier` [1.25] | Marked hook |
| `WFRadSink` | Takes a share of radiation and sheds absorbed radiation | `reduction` [0.5], `shedRate` [0.25/s] | Relayed engine event, timed |
| `WFEmpHardened` | EMPs skip the wearer and all they carry | none | Marked hook |
| `WFDeconCoating` | Touch reactions skip the wearer, except extinguishing reagents | none | Marked hook |
| `WFWildlifePadding` | Creatures deal less damage | `multiplier` [0.5] | Relayed engine event |
| `WFHaulFrame` | Pulling doesn't slow the wearer | none | Relayed engine event |
| `WFBlastLiner` | Explosions can't sever covered limbs or make the listed wounds | `blocks` [organ contusion, internal bleeding] | Relayed Wolfmed events |
| `WFSuitThrusters` | One zero-G burst the way the wearer faces | `action`, `speed` [6] | Granted action |
| `WFLivingTissue` | Small, non-arterial bleeds close by themselves | `mendRate` [0.5/s] | Timed |
| `WFHonkOnHit` | Blunt hits honk | `sound`, `cooldown` [0.5s] | Relayed engine event |
| `WFVowOfSilence` | No pain noises | none | New Wolfmed event |

Upstream components reused as specializations: `NoSlip` (anti-slip treads), and `MiningScanner` with an always-on
`ItemToggle` (ore sense). Muffled footsteps for the mime are a `FootstepModifier` on the suit.

## Putting a specialization on a suit

Add the components to the suit's prototype, under one marker that names them:

```yaml
- type: entity
  parent: ClothingOuterHardsuitBase
  id: ClothingOuterHardsuitMedical
  components:
  # WOLFGATE(ArmorSpecialization): plasma reserve, steady hands and emergency stasis weave.
  - type: WFPlasmaReserve
  - type: WFSteadyHands
  - type: WFStasisWeave
  - type: Sprite
    ...
```

A marker alone on its line covers the lines below it up to the next blank line, so the block needs no `END`. On a
`_WF` prototype no marker is needed at all.

- **One ward per suit.** A suit has one `WFWoundWard`. To combine wards (a spall liner and a concussion ward), write one
  ward with both wound lists and give it a combined examine string, like `wf-armor-spec-overseer-examine`.
- **Inheritance.** A child prototype gets every component its parent has, and a component can't be removed in YAML.
  Check `parent:` chains before adding a passive, and list the children that inherit it. If a child mustn't inherit
  (the mime from the clown), re-parent it and copy over the components it needs, inside a marked block.
- **The description.** Make the suit's description agree with its passives. If it promises protection the suit
  doesn't have, rewrite it in a marked block that keeps the old line commented out.

## Adding a new specialization

1. Check that nothing upstream or in another fork layer already does it.
2. Pick the pattern above. Prefer a relayed event; add a Wolfmed event before a marked upstream hook.
3. Add the component to `WFArmorSpecializationComponents.cs`, with a one-line summary and data fields for its numbers.
   Add `[NetworkedComponent]`, and `AutoGenerateComponentState` for any field the client shows.
4. Handle it in the system that fits: `WFArmorSpecializationSystem` for wound and damage armour,
   `WFCivilianSpecializationSystem` for other event answers, a server system for anything timed or server-only.
   Check coverage for anything that protects a body part, and use `GetWearer` for anything timed.
5. Add an examine line to the locale file, in the same `[color=#8fb7d6]Name:[/color]` format.
6. Add a test to `WFArmorSpecializationTest`. Most tests equip the suit on a `MobHuman` and raise the event on the body
   by hand, which exercises the relay and the coverage check without a full fight.
7. Put it on suits, update `SPECIALIZATIONS.md`, and run `python Tools/_WF/Ci/modules.py --write`.

## The IV line

`WolfmedIvLineSystem` draws tgstation's silver IV line and shows the IV Connected alert. Anything that feeds a patient
calls `Show(source, patient, offset)` when the line goes in and `Hide(source, patient)` when it comes out. The line is
a `JointVisualsComponent` on the source, so one source draws one line. The patient keeps the alert until the last line
is out, and lines from deleted sources are dropped within a second. The sprites are
`Resources/Textures/_WF/Wolfmed/Effects/iv_line.rsi` and `Resources/Textures/_WF/Wolfmed/Interface/Alerts/iv_connected.rsi`.

## Porting to another codebase

What each part depends on:

- **Wound wards, limb locks, blast liners, steady hands, sterile weave, the stasis weave, living tissue and the
  auto-tourniquet** need Wolfmed's wound rules, amputation, infection, do-after interrupts and stasis hold, plus the
  events in `WolfmedArmorEvents.cs`. Without Wolfmed, raise an equivalent event from your own wound or medical code at
  the same moment and keep the handlers.
- **The ablative coat, rad sink, wildlife padding, haul frame, servo assist, EMP hardening, decon coating, the honk and
  the thrusters** need only upstream SS14 systems, plus the three marked hooks (tools, EMP, reactions) and the
  inventory relay. They port almost as-is. Drop the `PartDamageModifyEvent` half of the ablative coat if your codebase
  has no per-part damage.
- **The plasma reserve** needs a bloodstream and a blood pack item. It borrows `WolfmedIvDripSystem.PacksCanTreat` and
  `UnitsPerPack`; replace them with your own check of which bodies take blood.

Copy the components, the systems, the locale file and the action prototype; then re-add the relay lines and the
upstream hooks listed above, marked the way your fork marks its edits. Run the tests in
`WFArmorSpecializationTest` to confirm each piece.
