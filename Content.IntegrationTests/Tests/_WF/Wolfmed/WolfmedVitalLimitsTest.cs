#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Systems;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// AUTODOC5: the three numbers that used to run away. Airloss counted past 700 on a body that cannot die of
/// it, a blood pack was spent over and over on a full bloodstream, and a repaired corpse was shocked at a
/// chance nothing on it could raise.
/// </summary>
[TestFixture]
public sealed class WolfmedVitalLimitsTest : GameTest
{
    /// <summary>Systemic airloss stops at the cap however much suffocation is thrown at the body.</summary>
    [Test]
    public async Task AirlossStopsAtTheCapTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var damage = entities.System<DamageableSystem>();
            var cap = FixedPoint2.New(config.GetCVar(WolfmedCVars.AirlossCap));

            for (var i = 0; i < 10; i++)
                damage.TryChangeDamage(body, Spec(100, "Asphyxiation"), origin: null);

            var total = entities.GetComponent<DamageableComponent>(body)
                .Damage.DamageDict.GetValueOrDefault(new ProtoId<DamageTypePrototype>("Asphyxiation"));

            Assert.That(total, Is.LessThanOrEqualTo(cap), $"asphyxiation reached {total}, the cap is {cap}.");
            Assert.That(total, Is.GreaterThan(FixedPoint2.Zero), "the body took no airloss at all.");
        });
    }

    /// <summary>A blood pack has nothing to do on a bloodstream that is already full.</summary>
    [Test]
    public async Task BloodPackIsRefusedOnAFullBloodstreamTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var pack = entities.SpawnEntity("Bloodpack", map.GridCoords);
            var healing = entities.GetComponent<HealingComponent>(pack);
            var system = entities.System<HealingSystem>();
            var damageable = entities.GetComponent<DamageableComponent>(body);

            // Residual bloodloss damage with a full bloodstream: the pack used to read that as work.
            entities.System<DamageableSystem>().TryChangeDamage(body, Spec(40, "Bloodloss"), origin: null);
            Assert.That(system.IsWoundDamaged((body, damageable), healing, null), Is.False,
                "a blood pack still had work on a patient with all their blood.");

            entities.System<BloodstreamSystem>().TryModifyBloodLevel(body, FixedPoint2.New(-100));
            Assert.That(system.IsWoundDamaged((body, damageable), healing, null), Is.True,
                "a blood pack has nothing to do on a patient who has lost blood.");
        });
    }

    /// <summary>
    /// A corpse whose brain has been repaired and whose blood is back gets the flat base chance; an arrested
    /// body still on the clock keeps the oxygenation scaling that makes speed matter.
    /// </summary>
    [Test]
    public async Task RepairedCorpseGetsTheFlatDefibChanceTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var revival = entities.System<WolfmedRevivalSystem>();
            var life = entities.System<WolfmedLifeSystem>();
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var flat = config.GetCVar(WolfmedCVars.DefibChance);

            life.SetOxygenation(body, 0f);
            var alive = revival.GetChance(body);
            Assert.That(alive, Is.LessThan(flat),
                "a body still on the arrest clock is not scaled by its oxygenation any more.");

            entities.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
            life.SetOxygenation(body, 0f);
            Assert.That(revival.GetChance(body), Is.EqualTo(flat).Within(0.001f),
                "a repaired corpse is still being shocked at the hypoxia-scaled chance.");
        });
    }

    /// <summary>
    /// The pod's promise to charge again is kept: a failed shock is followed by another, and the line the
    /// pod says matches what the paddles actually did.
    /// </summary>
    [Test]
    public async Task PodChargesAgainAfterAFailedShockTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        Entity<AutodocComponent> pod = default;
        EntityUid body = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.GridCoords);
            pod = Pod(entities, map);
            entities.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>().TryInsert(pod.Owner,
                AutodocComponent.ModuleSlotId, entities.SpawnEntity("WFAutodocDefibModule", map.GridCoords), null);
            Assert.That(entities.System<AutodocSystem>().TryInsert(pod, body), Is.True);

            // The pod shocks a dead occupant on its own tick, on whatever roll is set. A roll the chance can never
            // beat is set before the death, so the ticks below leave the patient dead and the count untouched.
            entities.System<WolfmedRevivalSystem>().ForcedRoll = 0.999f;
            pod.Comp!.DefibNext = TimeSpan.MaxValue;
            entities.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
        });

        await Pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var autodoc = entities.System<AutodocSystem>();
            var revival = entities.System<WolfmedRevivalSystem>();

            // The shock happens and fails.
            pod.Comp!.DefibNext = TimeSpan.Zero;
            Assert.That(autodoc.TryDefibrillateOccupant(pod, body), Is.False, "a hopeless roll revived the patient.");
            Assert.That(pod.Comp.DefibAttempt, Is.EqualTo(1), "the pod did not count its first shock.");
            Assert.That(Said(pod, "defib-failure"), Is.True,
                $"the pod never said it was charging again (last line '{pod.Comp.LastLine}').");

            // Second attempt, this time a roll nothing can lose.
            revival.ForcedRoll = 0f;
            pod.Comp.DefibNext = TimeSpan.Zero;
            Assert.That(autodoc.TryDefibrillateOccupant(pod, body), Is.True, "the second shock did not take.");
            Assert.That(Said(pod, "defib-success"), Is.True,
                $"the pod never said the heart was back (last line '{pod.Comp.LastLine}').");
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.False, "the patient is still dead.");

            revival.ForcedRoll = null;
        });
    }

    /// <summary>
    /// Whether the pod has said a line or is about to. "CLEAR." is Urgent and cuts in front of the Info line
    /// that follows it, so the outcome is in the queue rather than on the terminal at that instant.
    /// </summary>
    private static bool Said(Entity<AutodocComponent> pod, string line)
    {
        return pod.Comp.LastLine == Loc.GetString("wolfmed-autodoc-voice-" + line) ||
               pod.Comp.VoiceQueue.Any(request => request.Line == line);
    }

    private static Entity<AutodocComponent> Pod(IEntityManager entities, TestMapData map)
    {
        var pod = entities.SpawnEntity("WolfmedCareTestAutodoc", map.GridCoords);
        return (pod, entities.GetComponent<AutodocComponent>(pod));
    }

    private static DamageSpecifier Spec(int amount, string type) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };
}
