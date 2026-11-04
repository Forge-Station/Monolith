#nullable enable
using Content.Server._CE.ZLevels.Core;
using Content.Shared._WF.PlanetCities;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>A camp's or wreck's hull keeps its helm but refuses liftoff, instead of climbing and sticking at the first level.</summary>
[TestFixture]
[TestOf(typeof(CEZLevelsSystem))]
public sealed class SettledHullLiftoffTest
{
    [Test]
    public async Task SettledHullIsHeldDown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var zLevels = server.System<CEZLevelsSystem>();
        await server.WaitAssertion(() =>
        {
            var settled = Loc.GetString("wf-liftoff-settled");
            var hull = map.Grid.Owner;
            Assert.That(zLevels.WfLiftoffHold(hull), Is.Not.EqualTo(settled), "A hull that is not settled is held as settled.");
            server.EntMan.EnsureComponent<WFPlanetCityGridComponent>(hull);
            Assert.That(zLevels.WfLiftoffHold(hull), Is.EqualTo(settled), "A settled hull may still lift off.");
            Assert.That(zLevels.WfCanLiftoff(hull, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(settled));
        });

        await pair.CleanReturnAsync();
    }
}
