#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Planets;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>Shared planet test scaffolding, pulled in with <c>using static</c>.</summary>
public static class PlanetFixture
{
    /// <summary>A mini gravity generator that starts cold and is rated like a capital ship's: 3000 mass of lift.</summary>
    public const string GravgenProto = "WFPlanetTestGravgen";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFPlanetTestGravgen
  parent: GravityGeneratorMini
  components:
  - type: PowerCharge
    idlePower: 15
    activePower: 500
    charge: 0
    chargeRate: 0.00417
  - type: GravityGenerator
    lightRadiusMin: 0.75
    lightRadiusMax: 2.5
    maxHandledMass: 3000
";

    /// <summary>The world every fixture builds its stack from.</summary>
    public const string SurfaceProto = "WFSurfaceAsclepiu";

    /// <summary>The sector body every owned stack hangs off.</summary>
    public const string PlanetBodyProto = "PlanetEntity";

    /// <summary>Deck plating, used for both hull decks and hand-laid ground.</summary>
    public const string FloorTile = "FloorSteel";

    /// <summary>What <see cref="AttachViewer"/> attaches the session to by default: a mob, not a ghost.</summary>
    public const string ViewerProto = "MobHuman";

    /// <summary>Turns the feature on for this pair; TestPair reverts the change when the pair is returned.</summary>
    public static async Task EnableFeature(TestPair pair)
    {
        await pair.Server.WaitPost(() =>
        {
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);

            // Tests wait a fixed time for terrain; the ones about the loading budget turn it back on.
            pair.Server.CfgMan.SetCVar(PlanetCVars.TerrainLoadBudget, 0f);
        });
    }

    /// <summary>Builds an unowned Asclepiu stack at the origin and returns its layers, ground first.</summary>
    public static async Task<List<EntityUid>> BuildStandalone(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var networks = server.System<WFPlanetNetworkSystem>();
        var layers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var surface = proto.Index<WFPlanetSurfacePrototype>(SurfaceProto);
            var built = networks.BuildNetwork(surface, Vector2.Zero, "Asclepiu", null);

            Assert.That(built, Is.Not.Null, "The planet network failed to build.");
            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(built!.Value).Layers);
        });

        await server.WaitRunTicks(1);
        return layers;
    }

    /// <summary>Builds an Asclepiu stack owned by a sector body on its own map.</summary>
    public static async Task<(List<EntityUid> Layers, EntityUid Body, EntityUid BodyMap)> BuildOwnedStack(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var networks = server.System<WFPlanetNetworkSystem>();
        var map = await pair.CreateTestMap();
        var layers = new List<EntityUid>();
        var body = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity(PlanetBodyProto, new MapCoordinates(Vector2.Zero, map.MapId));

            // The production writer, so Sanctioned mirrors the surface prototype.
            ApplySurfaceTo(pair, body, SurfaceProto);

            var sector = new Entity<WFSectorPlanetComponent>(body, entMan.GetComponent<WFSectorPlanetComponent>(body));

            Assert.That(networks.TryBuildNetwork(sector, out var network), Is.True,
                "The sector body's planet network failed to build.");

            layers.AddRange(entMan.GetComponent<WFPlanetNetworkComponent>(network).Layers);
        });

        await server.WaitRunTicks(1);
        return (layers, body, map.MapUid);
    }

    /// <summary>Attaches the session to a fresh mob, or another prototype; biomes only generate around non-ghosts.</summary>
    public static async Task<EntityUid> AttachViewer(TestPair pair, EntityUid map, Vector2 pos, string proto = ViewerProto)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var viewer = EntityUid.Invalid;

        Assert.That(pair.Player, Is.Not.Null,
            "AttachViewer needs a session, so the pair has to be built with PoolSettings { Connected = true }.");

        await server.WaitPost(() =>
        {
            viewer = entMan.SpawnEntity(proto, new EntityCoordinates(map, pos));
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
        });

        await pair.RunTicksSync(2);
        return viewer;
    }

    /// <summary>Stamps a surface onto a test body through the production writer; call on the server thread.</summary>
    public static void ApplySurfaceTo(TestPair pair, EntityUid body, string surfaceId)
    {
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        pair.Server.System<WFPlanetRegistrySystem>()
            .ApplySurface(body, proto.Index<WFPlanetSurfacePrototype>(surfaceId));
    }

    /// <summary>Lays deck plating over a rectangle without touching BiomeComponent.ModifiedTiles.</summary>
    public static async Task LayTiles(TestPair pair, EntityUid ground, Vector2i from, Vector2i to)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var floor = new Tile(tileDefs[FloorTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>();

            for (var x = from.X; x <= to.X; x++)
            for (var y = from.Y; y <= to.Y; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(ground, grid, tiles);
        });

        await server.WaitRunTicks(1);
    }

    /// <summary>Tears a stack down through its own network entity.</summary>
    public static async Task Teardown(TestPair pair, List<EntityUid> layers)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var networks = server.System<WFPlanetNetworkSystem>();

        await server.WaitPost(() =>
        {
            if (entMan.TryGetComponent(layers[0], out CEZMapComponent? zMap) && zMap.NetworkUid is { } network)
                networks.DeleteNetwork(network);
        });

        await server.WaitRunTicks(5);
    }

    /// <summary>Every entity parented straight to a grid; machine parts live inside their machine, not here.</summary>
    public static IEnumerable<EntityUid> Children(IEntityManager entMan, EntityUid grid)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var xform))
        {
            if (xform.ParentUid == grid)
                found.Add(uid);
        }

        return found;
    }

    /// <summary>Width and height of the test hull, in tiles.</summary>
    private const int HullSize = 15;

    /// <summary>Builds a 15x15 flyable test hull (console, gyro, FTL drive, cold gravgen, four thrusters) and lets it settle.</summary>
    public static async Task<EntityUid> BuildHull(TestPair pair, MapId map, Vector2? offset = null)
    {
        var grid = EntityUid.Invalid;

        await pair.Server.WaitPost(() =>
        {
            var hull = CreateHull(pair, map, offset ?? Vector2.Zero, HullSize, HullSize);

            SpawnOnHull(pair, hull, "ComputerShuttle", 2, 2);
            SpawnOnHull(pair, hull, "DebugGyroscope", 7, 2);
            // A hull has no FTL range without a powered drive.
            SpawnOnHull(pair, hull, "MachineFTLDrive", 11, 2);
            SpawnOnHull(pair, hull, GravgenProto, 7, 7);
            SpawnOnHull(pair, hull, "AirlockShuttle", 0, 7, 270);

            // Thruster facing picks the LinearThrust index: South 0, East 1, North 2, West 3.
            SpawnOnHull(pair, hull, "DebugThruster", 1, 1);
            SpawnOnHull(pair, hull, "DebugThruster", 13, 1, 90);
            SpawnOnHull(pair, hull, "DebugThruster", 1, 13, 180);
            SpawnOnHull(pair, hull, "DebugThruster", 13, 13, 270);

            FinishHull(pair, hull);
            grid = hull;
        });

        await pair.Server.WaitRunTicks(pair.SecondsToTicks(1f));
        return grid;
    }

    /// <summary>Builds a 7x9 lander on two landing thrusters with no gravgen and lets it settle.</summary>
    public static async Task<EntityUid> BuildLander(TestPair pair, MapId map, Vector2? offset = null)
    {
        var grid = EntityUid.Invalid;

        await pair.Server.WaitPost(() =>
        {
            var hull = CreateHull(pair, map, offset ?? new Vector2(100f, 0f), 7, 9);

            SpawnOnHull(pair, hull, "ComputerShuttle", 3, 1);
            SpawnOnHull(pair, hull, "WFThrusterLanding", 2, 4);
            SpawnOnHull(pair, hull, "WFThrusterLanding", 4, 4);
            // Without one the hull can strafe but never turn.
            SpawnOnHull(pair, hull, "DebugGyroscope", 3, 4);
            SpawnOnHull(pair, hull, "AirlockShuttle", 3, 0);

            SpawnOnHull(pair, hull, "DebugThruster", 0, 1);
            SpawnOnHull(pair, hull, "DebugThruster", 6, 1, 90);
            SpawnOnHull(pair, hull, "DebugThruster", 0, 7, 180);
            SpawnOnHull(pair, hull, "DebugThruster", 6, 7, 270);

            FinishHull(pair, hull);
            grid = hull;
        });

        await pair.Server.WaitRunTicks(pair.SecondsToTicks(1f));
        return grid;
    }

    /// <summary>Creates a grid and floors it in one SetTiles call, because every tile change re-floods the CE connectors.</summary>
    private static EntityUid CreateHull(TestPair pair, MapId map, Vector2 offset, int width, int height)
    {
        var server = pair.Server;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(map);
        server.System<SharedTransformSystem>().SetLocalPosition(grid.Owner, offset);

        var floor = new Tile(tileDefs[FloorTile].TileId);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>(width * height);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            tiles.Add((new Vector2i(x, y), floor));
        }

        server.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
        return grid.Owner;
    }

    /// <summary>Spawns one prototype on the centre of a hull tile, facing the given angle, needing no power.</summary>
    private static void SpawnOnHull(TestPair pair, EntityUid hull, string proto, int x, int y, double degrees = 0)
    {
        var server = pair.Server;
        var uid = server.EntMan.SpawnEntity(proto, new EntityCoordinates(hull, new Vector2(x + 0.5f, y + 0.5f)));

        if (degrees != 0)
            server.System<SharedTransformSystem>().SetLocalRotation(uid, Angle.FromDegrees(degrees));

        // No cabling on a test hull: PowerNetSystem short-circuits on !NeedsPower, so machines just run.
        server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
    }

    /// <summary>Turns a finished code-built grid into a map-initialised, flyable shuttle.</summary>
    private static void FinishHull(TestPair pair, EntityUid hull)
    {
        var entMan = pair.Server.EntMan;

        entMan.EnsureComponent<ShuttleComponent>(hull);
        pair.Server.System<ShuttleSystem>().Enable(hull, force: true);

        // Code-built grids aren't map-initialised, so later components (the ForceAnchor lock) get no MapInit.
        entMan.RunMapInit(hull, entMan.GetComponent<MetaDataComponent>(hull));
    }


    /// <summary>Builds a bare square debris grid with no console, thrusters or shuttle component.</summary>
    public static async Task<EntityUid> BuildDebris(TestPair pair, MapId map, int size = 3, Vector2? offset = null)
    {
        var server = pair.Server;
        var mapMan = server.ResolveDependency<IMapManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var grid = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var built = mapMan.CreateGridEntity(map);
            transform.SetLocalPosition(built.Owner, offset ?? Vector2.Zero);

            var floor = new Tile(tileDefs[FloorTile].TileId);
            var tiles = new List<(Vector2i GridIndices, Tile Tile)>(size * size);

            for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            maps.SetTiles(built.Owner, built.Comp, tiles);
            grid = built.Owner;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));
        return grid;
    }

    /// <summary>Map-initialises a code-built hull, or ForceAnchorComponent would never apply.</summary>
    public static async Task MapInitHull(TestPair pair, EntityUid grid)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() => entMan.RunMapInit(grid, entMan.GetComponent<MetaDataComponent>(grid)));
        await server.WaitRunTicks(1);
    }

    /// <summary>Winds every PowerCharge machine on a hull up by raising its access-locked charge rate.</summary>
    public static async Task Energise(TestPair pair, EntityUid grid)
    {
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            foreach (var uid in Children(entMan, grid))
            {
                if (entMan.TryGetComponent(uid, out PowerChargeComponent? charge))
                    ChargeRateProperty.SetValue(charge, 10f);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));
    }

    /// <summary>The shuttle console resting on a hull, or Invalid.</summary>
    public static EntityUid FindShuttleConsole(IEntityManager entMan, EntityUid hull)
    {
        foreach (var uid in Children(entMan, hull))
        {
            if (entMan.HasComponent<ShuttleConsoleComponent>(uid))
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>Seats a pilot at the hull's shuttle console holding the descend key, and returns the pilot.</summary>
    public static Task<EntityUid> HoldDescend(TestPair pair, EntityUid hull)
    {
        return HoldVertical(pair, hull, ShuttleButtons.DescendZ);
    }

    /// <summary>Seats a pilot at the hull's own shuttle console holding one of the two vertical keys.</summary>
    public static async Task<EntityUid> HoldVertical(TestPair pair, EntityUid hull, ShuttleButtons button)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var pilot = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var console = FindShuttleConsole(entMan, hull);

            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "The hull has no shuttle console to pilot from.");

            pilot = entMan.SpawnEntity(ViewerProto, new EntityCoordinates(hull, new Vector2(2.5f, 3.5f)));
            var pilotComp = entMan.EnsureComponent<PilotComponent>(pilot);
            pilotComp.Console = console;
            pilotComp.HeldButtons = button;
        });

        await server.WaitRunTicks(1);

        return pilot;
    }

    /// <summary>Drops a hull out of orbit through the console's system call and returns the refusal, if any.</summary>
    /// <param name="settle">Seconds to tick afterwards; zero leaves the hull where the call put it.</param>
    public static async Task<string?> EnterAtmosphere(TestPair pair, EntityUid hull, bool confirmed = true, float settle = 1f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbit = server.System<WFOrbitEntrySystem>();
        string? reason = null;

        await server.WaitPost(() =>
        {
            var console = FindShuttleConsole(entMan, hull);

            Assert.That(console, Is.Not.EqualTo(EntityUid.Invalid), "The hull has no shuttle console to descend from.");

            orbit.TryEnterAtmosphere(console, confirmed, out reason);
        });

        if (settle > 0f)
            await server.WaitRunTicks(pair.SecondsToTicks(settle));

        return reason;
    }

    /// <summary>Bolts landing thrusters onto a hull; rated force is lift times 9.81.</summary>
    public static async Task<List<EntityUid>> AddLandingThrusters(TestPair pair, EntityUid hull, int count, float lift = 50f)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var receiver = server.System<SharedPowerReceiverSystem>();
        var thrusters = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var i = 0; i < count; i++)
            {
                var uid = entMan.SpawnEntity("WFThrusterLanding", new EntityCoordinates(hull, new Vector2(1.5f + i, 1.5f)));

                // No cabling on a code-built hull.
                receiver.SetNeedsPower(uid, false);
                server.System<ThrusterSystem>().WfSetRatedThrust(uid, lift * 9.81f);

                thrusters.Add(uid);
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        return thrusters;
    }

    /// <summary>Isolates gravgen and converted-lift tests from the hull's ordinary engines.</summary>
    public static async Task RemoveOrdinaryThrusters(TestPair pair, EntityUid hull)
    {
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            foreach (var uid in Children(entMan, hull))
            {
                if (entMan.TryGetComponent<ThrusterComponent>(uid, out var thruster)
                    && thruster.Type == ThrusterType.Linear
                    && !entMan.HasComponent<WFLandingThrusterComponent>(uid))
                    entMan.DeleteEntity(uid);
            }
        });
    }

    /// <summary>PowerChargeComponent.ChargeRate, which is access-locked to its own system.</summary>
    private static readonly PropertyInfo ChargeRateProperty = Resolve("ChargeRate");

    /// <summary>One access-locked charge property, failing loudly here rather than as a null deref in a test.</summary>
    private static PropertyInfo Resolve(string name)
    {
        var property = typeof(PowerChargeComponent).GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(property, Is.Not.Null, $"PowerChargeComponent.{name} was not found.");
        return property!;
    }
}
