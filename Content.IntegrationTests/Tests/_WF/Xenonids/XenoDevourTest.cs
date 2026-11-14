#nullable enable
using Content.Shared._WF.Xenonids.Devour;
using Content.Shared.DoAfter;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A xeno devours a pulled human by toggling the pull, as in RMC-14.</summary>
[TestFixture]
public sealed class XenoDevourTest
{
    [Test]
    public async Task PullToggleStartsDevour()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var pulling = entities.System<PullingSystem>();
        var map = await pair.CreateTestMap();

        EntityUid drone = default;
        EntityUid human = default;
        await server.WaitAssertion(() =>
        {
            drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            human = entities.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(entities.HasComponent<DevourableComponent>(human), "Species can be devoured.");
            Assert.That(entities.HasComponent<XenoDevourComponent>(drone));
            Assert.That(pulling.TryStartPull(drone, human), "The drone grabs the human.");
            Assert.That(entities.GetComponent<PullerComponent>(drone).Pulling, Is.EqualTo(human));

            pulling.TogglePull(drone, entities.GetComponent<PullerComponent>(drone));
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<ActiveDoAfterComponent>(drone), "Toggling the pull starts the devour do-after.");
        });

        await pair.CleanReturnAsync();
    }
}
