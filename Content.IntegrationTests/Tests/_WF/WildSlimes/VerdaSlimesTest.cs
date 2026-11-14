#nullable enable
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>Verda's wild slimes: the dew slime and the bloom slime, their patches and grottos, and their climate.</summary>
[TestFixture]
public sealed class VerdaSlimesTest
{
    private const string Habitat = "WFWildSlimesVerda";

    /// <summary>The dew slime comes only from Verda and breeds into the bloom slime, which breeds back.</summary>
    [Test]
    public async Task BreedsComeOnlyFromTheirWorld()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckBreeds(pair, Habitat, "WFDewMutation", "WFBloomMutation");
        await pair.CleanReturnAsync();
    }

    /// <summary>Patches over grottos with air, and wild slimes that live unhurt on the formed world.</summary>
    [Test]
    public async Task PatchesSitOverGrottosAndSlimesThrive()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckWorld(pair, "WFSurfaceAsclepiu", Habitat, 3, 40f);
        await pair.CleanReturnAsync();
    }

    /// <summary>In mild air a dew slime splits only while it sits in water.</summary>
    [Test]
    public async Task SplitsOnlyInItsClimate()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckClimate(pair, Habitat, 293.15f, 293.15f, "MonoFloorWaterEntity");
        await pair.CleanReturnAsync();
    }

    /// <summary>A hurt dew slime sitting in water heals.</summary>
    [Test]
    public async Task WaterHealsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var (map, slime) = await WildSlimeWorldChecks.SpawnInAir(pair, Habitat, 293.15f);
        var (_, dry) = await WildSlimeWorldChecks.SpawnInAir(pair, Habitat, 293.15f);

        var hurt = FixedPoint2.Zero;
        await server.WaitPost(() =>
        {
            var blunt = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 20);
            server.System<DamageableSystem>().TryChangeDamage(slime, blunt, true);
            server.System<DamageableSystem>().TryChangeDamage(dry, blunt, true);
            hurt = entMan.GetComponent<DamageableComponent>(slime).TotalDamage;
            entMan.SpawnEntity("MonoFloorWaterEntity", map.GridCoords);
        });

        await pair.RunSeconds(6);

        await server.WaitAssertion(() =>
        {
            Assert.That(hurt, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(entMan.GetComponent<DamageableComponent>(slime).TotalDamage, Is.LessThan(hurt), "Water didn't heal a dew slime.");
            Assert.That(entMan.GetComponent<DamageableComponent>(dry).TotalDamage, Is.EqualTo(hurt), "A dry dew slime healed too.");
        });

        await pair.CleanReturnAsync();
    }
}
