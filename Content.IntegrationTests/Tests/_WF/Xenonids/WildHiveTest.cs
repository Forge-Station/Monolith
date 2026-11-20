#nullable enable
using System.Linq;
using Content.Server.Gravity;
using Content.Shared._WF.Xenonids.Egg;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared._WF.Xenonids.Weeds;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Aerumna's wild hive: weeds that stay put and eggs that come grown and open for whoever walks up.</summary>
[TestFixture]
public sealed class WildHiveTest
{
    /// <summary>A wild egg is grown, anchored and cultivated from the start, and never turns fragile.</summary>
    [Test]
    public async Task WildEggComesGrown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid egg = default;
        await server.WaitAssertion(() =>
        {
            egg = entities.SpawnEntity("WFXenoEggWild", map.GridCoords);
            var comp = entities.GetComponent<XenoEggComponent>(egg);
            Assert.That(comp.State, Is.EqualTo(XenoEggState.Grown), "A wild egg starts grown.");
            Assert.That(comp.Cultivated, Is.True, "A wild egg needs no weeds under it.");
            Assert.That(entities.GetComponent<TransformComponent>(egg).Anchored, Is.True, "A wild egg is fixed to its tile.");
        });

        // The weeds check runs every 2 s.
        await server.WaitRunTicks(3 * 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<XenoFragileEggComponent>(egg), Is.False, "A wild egg on bare ground stays healthy.");
            Assert.That(entities.GetComponent<XenoEggComponent>(egg).State, Is.EqualTo(XenoEggState.Grown));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Walking up to a wild egg opens it and infects the walker, as any grown egg does.</summary>
    [Test]
    public async Task WildEggOpensForAWalker()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        // A step trigger ignores the weightless, and the test map has no gravity.
        await server.WaitPost(() => server.System<GravitySystem>().EnableGravity(map.Grid.Owner));

        EntityUid egg = default;
        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            egg = entities.SpawnEntity("WFXenoEggWild", map.GridCoords);
            human = entities.SpawnEntity("MobHuman", map.GridCoords);
        });

        await server.WaitRunTicks(5 * 30);
        await server.WaitAssertion(() =>
        {
            var state = entities.GetComponent<XenoEggComponent>(egg).State;
            Assert.That(state, Is.EqualTo(XenoEggState.Opening).Or.EqualTo(XenoEggState.Opened), "The egg stayed shut.");
            Assert.That(entities.GetComponent<InfectableComponent>(human).BeingInfected
                        || entities.HasComponent<VictimInfectedComponent>(human), Is.True, "The walker was not infected.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Wild weeds never spread, so a hive's patch stays inside its region.</summary>
    [Test]
    public async Task WildWeedsStayPut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() => entities.SpawnEntity("WFXenoWeedsWild", map.GridCoords));
        await server.WaitRunTicks(20 * 30);
        await server.WaitAssertion(() =>
        {
            var weeds = entities.EntityQuery<XenoWeedsComponent>().Count();
            Assert.That(weeds, Is.EqualTo(1), "Wild weeds spread.");
        });

        await pair.CleanReturnAsync();
    }
}
