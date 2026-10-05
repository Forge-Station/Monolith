#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Planets;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Planet z-map networks: build order, per-layer fixups, the orbit fall exemption and the FTL gate.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetNetworkSystem))]
public sealed class PlanetNetworkTest
{
    private const string Surface = "WFSurfaceAsclepiu";
    private const string PlanetBody = "PlanetEntity";

    /// <summary>WFSurfaceAsclepiu's orbit ambient light, dim but not black.</summary>
    private static readonly Color OrbitAmbient = Color.FromHex("#2a3340");

    /// <summary>The surface mixture from WFAsclepiuSurface, re-applied after MapInit.</summary>
    private const float GroundTemperature = 288.15f;

    /// <summary>The network registry's mixture, which is stamped over every layer at MapInit.</summary>
    private const float RegistryTemperature = 293.15f;

    /// <summary>Layer roles for WFSurfaceAsclepiu: 0 ground, 1-3 air, 4 orbit. No cloud layer.</summary>
    private const int LayerCount = 5;

    /// <summary>The air layers of the Asclepiu stack, which airLayers 3 puts at depths 1, 2 and 3.</summary>
    private const int AirLayerCount = 3;

    /// <summary>Everything a built stack exposes to a test.</summary>
    private sealed class Stack
    {
        public EntityUid Network;
        public List<EntityUid> Layers = new();

        public EntityUid Ground => Layers[0];
        public EntityUid Air => Layers[1];
        public EntityUid Orbit => Layers[^1];
    }

    /// <summary>Five maps at the right depths, linked both ways, with solid ground and a bare orbit.</summary>
    [Test]
    public async Task BuildsTheFullStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var zLevels = server.System<CEZLevelsSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(stack.Layers, Has.Count.EqualTo(LayerCount), "The Asclepiu stack should be five layers.");

            using (Assert.EnterMultipleScope())
            {
                for (var depth = 0; depth < stack.Layers.Count; depth++)
                {
                    var layer = stack.Layers[depth];
                    Assert.That(entMan.TryGetComponent(layer, out CEZMapComponent? zMap), Is.True,
                        $"Layer {depth} is not a z-network member.");
                    Assert.That(zMap!.Depth, Is.EqualTo(depth), $"Layer {depth} has the wrong depth.");
                    Assert.That(zMap.NetworkUid, Is.EqualTo(stack.Network), $"Layer {depth} points at another network.");
                    Assert.That(entMan.HasComponent<WFPlanetLayerComponent>(layer), Is.True,
                        $"Layer {depth} is not marked as a planet layer.");
                }
            }

            Assert.That(zLevels.TryMapUp(stack.Ground, out var above), Is.True, "The ground layer has nothing above it.");
            Assert.That(above.Owner, Is.EqualTo(stack.Layers[1]));

