using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

[TestFixture]
public sealed class WolfmedHealingTargetTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    // WOLFGATE (W0): Blunt rather than Slash - a bruise pack's `treatedDamageTypes: [Blunt]` means a Slash
    // wound is no longer something it can treat at all, and the do-after this test needs would never start.
    // 6 also stays under WFWolfmedFractureProfile's Hairline threshold (12), so no stray bone fracture.
    [TestCase("Brutepack", "Blunt", false)]
    [TestCase("Brutepack", "Blunt", true)]
    [TestCase("CableApcStack", "Heat", false)]
    [TestCase("CableApcStack", "Heat", true)]
    public async Task TargetedItemsFinishTheirLimbFirstTest(string item, string damageType, bool missingPart)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid left = default, right = default;

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var mob in new[] { body, user })
            {
                entities.RemoveComponent<BarotraumaComponent>(mob);
                entities.RemoveComponent<TemperatureComponent>(mob);
            }
            var graph = entities.System<SharedBodySystem>();
            var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
            left = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Left).Single().Id;
            right = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Right).Single().Id;
            if (item == "CableApcStack")
            {
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(left), Is.True);
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(right), Is.True);
                left = entities.SpawnEntity("SpeedLeftLeg", map.GridCoords);
                right = entities.SpawnEntity("SpeedRightLeg", map.GridCoords);
                Assert.That(graph.AttachPart(torso, "left leg", left), Is.True);
                Assert.That(graph.AttachPart(torso, "right leg", right), Is.True);
            }
            var damage = new DamageSpecifier(server.ResolveDependency<IPrototypeManager>()
                .Index<DamageTypePrototype>(damageType), 6);
            var routing = entities.System<WoundDamageRoutingSystem>();
            Assert.That(routing.TryApplyPartDamage(body, left, damage, ignoreResistances: true), Is.True);
            Assert.That(routing.TryApplyPartDamage(body, right, damage, ignoreResistances: true), Is.True);
            if (missingPart)
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(left), Is.True);

            var medicine = entities.SpawnEntity(item, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, medicine), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.LeftLeg;
            var interact = new AfterInteractEvent(user, medicine, body, map.GridCoords, true);
            entities.EventBus.RaiseLocalEvent(medicine, interact);
            Assert.That(entities.GetComponent<DoAfterComponent>(user).DoAfters.Count,
                missingPart ? Is.Zero : Is.GreaterThan(0));
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.RightLeg;
        });

        await Pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>();
            var other = wounds.GetWounds(right).Sum(wound => wound.Comp.Severity.Float());
            if (missingPart)
            {
                Assert.That(other, Is.EqualTo(6f), "a missing target must not send the treatment to another leg.");
                return;
            }

            var treated = wounds.GetWounds(left).Sum(wound => wound.Comp.Severity.Float());
            Assert.Multiple(() =>
            {
                Assert.That(treated, Is.LessThan(6f),
                    "changing aim during treatment does not redirect it: the leg it started on is the one treated.");
                // SS13-style carry-on (owner request): the item moves to the next treatable part by itself, but
                // only once the part it is on is finished. The user's own aim is never what moves it.
                Assert.That(other == 6f || treated == 0f, Is.True,
                    $"the other leg ({other}) may only be touched after the first ({treated}) is done.");
            });
        });
    }
}
