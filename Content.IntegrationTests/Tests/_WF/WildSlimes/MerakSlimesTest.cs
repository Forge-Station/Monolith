#nullable enable
using Content.Server._WF.WildSlimes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Movement.Components;

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>Merak's wild slimes: the sandglass slime and the amber slime, their patches and grottos, and their climate.</summary>
[TestFixture]
public sealed class MerakSlimesTest
{
    private const string Habitat = "WFWildSlimesMerak";

    /// <summary>The sandglass slime comes only from Merak and breeds into the amber slime, which breeds back.</summary>
    [Test]
    public async Task BreedsComeOnlyFromTheirWorld()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckBreeds(pair, Habitat, "WFSandglassMutation", "WFAmberMutation");
        await pair.CleanReturnAsync();
    }

    /// <summary>Patches over grottos with air, and wild slimes that live unhurt on the formed world.</summary>
    [Test]
    public async Task PatchesSitOverGrottosAndSlimesThrive()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckWorld(pair, "WFSurfaceMerak", Habitat, 3, 40f);
        await pair.CleanReturnAsync();
    }

    /// <summary>A sandglass slime splits in desert heat and not at room temperature.</summary>
    [Test]
    public async Task SplitsOnlyInItsClimate()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckClimate(pair, Habitat, 293.15f, 318.15f);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hurt sandglass slime digs in, taking less damage and barely moving, and spills water when it dies.</summary>
    [Test]
    public async Task DigsInWhenHurtAndSpillsWaterOnDeath()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var damageable = server.System<DamageableSystem>();
        var (_, slime) = await WildSlimeWorldChecks.SpawnInAir(pair, Habitat, 318.15f);

        var blunt = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 10);
        FixedPoint2 first = default, second = default;
        float walk = 0;
        await server.WaitAssertion(() =>
        {
            first = damageable.TryChangeDamage(slime, blunt)!.GetTotal();
            second = damageable.TryChangeDamage(slime, blunt)!.GetTotal();
            Assert.That(entMan.GetComponent<WFDigInComponent>(slime).DugInUntil, Is.Not.Null, "A hurt sandglass slime didn't dig in.");
            walk = entMan.GetComponent<MovementSpeedModifierComponent>(slime).CurrentWalkSpeed;
        });

        Assert.That(second, Is.LessThan(first), "A dug in sandglass slime took no less damage.");
        Assert.That(walk, Is.LessThan(1f), "A dug in sandglass slime didn't slow.");

        await server.WaitPost(() => damageable.TryChangeDamage(slime, blunt * 50, true));
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var solutions = server.System<SharedSolutionContainerSystem>();
            var water = FixedPoint2.Zero;
            var query = entMan.EntityQueryEnumerator<PuddleComponent>();
            while (query.MoveNext(out var uid, out var puddle))
            {
                if (solutions.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var solution))
                    water += solution.GetTotalPrototypeQuantity("Water");
            }

            Assert.That(water, Is.GreaterThan(FixedPoint2.Zero), "A dead sandglass slime spilled no water.");
        });

        await pair.CleanReturnAsync();
    }
}
