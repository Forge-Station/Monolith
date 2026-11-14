#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EntityEffects.Effects;
using Content.Server.Temperature.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.EntityEffects;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The reagent audit of 2026-09-29: the medicines whose shipped effects already reach what the wound model reads, driven
/// on a real body from their own prototypes so a later change to either side shows up here.
/// </summary>
[TestFixture]
public sealed class WolfmedReagentAuditTest : GameTest
{
    /// <summary>Saline's blood restore lands in the bloodstream the life model reads its blood level from.</summary>
    [Test]
    public async Task SalineRefillsAWoundHostTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var life = SEntMan.System<WolfmedLifeSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(body);
            Assert.That(SEntMan.System<BloodstreamSystem>().TryModifyBloodLevel(body, FixedPoint2.New(-150), bloodstream));
            var low = life.GetBlood(body);
            Assert.That(low, Is.LessThan(0.6f));

            var saline = Effects("Saline").OfType<ModifyBloodLevel>().First();
            for (var tick = 0; tick < 10; tick++)
                saline.Effect(Args(body));

            FixedPoint2 max = bloodstream.BloodMaxVolume;
            Assert.That(life.GetBlood(body), Is.EqualTo(low + 10 * saline.Amount.Float() / max.Float()).Within(0.005f),
                "saline's blood never reached the blood level the brain's circulation clock reads.");
        });
    }

    /// <summary>Dexalin's airloss healing lowers what a suffocating wound host's hypoxia is read from.</summary>
    [Test]
    public async Task DexalinEasesSuffocationTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var breathing = SEntMan.System<WolfmedBreathingSystem>();
            var full = Server.CfgMan.GetCVar(WolfmedCVars.AirlossFull);
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var respirator = SEntMan.GetComponent<RespiratorComponent>(body);
#pragma warning disable RA0002
            respirator.SuffocationCycles = System.Math.Max(1, respirator.SuffocationCycleThreshold);
#pragma warning restore RA0002
            SEntMan.System<DamageableSystem>().TryChangeDamage(body,
                new DamageSpecifier { DamageDict = { ["Asphyxiation"] = FixedPoint2.New(50) } }, ignoreResistances: true);
            Assert.That(breathing.SuffocationLevel(body), Is.EqualTo(50f / full).Within(0.001f));

            var dexalin = Effects("DexalinPlus").OfType<HealthChange>()
                .First(effect => effect.Damage.DamageDict.GetValueOrDefault("Asphyxiation") < 0);
            for (var tick = 0; tick < 4; tick++)
                dexalin.Effect(Args(body));

            Assert.That(breathing.SuffocationLevel(body), Is.EqualTo((50f - 4 * 3.5f) / full).Within(0.001f),
                "dexalin plus no longer eases a suffocating patient's hypoxia.");
        });
    }

    /// <summary>Osteogen knits a simple break on a real body and leaves a comminuted one to the surgeon.</summary>
    [Test]
    public async Task OsteogenMendsASimpleBreakTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var fractures = SEntMan.System<WoundFractureSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .First(part => part.Component.PartType == BodyPartType.Arm).Id;
            var osteogen = Effects("Osteogen").OfType<MendFractures>().Single();

            // 50 Blunt clears the Comminuted line (45), whose creation chance is 1.
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(body, arm,
                new DamageSpecifier { DamageDict = { ["Blunt"] = FixedPoint2.New(50) } }));
            var fracture = fractures.GetFracture(arm)!.Value;
            Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Comminuted));
            var severity = fracture.Comp1.Severity;
            for (var tick = 0; tick < 5; tick++)
                osteogen.Effect(Args(body));
            Assert.That(fracture.Comp1.Severity, Is.EqualTo(severity), "osteogen reached a comminuted break.");

            // Down to a simple break, which a pill's worth of ticks knits and removes.
            Assert.That(wounds.ChangeSeverity(fracture.Owner, FixedPoint2.New(25) - severity));
            Assert.That(fracture.Comp2.Grade, Is.EqualTo(FractureGrade.Simple));
            for (var tick = 0; tick < 20; tick++)
                osteogen.Effect(Args(body));
            Assert.That(fractures.GetFracture(arm), Is.Null, "osteogen did not knit a simple break.");
        });
    }

    /// <summary>Leporazine's heat reaches the surface, and the Wolfmed core follows a warmer surface at once.</summary>
    [Test]
    public async Task LeporazineRewarmsTheCoreTest()
    {
        var map = await Pair.CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var core = SEntMan.System<WolfmedBodyTemperatureSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<TemperatureSystem>().ForceChangeTemperature(body, 270f);
            core.SetCore(body, 270f);
            core.Tick(body, 1f);
            Assert.That(core.GetLevels(body).ColdDown, Is.GreaterThan(0f), "270 K is hypothermic for a human.");

            var warm = Effects("Leporazine").OfType<AdjustTemperature>().Single(effect => effect.Amount > 50000);
            warm.Effect(Args(body));
            core.Tick(body, 1f);

            Assert.Multiple(() =>
            {
                Assert.That(core.GetCore(body)!.Value, Is.GreaterThan(290f), "leporazine did not rewarm the core.");
                Assert.That(core.GetLevels(body).ColdDown, Is.Zero);
            });
        });
    }

    private IReadOnlyList<EntityEffect> Effects(string reagent) =>
        SProtoMan.Index<ReagentPrototype>(reagent).Metabolisms!["Medicine"].Effects;

    private EntityEffectReagentArgs Args(EntityUid body) =>
        new(body, SEntMan, null, null, FixedPoint2.New(1), null, null, FixedPoint2.New(1));
}
