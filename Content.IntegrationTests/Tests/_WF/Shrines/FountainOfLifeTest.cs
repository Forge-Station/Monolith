#nullable enable
using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Shared._WF.Shrines;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Shrines;

/// <summary>Vitae from the Fountain of Life: a dose heals everything and leaves cuffs on, less does nothing, and the fountain runs dry.</summary>
[TestFixture]
public sealed class FountainOfLifeTest
{
    private const string Vitae = "WFVitae";

    [Test]
    public async Task ADoseHealsEverythingButKeepsTheCuffsOn()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var damage = entMan.System<DamageableSystem>();
        var blood = entMan.System<BloodstreamSystem>();

        EntityUid dosed = default, underdosed = default;
        await server.WaitAssertion(() =>
        {
            dosed = entMan.SpawnEntity("MobHuman", map.GridCoords);
            underdosed = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var blunt = server.ProtoMan.Index<DamageTypePrototype>("Blunt");
            var heat = server.ProtoMan.Index<DamageTypePrototype>("Heat");
            foreach (var mob in new[] { dosed, underdosed })
            {
                damage.TryChangeDamage(mob, new DamageSpecifier(blunt, 30), true);
                damage.TryChangeDamage(mob, new DamageSpecifier(heat, 20), true);
            }

            var cuffs = entMan.SpawnEntity("Handcuffs", map.GridCoords);
            Assert.That(entMan.System<SharedCuffableSystem>().TryAddNewCuffs(dosed, dosed, cuffs), Is.True);
            Assert.That(blood.TryAddToChemicals(dosed, new Solution(Vitae, 10)), Is.True);
            Assert.That(blood.TryAddToChemicals(underdosed, new Solution(Vitae, 5)), Is.True);
        });
        await pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // The test map is a vacuum, so both keep taking pressure and heat damage; the dosed one must be rid of
                // the 30 blunt and 20 heat it started with.
                var healed = entMan.GetComponent<DamageableComponent>(dosed).Damage.DamageDict;
                var hurt = entMan.GetComponent<DamageableComponent>(underdosed).Damage.DamageDict;
                Assert.That(healed.GetValueOrDefault("Blunt"), Is.LessThanOrEqualTo(hurt.GetValueOrDefault("Blunt") - 30),
                    $"A full dose of vitae should heal everything: {string.Join(", ", healed)}");
                Assert.That(healed.GetValueOrDefault("Heat"), Is.LessThanOrEqualTo(hurt.GetValueOrDefault("Heat") - 20),
                    $"A full dose of vitae should heal everything: {string.Join(", ", healed)}");
                Assert.That(entMan.GetComponent<CuffableComponent>(dosed).CuffedHandCount, Is.GreaterThan(0),
                    "Vitae must not free a prisoner.");
                Assert.That(hurt.GetValueOrDefault("Blunt"), Is.GreaterThanOrEqualTo(FixedPoint2.New(30)),
                    "Half a dose should do nothing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheFountainHoldsThreeDosesAndRunsDry()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        EntityUid fountain = default;
        await server.WaitAssertion(() =>
        {
            fountain = entMan.SpawnEntity("WFFountainOfLife", map.GridCoords);
            Assert.That(solutions.TryGetSolution(fountain, "fountain", out var soln, out var solution), Is.True);
            Assert.That(solution!.GetTotalPrototypeQuantity(Vitae), Is.EqualTo(FixedPoint2.New(30)));
            solutions.SplitSolution(soln!.Value, solution.Volume);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var appearance = entMan.System<SharedAppearanceSystem>();
            Assert.That(appearance.TryGetData<bool>(fountain, WFFountainVisuals.Dry, out var dry) && dry, Is.True,
                "An empty fountain should show dry.");
        });

        await pair.CleanReturnAsync();
    }
}
