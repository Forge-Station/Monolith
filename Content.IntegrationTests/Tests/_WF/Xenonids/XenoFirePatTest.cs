#nullable enable
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.Xenonids.FirePat;
using Content.Shared.Atmos.Components;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>Xenos pat out a burning mob by clicking it with an empty hand.</summary>
[TestFixture]
public sealed class XenoFirePatTest
{
    [Test]
    public async Task DronePatsOutBurningHuman()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid drone = default, human = default;
        await server.WaitPost(() =>
        {
            human = entities.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            drone = entities.SpawnEntity("CMXenoDrone", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<XenoFirePatterComponent>(drone), "Developed xenos can pat out fires.");

            var flammable = entities.GetComponent<FlammableComponent>(human);
            var fire = entities.System<FlammableSystem>();
            fire.AdjustFireStacks(human, 8, flammable, ignite: true);
            Assert.That(flammable.OnFire);

            var interaction = entities.System<SharedInteractionSystem>();
            interaction.InteractHand(drone, human);
            Assert.That(flammable.FireStacks, Is.EqualTo(3f).Within(0.01f), "One pat takes five stacks off.");

            interaction.InteractHand(drone, human);
            Assert.That(flammable.FireStacks, Is.EqualTo(3f).Within(0.01f), "Patting again inside the cooldown does nothing.");
        });

        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            var flammable = entities.GetComponent<FlammableComponent>(human);
            entities.System<SharedInteractionSystem>().InteractHand(drone, human);
            Assert.That(flammable.OnFire, Is.False, "The second pat puts the fire out.");
        });

        await pair.CleanReturnAsync();
    }
}
