using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._EinsteinEngines.Silicon.WeldingHealing;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Tag;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

[TestFixture]
public sealed class WolfmedWeldingRepairTest : GameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    [TestCase("MobHuman", "SpeedLeftLeg", TargetBodyPart.LeftLeg)]
    [TestCase("MobHuman", "SpeedRightLeg", TargetBodyPart.RightLeg)]
    [TestCase("MobIPC", null, TargetBodyPart.LeftLeg)]
    [TestCase("MobHuman", null, TargetBodyPart.LeftLeg)]
    public async Task WelderRepairsSelectedMechanicalLimbTest(string prototype, string replacement, TargetBodyPart target)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, part = default, tool = default;
        var mechanical = replacement != null || prototype == "MobIPC";
        var fuelBefore = 0f;
        Content.Shared.DoAfter.DoAfter repair = null;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity(prototype, map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.RemoveComponent<BarotraumaComponent>(body);
            entities.RemoveComponent<BarotraumaComponent>(user);
            entities.RemoveComponent<TemperatureComponent>(body);
            entities.RemoveComponent<TemperatureComponent>(user);
            var graph = entities.System<SharedBodySystem>();
            var (type, symmetry) = graph.ConvertTargetBodyPart(target);
            part = graph.GetBodyChildrenOfType(body, type, symmetry: symmetry).Single().Id;
            if (replacement != null)
            {
                var torso = graph.GetBodyChildrenOfType(body, BodyPartType.Torso).Single().Id;
                Assert.That(entities.System<WolfmedBodySystem>().TryDetachPart(part), Is.True);
                part = entities.SpawnEntity(replacement, map.GridCoords);
                var slot = target == TargetBodyPart.LeftLeg ? "left leg" : "right leg";
                Assert.That(graph.AttachPart(torso, slot, part), Is.True);
            }

            var slash = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Slash");
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, part, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            Assert.That(entities.System<WoundSystem>().GetWounds(part).Any(), Is.True);
            if (mechanical)
            {
                entities.System<DamageableSystem>().TryChangeDamage(body, new DamageSpecifier(slash, -5),
                    true, false, origin: body, targetPart: target,
                    originFlag: DamageableSystem.DamageOriginFlag.PassiveRecovery);
                Assert.That(entities.GetComponent<DamageableComponent>(part).TotalDamage.Float(), Is.EqualTo(15f),
                    "Mechanical limbs must still reject passive regeneration while accepting tool repair.");
            }
            if (replacement != null)
                Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.GreaterThan(0f));

            tool = entities.SpawnEntity("Welder", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(tool, user), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = target;
            fuelBefore = entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float();
            var interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            if (mechanical)
            {
                Assert.That(interact.Handled, Is.True);
                repair = entities.GetComponent<DoAfterComponent>(user).DoAfters.Values.Single();
            }
            // Changing the aiming selector must not redirect an in-progress repair onto flesh.
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.Torso;
        });

        await Pair.RunSeconds(4);

        await server.WaitAssertion(() =>
        {
            var wounds = entities.System<WoundSystem>().GetWounds(part).ToList();
            if (!mechanical)
            {
                Assert.That(wounds, Is.Not.Empty, "Welding must not heal an organic leg.");
                return;
            }

            Assert.That(repair.Cancelled, Is.False, "The test repair was interrupted before it could finish.");
            Assert.That(repair.Completed, Is.True, $"Repair delay: {repair.Args.Delay}");
            Assert.That(wounds, Is.Empty, "The actual tool interaction must close the mechanical wound.");
            Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.Zero);
            Assert.That(entities.GetComponent<DamageableComponent>(part).TotalDamage.Float(), Is.Zero);
            Assert.That(entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float(),
                Is.LessThanOrEqualTo(fuelBefore - 5), "A successful repair must consume its fuel cost.");
        });
    }

    /// <summary>
    /// Playtest 5: "welding a chassis breach doesn't seem to fix it" and "an IPC used to heal with the nanite
    /// applicator". The analyzer says weld the fluid leak without saying where, so the tool finds the breach with the
    /// aiming doll left on the chest; the applicator lists the IPC part container again; and a chassis repairs itself
    /// with either, at the self-repair delay. A synth's parts sit in the same container (playtest 5: they were in
    /// BasePart's Inorganic, which no repair tool lists, so a synth heard "nothing needs the welder").
    /// </summary>
    [TestCase("Welder", false, TargetBodyPart.Torso, "MobIPC")]
    [TestCase("Welder", true, TargetBodyPart.Torso, "MobIPC")]
    [TestCase("NaniteApplicator", false, TargetBodyPart.RightLeg, "MobIPC")]
    [TestCase("NaniteApplicator", true, TargetBodyPart.Torso, "MobIPC")]
    [TestCase("Welder", true, TargetBodyPart.Torso, "MobSynth")]
    [TestCase("NaniteApplicator", false, TargetBodyPart.RightLeg, "MobSynth")]
    public async Task ToolFindsTheBreachWhereverTheDollAimsTest(string toolId, bool self, TargetBodyPart aim, string mobId)
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, leg = default, tool = default;
        var passes = toolId == "NaniteApplicator" ? 2 : 1;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity(mobId, map.GridCoords);
            var user = self ? body : entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.RemoveComponent<BarotraumaComponent>(body);
            entities.RemoveComponent<TemperatureComponent>(body);
            entities.RemoveComponent<BarotraumaComponent>(user);
            entities.RemoveComponent<TemperatureComponent>(user);
            var graph = entities.System<SharedBodySystem>();
            leg = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Right).Single().Id;
            var slash = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Slash");
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            Assert.That(entities.System<WoundSystem>().GetWounds(leg).Any(), Is.True, "no breach to weld.");
            Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.GreaterThan(0f), "the breach does not leak.");

            tool = entities.SpawnEntity(toolId, map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            if (entities.HasComponent<Content.Shared.Item.ItemToggle.Components.ItemToggleComponent>(tool))
                Assert.That(entities.System<ItemToggleSystem>().TryActivate(tool, user), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = aim;
            var interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            Assert.That(interact.Handled, Is.True, "the tool did not start on the breach.");
        });

        // The tool's delay, three times over for self-repair, once per pass it takes to close the breach.
        await Pair.RunSeconds((self ? 9 : 3) * passes + 2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<WoundSystem>().GetWounds(leg).ToList(), Is.Empty, "the breach is still open.");
            Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.Zero, "the leak did not stop.");
        });
    }

    /// <summary>
    /// Playtest 5 (Peter): "welding will infinitely go on if you don't do it in surgery mode". A round lodged in a
    /// breach refuses every repair, and the welder counted the breach as work and repeated its pass until the tank was
    /// empty, never closing the leak. Now it refuses up front and names the obstacle; with the round out it closes the
    /// breach in one pass and stops.
    /// </summary>
    [Test]
    public async Task WelderStopsOnALodgedRoundAndSaysSoTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid body = default, user = default, leg = default, tool = default;
        var fuelBefore = 0f;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobIPC", map.GridCoords);
            user = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.RemoveComponent<BarotraumaComponent>(body);
            entities.RemoveComponent<TemperatureComponent>(body);
            entities.RemoveComponent<BarotraumaComponent>(user);
            entities.RemoveComponent<TemperatureComponent>(user);
            var graph = entities.System<SharedBodySystem>();
            var wounds = entities.System<WoundSystem>();
            var embedded = entities.System<WolfmedEmbeddedObjectSystem>();
            leg = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Right).Single().Id;
            var slash = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Slash");
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            var breaches = wounds.GetWounds(leg).Select(wound => wound.Owner).ToList();
            Assert.That(breaches, Is.Not.Empty, "no breach to weld.");
            foreach (var breach in breaches)
                Assert.That(embedded.Add(breach, "WFWolfmedSpentRound", 1, 1), Is.EqualTo(1));

            tool = entities.SpawnEntity("Welder", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(tool, user), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.RightLeg;
            fuelBefore = entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float();

            var welder = entities.GetComponent<WeldingHealingComponent>(tool).Damage;
            Assert.That(wounds.GetHealingPotential(leg, welder), Is.EqualTo(FixedPoint2.Zero),
                "a breach with a round in it still counts as something to weld.");

            var interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            Assert.Multiple(() =>
            {
                Assert.That(interact.Handled, Is.True, "the welder passed a chassis repair attempt on to the fire.");
                Assert.That(entities.GetComponent<DoAfterComponent>(user).DoAfters, Is.Empty,
                    "a repair pass was queued on a breach with a round in it.");
            });

            // The round comes out; the breach is welded in one pass and the welder stops.
            foreach (var breach in breaches)
                Assert.That(embedded.TryTakeOne(breach, out _), Is.True);
            Assert.That(wounds.GetHealingPotential(leg, welder), Is.GreaterThan(FixedPoint2.Zero));
            interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            Assert.That(interact.Handled, Is.True, "the welder did not start once the round was out.");
        });

        await Pair.RunSeconds(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entities.System<WoundSystem>().GetWounds(leg).ToList(), Is.Empty, "the breach is still open.");
                Assert.That(entities.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.Zero, "the leak did not stop.");
                Assert.That(entities.GetComponent<DoAfterComponent>(user).DoAfters.Values.All(pass => pass.Completed || pass.Cancelled),
                    Is.True, "the welder is still going.");
                // A lit welder burns fuel on its own, so the pass count is the do-after check above; this is only "the pass was paid".
                Assert.That(entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float(),
                    Is.LessThanOrEqualTo(fuelBefore - 5), "a successful pass must spend its fuel.");
            });
        });
    }

    /// <summary>
    /// Playtest 5: "the nanite applicator spams 40 messages in one tick". An admin ghost's do-afters are instant, so a
    /// repair chain re-entered OnWoundRepairFinished from inside its own StartWoundRepair, one pass, one sound and one
    /// line at a time, forty deep for a bad torso. The frame that started the chain now applies the instant passes
    /// itself: the whole chain lands in the click, pays every pass, and leaves no do-after behind.
    /// </summary>
    [Test]
    public async Task InstantRepairChainLandsInOneClickTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.RemoveComponent<BarotraumaComponent>(body);
            entities.RemoveComponent<TemperatureComponent>(body);
            entities.RemoveComponent<BarotraumaComponent>(user);
            entities.RemoveComponent<TemperatureComponent>(user);
            entities.System<TagSystem>().AddTag(user, "InstantDoAfters");
            var graph = entities.System<SharedBodySystem>();
            var leg = graph.GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Right).Single().Id;
            var slash = server.ResolveDependency<IPrototypeManager>().Index<DamageTypePrototype>("Slash");
            Assert.That(entities.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leg, new DamageSpecifier(slash, 100), ignoreResistances: true), Is.True);
            Assert.That(entities.System<WoundSystem>().GetWounds(leg).Any(), Is.True, "no breach to weld.");

            var tool = entities.SpawnEntity("Welder", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(tool, user), Is.True);
            entities.GetComponent<TargetingComponent>(user).Target = TargetBodyPart.RightLeg;
            var fuelBefore = entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float();

            var interact = new InteractUsingEvent(user, tool, body, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(body, interact);
            Assert.That(interact.Handled, Is.True, "the welder did not start.");

            // 100 damage at 25 a pass: four passes, all inside the click.
            Assert.Multiple(() =>
            {
                // A hit this hard also costs the leg its servos, which is the cable coil's to fix, not the welder's.
                Assert.That(entities.System<WoundSystem>().GetWounds(leg).Select(wound => wound.Comp.Prototype.Id),
                    Has.None.EqualTo("WFWolfmedBreachWound"), "the breach is still open after the click.");
                Assert.That(entities.GetComponent<DamageableComponent>(leg).TotalDamage, Is.EqualTo(FixedPoint2.Zero), "damage is left on the leg.");
                Assert.That(entities.System<SharedToolSystem>().GetWelderFuelAndCapacity(tool).fuel.Float(),
                    Is.EqualTo(fuelBefore - 20).Within(0.01f), "the chain did not pay four passes.");
                Assert.That(entities.GetComponent<DoAfterComponent>(user).DoAfters.Values.All(pass => pass.Completed || pass.Cancelled),
                    Is.True, "a pass is still queued.");
            });
        });
    }
}
