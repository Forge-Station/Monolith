#nullable enable
using Content.IntegrationTests.Tests._WF.Caverns;
using Content.Shared._WF.Xenobiology.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.WildSlimes;

/// <summary>Fervidus's wild slimes: the cinder slime and the obsidian slime bred from it.</summary>
[TestFixture]
public sealed class FervidusSlimesTest
{
    private static readonly SlimeWorldTestKit.Spec Spec = new("WFSurfaceFervidus", "WFWildSlimesFervidus",
        "WFCinderMutation", "WFObsidianMutation", "WFCinderSlimeExtract", "WFObsidianSlimeExtract");

    [Test]
    public async Task CinderSlimesComeOnlyFromFervidus()
    {
        await SlimeWorldTestKit.CheckBreeds(Spec);
    }

    /// <summary>
    /// Fervidus has obsidian glass patches over glass vaults with wild cinder slimes in both, and cinder and obsidian
    /// slimes left there, some by the magma, take no harm from its hot, oxygen-free air. The cinder slime glows.
    /// </summary>
    [Test]
    public async Task CinderSlimesLiveOnFervidus()
    {
        await using var pair = await PoolManager.GetServerClient();
        var formed = await SlimeWorldTestKit.CheckPatches(pair, Spec);
        var slimes = await SlimeWorldTestKit.CheckSurvival(pair, Spec, formed);

        var entMan = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var slime in slimes)
            {
                if (entMan.GetComponent<SlimeComponent>(slime).Breed.Id == Spec.Wild)
                    Assert.That(entMan.HasComponent<PointLightComponent>(slime), Is.True, "A cinder slime doesn't glow.");
            }
        });

        await CavernFixture.Teardown(pair, formed.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>Cinder and obsidian slimes only split in Fervidus's heat.</summary>
    [Test]
    public async Task CinderSlimesSplitOnlyInTheHeat()
    {
        await SlimeWorldTestKit.CheckClimate(Spec, 373.15f, new[] { 0f, 75f, 25f });
    }
}
