using Content.IntegrationTests.Fixtures;
using Content.Server.Storage.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>The debug crate spawns with every listed item; a renamed prototype id fails here, not in a playtest.</summary>
[TestFixture]
public sealed class WolfmedDebugCrateTest : GameTest
{
    [Test]
    public async Task DebugCrateFillsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var crate = entities.SpawnEntity("WFCrateWolfmedDebug", map.GridCoords);
            var storage = entities.GetComponent<EntityStorageComponent>(crate);
            Assert.That(storage.Contents.ContainedEntities, Has.Count.GreaterThanOrEqualTo(50));
        });
    }
}
