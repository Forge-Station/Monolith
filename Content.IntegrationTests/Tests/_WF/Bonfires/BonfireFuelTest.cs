#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WF.GroundCover;
using Content.Server._Mono.Temperature.Components;
using Content.Server._Mono.Temperature.Systems;
using Content.Server._WF.Bonfires;
using Content.Server._WF.GroundCover;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.GroundCover;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Stacks;
using Content.Shared.Temperature.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Bonfires;

/// <summary>Bonfires burn their fuel down, go out and stop warming, and take wood and a flame to feed and light again.</summary>
[TestFixture]
[TestOf(typeof(WFBonfireFuelSystem))]
public sealed class BonfireFuelTest
{
    [Test]
    public async Task ABonfireBurnsDownAndGoesOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid fire = default;
        var near = new Vector2i(0, 0);

        await server.WaitPost(() =>
        {
            GroundCoverTest.LayOpenGround(pair, map.Grid);
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, GroundCoverTest.Air(250f));
            fire = server.EntMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            var comp = server.EntMan.GetComponent<WFBonfireFuelComponent>(fire);
            Assert.That(comp.Lit, Is.True, "A bonfire placed in a map should start lit.");
            Assert.That(comp.Fuel, Is.GreaterThan(0f), "A bonfire placed in a map should start fuelled.");
            comp.Fuel = 6f;
        });

        await server.WaitAssertion(() =>
            Assert.That(server.System<EntityRadiusHeaterSystem>().IsActive(fire, server.EntMan.GetComponent<EntityRadiusHeaterComponent>(fire)), Is.True,
                "A lit bonfire gave no warmth."));

        await server.WaitRunTicks(pair.SecondsToTicks(10));
        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            Assert.That(entMan.GetComponent<WFBonfireFuelComponent>(fire).Lit, Is.False, "The bonfire didn't go out when its fuel ran out.");
            Assert.That(server.System<EntityRadiusHeaterSystem>().IsActive(fire, entMan.GetComponent<EntityRadiusHeaterComponent>(fire)), Is.False,
                "A burnt out bonfire still gave warmth.");
            Assert.That(entMan.HasComponent<AlwaysHotComponent>(fire), Is.False, "A burnt out bonfire still lit things.");
            Assert.That(server.System<SharedPointLightSystem>().TryGetLight(fire, out var light) && light.Enabled, Is.False, "A burnt out bonfire still gave light.");

            // Snow beside an out fire is left alone.
            server.System<WFGroundCoverSystem>().SetDepth(map.Grid, near, WFSnowCover.MaxDepth);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10));
        await server.WaitAssertion(() =>
            Assert.That(server.System<WFGroundCoverSystem>().GetDepth(map.Grid, near), Is.EqualTo(WFSnowCover.MaxDepth),
                "A burnt out bonfire still melted snow."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WoodFeedsItAndAFlameLightsIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid fire = default;
        EntityUid user = default;
        EntityUid plank = default;
        EntityUid log = default;
        EntityUid lighter = default;

        await server.WaitPost(() =>
        {
            var coords = new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f));
            fire = server.EntMan.SpawnEntity("Bonfire", coords);
            user = server.EntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(1.5f, 0.5f)));
            plank = server.EntMan.SpawnEntity("MaterialWoodPlank10", coords);
            log = server.EntMan.SpawnEntity("Log", coords);
            lighter = server.EntMan.SpawnEntity("Lighter", coords);
            var comp = server.EntMan.GetComponent<WFBonfireFuelComponent>(fire);
            comp.Fuel = 0f;
            server.System<WFBonfireFuelSystem>().SetLit((fire, comp), false);
        });

        await server.WaitAssertion(() =>
        {
            var comp = server.EntMan.GetComponent<WFBonfireFuelComponent>(fire);
            Assert.That(comp.Lit, Is.False);

            // No fuel, so a flame has nothing to light.
            server.System<ItemToggleSystem>().TrySetActive(lighter, true);
            Use(server, user, lighter, fire, map.Grid);
            Assert.That(comp.Lit, Is.False, "A bonfire with no fuel was lit.");

            // One plank of a stack gives 300 seconds, and a log 600: ten minutes.
            Use(server, user, plank, fire, map.Grid);
            Assert.That(comp.Fuel, Is.EqualTo(300f).Within(0.01f), "A plank gave the wrong fuel.");
            Assert.That(server.EntMan.GetComponent<StackComponent>(plank).Count, Is.EqualTo(9), "Feeding a plank took the whole stack.");
            Use(server, user, log, fire, map.Grid);
            Assert.That(comp.Fuel, Is.EqualTo(900f).Within(0.01f), "A log gave the wrong fuel.");
            Assert.That(server.EntMan.Deleted(log) || server.EntMan.IsQueuedForDeletion(log), Is.True, "The log wasn't burnt.");
            Assert.That(comp.Lit, Is.False, "Feeding an out bonfire lit it.");

            Use(server, user, lighter, fire, map.Grid);
            Assert.That(comp.Lit, Is.True, "A flame didn't light a fuelled bonfire.");
            Assert.That(server.System<EntityRadiusHeaterSystem>().IsActive(fire, server.EntMan.GetComponent<EntityRadiusHeaterComponent>(fire)), Is.True,
                "A relit bonfire gave no warmth.");

            // A full bonfire takes no more.
            comp.Fuel = comp.MaxFuel;
            Use(server, user, plank, fire, map.Grid);
            Assert.That(server.EntMan.GetComponent<StackComponent>(plank).Count, Is.EqualTo(9), "A full bonfire took more wood.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SomeFiresBurnForEver()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        EntityUid decor = default;
        EntityUid camp = default;

        await server.WaitPost(() =>
        {
            decor = server.EntMan.SpawnEntity("LegionnaireBonfire", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            camp = server.EntMan.SpawnEntity("WFCavernCampfire", new EntityCoordinates(map.Grid, new Vector2(2.5f, 0.5f)));
            server.EntMan.GetComponent<WFBonfireFuelComponent>(decor).Fuel = 0f;
            server.EntMan.GetComponent<WFBonfireFuelComponent>(camp).Fuel = 0f;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<WFBonfireFuelComponent>(decor).Lit, Is.True, "The lava world's bonfire went out.");
            Assert.That(server.EntMan.GetComponent<WFBonfireFuelComponent>(camp).Lit, Is.True, "A cavern camp's fire went out.");
        });

        await pair.CleanReturnAsync();
    }

    private static void Use(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid user, EntityUid used, EntityUid target, EntityUid grid)
    {
        server.System<SharedInteractionSystem>().InteractUsing(user, used, target, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)),
            checkCanInteract: false, checkCanUse: false);
    }
}
