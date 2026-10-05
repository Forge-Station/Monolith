#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Atmosphere;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Piping.Unary.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Planet ground has an atmosphere that simulates only what is built on it; the bare ground keeps the planet's air.</summary>
[TestFixture]
[TestOf(typeof(WFTerrainAtmosphereSystem))]
public sealed class TerrainAtmosphereTest
{
    /// <summary>Ground the Asclepiu biome lays.</summary>
    private const string Grass = "FloorPlanetGrass";

    private const string Wall = "WallSolid";

    private const string Canister = "NitrogenCanister";

    /// <summary>A floored room walled in on bare ground keeps what a canister lets out, and loses it through a breach.</summary>
    [Test]
    public async Task ARoomBuiltOnAPlanetHoldsItsAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];
        var room = Area(Vector2i.Zero, new Vector2i(2, 2)).ToList();
        var walls = new Dictionary<Vector2i, EntityUid>();

        await server.WaitPost(() =>
        {
            SetTiles(pair, ground, Grass, Area(new Vector2i(-4, -4), new Vector2i(6, 6)));
            SetTiles(pair, ground, FloorTile, room);

            // The walls stand on the bare ground around the floor.
            foreach (var index in Area(new Vector2i(-1, -1), new Vector2i(3, 3)).Except(room))
            {
                walls[index] = entMan.SpawnEntity(Wall, new EntityCoordinates(ground, index.X + 0.5f, index.Y + 0.5f));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        var planetMoles = 0f;
        var canister = EntityUid.Invalid;
        var roomBefore = 0f;
        var canisterBefore = 0f;

        await server.WaitAssertion(() =>
        {
            var planetAir = atmos.GetTileMixture(null, ground, Vector2i.Zero)!;
            planetMoles = planetAir.TotalMoles;
            var airs = room.Select(index => atmos.GetTileMixture(ground, ground, index)).ToList();

            Assert.That(airs, Has.All.Matches<GasMixture?>(air => air is { Immutable: false }),
                "The room's floor shares the planet's air instead of holding its own.");
            Assert.That(RoomMoles(atmos, ground, room) / room.Count, Is.EqualTo(planetMoles).Within(planetMoles * 0.01f),
                "A room built on a planet does not start with the planet's air.");
            Assert.That(atmos.GetTileMixture(ground, ground, new Vector2i(6, 6)), Is.SameAs(planetAir),
                "Bare ground away from the room does not share the planet's air.");
            Assert.That(entMan.GetComponent<GridAtmosphereComponent>(ground).Tiles, Has.Count.LessThanOrEqualTo(25),
                "Atmos tracks more of the planet than the room and the ground beside it.");

            canister = entMan.SpawnEntity(Canister, new EntityCoordinates(ground, 1.5f, 1.5f));
            var comp = entMan.GetComponent<GasCanisterComponent>(canister);
            comp.ReleasePressure = comp.MaxReleasePressure;
            comp.ReleaseValve = true;
            roomBefore = RoomMoles(atmos, ground, room);
            canisterBefore = comp.Air.TotalMoles;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<GasCanisterComponent>(canister);
            var released = canisterBefore - comp.Air.TotalMoles;
            var gained = RoomMoles(atmos, ground, room) - roomBefore;

            Assert.That(released, Is.GreaterThan(planetMoles), "The canister let nothing out.");
            Assert.That(gained, Is.EqualTo(released).Within(released * 0.01f),
                "What the canister let out did not stay in the room.");

            comp.ReleaseValve = false;

            for (var x = 0; x <= 2; x++)
            {
                entMan.DeleteEntity(walls[new Vector2i(x, -1)]);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(20f));

        await server.WaitAssertion(() =>
            Assert.That(RoomMoles(atmos, ground, room) / room.Count, Is.EqualTo(planetMoles).Within(planetMoles * 0.1f),
                "A breached room did not settle back to the planet's air."));

        await pair.CleanReturnAsync();
    }

    /// <summary>Ground nobody built on is never tracked, walls and canisters on it included, and gas let out there is gone.</summary>
    [Test]
    public async Task BareGroundIsLeftToThePlanetsAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];
        var canister = EntityUid.Invalid;
        var canisterBefore = 0f;

        await server.WaitAssertion(() =>
        {
            SetTiles(pair, ground, Grass, Area(Vector2i.Zero, new Vector2i(15, 15)));

            for (var x = 3; x <= 6; x++)
            {
                entMan.SpawnEntity(Wall, new EntityCoordinates(ground, x + 0.5f, 3.5f));
            }

            // Before atmos gets a tick: laying ground and walling it queues nothing.
            Assert.That(entMan.GetComponent<GridAtmosphereComponent>(ground).InvalidatedCoords, Is.Empty,
                "Bare planet ground was queued for atmos.");

            canister = entMan.SpawnEntity(Canister, new EntityCoordinates(ground, 10.5f, 10.5f));
            var comp = entMan.GetComponent<GasCanisterComponent>(canister);
            comp.ReleasePressure = comp.MaxReleasePressure;
            comp.ReleaseValve = true;
            canisterBefore = comp.Air.TotalMoles;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var gridAtmos = entMan.GetComponent<GridAtmosphereComponent>(ground);

            Assert.That(gridAtmos.Tiles, Is.Empty, "Atmos tracks bare planet ground.");
            Assert.That(atmos.GetTileMixture(ground, ground, new Vector2i(10, 10)),
                Is.SameAs(atmos.GetTileMixture(null, ground, Vector2i.Zero)),
                "Bare ground does not share the planet's air.");
            Assert.That(entMan.GetComponent<GasCanisterComponent>(canister).Air.TotalMoles, Is.LessThan(canisterBefore),
                "A canister on bare ground let nothing out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Terrain the biome loads around a player, rocks and all, is never tracked.</summary>
    [Test]
    public async Task LoadedTerrainCostsAtmosNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];
        var viewer = await AttachViewer(pair, ground, Vector2.Zero);

        // Keeps the viewer on the layer while the ground under it loads.
        await server.WaitPost(() => entMan.RemoveComponent<CEZPhysicsComponent>(viewer));
        await pair.RunTicksSync(pair.SecondsToTicks(4f));

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var gridAtmos = entMan.GetComponent<GridAtmosphereComponent>(ground);
            var loaded = server.System<SharedMapSystem>().GetAllTiles(ground, grid).Count();

            Assert.That(loaded, Is.GreaterThan(500), "The biome loaded no terrain around the viewer.");
            Assert.That(gridAtmos.Tiles, Is.Empty, "Atmos tracks terrain the biome loaded.");
            Assert.That(gridAtmos.InvalidatedCoords, Is.Empty, "Terrain the biome loaded is queued for atmos.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A tile built on a planet starts with the air around it, at its temperature: the planet's where no air reaches it,
    /// its neighbour's where one has air. Thrascias, because its air is far from room temperature.
    /// </summary>
    [Test]
    public async Task ATileBuiltOnAPlanetStartsWithTheAirAroundIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();

        await EnableFeature(pair);
        var ground = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var surface = server.ResolveDependency<IPrototypeManager>().Index<WFPlanetSurfacePrototype>("WFSurfaceThrascias");
            var network = server.System<WFPlanetNetworkSystem>().BuildNetwork(surface, Vector2.Zero, "Thrascias", null);
            ground = entMan.GetComponent<WFPlanetNetworkComponent>(network!.Value).GroundMap;
        });

        // One tile walled in on all four sides, and two side by side walled in together.
        var alone = new Vector2i(2, 2);
        var first = new Vector2i(6, 2);
        var second = new Vector2i(7, 2);
        var floors = new[] { alone, first, second };

        await server.WaitPost(() =>
        {
            SetTiles(pair, ground, "FloorSnow", Area(Vector2i.Zero, new Vector2i(9, 4)));

            foreach (var index in Area(new Vector2i(1, 1), new Vector2i(8, 3)).Except(floors))
            {
                entMan.SpawnEntity(Wall, new EntityCoordinates(ground, index.X + 0.5f, index.Y + 0.5f));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        await server.WaitPost(() => SetTiles(pair, ground, "Plating", new[] { alone, first }));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));
        await server.WaitPost(() => SetTiles(pair, ground, "Plating", new[] { second }));
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var planetAir = atmos.GetTileMixture(null, ground, Vector2i.Zero)!;

            using (Assert.EnterMultipleScope())
            {
                foreach (var index in floors)
                {
                    var air = atmos.GetTileMixture(ground, ground, index);

                    Assert.That(air is { Immutable: false }, $"The plated tile at {index} does not hold its own air.");
                    Assert.That(air!.Temperature, Is.EqualTo(planetAir.Temperature).Within(1f),
                        $"The tile built at {index} did not start at the planet's temperature.");
                    Assert.That(air.Pressure, Is.EqualTo(planetAir.Pressure).Within(planetAir.Pressure * 0.01f),
                        $"The tile built at {index} did not start with the planet's air.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship is untouched by all this: sealed, it keeps what a canister lets out, in space and parked on a planet's
    /// bare ground. Breached, it empties into space or settles to the planet's air.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task AShipKeepsItsOwnAir(bool onPlanet)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var atmos = server.System<AtmosphereSystem>();
        var map = EntityUid.Invalid;

        if (onPlanet)
        {
            await EnableFeature(pair);
            map = (await BuildStandalone(pair))[0];
            await server.WaitPost(() => SetTiles(pair, map, Grass, Area(new Vector2i(-4, -4), new Vector2i(10, 10))));
        }
        else
        {
            map = (await pair.CreateTestMap()).MapUid;
        }

        var mapId = MapId.Nullspace;
        await server.WaitPost(() => mapId = entMan.GetComponent<MapComponent>(map).MapId);

        // A 5x5 deck walled around its edge, clear of the test map's own tile.
        var hull = await BuildDebris(pair, mapId, 5, onPlanet ? Vector2.Zero : new Vector2(20f, 20f));
        var cabin = Area(new Vector2i(1, 1), new Vector2i(3, 3)).ToList();
        var walls = new Dictionary<Vector2i, EntityUid>();
        var canister = EntityUid.Invalid;
        var canisterBefore = 0f;
        var cabinBefore = 0f;

        await server.WaitPost(() =>
        {
            foreach (var index in Area(Vector2i.Zero, new Vector2i(4, 4)).Except(cabin))
            {
                walls[index] = entMan.SpawnEntity(Wall, new EntityCoordinates(hull, index.X + 0.5f, index.Y + 0.5f));
            }

            canister = entMan.SpawnEntity(Canister, new EntityCoordinates(hull, 2.5f, 2.5f));
            var comp = entMan.GetComponent<GasCanisterComponent>(canister);
            comp.ReleasePressure = comp.MaxReleasePressure;
            comp.ReleaseValve = true;
            canisterBefore = comp.Air.TotalMoles;

            // On a planet the open deck has already taken some of the planet's air in.
            cabinBefore = RoomMoles(atmos, hull, cabin);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(5f));

        var filled = 0f;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<GasCanisterComponent>(canister);
            var released = canisterBefore - comp.Air.TotalMoles;
            filled = RoomMoles(atmos, hull, cabin);

            Assert.That(released, Is.GreaterThan(100f), "The canister let nothing out into the ship.");
            Assert.That(filled - cabinBefore, Is.EqualTo(released).Within(released * 0.01f),
                "What the canister let out did not stay in the ship.");
            comp.ReleaseValve = false;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(10f));

        await server.WaitAssertion(() =>
        {
            Assert.That(RoomMoles(atmos, hull, cabin), Is.EqualTo(filled).Within(filled * 0.005f), "A sealed ship leaked.");

            if (onPlanet)
            {
                Assert.That(entMan.GetComponent<GridAtmosphereComponent>(map).Tiles, Is.Empty,
                    "A ship parked on bare ground made atmos track the ground.");
            }

            entMan.DeleteEntity(walls[new Vector2i(2, 0)]);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(30f));

        await server.WaitAssertion(() =>
        {
            var settled = RoomMoles(atmos, hull, cabin) / cabin.Count;

            if (onPlanet)
            {
                var planetMoles = atmos.GetTileMixture(null, map, Vector2i.Zero)!.TotalMoles;
                Assert.That(settled, Is.EqualTo(planetMoles).Within(planetMoles * 0.1f),
                    "A ship breached on a planet did not settle to the planet's air.");
            }
            else
            {
                // Spacing is gradual here (atmos.mmos_spacing_speed), so most of it, not all of it.
                Assert.That(settled, Is.LessThan(filled / cabin.Count * 0.1f), "A ship breached in space kept its air.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>With the switch off the ground gets no atmosphere, as before.</summary>
    [Test]
    public async Task TheSwitchLeavesPlanetGroundAlone()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await EnableFeature(pair);
        await server.WaitPost(() => server.CfgMan.SetCVar(PlanetCVars.TerrainAtmosphere, false));
        var ground = (await BuildStandalone(pair))[0];

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<GridAtmosphereComponent>(ground), Is.False);
            Assert.That(entMan.HasComponent<WFTerrainAtmosphereComponent>(ground), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Vector2i> Area(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            yield return new Vector2i(x, y);
        }
    }

    private static void SetTiles(TestPair pair, EntityUid ground, string tile, IEnumerable<Vector2i> indices)
    {
        var server = pair.Server;
        var id = server.ResolveDependency<ITileDefinitionManager>()[tile].TileId;
        var grid = server.EntMan.GetComponent<MapGridComponent>(ground);

        server.System<SharedMapSystem>().SetTiles(ground, grid, indices.Select(index => (index, new Tile(id))).ToList());
    }

    private static float RoomMoles(AtmosphereSystem atmos, EntityUid ground, List<Vector2i> room)
    {
        return room.Sum(index => atmos.GetTileMixture(ground, ground, index)?.TotalMoles ?? 0f);
    }
}
