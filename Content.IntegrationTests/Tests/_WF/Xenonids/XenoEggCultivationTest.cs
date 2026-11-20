#nullable enable
using Content.Shared._WF.Xenonids.Egg;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A human plants an egg on bare floor; it grows there without weeds, plasma or turning fragile.</summary>
[TestFixture]
public sealed class XenoEggCultivationTest
{
    [Test]
    public async Task HumanPlantsEggWithoutWeeds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hands = entities.System<SharedHandsSystem>();
        var interaction = entities.System<SharedInteractionSystem>();
        var map = await pair.CreateTestMap();

        EntityUid human = default;
        EntityUid egg = default;
        await server.WaitAssertion(() =>
        {
            human = entities.SpawnEntity("MobHuman", map.GridCoords);
            egg = entities.SpawnEntity("XenoEgg", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(human, egg), "The human picks up the egg.");
            // clicking the floor with the egg in hand starts the plant do-after
            interaction.UserInteraction(human, map.GridCoords, null, checkCanInteract: false);
        });

        // the plant do-after is 3.5 s
        await server.WaitRunTicks(4 * 30 + 10);
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<XenoEggComponent>(egg);
            Assert.That(entities.GetComponent<TransformComponent>(egg).Anchored, "The egg is planted.");
            Assert.That(comp.Cultivated, "The egg is marked cultivated.");
            Assert.That(comp.State, Is.EqualTo(XenoEggState.Growing));
        });

        // the weeds check runs every 2 s; a cultivated egg never turns fragile
        await server.WaitRunTicks(3 * 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<XenoFragileEggComponent>(egg), Is.False, "Off weeds, the cultivated egg stays healthy.");
        });

        await pair.CleanReturnAsync();
    }
}
