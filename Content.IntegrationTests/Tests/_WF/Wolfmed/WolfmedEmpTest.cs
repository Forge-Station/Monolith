using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Emp;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// An EMP burns machine body parts and leaves flesh alone: a whole chassis shares the per-body budget, a lone
/// cybernetic limb takes the per-part figure, and an organic body takes nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedEmpSystem))]
public sealed class WolfmedEmpTest : GameTest
{
    [Test]
    public async Task EmpBurnsMachinePartsOnlyTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        EntityUid ipc = default, cyborg = default, human = default, leg = default;
        await server.WaitAssertion(() =>
        {
            var graph = entities.System<SharedBodySystem>();
            ipc = entities.SpawnEntity("MobIPC", map.GridCoords);
            human = entities.SpawnEntity("MobHuman", map.GridCoords);
            cyborg = entities.SpawnEntity("MobHuman", map.GridCoords);

            var torso = graph.GetBodyChildrenOfType(cyborg, BodyPartType.Torso).Single().Id;
            var old = graph.GetBodyChildrenOfType(cyborg, BodyPartType.Leg, symmetry: BodyPartSymmetry.Left).Single().Id;
            Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(old), Is.True);
            leg = entities.SpawnEntity("SpeedLeftLeg", map.GridCoords);
            Assert.That(graph.AttachPart(torso, "left leg", leg), Is.True);

            entities.System<SharedEmpSystem>().EmpPulse(map.GridCoords, 3f, 10000f, System.TimeSpan.FromSeconds(5));
        });

        await Pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var config = server.CfgMan;
            var perPart = FixedPoint2.New(config.GetCVar(WolfmedCVars.EmpPartDamage));
            var perBody = FixedPoint2.New(config.GetCVar(WolfmedCVars.EmpBodyDamage));

            Assert.Multiple(() =>
            {
                Assert.That(Shock(entities, human), Is.EqualTo(FixedPoint2.Zero), "flesh is not a circuit.");
                Assert.That(Shock(entities, leg), Is.EqualTo(perPart), "one cybernetic limb takes the whole per-part hit.");
                Assert.That(Total(entities, ipc), Is.GreaterThan(FixedPoint2.Zero), "a chassis is all machine.");
                Assert.That(Total(entities, ipc), Is.LessThanOrEqualTo(perBody + 1), "and shares one budget per pulse.");

                // Burnt-out machine parts spark until someone fixes them; flesh never does.
                Assert.That(entities.HasComponent<Content.Server._WF.Wolfmed.Gore.WolfmedMachineSparkComponent>(ipc), Is.True);
                Assert.That(entities.HasComponent<Content.Server._WF.Wolfmed.Gore.WolfmedMachineSparkComponent>(cyborg), Is.True);
                Assert.That(entities.HasComponent<Content.Server._WF.Wolfmed.Gore.WolfmedMachineSparkComponent>(human), Is.False);
            });
        });
    }

    private static FixedPoint2 Shock(IEntityManager entities, EntityUid uid) =>
        entities.GetComponent<DamageableComponent>(uid).Damage.DamageDict
            .GetValueOrDefault(new ProtoId<DamageTypePrototype>("Shock"));

    private static FixedPoint2 Total(IEntityManager entities, EntityUid body) =>
        entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Aggregate(FixedPoint2.Zero, (sum, part) => sum + Shock(entities, part.Id));
}