            Assert.That(zLevels.TryMapDown(stack.Orbit, out var below), Is.True, "The orbit layer has nothing below it.");
            Assert.That(below.Owner, Is.EqualTo(stack.Layers[^2]));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<MapGridComponent>(stack.Ground), Is.True,
                    "The ground layer needs a mapgrid to be solid.");
                Assert.That(entMan.HasComponent<BiomeComponent>(stack.Ground), Is.True,
                    "The ground layer should carry the biome.");
                Assert.That(entMan.HasComponent<MapLightComponent>(stack.Ground), Is.True,
                    "The ground layer lost its map light to the network registry.");

                Assert.That(entMan.HasComponent<MapGridComponent>(stack.Orbit), Is.False,
                    "A mapgrid on the orbit layer disables arriving hulls and blocks climbs.");
                // Without this light the engine falls back to black and orbit is unlit.
                Assert.That(entMan.TryGetComponent(stack.Orbit, out MapLightComponent? orbitLight), Is.True,
                    "Orbit has no map light at all, so everything parked there renders pitch black.");
                Assert.That(orbitLight!.AmbientLightColor, Is.EqualTo(OrbitAmbient),
                    "Orbit's ambient light is not the dim one the surface prototype asks for.");
                Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(stack.Orbit), Is.True,
                    "The top layer is not marked as orbit.");
            }

            // Orbit is entered from the shuttle console, not as an FTL destination.
            Assert.That(entMan.HasComponent<FTLDestinationComponent>(stack.Orbit), Is.False,
                "The orbit layer is an FTL destination again; it is entered from the console's orbit button.");
        });

        await Teardown(pair, stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>Each layer keeps its own atmosphere once the post-init fixups undo the registry stamp.</summary>
    [Test]
    public async Task AtmosphereIsPerLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var atmos = server.System<AtmosphereSystem>();

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(IsSpace(atmos, stack.Ground), Is.False, "The ground layer should not be space.");

                // Every air layer, including depth 3, has real air.
                for (var depth = 1; depth <= AirLayerCount; depth++)
                {
                    Assert.That(IsSpace(atmos, stack.Layers[depth]), Is.False, $"Air layer {depth} should not be space.");
                }

                Assert.That(IsSpace(atmos, stack.Orbit), Is.True, "The orbit layer should be vacuum.");
            }

            var ground = Mixture(atmos, stack.Ground);
            Assert.That(ground, Is.Not.Null, "The ground layer has no map mixture.");

            // The mixtures differ only in temperature, which shows the fixup ran.
            Assert.That(ground!.Temperature, Is.EqualTo(GroundTemperature).Within(0.01f),
                $"The ground layer kept the network registry's {RegistryTemperature} K air instead of its own surface mixture.");
        });

        await Teardown(pair, stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>Asclepiu has air layers but no cloud deck, which would hide the ground from orbit.</summary>
    [Test]
    public async Task AsclepiuHasNoCloudLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);

        await server.WaitAssertion(() =>
        {
            Assert.That(stack.Layers, Has.Count.EqualTo(LayerCount),
                "Dropping the cloud layer must not change the depth of the stack.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var layer in stack.Layers)
                {
                    Assert.That(entMan.HasComponent<CEZCloudLayerComponent>(layer), Is.False,
                        $"Layer {entMan.GetComponent<CEZMapComponent>(layer).Depth} is a cloud deck, which blocks the view from orbit.");
                }

                // Every air layer carries map light and gravity.
                for (var depth = 1; depth <= AirLayerCount; depth++)
                {
                    var layer = stack.Layers[depth];
                    Assert.That(entMan.HasComponent<MapLightComponent>(layer), Is.True,
                        $"Air layer {depth} has no map light.");
                    Assert.That(entMan.HasComponent<GravityComponent>(layer), Is.True,
                        $"Air layer {depth} has no inherent gravity.");
                    Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(layer), Is.False,
                        $"Air layer {depth} is marked as orbit.");
                }

                Assert.That(entMan.GetComponent<CEZMapComponent>(stack.Orbit).Depth, Is.EqualTo(LayerCount - 1),
                    "Orbit must stay at depth 4, because every fall duration is keyed to it.");
            }
        });

        await Teardown(pair, stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull parked in orbit with a cold gravgen stays there.</summary>
    [Test]
    public async Task GridOnOrbitLayerDoesNotFall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        var ship = await SpawnShip(pair, stack.Orbit);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<CEZGridFallerComponent>(ship), Is.True,
                "Precondition: the hull is on a z-network and subject to the fall gate.");
            Assert.That(entMan.GetComponent<TransformComponent>(ship).MapUid, Is.EqualTo(stack.Orbit),
                "Precondition: the hull starts on the orbit layer.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(ship).MapUid;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(mapUid, Is.EqualTo(stack.Orbit), "The hull left the orbit layer.");
                Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.False,
                    "The hull started falling out of orbit.");
            }
        });

        await Teardown(pair, stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>The same hull on an air layer plummets once the grace window is up.</summary>
    [Test]
    public async Task GridOnAirLayerFallsWithoutGravgen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var stack = await BuildStandalone(pair);
        var ship = await SpawnShip(pair, stack.Air);

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<CEZGridFallerComponent>(ship), Is.True,
                "Precondition: the hull is subject to the fall gate."));

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var mapUid = entMan.GetComponent<TransformComponent>(ship).MapUid;
            Assert.That(entMan.HasComponent<CEZTransitMapComponent>(mapUid), Is.True,
                "The hull should have fallen off the air layer into transit.");
        });

        await Teardown(pair, stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>An orbit layer is never an FTL destination at any range.</summary>
    [Test]
    public async Task OrbitIsNeverAnFTLDestination()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        await EnableFeature(pair);
        var sector = await BuildForSectorBody(pair);
        var ship = await SpawnShip(pair, sector.SectorMap);

        var orbitMapId = entMan.GetComponent<MapComponent>(sector.Stack.Orbit).MapId;

        // Parked close to the body.
        await MoveTo(pair, ship, sector.SectorMap, new Vector2(500f, 0f));

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.HasComponent<FTLDestinationComponent>(sector.Stack.Orbit), Is.False,
                    "The orbit layer is registered as an FTL destination.");
                Assert.That(shuttles.CanFTLTo(ship, orbitMapId, EntityUid.Invalid), Is.False,
                    "A hull beside the sector body must not be able to FTL into orbit.");
            }
        });

        // Even hand-registered, the gate refuses it.
        await server.WaitPost(() => shuttles.TryAddFTLDestination(orbitMapId, true, false, false, out _));

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(ship, orbitMapId, EntityUid.Invalid), Is.False,
                "WfAllowFTL let a hull jump into an orbit layer that was hand-registered as a destination."));

        await MoveTo(pair, ship, sector.Stack.Orbit, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(ship, orbitMapId, EntityUid.Invalid), Is.True,
                "A hull already on the orbit layer must still be able to target its own map."));

        await Teardown(pair, sector.Stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>A hull on a surface layer cannot FTL out; only orbit can.</summary>
    [Test]
    public async Task CannotFTLOffASurfaceLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        await EnableFeature(pair);
        var sector = await BuildForSectorBody(pair);
        var ship = await SpawnShip(pair, sector.SectorMap);

        var sectorMapId = entMan.GetComponent<MapComponent>(sector.SectorMap).MapId;

        // The sector map is an ordinary open destination.
        await server.WaitPost(() => shuttles.TryAddFTLDestination(sectorMapId, true, false, false, out _));

        await MoveTo(pair, ship, sector.Stack.Ground, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(ship, sectorMapId, EntityUid.Invalid), Is.False,
                "A hull resting on the surface must not be able to FTL straight off the planet."));

        await MoveTo(pair, ship, sector.Stack.Air, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(ship, sectorMapId, EntityUid.Invalid), Is.False,
                "A hull on an air layer must not be able to FTL off the planet."));

        await MoveTo(pair, ship, sector.Stack.Orbit, Vector2.Zero);

        await server.WaitAssertion(() =>
            Assert.That(shuttles.CanFTLTo(ship, sectorMapId, EntityUid.Invalid), Is.True,
                "Leaving orbit for the sector map is the one jump off a planet that must work."));

        await Teardown(pair, sector.Stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>No FTL starts from inside an atmosphere by any path, while the orbit hop still gets through.</summary>
    [Test]
    public async Task NoFTLStartsFromInsideAnAtmosphere()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var shuttles = server.System<ShuttleSystem>();

        await EnableFeature(pair);
        var sector = await BuildForSectorBody(pair);
        var ship = await SpawnShip(pair, sector.SectorMap);

        var sectorMapId = entMan.GetComponent<MapComponent>(sector.SectorMap).MapId;

        await server.WaitPost(() => shuttles.TryAddFTLDestination(sectorMapId, true, false, false, out _));

        foreach (var (layer, what) in new[]
                 {
                     (sector.Stack.Ground, "the surface"),
                     (sector.Stack.Air, "an air layer"),
                 })
        {
            await MoveTo(pair, ship, layer, Vector2.Zero);

            await server.WaitAssertion(() =>
            {
                var shuttle = entMan.GetComponent<ShuttleComponent>(ship);

                shuttles.FTLToCoordinates(ship, shuttle, new EntityCoordinates(sector.SectorMap, Vector2.Zero), Angle.Zero);

                Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.False,
                    $"A hull on {what} started an FTL jump straight off the planet.");
            });
        }

        await MoveTo(pair, ship, sector.Stack.Orbit, Vector2.Zero);

        await server.WaitAssertion(() =>
        {
            var shuttle = entMan.GetComponent<ShuttleComponent>(ship);

            shuttles.FTLToCoordinates(ship, shuttle, new EntityCoordinates(sector.SectorMap, Vector2.Zero), Angle.Zero);

            Assert.That(entMan.HasComponent<FTLComponent>(ship), Is.True,
                "The gate ate the one departure that must work: leaving orbit for the sector map.");
        });

        // The hull is mid-jump and the teardown below takes the maps out from under it.
        await server.WaitPost(() => entMan.DeleteEntity(ship));
        await server.WaitRunTicks(1);

        await Teardown(pair, sector.Stack.Network);
        await pair.CleanReturnAsync();
    }

    /// <summary>Tearing a network down takes every layer with it and clears the sector body's record of it.</summary>
    [Test]
    public async Task DeleteNetworkRemovesEveryLayer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var sector = await BuildForSectorBody(pair);

        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(sector.Body).Network, Is.Not.Null,
                "Precondition: the sector body records its network."));

        await Teardown(pair, sector.Stack.Network);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var layer in sector.Stack.Layers)
                {
                    Assert.That(entMan.EntityExists(layer) && !entMan.IsQueuedForDeletion(layer), Is.False,
                        $"Layer {layer} outlived its network.");
                }

                Assert.That(entMan.EntityExists(sector.Stack.Network) && !entMan.IsQueuedForDeletion(sector.Stack.Network),
                    Is.False, "The network entity outlived its deletion.");
                Assert.That(entMan.GetComponent<WFSectorPlanetComponent>(sector.Body).Network, Is.Null,
                    "The sector body still points at a deleted network.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The planet spawn admin command builds a whole stack.</summary>
    [Test]
    public async Task WfPlanetSpawnCommandBuildsStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);

        await server.WaitPost(() => server.ConsoleHost.ExecuteCommand(null, $"wfplanet spawn {Surface}"));
        await server.WaitRunTicks(1);

        var network = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            var found = new List<EntityUid>();
            var query = entMan.AllEntityQueryEnumerator<WFPlanetNetworkComponent>();

            while (query.MoveNext(out var uid, out _))
            {
                found.Add(uid);
            }

            Assert.That(found, Has.Count.EqualTo(1), "The command should have built exactly one network.");

            network = found[0];
            var comp = entMan.GetComponent<WFPlanetNetworkComponent>(network);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(comp.Layers, Has.Count.EqualTo(LayerCount), "The command built the wrong number of layers.");
                Assert.That(entMan.HasComponent<WFOrbitLayerComponent>(comp.OrbitMap), Is.True,
                    "The command's stack has no orbit layer.");
                Assert.That(entMan.HasComponent<MapGridComponent>(comp.GroundMap), Is.True,
                    "The command's ground layer is not solid.");
            }
        });

        await Teardown(pair, network);
        await pair.CleanReturnAsync();
    }

    /// <summary>Turns the feature on for this pair through the shared fixture.</summary>
    private static Task EnableFeature(TestPair pair)
    {
        return PlanetFixture.EnableFeature(pair);
    }

    /// <summary>Builds an unowned Asclepiu stack at the origin, with no sector body behind it.</summary>
    private static async Task<Stack> BuildStandalone(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var stack = new Stack();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(Surface);
            var built = networks.BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);

            Assert.That(built, Is.Not.Null, "The planet network failed to build.");

            stack.Network = built!.Value;
            stack.Layers = new List<EntityUid>(entMan.GetComponent<WFPlanetNetworkComponent>(stack.Network).Layers);
        });

        await server.WaitRunTicks(1);
        return stack;
    }

    /// <summary>A throwaway sector map, a sector body on it, and the stack that body owns.</summary>
    private sealed class SectorFixture
    {
        public EntityUid SectorMap;
        public EntityUid Body;
        public Stack Stack = new();
    }

    /// <summary>Builds a stack through the registered-body path.</summary>
    private static async Task<SectorFixture> BuildForSectorBody(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var registry = server.System<WFPlanetRegistrySystem>();
        var map = await pair.CreateTestMap();
        var fixture = new SectorFixture { SectorMap = map.MapUid };

        await server.WaitPost(() =>
        {
            fixture.Body = entMan.SpawnEntity(PlanetBody, new MapCoordinates(Vector2.Zero, map.MapId));

            // The production writer, so Sanctioned mirrors the surface prototype.
            var sector = registry.ApplySurface(fixture.Body, proto.Index<WFPlanetSurfacePrototype>(Surface));

            Assert.That(networks.TryBuildNetwork(sector, out var network), Is.True,
                "The sector body's planet network failed to build.");

            fixture.Stack.Network = network;
            fixture.Stack.Layers = new List<EntityUid>(entMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
        });

        await server.WaitRunTicks(1);
        return fixture;
    }

    /// <summary>A 3x3 hull that is Dynamic, because the fall gate skips static bodies outright.</summary>
    private static async Task<EntityUid> SpawnShip(TestPair pair, EntityUid layer)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapMan = server.ResolveDependency<IMapManager>();
        var maps = server.System<SharedMapSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var ship = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var grid = mapMan.CreateGridEntity(entMan.GetComponent<MapComponent>(layer).MapId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 3; y++)
                {
                    tiles.Add((new Vector2i(x, y), new Tile(1)));
                }
            }

            maps.SetTiles(grid.Owner, grid.Comp, tiles);
            entMan.EnsureComponent<ShuttleComponent>(grid.Owner);
            shuttles.Enable(grid.Owner, force: true);
            ship = grid.Owner;
        });

        await server.WaitRunTicks(2);
        return ship;
    }

    /// <summary>Parks a hull on a map at a world offset, the way an FTL arrival would leave it.</summary>
    private static async Task MoveTo(TestPair pair, EntityUid ship, EntityUid map, Vector2 position)
    {
        var server = pair.Server;
        var transform = server.System<SharedTransformSystem>();

        await server.WaitPost(() => transform.SetCoordinates(ship, new EntityCoordinates(map, position)));
        await server.WaitRunTicks(1);
    }

    /// <summary>Tears the stack down and sweeps whatever transit maps a falling test left behind.</summary>
    private static async Task Teardown(TestPair pair, EntityUid network)
    {
        var server = pair.Server;
        var networks = server.System<WFPlanetNetworkSystem>();

        await server.WaitPost(() => networks.DeleteNetwork(network));
        await server.WaitRunTicks(5);
    }

    /// <summary>The map's own space flag, read through the atmos API rather than the restricted component.</summary>
    private static bool IsSpace(AtmosphereSystem atmos, EntityUid map)
    {
        return atmos.IsTileSpace(null, new Entity<MapAtmosphereComponent?>(map, null), Vector2i.Zero);
    }

    /// <summary>The map's own default mixture, read through the atmos API.</summary>
    private static GasMixture? Mixture(AtmosphereSystem atmos, EntityUid map)
    {
        return atmos.GetTileMixture(null, new Entity<MapAtmosphereComponent?>(map, null), Vector2i.Zero);
    }
}
