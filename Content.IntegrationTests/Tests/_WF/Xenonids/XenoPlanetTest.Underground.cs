#nullable enable
using Content.Server.Mind;
using Content.Shared._WF.Xenonids.Burrow;
using Content.Shared._WF.Xenonids.Construction.Tunnel;
using Content.Shared._WF.Xenonids.Eye;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Caverns.CavernFixture;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

public sealed partial class XenoPlanetTest
{
    /// <summary>In a cavern tunnels link on the cavern's own grid, and a burrower tunnels and surfaces there, never on the surface.</summary>
    [Test]
    public async Task CavernTunnelsAndBurrowingStayInTheCavern()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var (planet, centre) = await Create(pair, underground: true);
        var entities = planet.Entities;
        var tunnels = entities.System<XenoTunnelSystem>();
        var containers = entities.System<SharedContainerSystem>();
        var maps = entities.System<SharedMapSystem>();
        var surface = planet.World.Ground;

        EntityUid hive = default;
        EntityUid burrower = default;
        EntityUid traveller = default;
        EntityUid first = default;
        EntityUid second = default;
        EntityUid onSurface = default;
        var burrowTile = centre + new Vector2i(2, 2);
        EntityCoordinates surfaceCoords = default;

        await server.WaitPost(() =>
        {
            hive = SpawnHive(planet);

            // The surface above has ground at the same coordinates, so refusing it is the rule and not missing floor.
            var surfaceGrid = entities.GetComponent<MapGridComponent>(surface);
            maps.SetTile(surface, surfaceGrid, centre, new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId));
            surfaceCoords = new EntityCoordinates(surface, centre.X + 0.5f, centre.Y + 0.5f);

            Assert.That(tunnels.TryPlaceTunnel(hive, "cave one", planet.At(centre + new Vector2i(-2, 0)), out var one), "A cavern tunnel registers.");
            Assert.That(tunnels.TryPlaceTunnel(hive, "cave two", planet.At(centre + new Vector2i(0, -3)), out var two), "A second cavern tunnel registers.");
            Assert.That(tunnels.TryPlaceTunnel(hive, "surface", surfaceCoords, out var up), "A surface tunnel registers.");
            first = one!.Value;
            second = two!.Value;
            onSurface = up!.Value;

            traveller = entities.SpawnEntity("CMXenoDrone", planet.At(centre + new Vector2i(-2, 0)));
            SetHive(planet, hive, traveller);
            var container = containers.EnsureContainer<Container>(first, XenoTunnelComponent.ContainedMobsContainerId);
            Assert.That(containers.Insert(traveller, container), "The drone enters the tunnel.");

            burrower = entities.SpawnEntity("CMXenoBurrower", planet.At(centre));
            SetHive(planet, hive, burrower);
            var burrowed = new BurrowedEvent(true);
            entities.EventBus.RaiseLocalEvent(burrower, ref burrowed, false);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(tunnels.GetTravelDestinations(first).Keys, Is.EquivalentTo(new[] { "cave two" }),
                "Only the other cavern tunnel is a destination in the cavern.");

            var toSurface = new TraverseXenoTunnelMessage(entities.GetNetEntity(onSurface)) { Actor = traveller, Entity = entities.GetNetEntity(first) };
            entities.EventBus.RaiseLocalEvent(first, toSurface);
            Assert.That(entities.HasComponent<ActiveDoAfterComponent>(traveller), Is.False, "Travel up to the surface tunnel is refused.");

            var toSame = new TraverseXenoTunnelMessage(entities.GetNetEntity(second)) { Actor = traveller, Entity = entities.GetNetEntity(first) };
            entities.EventBus.RaiseLocalEvent(first, toSame);
            Assert.That(entities.HasComponent<ActiveDoAfterComponent>(traveller), "Travel between cavern tunnels starts.");

            var up = new XenoBurrowActionEvent { Performer = burrower, Target = surfaceCoords };
            entities.EventBus.RaiseLocalEvent(burrower, up);
            Assert.That(entities.HasComponent<ActiveDoAfterComponent>(burrower), Is.False, "Burrowing up to the surface is refused.");

            var across = new XenoBurrowActionEvent { Performer = burrower, Target = planet.At(burrowTile) };
            entities.EventBus.RaiseLocalEvent(burrower, across);
            Assert.That(entities.HasComponent<ActiveDoAfterComponent>(burrower), "Burrowing across the cavern starts.");
        });

        await pair.RunSeconds(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(containers.TryGetContainingContainer((traveller, null), out var inside) && inside.Owner == second,
                "The drone did not arrive in the second cavern tunnel.");
            Assert.That(entities.GetComponent<TransformComponent>(traveller).MapUid, Is.EqualTo(planet.Ground));

            var xform = entities.GetComponent<TransformComponent>(burrower);
            Assert.That(xform.MapUid, Is.EqualTo(planet.Ground), "The burrower surfaced off the cavern.");
            Assert.That(maps.CoordinatesToTile(planet.Ground, planet.Grid, xform.Coordinates), Is.EqualTo(burrowTile),
                "The burrower surfaced where it tunnelled to.");
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }

    /// <summary>The queen's eye, opened in the cavern, follows her weeds and stays in the cavern.</summary>
    [Test]
    public async Task QueenEyeStaysInTheCavern()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var (planet, centre) = await Create(pair, underground: true);
        var entities = planet.Entities;
        var session = server.PlayerMan.Sessions[0];

        EntityUid queen = default;
        EntityUid eye = default;

        await server.WaitPost(() =>
        {
            var hive = SpawnHive(planet);
            queen = entities.SpawnEntity("CMXenoQueen", planet.At(centre));
            SetHive(planet, hive, queen);

            for (var step = 0; step <= 4; step++)
            {
                SetHive(planet, hive, entities.SpawnEntity("XenoWeedsSource", planet.At(centre + new Vector2i(step * 3, 0))));
            }

            entities.System<MindSystem>().TransferTo(entities.System<MindSystem>().GetOrCreateMind(session.UserId), queen);
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            entities.EventBus.RaiseLocalEvent(queen, new QueenEyeActionEvent { Performer = queen });
            eye = entities.GetComponent<QueenEyeActionComponent>(queen).Eye!.Value;
            Assert.That(entities.GetComponent<TransformComponent>(eye).MapUid, Is.EqualTo(planet.Ground), "The eye opened off the cavern.");
        });

        for (var step = 1; step <= 4; step++)
        {
            await server.WaitPost(() =>
                entities.System<SharedTransformSystem>().SetCoordinates(eye, planet.At(centre + new Vector2i(step * 3, 0))));
            await pair.RunTicksSync(pair.SecondsToTicks(1f));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<TransformComponent>(eye).MapUid, Is.EqualTo(planet.Ground), "The eye left the cavern.");
            Assert.That(entities.GetComponent<TransformComponent>(queen).MapUid, Is.EqualTo(planet.Ground));
        });

        await Teardown(pair, planet.World);
        await pair.CleanReturnAsync();
    }
}
