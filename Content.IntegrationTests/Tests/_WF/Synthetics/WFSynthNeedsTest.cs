#nullable enable
using Content.Server.Body.Components;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._WF.Biotype;
using Content.Shared.Body.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>A synthetic body never needs food or water, cannot take either, and neither refills its fluid.</summary>
[TestFixture]
public sealed class WFSynthNeedsTest
{
    [Test]
    public async Task NoFoodOrWaterTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
            var synth = entMan.System<StationSpawningSystem>().SpawnPlayerMob(map.GridCoords, job: null, profile, station: null);
            var food = entMan.SpawnEntity("FoodBreadPlain", map.GridCoords);
            var blood = entMan.GetComponent<BloodstreamComponent>(synth);
            var before = blood.BloodSolution?.Comp.Solution.Volume;

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<HungerComponent>(synth), Is.False, "a synthetic gets hungry.");
                Assert.That(entMan.HasComponent<ThirstComponent>(synth), Is.False, "a synthetic gets thirsty.");
                Assert.That(entMan.System<SharedBodySystem>().TryGetBodyOrganEntityComps<StomachComponent>(synth, out _), Is.False,
                    "a synthetic has a stomach, so it can eat and drink.");
                var (fed, _) = entMan.System<FoodSystem>().TryFeed(synth, synth, food, entMan.GetComponent<FoodComponent>(food));
                Assert.That(fed, Is.False, "a synthetic started eating.");
                Assert.That(blood.BloodSolution?.Comp.Solution.Volume, Is.EqualTo(before), "food changed its fluid.");
            });

            entMan.DeleteEntity(synth);
            entMan.DeleteEntity(food);
        });

        await pair.CleanReturnAsync();
    }
}
