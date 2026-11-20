#nullable enable

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>Thrascias's wild slimes: the rime slime and the aurora slime, their patches and grottos, and their climate.</summary>
[TestFixture]
public sealed class ThrasciasSlimesTest
{
    private const string Habitat = "WFWildSlimesThrascias";

    /// <summary>The rime slime comes only from Thrascias and breeds into the aurora slime, which breeds back.</summary>
    [Test]
    public async Task BreedsComeOnlyFromTheirWorld()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckBreeds(pair, Habitat, "WFRimeMutation", "WFAuroraMutation");
        await pair.CleanReturnAsync();
    }

    /// <summary>Patches over grottos with air, and wild slimes that live unhurt on the formed world.</summary>
    [Test]
    public async Task PatchesSitOverGrottosAndSlimesThrive()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckWorld(pair, "WFSurfaceThrascias", Habitat, 3, 40f);
        await pair.CleanReturnAsync();
    }

    /// <summary>A rime slime splits in deep cold and not at room temperature.</summary>
    [Test]
    public async Task SplitsOnlyInItsClimate()
    {
        await using var pair = await PoolManager.GetServerClient();
        await WildSlimeWorldChecks.CheckClimate(pair, Habitat, 293.15f, 200f);
        await pair.CleanReturnAsync();
    }
}
