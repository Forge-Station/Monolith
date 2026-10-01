#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Destructible; // WOLFGATE: DestructibleComponent and its triggers are server-only.
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps; // WOLFGATE: SurgeryStepCompleteCheckEvent.
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8, fractureProfile lives on WolfmedBodyPartComponent.
using Content.Shared._WF.Wolfmed.Compat; // WOLFGATE: Onyx's TryDetachPart lives on WolfmedBodySystem here.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The four phase-5 species part profiles as they resolve on a real mob: routing, bleeding, pain, scarring,
/// fracture profile and the gib ceilings those profiles sit under.
/// </summary>
/// <remarks>
/// <para>PLAN5 §6.2 T-P5-1 / -4 / -5 / -6 / -7 / -8 / -19 / -21 (WP13-6).</para>
/// <para>
/// Every expected number is derived at the assertion that uses it, from the shipped prototypes:
/// <c>Resources/Prototypes/_Onyx/Wounds/wounds.yml</c> (the four <c>bodyPartProfile</c>s and the ten new
/// wounds, WP13-0) and <c>Resources/Prototypes/_WF/Wolfmed/Body/species_parts.yml</c> (the four part
/// abstracts, WP13-1). Nothing is copied from Onyx's own test suite.
/// </para>
/// <para>
/// Three standing traps from PLAN5 §6.1 shape everything here. Trap 3: bleeding is only deterministic on the
/// Slash/Piercing-family wounds and on the two mechanical wounds (<c>chance: 1</c>), never on a Blunt one.
/// Trap 5: <c>canFeelPain: false</c> REMOVES <see cref="PainComponent"/> from the part
/// (<c>WoundDamageProjectionSystem.SetupPart</c>), so its absence is asserted as absence, not as zero pain.
/// Trap 8: <c>GibPartBehavior</c> ends in <c>QueueDel</c>, so every "is/is not destroyed" assertion runs
/// after <c>Pair.RunTicksSync</c>, never in the same block that dealt the damage.
/// </para>
/// <para>
/// Damage is applied with <c>ignoreResistances: true</c> wherever an exact severity matters. The species mobs
/// carry body-level <c>damageModifierSet</c>s (IPC <c>Cold 0.2 / Heat 1.5 / Shock 2.5</c>, Slime
/// <c>Slash 1.2 / Blunt 0.6</c>, Diona <c>Slash 0.8 / Blunt 0.7</c>) and
/// <c>DamageableSystem.TryChangeDamage</c> applies them BEFORE the Wolfmed routing seam (the resistance block
/// precedes the <c>DamageDealtEvent</c> raise), so a raw hit would otherwise reach the limb scaled and every
/// literal below would be a different number per species.
/// </para>
/// </remarks>
[TestFixture]
[TestOf(typeof(WoundDamageProjectionSystem))]
public sealed class WolfmedSpeciesProfileTest : GameTest
{
    /// <summary>
    /// PLAN5 §6.2 T-P5-1. The IPC chassis profile end to end: routing creates the mechanical wound, the wound
    /// leaks from the first point, the part still feels pain (P5-D10/U1(a) — Onyx's default, kept), and it
    /// never scars or fractures.
    /// </summary>
    [Test]
    public async Task IpcPartRoutesBleedsAndFeelsPainTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            Assert.That(entities.HasComponent<WoundHostComponent>(body), Is.True,
                "PROTO Q puts `- type: WoundHost` on MobIPC; without it nothing below can route.");
            Assert.That(entities.GetComponent<WoundableComponent>(arm).Profile,
                Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("IpcBodyPartProfile")),
                "R1: WFWolfmedPartIpc must be FIRST in PartIPCBase's parent list (PLAN5 P5-D1).");

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 20), null,
                ignoreResistances: true));

            // IpcMechanicalDamageWound's `damageTypes.Slash` sets no severityMultiplier, so it takes the C#
            // default of 1 (WoundPrototype.cs) - 20 Slash is severity 20 on the nose.
            var wound = FindWound(entities, wounds, arm, "IpcMechanicalDamageWound");
            Assert.Multiple(() =>
            {
                Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)));

                // The bleed behaviour sits at WOUND level with `rate: 0.08 chance: 1` and NO minimumSeverity,
                // unlike the organic SlashWound's per-stage `minimumSeverity: 9` - guaranteed from severity 1.
                Assert.That(entities.HasComponent<WoundBleedingComponent>(wound), Is.True,
                    "a chassis wound leaks from the first point - wound-level chance: 1, no minimumSeverity.");

                // IpcBodyPartProfile never sets canFeelPain, so it keeps the C# default true (P5-D10). This is
                // the one line U1(b) would flip. Trap 5: assert the component, not the pain number.
                Assert.That(entities.HasComponent<PainComponent>(arm), Is.True,
                    "IpcBodyPartProfile leaves canFeelPain at its default - an IPC feels pain (U1(a)).");

                // scarrable: false on IpcBodyPartProfile; CreateScar checks exactly that field.
                Assert.That(entities.System<WoundScarSystem>().CreateScar(wound.Owner), Is.Null,
                    "scarrable: false - an IPC never scars.");

                // fractureProfile: null on WFWolfmedPartIpc - an IPC can never carry a fracture at all, so it
                // never gets the BrokenBones alert or WFSurgeryMendFracture.
                Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).FractureProfile, Is.Null);
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-21 (U13′(b) shipped, so this replaces T-P5-13). Onyx's <c>SiliconIpc</c> container
    /// takes the whole <c>Burn</c> group; Wolfgate's stock <c>Inorganic</c>/<c>Silicon</c> do not, which left
    /// <c>Cold</c> and <c>Caustic</c> dead — two of the seven <c>acceptedDamageTypes</c> and two of the six
    /// <c>damageTypes</c> on both mechanical wounds. <c>WFInorganicWolfmed</c> (WP13-0, wired in WP13-1)
    /// restores them on the part.
    /// </summary>
    [Test]
    public async Task IpcTakesColdAndCausticTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var host = entities.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var cyberArm = AttachCybernetic(entities, map.GridCoords, host);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<DamageableComponent>(arm).DamageContainerID,
                    Is.EqualTo("WFInorganicWolfmed"), "PROTO O(b): PartIPCBase moved off the stock Inorganic.");
                Assert.That(entities.GetComponent<DamageableComponent>(cyberArm).DamageContainerID,
                    Is.EqualTo("WFInorganicWolfmed"), "PROTO P(b): CyberneticPartBase moved off stock Silicon.");
            });

            var routing = Routing(entities);
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Caustic", 15), null, ignoreResistances: true));
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Cold", 20), null, ignoreResistances: true));

            var dict = entities.GetComponent<DamageableComponent>(arm).Damage.DamageDict;
            Assert.Multiple(() =>
            {
                // WFInorganicWolfmed = supportedGroups [Brute, Burn] + supportedTypes [Radiation], and Burn is
                // {Heat, Shock, Cold, Caustic} (Resources/Prototypes/Damage/groups.yml).
                Assert.That(dict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Caustic")),
                    Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(dict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Cold")),
                    Is.EqualTo(FixedPoint2.New(20)));

                // Both types are in IpcBodyPartProfile.acceptedDamageTypes AND in
                // IpcMechanicalDamageWound.damageTypes (Cold reopenMinimumDamage 18 severityMultiplier 1,
                // Caustic 12 / 1), so the 35 points merge into the one chassis wound.
                Assert.That(FindWound(entities, wounds, arm, "IpcMechanicalDamageWound").Comp.Severity,
                    Is.EqualTo(FixedPoint2.New(35)));
            });

            // WOLFGATE (WP13-6, correcting WP13-2's finding WP13-2-1): WP13-2 recorded that the MOB container
            // WFSiliconWolfmed supports neither Cold nor Caustic (it is stock Silicon plus the Bloodloss type),
            // and therefore that acid and cryo would wound, hurt and leak an IPC without ever moving the
            // number MobThresholds and SlowOnDamage read. MEASURED, THAT IS NOT WHAT HAPPENS.
            // WoundDamageProjectionSystem.RefreshBodyDamage projects the part total with
            // WolfmedDamageableSystem.SetDamage, which writes `dict[type] = amount` for every type in the
            // incoming spec (Content.Shared/_WF/Wolfmed/Compat/WolfmedDamageableSystem.cs) - it is a SET, not
            // a TryChangeDamage, so DamageableSystem's `if (!dict.TryGetValue(type, …)) continue;` container
            // filter is never on the path. The mob container gates what can be DEALT to the mob directly, not
            // what the projection writes. DamageChanged then recomputes TotalDamage from the whole dict, so
            // Cold and Caustic do count toward crit, death and the slow bands after all.
            // Pinned so the behaviour cannot drift silently in either direction.
            var bodyDict = entities.GetComponent<DamageableComponent>(body).Damage.DamageDict;
            Assert.Multiple(() =>
            {
                Assert.That(bodyDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Caustic")),
                    Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(bodyDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Cold")),
                    Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage,
                    Is.EqualTo(FixedPoint2.New(35)),
                    "so an IPC really can be killed with acid and cryogenics, exactly as in Onyx.");
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-4. A cybernetic limb on an ORGANIC body: the one profile with
    /// <c>canFeelPain: false</c>, a halved bleed, and a frame fracture instead of a bone one.
    /// <c>JawsOfLifeLeftArm</c> is used because every <c>*CyberneticBase</c> id is <c>abstract: true</c>
    /// (PLAN5 §1.3 row 1 / E8) — it is the only concrete cybernetic ARM in the tree.
    /// </summary>
    [Test]
    public async Task CyberneticLimbBleedsHalfFeelsNoPainAndFramefracturesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // The literals below are Onyx's unscaled rates; wolfmed.bleed_rate slows every bleed in normal play.
        var bleedRateBefore = server.CfgMan.GetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate);
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate, 1f));
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var arm = AttachCybernetic(entities, map.GridCoords, body);

            Assert.Multiple(() =>
            {
                Assert.That(entities.GetComponent<WoundableComponent>(arm).Profile,
                    Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("CyberneticBodyPartProfile")),
                    "a prosthetic on a human host takes the cybernetic profile, not the host's organic one.");

                // R9: the two fields must agree, or WoundFractureSystem asks for a wound the bodyPartProfile
                // does not support and the fracture silently never appears (trap 6).
                Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).FractureProfile,
                    Is.EqualTo(new ProtoId<FractureProfilePrototype>("CyberneticFractureProfile")));

                // canFeelPain: false is set by this profile and no other (wounds.yml
                // CyberneticBodyPartProfile). Trap 5: SetupPart RemComp<PainComponent>s the part.
                Assert.That(entities.HasComponent<PainComponent>(arm), Is.False,
                    "a cybernetic limb is numb - canFeelPain: false removes PainComponent outright.");
            });

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 20), null,
                ignoreResistances: true));
            var wound = FindWound(entities, wounds, arm, "CyberneticMechanicalDamageWound");
            Assert.Multiple(() =>
            {
                Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(entities.System<WoundScarSystem>().CreateScar(wound.Owner), Is.Null,
                    "scarrable: false - a prosthetic never scars.");
            });

            // The bleed multiplier is read off a wound created directly on a SECOND, undamaged prosthetic:
            // damage-created bleeding is immediately reduced by the host's DamageBleedModifiers
            // (WoundBleedingSystem.HandlePartDamageApplied), which would make the multiplier unreadable.
            // Derivation - WoundBleedingSystem.RefreshWound sets
            //   BaseRate = BleedingSeverity * behavior.Rate * profile.BleedingMultiplier
            // and CurrentRate = BaseRate (NaturalClotting is 0 on a wound created this tick). With
            // CyberneticMechanicalDamageWound's wound-level `rate: 0.08` and CyberneticBodyPartProfile's
            // `bleedingMultiplier: 0.5`: 40 * 0.08 * 0.5 = 1.6. The same wound shape on an IPC chassis
            // (IpcBodyPartProfile, bleedingMultiplier: 1) would give 3.2 - exactly twice.
            var second = AttachCybernetic(entities, map.GridCoords,
                entities.SpawnEntity("MobHuman", map.GridCoords));
            var bleedWound = wounds.CreateOrMergeWound(second, "CyberneticMechanicalDamageWound", 40);
            Assert.That(bleedWound, Is.Not.Null);
            Assert.That(entities.GetComponent<WoundBleedingComponent>(bleedWound!.Value).CurrentRate,
                Is.EqualTo(40f * 0.08f * 0.5f).Within(0.0001f),
                "bleedingMultiplier: 0.5 - a prosthetic leaks at half the rate of an IPC chassis.");

            // P2-D23 carries to CyberneticFractureProfile unchanged (its grade table is byte-identical to the
            // organic one): only a 75-Blunt hit is deterministic - Comminuted threshold 60, creationChance 1.
            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Blunt", 75), null,
                ignoreResistances: true));
            var fracture = entities.System<WoundFractureSystem>().GetFracture(arm);
            Assert.That(fracture, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(fracture!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));
                Assert.That(fracture.Value.Comp1.Prototype,
                    Is.EqualTo(new ProtoId<WoundPrototype>("CyberneticFrameFractureWound")),
                    "trap 6: a steel limb cracks its frame, not a bone.");
            });
        });
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate, bleedRateBefore));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-8. Resolves <c>species.md</c> T3 empirically: phase 4's <c>WFSurgeryMendFracture</c>
    /// chain is profile-agnostic, so the bone-setter/bone-gel ladder mends a steel frame with no capability
    /// gate anywhere — thematically odd, mechanically correct.
    /// </summary>
    [Test]
    public async Task CyberneticFrameFractureIsMendableBySurgeryTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var arm = AttachCybernetic(entities, map.GridCoords, body);
            var fractures = entities.System<WoundFractureSystem>();
            // WolfmedWoundSurgeryTest's two bare effect entities. [TestPrototypes] is one global pool across
            // the whole suite (PLAN2 §4 rule 3), so they are reused rather than redeclared.
            var setBone = entities.SpawnEntity("WolfmedStepSetBone", map.GridCoords);
            var mendBone = entities.SpawnEntity("WolfmedStepMendBone", map.GridCoords);

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Blunt", 75), null,
                ignoreResistances: true));
            var fracture = fractures.GetFracture(arm);
            Assert.That(fracture, Is.Not.Null);
            Assert.That(fracture!.Value.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));

            // CyberneticFractureProfile's reductionMinimumGrade is Simple, identical to the organic profile's,
            // so a Comminuted frame can be reduced.
            RaiseStep(entities, setBone, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(fracture.Value.Comp2.Treatment, Is.EqualTo(FractureTreatment.Reduced));
                Assert.That(StepIncomplete(entities, setBone, body, arm), Is.False,
                    "the completion check must not stall on a frame fracture (P4-D20 behaves identically).");
            });

            // removeWoundWhenMended: true on CyberneticFractureProfile, same as the organic one.
            RaiseStep(entities, mendBone, body, arm);
            Assert.Multiple(() =>
            {
                Assert.That(fractures.GetFracture(arm), Is.Null,
                    "removeWoundWhenMended: true deletes the frame fracture outright.");
                Assert.That(StepIncomplete(entities, mendBone, body, arm), Is.False);
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-19 (U15(a) shipped). Before WP13-1 <c>CyberneticPartBase</c> declared no
    /// <c>Destructible</c>, so every concrete limb took its OTHER parent's — Mono's
    /// <c>MajorLimb</c>/<c>MinorLimb</c> — and a steel prosthetic at Heat 250 spawned <c>Ash</c>, ran
    /// <c>BurnBodyBehavior</c> and played the flesh <c>MeatLaserImpact</c> sound (PLAN5 R12). PROTO P(c)
    /// replaces that list with Blunt 190 / Slash 210 and no Heat rung.
    /// </summary>
    [Test]
    public async Task CyberneticLimbGibCeilingTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var slashed = EntityUid.Invalid;
        var burned = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            slashed = AttachCybernetic(entities, map.GridCoords, body);

            // Structural half. CyberneticPartBase's block is first in every concrete limb's parent list and
            // `thresholds` is a plain non-Always DataField, so the child list REPLACES MajorLimb's three rungs
            // wholesale rather than merging with them.
            var triggers = entities.GetComponent<DestructibleComponent>(slashed).Thresholds
                .Select(threshold => threshold.Trigger)
                .OfType<DamageTypeTrigger>()
                .ToDictionary(trigger => trigger.DamageType, trigger => trigger.Damage);
            Assert.Multiple(() =>
            {
                Assert.That(triggers, Has.Count.EqualTo(2), "two rungs only: no Heat/Ash rung (U15(a)).");
                Assert.That(triggers["Blunt"], Is.EqualTo(190));
                Assert.That(triggers["Slash"], Is.EqualTo(210));
                Assert.That(triggers.ContainsKey("Heat"), Is.False,
                    "a steel prosthetic must no longer burn to Ash with a flesh sound (PLAN5 R12).");
            });

            // (i) 140 pure Slash: under the 210 rung, and over WFWolfmedBaseLeftArm's Slash amputation threshold
            // of 130 (_WF/Wolfmed/Body/parts.yml - P5-D7 keeps cybernetic limbs on the organic set), so the
            // limb survives and AmputationSystem arms it. Trap 7: arming takes only the crossing hit; severing
            // needs a second, finishing one.
            Assert.That(Routing(entities).TryApplyPartDamage(body, slashed, Spec("Slash", 140), null,
                ignoreResistances: true));

            // (ii) Heat 260 on a fresh prosthetic, on its own host: above the OLD inherited MajorLimb Heat rung
            // of 250, and nothing must happen, because U15(a) deleted that rung.
            var host = entities.SpawnEntity("MobHuman", map.GridCoords);
            burned = AttachCybernetic(entities, map.GridCoords, host);
            Assert.That(Routing(entities).TryApplyPartDamage(host, burned, Spec("Heat", 260), null,
                ignoreResistances: true));
        });

        // Trap 8: GibPartBehavior ends in QueueDel, which is only flushed on a tick.
        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(slashed), Is.False, "Slash 140 is below the Slash rung of 210.");
                Assert.That(entities.GetComponent<WoundableComponent>(slashed).Severable, Is.True,
                    "Slash 140 >= the arm's inherited Slash amputation threshold of 130.");
                Assert.That(entities.Deleted(burned), Is.False,
                    "Heat 260 clears MajorLimb's old 250 Ash rung, which U15(a) removed - the limb survives.");
            });
        });
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-5. Slime: the organic wound shapes minus scarring and minus the
    /// <c>minimumSeverity</c> gate on bleeding, and no bones at all.
    /// </summary>
    [Test]
    public async Task SlimePartRoutesBleedsSoonerAndNeverFracturesTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobSlimePerson", map.GridCoords);
            var control = entities.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

            // R1: WFWolfmedPartSlime must be FIRST in PartSlime's parent list, or Base<Slot>'s organic block wins
            // and slimes get bones.
            Assert.That(entities.GetComponent<WoundableComponent>(arm).Profile,
                Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("SlimeBodyPartProfile")));

            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Slash", 10), null,
                ignoreResistances: true));
            var wound = FindWound(entities, wounds, arm, "SlimeSlashWound");
            Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(10)),
                "SlimeSlashWound's Slash severityMultiplier is 1, exactly as SlashWound's.");

            // The single mechanical difference between the slime set and the organic one: SlashWound carries
            // `minimumSeverity: 9` on all four stage bleeding blocks and SlimeSlashWound carries none, so a
            // slime leaks from the first scratch. Severity 8 sits under the organic gate and over nothing.
            var slimeMinor = wounds.CreateOrMergeWound(
                Part(entities, body, BodyPartType.Leg, BodyPartSymmetry.Left), "SlimeSlashWound", 8);
            var humanMinor = wounds.CreateOrMergeWound(
                Part(entities, control, BodyPartType.Leg, BodyPartSymmetry.Left), "SlashWound", 8);
            Assert.That(slimeMinor, Is.Not.Null);
            Assert.That(humanMinor, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<WoundBleedingComponent>(slimeMinor!.Value), Is.True,
                    "no minimumSeverity on SlimeSlashWound - a slime bleeds at severity 8.");
                Assert.That(entities.HasComponent<WoundBleedingComponent>(humanMinor!.Value), Is.False,
                    "SlashWound's minimumSeverity: 9 - the organic control does not, at the same severity.");

                // SlimeBodyPartProfile leaves canFeelPain unset; only the cybernetic profile sets it false.
                Assert.That(entities.HasComponent<PainComponent>(arm), Is.True);
                // scarrable: false, and SlimeBodyPartProfile does not even list MedicalScarWound among its
                // supportedWounds - a slime never scars, including from surgery.
                Assert.That(entities.System<WoundScarSystem>().CreateScar(wound.Owner), Is.Null);
                // fractureProfile: null on WFWolfmedPartSlime.
                Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).FractureProfile, Is.Null);
            });

            // 75 Blunt is the hit guaranteed to fracture an organic limb (P2-D23). On a slime it must do
            // nothing of the kind: WoundFractureSystem.TryGetProfile returns false with a null fractureProfile.
            Assert.That(Routing(entities).TryApplyPartDamage(body, arm, Spec("Blunt", 75), null,
                ignoreResistances: true));
            Assert.That(entities.System<WoundFractureSystem>().GetFracture(arm), Is.Null,
                "a slime can never fracture, at any Blunt amount.");
        });
    }

    /// <summary>PLAN5 §6.2 T-P5-7. The one number that separates slime bleeding from organic bleeding.</summary>
    [Test]
    public async Task SlimeBleedsFifteenPercentFasterTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        // The literals below are Onyx's unscaled rates; wolfmed.bleed_rate slows every bleed in normal play.
        var bleedRateBefore = server.CfgMan.GetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate);
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate, 1f));
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var slime = entities.SpawnEntity("MobSlimePerson", map.GridCoords);
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var wounds = entities.System<WoundSystem>();

            // Equal severity, same stage (Minor spans severity 0-24) and the same per-stage `rate: 0.1` on both
            // prototypes, so the only difference left is bodyPartProfile.bleedingMultiplier: 1.15 vs 1.0.
            // Created directly rather than by damage: HandlePartDamageApplied immediately reduces a
            // damage-created bleed by the body's DamageBleedModifiers, which differ per species.
            var slimeWound = wounds.CreateOrMergeWound(
                Part(entities, slime, BodyPartType.Arm, BodyPartSymmetry.Left), "SlimeSlashWound", 20);
            var humanWound = wounds.CreateOrMergeWound(
                Part(entities, human, BodyPartType.Arm, BodyPartSymmetry.Left), "SlashWound", 20);
            Assert.That(slimeWound, Is.Not.Null);
            Assert.That(humanWound, Is.Not.Null);

            var slimeRate = entities.GetComponent<WoundBleedingComponent>(slimeWound!.Value).CurrentRate;
            var humanRate = entities.GetComponent<WoundBleedingComponent>(humanWound!.Value).CurrentRate;
            Assert.Multiple(() =>
            {
                // BaseRate = BleedingSeverity * behavior.Rate * bleedingMultiplier, minus NaturalClotting,
                // which is 0 on a wound created this tick (WoundBleedingSystem.RestartAutomaticClotting).
                Assert.That(humanRate, Is.EqualTo(20f * 0.1f * 1.0f).Within(0.0001f));
                Assert.That(slimeRate, Is.EqualTo(20f * 0.1f * 1.15f).Within(0.0001f));
                Assert.That(slimeRate / humanRate, Is.EqualTo(1.15f).Within(0.0001f),
                    "SlimeBodyPartProfile's bleedingMultiplier: 1.15 against OrganicBodyPartProfile's 1.0.");
            });
        });
        await server.WaitPost(() => server.CfgMan.SetCVar(Content.Shared._WF.Wolfmed.CCVar.WolfmedCVars.BleedRate, bleedRateBefore));
    }

    /// <summary>
    /// PLAN5 §6.2 T-P5-6. Diona: the only non-organic profile that scars, and the only species whose limbs
    /// <c>AmputationSystem</c> never touches. The Slash total is deliberately kept under the inherited
    /// <c>MajorLimb</c> Slash rung of 210 — the destruction case is T-P5-20, in
    /// <c>WolfmedSpeciesSpawnTest</c>.
    /// </summary>
    [Test]
    public async Task DionaPartScarsAndIsNeverSeverableTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var arm = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobDiona", map.GridCoords);
            var wounds = entities.System<WoundSystem>();
            var routing = Routing(entities);
            arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            var woundable = entities.GetComponent<WoundableComponent>(arm);

            Assert.Multiple(() =>
            {
                Assert.That(woundable.Profile,
                    Is.EqualTo(new ProtoId<BodyPartProfilePrototype>("PlantBodyPartProfile")));
                Assert.That(entities.GetComponent<MetaDataComponent>(arm).EntityPrototype?.ID,
                    Is.EqualTo("LeftArmDiona"));

                // R2: `amputationThresholds: {}` is a PRESENT key, so it suppresses Base<Slot>'s dict rather
                // than inheriting it. A typo (`amputationThresholds:` with nothing after it) parses as null,
                // not as an empty dict, and would silently inherit the organic set instead.
                Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).AmputationThresholds, Is.Empty,
                    "a plant limb is never severed by damage (Onyx parity - OrganDionaExternal).");
                Assert.That(entities.System<WolfmedBodyPartSystem>().Get(arm).FractureProfile, Is.Null,
                    "no bones on a plant.");
            });

            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 20), null, ignoreResistances: true));
            var wound = FindWound(entities, wounds, arm, "PlantSlashWound");
            Assert.Multiple(() =>
            {
                Assert.That(wound.Comp.Severity, Is.EqualTo(FixedPoint2.New(20)));
                Assert.That(entities.HasComponent<PainComponent>(arm), Is.True);

                // PlantBodyPartProfile is the one new profile that leaves `scarrable` unset (C# default true)
                // AND lists MedicalScarWound in supportedWounds - CreateScar needs both.
                Assert.That(entities.System<WoundScarSystem>().CreateScar(wound.Owner), Is.Not.Null,
                    "diona are the only non-organic species that scar.");
                Assert.That(woundable.Severable, Is.False);
            });

            // Slash 140 total (20 + 120) clears an arm's organic Slash threshold of 130 and stays under the
            // 210 gib rung; then the phase-3 finishing hit (14 Piercing, DECISIONS §8.6-1). Neither can do
            // anything: AmputationSystem.HandlePartDamageApplied early-returns on an empty threshold dict.
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Slash", 120), null, ignoreResistances: true));
            Assert.That(routing.TryApplyPartDamage(body, arm, Spec("Piercing", 14), null, ignoreResistances: true));
        });

        await Pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.Deleted(arm), Is.False, "Slash 140 is under the inherited rung of 210.");
                Assert.That(entities.GetComponent<WoundableComponent>(arm).Severable, Is.False,
                    "an empty amputationThresholds dict keeps Severable false at every damage level.");
                Assert.That(entities.System<SharedBodySystem>().BodyHasChild(body, arm), Is.True,
                    "and the limb is still attached.");
            });
        });
    }

    /// <summary>
    /// Detaches the body's own left arm and bolts on a <c>JawsOfLifeLeftArm</c>, the only concrete cybernetic
    /// arm in the tree. <c>WolfmedBodyPartLifecycleSystem.OnPartAdded</c> runs
    /// <c>WoundDamageProjectionSystem.SetupPart</c> on the new limb, which is what applies the profile.
    /// </summary>
    private static EntityUid AttachCybernetic(IEntityManager entities, EntityCoordinates coords, EntityUid body)
    {
        var graph = entities.System<SharedBodySystem>();
        var torso = Part(entities, body, BodyPartType.Torso, null);
        var organic = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);

        Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(organic));
        var cybernetic = entities.SpawnEntity("JawsOfLifeLeftArm", coords);
        Assert.That(graph.AttachPart(torso, "left arm", cybernetic), Is.True,
            "JawsOfLifeLeftArm is an Arm/Left part, so the human left-arm slot accepts it.");
        return cybernetic;
    }

    private static EntityUid Part(
        IEntityManager entities,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry? symmetry)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type &&
                            (symmetry is not { } wanted || part.Component.Symmetry == wanted))
            .Id;
    }

    private static WoundDamageRoutingSystem Routing(IEntityManager entities) =>
        entities.System<WoundDamageRoutingSystem>();

    private static Entity<WoundComponent> FindWound(
        IEntityManager entities,
        WoundSystem wounds,
        EntityUid part,
        string prototype)
    {
        return wounds.GetWounds((part, entities.GetComponent<WoundableComponent>(part)))
            .Single(wound => wound.Comp.Prototype == new ProtoId<WoundPrototype>(prototype));
    }

    private static void RaiseStep(IEntityManager entities, EntityUid step, EntityUid body, EntityUid part)
    {
        // WOLFGATE: WG's SurgeryStepEvent is a 5-tuple (Onyx's is 4); none of the Wolfmed handlers reads User
        // or Surgery, so the body stands in for the surgeon and the step entity for the surgery singleton.
        var ev = new SurgeryStepEvent(body, body, part, new List<EntityUid>(), step);
        entities.EventBus.RaiseLocalEvent(step, ref ev);
    }

    private static bool StepIncomplete(IEntityManager entities, EntityUid step, EntityUid body, EntityUid part)
    {
        var ev = new SurgeryStepCompleteCheckEvent(body, part, step);
        entities.EventBus.RaiseLocalEvent(step, ref ev);
        return ev.Cancelled;
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
