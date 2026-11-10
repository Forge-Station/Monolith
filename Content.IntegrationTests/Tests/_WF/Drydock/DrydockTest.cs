#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.GameRule;
using Content.Server._NF.Bank;
using Content.Server._WF.Drydock;
using Content.Server._WF.Hailing;
using Content.Server.Destructible;
using Content.Server.Construction.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Hailing;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>
/// Drydock repairs and refits, berth areas and cranes, booking and jobs, and mobile shipyards serving a ship alongside.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDrydockSystem))]
public sealed class DrydockTest
{
    /// <summary>Plating used for every hull tile in these tests.</summary>
    private const string FloorTile = "Plating";

    /// <summary>Tile index of the thruster in the test hull.</summary>
    private static readonly Vector2i ThrusterTile = new(1, 1);

    /// <summary>Tile index of the wall in the test hull.</summary>
    private static readonly Vector2i WallTile = new(3, 3);

    /// <summary>Tile index cleared to create a missing floor tile.</summary>
    private static readonly Vector2i HoleTile = new(0, 4);

    /// <summary>Drydock station position in the tests, away from the test map's own grid.</summary>
    private static readonly Vector2 StationPos = new(500, 0);

    /// <summary>World position of a 5x5 test hull whose center sits in the middle of the South berth.</summary>
    private static readonly Vector2 BerthAHull = StationPos + new Vector2(0.5f, -67f);

    /// <summary>World position of a damaged hull outside the bays, past their open end.</summary>
    private static readonly Vector2 OutsideBayPos = StationPos + new Vector2(0.5f, -115f);

    /// <summary>World position of a mobile shipyard's host grid, clear of the drydock.</summary>
    private static readonly Vector2 HostPos = new(-300f, 0f);

    /// <summary>Funds given to the payer, enough for any repair in these tests.</summary>
    private const int PayerFunds = 1_000_000;

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<WFShipRefitPrototype> ThrusterRefit = "WFRefitThrusters";
    private static readonly ProtoId<WFShipRefitPrototype> GyroscopeRefit = "WFRefitGyroscopes";
    private static readonly ProtoId<WFShipRefitPrototype> LandingRefit = "WFRefitLandingThrusters";
    private static readonly EntProtoId WallProto = "WallSolid";

    /// <summary>A host grid with its crane and console, as a mobile shipyard.</summary>
    private readonly record struct Shipyard(EntityUid Grid, EntityUid Crane, EntityUid Console);

    /// <summary>Lays a plating floor of the given size from the grid origin.</summary>
    public static void SetFloor(TestPair pair, EntityUid grid, int size)
    {
        var server = pair.Server;
        var plating = Plating(pair);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
        {
            tiles.Add((new Vector2i(x, y), new Tile(plating)));
        }

        server.System<SharedMapSystem>().SetTiles(grid, server.EntMan.GetComponent<MapGridComponent>(grid), tiles);
    }

    /// <summary>The tile type ID of the test floor.</summary>
    private static ushort Plating(TestPair pair)
    {
        return pair.Server.ResolveDependency<ITileDefinitionManager>()[FloorTile].TileId;
    }

    /// <summary>Lays a 5x5 plating floor on a grid and spawns the test thruster and wall, both anchored.</summary>
    private static (EntityUid Thruster, EntityUid Wall) BuildHull(TestPair pair, EntityUid grid)
    {
        SetFloor(pair, grid, 5);
        var thruster = SpawnAnchored(pair, grid, "Thruster", ThrusterTile);
        var wall = SpawnAnchored(pair, grid, WallProto, WallTile);
        return (thruster, wall);
    }

    /// <summary>Clears a tile of the grid, leaving a hole in the hull.</summary>
    private static void CutHole(TestPair pair, EntityUid grid, Vector2i tile)
    {
        var server = pair.Server;
        server.System<SharedMapSystem>().SetTiles(grid, server.EntMan.GetComponent<MapGridComponent>(grid),
            new List<(Vector2i, Tile)> { (tile, Tile.Empty) });
    }

    /// <summary>Finds the thruster prototype anchored on a grid tile, or invalid when none is there.</summary>
    private static EntityUid FindThruster(TestPair pair, EntityUid grid, Vector2i tile)
    {
        var em = pair.Server.EntMan;
        var query = em.AllEntityQueryEnumerator<ThrusterComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID != "Thruster")
                continue;

            var xform = em.GetComponent<TransformComponent>(uid);
            if (xform.ParentUid != grid)
                continue;

            var pos = xform.LocalPosition;
            if (new Vector2i((int) MathF.Floor(pos.X), (int) MathF.Floor(pos.Y)) == tile)
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>A repair restores the missing tile, the destroyed entity and the wall's damage; a second pass finds nothing to do.</summary>
    [Test]
    public async Task RepairRestoresTilesEntitiesAndDamage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var maps = server.System<SharedMapSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var ship = server.System<ShipRepairSystem>();
        var damageable = server.System<DamageableSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var plating = Plating(pair);
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        await server.WaitAssertion(() =>
        {
            var (thruster, wall) = BuildHull(pair, grid);
            ship.GenerateRepairData(grid);

            // Damage the wall, then break the hull: a hole, a lost thruster.
            var blunt = new DamageSpecifier(protoMan.Index(Blunt), FixedPoint2.New(20));
            damageable.TryChangeDamage(wall, blunt);
            em.DeleteEntity(thruster);
            CutHole(pair, grid, HoleTile);

            var items = drydock.BuildRepairItems(grid, console);
            Assert.Multiple(() =>
            {
                Assert.That(items.Count(i => i.Kind == WFDrydockWorkKind.Tile), Is.EqualTo(1), "Expected one missing tile.");
                Assert.That(items.Count(i => i.Kind == WFDrydockWorkKind.Entity), Is.EqualTo(1), "Expected one missing entity.");
                Assert.That(items.Count(i => i.Kind == WFDrydockWorkKind.Heal), Is.EqualTo(1), "Expected one damaged entity.");
                Assert.That(items.All(i => i.Cost > 0), Is.True, "Every repair item should cost something.");
            });

            foreach (var item in items)
            {
                drydock.ApplyItem(grid, item);
            }

            var comp = em.GetComponent<MapGridComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(maps.GetTileRef(grid, comp, HoleTile).Tile.TypeId, Is.EqualTo(plating), "The hole was not refloored.");
                Assert.That(FindThruster(pair, grid, ThrusterTile).IsValid(), Is.True, "The thruster was not respawned.");
                Assert.That(em.GetComponent<DamageableComponent>(wall).TotalDamage, Is.EqualTo(FixedPoint2.Zero), "The wall was not healed.");
            });

            Assert.That(drydock.BuildRepairItems(grid, console), Is.Empty, "A repaired ship should have no work left.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship that owns a refit gets it back in repairs: a rebuilt machine is brought back to tier at the repair share,
    /// and a machine past the refit's coverage is brought up at the full price.
    /// </summary>
    [Test]
    public async Task RepairRestoresOwnedRefit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var drydock = server.System<WFDrydockSystem>();
        var parts = server.System<WFPartRefitSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var thrusters = protoMan.Index(ThrusterRefit);
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        EntityUid thruster = default;

        await server.WaitAssertion(() =>
        {
            (thruster, _) = BuildHull(pair, grid);
            server.System<ShipRepairSystem>().GenerateRepairData(grid);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            // The ship bought Thrusters Mk III for its one thruster part.
            refits.Apply(thrusters, thruster, 2);
            refits.SetRefit(grid, ThrusterRefit.Id, 2, 1);
            var fullPrice = thrusters.Tiers[0].Price + thrusters.Tiers[1].Price;

            em.DeleteEntity(thruster);
            var rebuild = drydock.BuildRepairItems(grid, console).Single(i => i.Kind == WFDrydockWorkKind.Entity);
            Assert.That(rebuild.RefitCost, Is.EqualTo((int) Math.Ceiling(fullPrice * console.RefitRepairShare)),
                "A covered part should be restored at the repair share of both tiers.");

            drydock.ApplyItem(grid, rebuild);
            Assert.That(parts.GetTier(FindThruster(pair, grid, ThrusterTile), thrusters), Is.EqualTo(2),
                "The rebuilt thruster should come back at Mk III.");

            // A thruster added after the refit is past its coverage.
            var added = SpawnAnchored(pair, grid, "Thruster", new Vector2i(3, 1));
            var refit = drydock.BuildRepairItems(grid, console).Single(i => i.Kind == WFDrydockWorkKind.Upgrade);
            Assert.Multiple(() =>
            {
                Assert.That(refit.Target, Is.EqualTo(added), "Only the new thruster should be below tier.");
                Assert.That(refit.Cost, Is.EqualTo(fullPrice), "A part past the coverage should pay the full refit.");
            });

            drydock.ApplyItem(grid, refit);
            Assert.That(parts.GetTier(added, thrusters), Is.EqualTo(2), "The new thruster should be brought to Mk III.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The atmospheric refit converts every linear thruster and no gyroscope, keeps a thruster refit's parts and thrust,
    /// and comes back on a rebuilt thruster at the repair share.
    /// </summary>
    [Test]
    public async Task LandingRefitConvertsThrusters()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var drydock = server.System<WFDrydockSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var thrusters = protoMan.Index(ThrusterRefit);
        var landing = protoMan.Index(LandingRefit);
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        EntityUid thruster = default;
        EntityUid gyroscope = default;

        await server.WaitAssertion(() =>
        {
            (thruster, _) = BuildHull(pair, grid);
            gyroscope = SpawnAnchored(pair, grid, "Gyroscope", new Vector2i(2, 1));
            server.System<ShipRepairSystem>().GenerateRepairData(grid);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var survey = refits.Survey(grid, landing, 1);
            Assert.Multiple(() =>
            {
                Assert.That(WFShipRefitSystem.Units(survey), Is.EqualTo(1), "Only the linear thruster is a unit.");
                Assert.That(survey.Below.Single().Entity, Is.EqualTo(thruster), "The stock thruster is unconverted.");
            });

            refits.Apply(thrusters, thruster, 3);
            var thrust = em.GetComponent<ThrusterComponent>(thruster).Thrust;
            refits.Apply(landing, thruster, 1);
            refits.Apply(landing, gyroscope, 1);
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFLandingThrusterComponent>(thruster), "The thruster should be converted.");
                Assert.That(em.HasComponent<WFLandingThrusterComponent>(gyroscope), Is.False, "A gyroscope is never converted.");
                Assert.That(PartsOf(pair, thruster), Has.All.StartsWith("QuadraticCapacitorStockPart:6"),
                    "The conversion should keep the Mk IV parts.");
                Assert.That(em.GetComponent<ThrusterComponent>(thruster).Thrust, Is.EqualTo(thrust).Within(0.01f),
                    "The conversion should keep the Mk IV thrust.");
                Assert.That(refits.NextTier(grid, landing, out _), Is.EqualTo(0), "A converted ship has nothing left to refit.");
            });

            refits.SetRefit(grid, LandingRefit.Id, 1, 1);
            em.DeleteEntity(thruster);
            var rebuild = drydock.BuildRepairItems(grid, console).Single(i => i.Kind == WFDrydockWorkKind.Entity);
            Assert.That(rebuild.RefitCost, Is.EqualTo((int) Math.Ceiling(landing.Tiers[0].Price * console.RefitRepairShare)),
                "A covered thruster should be converted again at the repair share.");

            drydock.ApplyItem(grid, rebuild);
            Assert.That(em.HasComponent<WFLandingThrusterComponent>(FindThruster(pair, grid, ThrusterTile)),
                "The rebuilt thruster should come back converted.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An original that still exists elsewhere on the ship is not counted as missing.</summary>
    [Test]
    public async Task ExistingOriginalNotCountedMissing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var xforms = server.System<SharedTransformSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var ship = server.System<ShipRepairSystem>();
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        await server.WaitAssertion(() =>
        {
            var (thruster, _) = BuildHull(pair, grid);
            ship.GenerateRepairData(grid);

            // Move the thruster three tiles away: it still exists, so it is not missing.
            xforms.Unanchor(thruster);
            xforms.SetLocalPosition(thruster, new Vector2(ThrusterTile.X + 3.5f, ThrusterTile.Y + 0.5f));

            var items = drydock.BuildRepairItems(grid, console);
            Assert.That(items.Count(i => i.Kind == WFDrydockWorkKind.Entity), Is.EqualTo(0), "A moved original was counted as missing.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns and anchors a prototype on a grid tile.</summary>
    internal static EntityUid SpawnAnchored(TestPair pair, EntityUid grid, string proto, Vector2i tile)
    {
        var em = pair.Server.EntMan;
        var uid = em.SpawnEntity(proto, new EntityCoordinates(grid, tile.X + 0.5f, tile.Y + 0.5f));
        if (!em.GetComponent<TransformComponent>(uid).Anchored)
            pair.Server.System<SharedTransformSystem>().AnchorEntity(uid);

        return uid;
    }

    /// <summary>Links a crane to a console on its source port, as a multitool would.</summary>
    internal static void LinkCrane(TestPair pair, EntityUid console, EntityUid crane)
    {
        Assert.That(pair.Server.System<DeviceLinkSystem>().ToggleLink(null, console, crane,
            WFDrydockConsoleComponent.CranePort, WFDrydockConsoleComponent.CraneReceiverPort),
            "The crane should link to the console.");
    }

    /// <summary>Raises a BUI message from an actor on an entity, as the entity's own UI would.</summary>
    internal static void RaiseUiMessage(TestPair pair, EntityUid uid, Enum key, BoundUserInterfaceMessage message, EntityUid actor)
    {
        var em = pair.Server.EntMan;
        message.Actor = actor;
        message.UiKey = key;
        message.Entity = em.GetNetEntity(uid);
        em.EventBus.RaiseLocalEvent(uid, (object) message);
    }

    /// <summary>Gives a host grid an HV source: a debug generator on cable beside the crane's anchor tile.</summary>
    private static void PowerHost(TestPair pair, EntityUid host)
    {
        SpawnAnchored(pair, host, "CableHV", new Vector2i(0, 5));
        SpawnAnchored(pair, host, "CableHV", new Vector2i(0, 4));
        SpawnAnchored(pair, host, "CableHV", new Vector2i(0, 3));
        SpawnAnchored(pair, host, "DebugGenerator", new Vector2i(0, 3));
    }

    /// <summary>Lists a machine's machine parts as "prototype:rating" strings, for readable assertions.</summary>
    private static List<string> PartsOf(TestPair pair, EntityUid machine)
    {
        var em = pair.Server.EntMan;
        var container = em.GetComponent<MachineComponent>(machine).PartContainer;
        return container.ContainedEntities
            .Where(e => em.HasComponent<MachinePartComponent>(e))
            .Select(e => $"{em.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID}:{em.GetComponent<MachinePartComponent>(e).Rating}")
            .ToList();
    }

    /// <summary>Refit tiers swap a machine's stock parts for higher-rated ones, and its stats follow the part ratings.</summary>
    [Test]
    public async Task RefitSwapsMachineParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var upgrades = server.System<WFPartRefitSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var thrusters = protoMan.Index(ThrusterRefit);
        var gyros = protoMan.Index(GyroscopeRefit);

        EntityUid thruster = default;
        EntityUid gyroscope = default;

        await server.WaitAssertion(() =>
        {
            (thruster, _) = BuildHull(pair, grid);
            gyroscope = SpawnAnchored(pair, grid, "Gyroscope", new Vector2i(2, 1));
        });

        // Machines fill their stock parts on map init, so let a tick run first.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var thrust = em.GetComponent<ThrusterComponent>(thruster);
            var gyro = em.GetComponent<ThrusterComponent>(gyroscope);
            var stockThrust = thrust.BaseThrust;
            var stockGyro = gyro.BaseThrust;

            Assert.That(upgrades.GetTier(thruster, thrusters), Is.EqualTo(0), "A stock thruster should be tier 0.");
            Assert.That(thrust.Thrust, Is.EqualTo(stockThrust).Within(0.01f), "A stock thruster should have base thrust.");

            refits.Apply(thrusters, thruster, 1);
            Assert.That(PartsOf(pair, thruster), Has.All.StartsWith("AdvancedCapacitorStockPart:3"),
                "Tier 1 should swap every thruster part for an advanced capacitor.");
            Assert.That(upgrades.GetTier(thruster, thrusters), Is.EqualTo(1));
            Assert.That(thrust.Thrust, Is.EqualTo(stockThrust * MathF.Pow(thrust.PartRatingThrustMultiplier, 2)).Within(0.01f),
                "Tier 1 thrust should follow the rating-3 capacitor.");

            refits.Apply(thrusters, thruster, 3);
            Assert.That(PartsOf(pair, thruster), Has.All.StartsWith("QuadraticCapacitorStockPart:6"),
                "Tier 3 should swap every thruster part for a quadratic capacitor.");
            Assert.That(upgrades.GetTier(thruster, thrusters), Is.EqualTo(3));
            Assert.That(thrust.Thrust, Is.EqualTo(stockThrust * MathF.Pow(thrust.PartRatingThrustMultiplier, 5)).Within(0.01f),
                "Tier 3 thrust should follow the rating-6 capacitor.");

            refits.Apply(gyros, gyroscope, 1);
            Assert.That(PartsOf(pair, gyroscope), Has.Some.StartsWith("NanoManipulatorStockPart:3"),
                "The manipulator should become a nano manipulator.");
            Assert.That(PartsOf(pair, gyroscope), Has.Some.StartsWith("AdvancedCapacitorStockPart:3"),
                "The capacitor should become an advanced capacitor.");
            Assert.That(upgrades.GetTier(gyroscope, gyros), Is.EqualTo(1));
            Assert.That(gyro.Thrust, Is.EqualTo(stockGyro * MathF.Pow(gyro.PartRatingThrustMultiplier, 2)).Within(0.01f),
                "Gyroscope thrust should follow the rating-3 manipulator.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A machine built in a frame holds its parts as stacks; a refit counts and replaces every part in them.</summary>
    [Test]
    public async Task RefitSwapsStackedParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var upgrades = server.System<WFPartRefitSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var stacks = server.System<SharedStackSystem>();
        var containers = server.System<SharedContainerSystem>();
        var gyros = server.ResolveDependency<IPrototypeManager>().Index(GyroscopeRefit);

        EntityUid gyroscope = default;

        await server.WaitAssertion(() =>
        {
            BuildHull(pair, grid);
            gyroscope = SpawnAnchored(pair, grid, "Gyroscope", new Vector2i(2, 1));
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            // Swap the two single manipulators for one stack of two, as a machine frame inserts them.
            var parts = em.GetComponent<MachineComponent>(gyroscope).PartContainer;
            foreach (var part in parts.ContainedEntities.ToList())
            {
                if (!em.TryGetComponent<MachinePartComponent>(part, out var partComp) || partComp.PartType.Id != "Manipulator")
                    continue;

                containers.Remove(part, parts);
                em.DeleteEntity(part);
            }

            var stack = em.SpawnEntity("MicroManipulatorStockPart", em.GetComponent<TransformComponent>(gyroscope).Coordinates);
            stacks.SetCount(stack, 2);
            containers.Insert(stack, parts);

            Assert.That(WFShipRefitSystem.Units(refits.Survey(grid, gyros, 1)), Is.EqualTo(3),
                "A stack of two manipulators and a capacitor should count as three parts.");

            refits.Apply(gyros, gyroscope, 1);
            Assert.That(PartsOf(pair, gyroscope).Count(p => p.StartsWith("NanoManipulatorStockPart:3")), Is.EqualTo(2),
                "The stack of two manipulators should become two nano manipulators.");
            Assert.That(upgrades.GetTier(gyroscope, gyros), Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The console quotes refits per stock part swapped and never charges more to heal a part than to rebuild it, and
    /// sends the berth plot its berth, ship and work marks.
    /// </summary>
    [Test]
    public async Task QuotePricesPerPartAndCapsHeals()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var destructible = server.System<DestructibleSystem>();
        var damageable = server.System<DamageableSystem>();
        var ui = server.System<SharedUserInterfaceSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var thrusters = protoMan.Index(ThrusterRefit);
        var gyros = protoMan.Index(GyroscopeRefit);

        EntityUid console = default, ship = default, wall = default;

        await server.WaitAssertion(() =>
        {
            (_, console, _) = LoadDrydock(pair, map.MapId);
            ship = CreateShip(pair, map.MapId);
            (_, wall) = BuildHull(pair, ship);
            SpawnAnchored(pair, ship, "Gyroscope", new Vector2i(2, 1));
            server.System<ShipRepairSystem>().GenerateRepairData(ship);
        });

        // Machines fill their stock parts on map init.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(WFShipRefitSystem.Units(refits.Survey(ship, thrusters, 1)), Is.EqualTo(1), "A thruster holds one capacitor.");
                Assert.That(WFShipRefitSystem.Units(refits.Survey(ship, gyros, 1)), Is.EqualTo(3), "A gyroscope holds three parts.");
            });

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            var blunt = new DamageSpecifier(protoMan.Index(Blunt), FixedPoint2.New(100));
            damageable.TryChangeDamage(wall, blunt);

            var heal = drydock.BuildRepairItems(ship, comp).Single(i => i.Kind == WFDrydockWorkKind.Heal);
            var rebuild = drydock.RebuildPrice(comp, protoMan.Index(WallProto));
            var destroyedAt = destructible.DestroyedAt(wall).Float();
            Assert.That(heal.Cost, Is.EqualTo((int) Math.Ceiling(rebuild * Math.Min(1f, em.GetComponent<DamageableComponent>(wall).TotalDamage.Float() / destroyedAt))),
                "Healing should cost the rebuild price times the share of health lost.");

            drydock.UpdateUi(console, comp, force: true);
            Assert.That(ui.TryGetUiState<WFDrydockConsoleState>(console, WFDrydockConsoleUiKey.Key, out var state), Is.True,
                "The console should have a state.");
            var thrusterLine = state!.Upgrades.Single(u => u.Id == ThrusterRefit.Id);
            var gyroLine = state.Upgrades.Single(u => u.Id == GyroscopeRefit.Id);
            Assert.Multiple(() =>
            {
                Assert.That(thrusterLine.NextPrice, Is.EqualTo(thrusters.Tiers[0].Price), "One thruster part costs one part's price.");
                Assert.That(gyroLine.NextPrice, Is.EqualTo(gyros.Tiers[0].Price * 3), "A gyroscope's three parts cost three parts' price.");
                Assert.That(state.Ship, Is.EqualTo(em.GetNetEntity(ship)), "The plot should get the berthed ship.");
                Assert.That(state.BerthLabel, Is.Not.Null, "The plot should get the berth.");
                Assert.That(state.Marks.Count(m => m.Kind == WFDrydockMarkKind.Damage), Is.EqualTo(1),
                    "The plot should mark the damaged wall.");
            });

            // A shipyard ship's repair never costs more than its cap share of the ship's price, plus the fee.
            var vessel = protoMan.EnumeratePrototypes<VesselPrototype>().Where(v => !v.Abstract && v.Price > 0).MinBy(v => v.Price)!;
            em.EnsureComponent<VesselComponent>(ship).VesselId = vessel.ID;
            CutHole(pair, ship, HoleTile);
            comp.TilePrice = 10_000_000;
            var capped = drydock.BuildRepairItems(ship, comp);
            Assert.Multiple(() =>
            {
                Assert.That(capped.Sum(i => (long) i.Cost), Is.LessThanOrEqualTo((long) (vessel.Price * comp.CapFraction)),
                    "A repair's work should be capped at a share of the ship's price.");
                Assert.That(drydock.RepairFee(ship, comp),
                    Is.EqualTo(Math.Max(comp.MinFee, (int) Math.Round(vessel.Price * comp.FeeFraction / 10) * 10)),
                    "The fee should be a share of the ship's price.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Loads the drydock station away from the origin and returns it with its South (A) and North (B) berth consoles.</summary>
    public static (EntityUid Station, EntityUid ConsoleA, EntityUid ConsoleB) LoadDrydock(TestPair pair, MapId map)
    {
        var server = pair.Server;
        var em = server.EntMan;
        var loader = server.System<MapLoaderSystem>();
        Assert.That(loader.TryLoadGrid(map, new ResPath("/Maps/_WF/Drydock/drydock.yml"), out var stationGrid, offset: StationPos),
            "drydock.yml should load.");
        var station = stationGrid!.Value.Owner;

        EntityUid consoleA = default, consoleB = default;
        var query = em.EntityQueryEnumerator<WFDrydockConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != station)
                continue;

            if (comp.Berth == "South")
                consoleA = uid;
            else if (comp.Berth == "North")
                consoleB = uid;
        }

        Assert.That(consoleA.IsValid() && consoleB.IsValid(), "The drydock needs a console for each berth.");
        return (station, consoleA, consoleB);
    }

    /// <summary>Finds the drydock's operator, the dockmaster with the one per-caller hail service, on the station grid.</summary>
    public static EntityUid FindOperator(TestPair pair, EntityUid station)
    {
        var query = pair.Server.EntMan.EntityQueryEnumerator<WFDrydockOperatorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == station)
                return uid;
        }

        Assert.Fail("The drydock needs an operator.");
        return EntityUid.Invalid;
    }

    /// <summary>Lists the hail contacts on the station grid; the drydock has one, its dockmaster.</summary>
    public static List<EntityUid> HailContactsOn(TestPair pair, EntityUid station)
    {
        var em = pair.Server.EntMan;
        var contacts = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<HailResponderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == station)
                contacts.Add(uid);
        }

        return contacts;
    }

    /// <summary>Creates an empty grid at a world position, in the middle of berth A unless told otherwise.</summary>
    public static EntityUid CreateShip(TestPair pair, MapId map, Vector2? worldPos = null)
    {
        var server = pair.Server;
        var grid = server.System<SharedMapSystem>().CreateGridEntity(map).Owner;
        server.System<SharedTransformSystem>().SetWorldPosition(grid, worldPos ?? BerthAHull);
        return grid;
    }

    /// <summary>Creates a ship with the test hull, its repair data taken and a hole cut in its floor.</summary>
    internal static EntityUid CreateDamagedShip(TestPair pair, MapId map, Vector2? worldPos = null)
    {
        var ship = CreateShip(pair, map, worldPos);
        BuildHull(pair, ship);
        pair.Server.System<ShipRepairSystem>().GenerateRepairData(ship);
        CutHole(pair, ship, HoleTile);
        return ship;
    }

    /// <summary>Moves a ship so the centre of its grid sits at a local point on a host grid.</summary>
    internal static void PlaceOver(TestPair pair, EntityUid ship, EntityUid host, Vector2 hostLocalCenter)
    {
        var server = pair.Server;
        var xforms = server.System<SharedTransformSystem>();
        var target = xforms.ToWorldPosition(new EntityCoordinates(host, hostLocalCenter));
        xforms.SetWorldPosition(ship, target - server.EntMan.GetComponent<MapGridComponent>(ship).LocalAABB.Center);
    }

    /// <summary>Lists a berth's cranes on the station.</summary>
    private static List<(EntityUid Uid, WFDrydockCraneComponent Comp)> Cranes(TestPair pair, EntityUid station, string berth)
    {
        var em = pair.Server.EntMan;
        var cranes = new List<(EntityUid, WFDrydockCraneComponent)>();
        var query = em.EntityQueryEnumerator<WFDrydockCraneComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crane, out var xform))
        {
            if (xform.GridUid == station && crane.LinkedConsole is { } console &&
                em.GetComponent<WFDrydockConsoleComponent>(console).Berth == berth)
                cranes.Add((uid, crane));
        }

        return cranes;
    }

    /// <summary>Ticks the server in steps until the condition holds or the step budget runs out.</summary>
    public static async Task RunUntil(TestPair pair, int steps, int ticksPerStep, Func<bool> done)
    {
        for (var i = 0; i < steps && !done(); i++)
        {
            await pair.Server.WaitRunTicks(ticksPerStep);
        }
    }

    /// <summary>Ticks until a console's job ends, returning how many items its cranes skipped.</summary>
    private static async Task<int> RunJob(TestPair pair, EntityUid console, int steps = 120)
    {
        var em = pair.Server.EntMan;
        var skipped = -1;
        for (var i = 0; i < steps && em.HasComponent<WFDrydockJobComponent>(console); i++)
        {
            if (em.TryGetComponent(console, out WFDrydockJobComponent? running))
                skipped = running.Skipped;
            await pair.Server.WaitRunTicks(30);
        }

        return skipped;
    }

    /// <summary>Spawns a host grid with a crane linked to a console, as a mobile shipyard, optionally with HV power.</summary>
    private static Shipyard SpawnShipyard(TestPair pair, MapId map, bool powered = true)
    {
        var host = CreateShip(pair, map, HostPos);
        SetFloor(pair, host, 6);
        var crane = SpawnAnchored(pair, host, "WFDrydockCrane", new Vector2i(0, 5));
        if (powered)
            PowerHost(pair, host);

        var console = SpawnAnchored(pair, host, "WFComputerDrydock", new Vector2i(5, 0));
        LinkCrane(pair, console, crane);
        return new Shipyard(host, crane, console);
    }

    /// <summary>Creates a connected player's body with funds, since charging reads the player's saved character.</summary>
    internal static EntityUid CreatePayer(TestPair pair, MapId map)
    {
        var server = pair.Server;
        var em = server.EntMan;
        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var mindSys = server.System<SharedMindSystem>();
        mindSys.WipeMind(session.ContentData()?.Mind);
        var payer = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map));
        mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, payer);
        em.EnsureComponent<BankAccountComponent>(payer);
        Assert.That(server.System<BankSystem>().TryBankDeposit(payer, PayerFunds, tax: false), Is.True, "Could not fund the payer.");
        return payer;
    }

    /// <summary>Loads the drydock map, checks its berth areas, cranes, dockmaster and POI, and finds a ship by its position alone.</summary>
    [Test]
    public async Task DrydockMapLoadsAndDetectsShipInBerth()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var xforms = server.System<SharedTransformSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var (station, consoleA, consoleB) = LoadDrydock(pair, map.MapId);
            var compA = em.GetComponent<WFDrydockConsoleComponent>(consoleA);
            var compB = em.GetComponent<WFDrydockConsoleComponent>(consoleB);

            Assert.Multiple(() =>
            {
                // The work area is the cranes' reach: the bay plus the approach out of its open end.
                Assert.That(drydock.TryGetBerth(consoleA, compA, out var berthA), "Berth A needs cranes.");
                Assert.That(berthA.Area.Contains(new Vector2(-20f, -100f)) && berthA.Area.Contains(new Vector2(20f, -30f)),
                    "Berth A's area should cover its bay.");
                Assert.That(berthA.Area.Center.X, Is.EqualTo(0.5f).Within(2f), "Berth A's area should be centred on its bay.");
                Assert.That(berthA.Approach, Is.EqualTo(new Vector2(0f, -1f)), "Berth A should be approached from the bay's open end.");
                Assert.That(drydock.TryGetBerth(consoleB, compB, out var berthB), "Berth B needs cranes.");
                Assert.That(berthB.Area.Contains(new Vector2(-20f, 100f)) && berthB.Area.Contains(new Vector2(20f, 30f)),
                    "Berth B's area should cover its bay.");
                Assert.That(berthB.Area.Center.X, Is.EqualTo(0.5f).Within(2f), "Berth B's area should be centred on its bay.");
                Assert.That(berthB.Approach, Is.EqualTo(new Vector2(0f, 1f)), "Berth B should be approached from the bay's open end.");
                Assert.That(Cranes(pair, station, "South"), Has.Count.EqualTo(2), "Berth A needs a crane on each side.");
                Assert.That(Cranes(pair, station, "North"), Has.Count.EqualTo(2), "Berth B needs a crane on each side.");
                var op = FindOperator(pair, station);
                Assert.That(em.TryGetComponent<HailServiceComponent>(op, out var service) && service.PerCaller,
                    "The operator should be the one per-caller hail service.");
                Assert.That(server.System<SharedUserInterfaceSystem>().HasUi(op, WFDrydockConsoleUiKey.Key),
                    "The operator should carry the drydock window so a hail relay can copy it.");
                Assert.That(em.HasComponent<HailServiceComponent>(consoleA), Is.False, "Berth A should not be offered over hails.");
                Assert.That(em.HasComponent<HailServiceComponent>(consoleB), Is.False, "Berth B should not be offered over hails.");
                Assert.That(HailContactsOn(pair, station), Has.Count.EqualTo(1), "The drydock needs one dockmaster to answer hails.");
            });

            var poi = protoMan.Index<PointOfInterestPrototype>("WFDrydock");
            Assert.That(poi.GridPath, Is.EqualTo(new ResPath("/Maps/_WF/Drydock/drydock.yml")));
            Assert.That(poi.SpawnGroup, Is.EqualTo("Required"));

            // A hull parked in berth A, no docking needed.
            var ship = CreateShip(pair, map.MapId);
            BuildHull(pair, ship);
            Assert.That(drydock.GetBerthedShip(consoleA, compA), Is.EqualTo(ship), "Berth A should report the ship in its bay.");
            Assert.That(drydock.GetBerthedShip(consoleB, compB), Is.Null, "Berth B should stay empty.");

            // Out past the bay's open end, it no longer counts.
            xforms.SetWorldPosition(ship, OutsideBayPos);
            Assert.That(drydock.GetBerthedShip(consoleA, compA), Is.Null, "A ship outside the bay should not be berthed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Berth A's two cranes reach 95% of their working draw within 45 s of starting from idle.</summary>
    [Test]
    public async Task DrydockCranesArePowered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var berthCranes = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var (station, _, _) = LoadDrydock(pair, map.MapId);
            berthCranes.AddRange(Cranes(pair, station, "South").Select(c => c.Uid));
            Assert.That(berthCranes, Has.Count.EqualTo(2), "Berth A needs two cranes.");
        });

        // Let the RTGs and SMES settle at idle before berth A starts work.
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            foreach (var crane in berthCranes)
            {
                em.GetComponent<PowerConsumerComponent>(crane).DrawRate = em.GetComponent<WFDrydockCraneComponent>(crane).WorkingLoad;
            }
        });

        var ticksPerSecond = server.ResolveDependency<IGameTiming>().TickRate;
        await RunUntil(pair, 45, ticksPerSecond, () => berthCranes.All(crane =>
            em.GetComponent<PowerConsumerComponent>(crane).ReceivedPower >= 0.95f * em.GetComponent<WFDrydockCraneComponent>(crane).WorkingLoad));

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var crane in berthCranes)
                {
                    var working = em.GetComponent<WFDrydockCraneComponent>(crane).WorkingLoad;
                    Assert.That(em.GetComponent<PowerConsumerComponent>(crane).ReceivedPower,
                        Is.GreaterThanOrEqualTo(0.95f * working),
                        $"Crane {crane} should receive 95% of its working draw within 45 s.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A job deploys both cranes over the ship before working, restores the hull, then folds the cranes away.</summary>
    [Test]
    public async Task JobDeploysCranesRepairsAndParks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var plating = Plating(pair);

        EntityUid station = default, console = default, ship = default;

        await server.WaitAssertion(() =>
        {
            (station, console, _) = LoadDrydock(pair, map.MapId);
            ship = CreateDamagedShip(pair, map.MapId);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            var items = drydock.BuildRepairItems(ship, comp);
            Assert.That(items, Has.Count.EqualTo(1), "Only the hole should need work.");
            drydock.StartJob(console, comp, ship, null, items, 0);

            var job = em.GetComponent<WFDrydockJobComponent>(console);
            Assert.That(job.WorkStarts, Is.GreaterThan(timing.CurTime), "Work should wait for the cranes to deploy.");
            foreach (var (_, crane) in Cranes(pair, station, "South"))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(crane.Active, "Every berth crane should work the job.");
                    Assert.That(crane.Angle, Is.EqualTo(0f), "Booms should swing out over the bay.");
                    Assert.That(crane.Extension, Is.GreaterThan(crane.SectionLength), "Booms should extend across the ship.");
                    Assert.That(crane.Extension, Is.LessThanOrEqualTo(crane.MaxExtension));
                });
            }
        });

        // Deploy, travel and one tile of work take well under two minutes of game time.
        var skipped = await RunJob(pair, console);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The job should finish.");
            Assert.That(skipped, Is.EqualTo(0), "Cranes should reach every item on the real map.");
            var grid = em.GetComponent<MapGridComponent>(ship);
            Assert.That(maps.GetTileRef(ship, grid, HoleTile).Tile.TypeId, Is.EqualTo(plating), "The hole was not refloored.");
            foreach (var (_, crane) in Cranes(pair, station, "South"))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(crane.Active, Is.False, "Cranes should stop once the job is done.");
                    Assert.That(crane.Angle, Is.EqualTo(crane.ParkAngle), "Booms should fold back into their machines.");
                    Assert.That(crane.Extension, Is.EqualTo((float) crane.SectionLength), "Booms should retract.");
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A refit far longer than its cap is paced to finish within MaxRefitDuration, deploy and travel included.</summary>
    [Test]
    public async Task RefitFinishesWithinMaxRefitDuration()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var upgrades = server.System<WFPartRefitSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var thrusterRefit = server.ResolveDependency<IPrototypeManager>().Index(ThrusterRefit);

        EntityUid console = default, ship = default;
        var thrusters = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            EntityUid station;
            (station, console, _) = LoadDrydock(pair, map.MapId);

            // Full power throughout, so only the pacing sets the job's length.
            foreach (var (crane, _) in Cranes(pair, station, "South"))
            {
                em.RemoveComponent<PowerConsumerComponent>(crane);
            }

            ship = CreateShip(pair, map.MapId);
            SetFloor(pair, ship, 12);
            for (var x = 0; x < 10; x++)
            for (var y = 1; y < 12; y += 3)
            {
                thrusters.Add(SpawnAnchored(pair, ship, "Thruster", new Vector2i(x, y)));
            }
        });

        await server.WaitRunTicks(2);

        TimeSpan start = default;
        float maxDuration = 0;
        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            maxDuration = comp.MaxRefitDuration;
            var items = thrusters.Select(thruster => new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Upgrade,
                Target = thruster,
                Upgrade = ThrusterRefit.Id,
                Tier = 1,
                Cost = 1000,
                LocalPos = em.GetComponent<TransformComponent>(thruster).LocalPosition,
            }).ToList();

            Assert.That(items.Count * comp.UpgradeWorkTime / 2, Is.GreaterThan(maxDuration * 3),
                "The unpaced refit should run far past the cap.");
            start = timing.CurTime;
            drydock.StartJob(console, comp, ship, null, items, 0);
        });

        var skipped = await RunJob(pair, console, steps: 400);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The refit should finish.");
            Assert.That(skipped, Is.EqualTo(0), "Cranes should reach every thruster.");
            Assert.That((timing.CurTime - start).TotalSeconds, Is.LessThanOrEqualTo(maxDuration + 2),
                "The refit should end within its cap.");
            Assert.That(thrusters.Select(t => upgrades.GetTier(t, thrusterRefit)), Has.All.EqualTo(1),
                "Every thruster should be refitted.");
            Assert.That(server.System<WFShipRefitSystem>().GetRefit(ship, ThrusterRefit.Id), Is.EqualTo((1, thrusters.Count)),
                "The ship should own Mk II, covering every thruster's part.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A parked ship that drifts under its cranes keeps each working crane's targets on its item.</summary>
    [Test]
    public async Task CraneFollowsItemWhenShipDrifts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();

        EntityUid console = default, ship = default;

        await server.WaitAssertion(() =>
        {
            (_, console, _) = LoadDrydock(pair, map.MapId);
            ship = CreateDamagedShip(pair, map.MapId);

            // Slow work, so the item is still in progress after the drift below.
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            comp.TileWorkTime = comp.EntityWorkTime = comp.HealWorkTime = 20f;
            var items = drydock.BuildRepairItems(ship, comp);
            drydock.StartJob(console, comp, ship, null, items, 0);
        });

        // Wait until a crane has reached its item and is working on it.
        WFDrydockJobSlot? slot = null;
        await RunUntil(pair, 1500, 10, () =>
        {
            if (em.TryGetComponent(console, out WFDrydockJobComponent? running))
                slot = running.Slots.FirstOrDefault(s => s.Item != null && s.Arrived && !s.Unreachable);
            return slot != null;
        });

        Assert.That(slot, Is.Not.Null, "A crane should reach the hole.");
        var item = slot!.Item!;
        var crane = slot.Crane!.Value;
        var craneComp = em.GetComponent<WFDrydockCraneComponent>(crane);

        // The item's place in the crane's arm frame, from its current world position.
        Vector2 ItemInArm()
        {
            var world = Vector2.Transform(item.LocalPos, xforms.GetWorldMatrix(ship));
            return craneComp.LocalToArm(Vector2.Transform(world, xforms.GetInvWorldMatrix(crane)));
        }

        Vector2 before = default;
        await server.WaitAssertion(() => before = ItemInArm());

        // Drift the ship 1.5 tiles without velocity; the item moves out from under the head.
        await server.WaitAssertion(() =>
        {
            xforms.SetWorldPosition(ship, xforms.GetWorldPosition(ship) + new Vector2(1.5f, 0f));
            physics.SetLinearVelocity(ship, Vector2.Zero);
        });
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.True, "The job should still be running.");
            Assert.That(slot.Item, Is.SameAs(item), "The crane should still work the same item.");
            Assert.That(slot.Unreachable, Is.False, "The item should stay reachable.");

            var now = ItemInArm();
            Assert.That(Vector2.Distance(now, before), Is.GreaterThan(1f), "The item should have moved in the arm frame.");
            Assert.That(craneComp.Position, Is.EqualTo(Math.Clamp(now.Y, 0f, craneComp.Arm)).Within(0.25f),
                "The carriage should follow the item along the arm.");
            Assert.That(craneComp.TrolleyPosition, Is.EqualTo(Math.Clamp(now.X, 1f, craneComp.Extension)).Within(0.25f),
                "The tool head should follow the item across the boom.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An item no crane can reach is given up on after the reach timeout, refunded at its cost, and never applied.</summary>
    [Test]
    public async Task UnreachableItemIsSkippedAndRefunded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var plating = Plating(pair);
        const int farCost = 123;
        var farTile = new Vector2i(7, 7);

        EntityUid console = default, ship = default;
        WFDrydockJobComponent job = default!;

        await server.WaitAssertion(() =>
        {
            (_, console, _) = LoadDrydock(pair, map.MapId);
            ship = CreateDamagedShip(pair, map.MapId);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            var items = drydock.BuildRepairItems(ship, comp);

            // A hull tile about two hundred tiles along the ship, far outside every crane's reach.
            items.Add(new WFDrydockWorkItem
            {
                Kind = WFDrydockWorkKind.Tile,
                TileIndex = farTile,
                TileType = plating,
                LocalPos = new Vector2(farTile.X + 0.5f, 200f),
                Cost = farCost,
            });
            drydock.StartJob(console, comp, ship, null, items, 0);
            job = em.GetComponent<WFDrydockJobComponent>(console);
        });

        // Deploy, repair the reachable hole, then wait out the reach timeout on the far tile.
        await RunJob(pair, console, 240);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The job should finish once the far item is given up.");
            Assert.Multiple(() =>
            {
                Assert.That(job.Skipped, Is.EqualTo(1), "The far item should be skipped.");
                Assert.That(job.SkippedRefund, Is.EqualTo(farCost), "The skipped item should be refunded at its cost.");
                Assert.That(job.Done, Is.EqualTo(job.Total), "Skipped items should count as done.");
            });

            var grid = em.GetComponent<MapGridComponent>(ship);
            Assert.Multiple(() =>
            {
                Assert.That(maps.GetTileRef(ship, grid, farTile).Tile.TypeId, Is.Not.EqualTo(plating), "The skipped item must not be applied.");
                Assert.That(maps.GetTileRef(ship, grid, HoleTile).Tile.TypeId, Is.EqualTo(plating), "The reachable hole should still be repaired.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Loads the drydock with a damaged 5x5 hull parked outside the bays, and a funded payer.</summary>
    private static (EntityUid Station, EntityUid Console, EntityUid Operator, EntityUid Ship, EntityUid Payer) SetUpDamagedShip(TestPair pair, MapId map)
    {
        var (station, console, _) = LoadDrydock(pair, map);
        var ship = CreateDamagedShip(pair, map, OutsideBayPos);
        return (station, console, FindOperator(pair, station), ship, CreatePayer(pair, map));
    }

    /// <summary>A repair booked for a ship outside the bays guides it into berth A, then starts once it settles there.</summary>
    [Test]
    public async Task BookedRepairGuidesShipIntoBerthAndStarts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();

        EntityUid station = default, console = default, op = default, ship = default, payer = default;
        var startBalance = 0;

        await server.WaitAssertion(() =>
        {
            (station, console, op, ship, payer) = SetUpDamagedShip(pair, map.MapId);
            startBalance = em.GetComponent<BankAccountComponent>(payer).Balance;
            drydock.Book(op, em.GetComponent<WFDrydockOperatorComponent>(op), ship, null, payer, null);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "Berth A needs cranes.");
            var area = berth.Area;
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockBookingComponent>(console), "A small ship should book berth A.");
                Assert.That(em.GetComponent<WFDrydockBookingComponent>(console).Ship, Is.EqualTo(ship));

                var guide = em.GetComponent<WFDrydockGuideComponent>(ship);
                Assert.That(guide.Station, Is.EqualTo(station), "The guide should sit on the drydock grid.");
                Assert.That(guide.Size.X, Is.GreaterThanOrEqualTo(5f), "The guide should cover the ship's width.");
                Assert.That(guide.Size.Y, Is.GreaterThanOrEqualTo(5f), "The guide should cover the ship's length.");
                Assert.That(guide.Size.X, Is.LessThanOrEqualTo(area.Width), "The guide should fit inside berth A.");
                Assert.That(guide.Size.Y, Is.LessThanOrEqualTo(area.Height), "The guide should fit inside berth A.");
                Assert.That(area.Contains(guide.Center), "The guide's center should lie inside berth A.");
                Assert.That(guide.PathStart.Y, Is.LessThan(area.Bottom), "The approach should start outside the bay.");
                Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(startBalance), "Nothing should be paid until the work starts.");
            });
        });

        await server.WaitAssertion(() =>
        {
            xforms.SetWorldPosition(ship, BerthAHull);
            physics.SetLinearVelocity(ship, Vector2.Zero);
        });

        await RunUntil(pair, 600, 10, () => em.HasComponent<WFDrydockJobComponent>(console));

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), "Once settled in berth A, the booked job should start there.");
                Assert.That(em.HasComponent<WFDrydockBookingComponent>(console), Is.False, "The booking should be consumed.");
                Assert.That(em.HasComponent<WFDrydockGuideComponent>(ship), Is.False, "The guide should be removed.");
                Assert.That(em.GetComponent<WFDrydockJobComponent>(console).Ship, Is.EqualTo(ship));
                Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.LessThan(startBalance), "The payer should be charged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A booking nobody takes up lapses after its time, taking the guide with it and charging nothing.</summary>
    [Test]
    public async Task UnclaimedBookingLapsesAndReleasesGuide()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, ship = default, payer = default;
        var startBalance = 0;

        await server.WaitAssertion(() =>
        {
            EntityUid op;
            (_, console, op, ship, payer) = SetUpDamagedShip(pair, map.MapId);
            startBalance = em.GetComponent<BankAccountComponent>(payer).Balance;

            var opComp = em.GetComponent<WFDrydockOperatorComponent>(op);
            opComp.BookingTime = 2f;
            drydock.Book(op, opComp, ship, null, payer, null);
            Assert.That(em.HasComponent<WFDrydockBookingComponent>(console), "The booking should be held.");
            Assert.That(em.HasComponent<WFDrydockGuideComponent>(ship), "The ship should have its guide.");
        });

        await RunUntil(pair, 300, 10, () => !em.HasComponent<WFDrydockBookingComponent>(console));

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockBookingComponent>(console), Is.False, "The booking should lapse.");
                Assert.That(em.HasComponent<WFDrydockGuideComponent>(ship), Is.False, "The guide should go with the booking.");
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "No work should start.");
                Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(startBalance), "Nothing should be charged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crane and console on a host grid cruising at the ship's speed serve a damaged ship alongside, and repair it.</summary>
    [Test]
    public async Task MobileShipyardRepairsShipAlongside()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var drydock = server.System<WFDrydockSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var plating = Plating(pair);
        var cruise = new Vector2(3f, 0f);

        EntityUid console = default, ship = default;

        await server.WaitAssertion(() =>
        {
            var yard = SpawnShipyard(pair, map.MapId);
            console = yard.Console;
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "The host's crane should make a berth.");
            Assert.That(berth.Cranes, Has.Count.EqualTo(1), "One crane should serve the console.");

            // The damaged ship's centre goes to the middle of the host's reach.
            ship = CreateDamagedShip(pair, map.MapId);
            PlaceOver(pair, ship, yard.Grid, berth.Area.Center);

            // Both grids cruise together, so the ship stays in the host's reach.
            physics.SetLinearVelocity(yard.Grid, cruise);
            physics.SetLinearVelocity(ship, cruise);
            Assert.That(drydock.GetBerthedShip(console, comp), Is.EqualTo(ship), "The cruising ship should be berthed alongside.");

            var items = drydock.BuildRepairItems(ship, comp);
            Assert.That(items, Has.Count.EqualTo(1), "Only the hole should need work.");
            drydock.StartJob(console, comp, ship, null, items, 0);
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), "The job should start.");
        });

        var skipped = await RunJob(pair, console);

        await server.WaitAssertion(() =>
        {
            var grid = em.GetComponent<MapGridComponent>(ship);
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The job should finish.");
                Assert.That(skipped, Is.EqualTo(0), "The cranes should reach every item from a moving host.");
                Assert.That(maps.GetTileRef(ship, grid, HoleTile).Tile.TypeId, Is.EqualTo(plating), "The hole was not refloored.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A shipyard's own grid is never berthed or worked on, even when it is damaged.</summary>
    [Test]
    public async Task ShipyardNeverWorksOnOwnGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var repair = server.System<ShipRepairSystem>();

        await server.WaitAssertion(() =>
        {
            var yard = SpawnShipyard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockConsoleComponent>(yard.Console);
            Assert.That(drydock.TryGetBerth(yard.Console, comp, out _), "The host's crane should make a berth.");

            // The host is damaged, so it has work, but it is the shipyard's own grid.
            repair.GenerateRepairData(yard.Grid);
            CutHole(pair, yard.Grid, HoleTile);
            Assert.That(drydock.GetBerthedShip(yard.Console, comp), Is.Not.EqualTo(yard.Grid), "The host should never be its own berthed ship.");

            var items = drydock.BuildRepairItems(yard.Grid, comp);
            drydock.StartJob(yard.Console, comp, yard.Grid, null, items, 0);
            Assert.That(em.HasComponent<WFDrydockJobComponent>(yard.Console), Is.False, "The cranes should refuse to work on their own grid.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Unpowered cranes refuse to start work, and a job started anyway aborts once its cranes stay stalled.</summary>
    [Test]
    public async Task UnpoweredCranesStallThenAbort()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = server.System<SharedMapSystem>();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, ship = default;

        await server.WaitAssertion(() =>
        {
            var yard = SpawnShipyard(pair, map.MapId, powered: false);
            console = yard.Console;
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "The host's crane should make a berth.");
            comp.PowerAbortTime = 2f;

            ship = CreateDamagedShip(pair, map.MapId);
            PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
            Assert.That(drydock.GetBerthedShip(console, comp), Is.EqualTo(ship), "The ship should be berthed.");

            Assert.That(drydock.TryStartRepair(console, comp, ship, console), Is.False, "Unpowered cranes should refuse work.");
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "No job should start without power.");

            var items = drydock.BuildRepairItems(ship, comp);
            drydock.StartJob(console, comp, ship, null, items, 0);
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), "The job should start.");
        });

        await RunUntil(pair, 300, 10, () => !em.HasComponent<WFDrydockJobComponent>(console));

        await server.WaitAssertion(() =>
        {
            var grid = em.GetComponent<MapGridComponent>(ship);
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The stalled job should abort.");
                Assert.That(maps.GetTileRef(ship, grid, HoleTile).Tile.IsEmpty, Is.True, "No work should be applied without power.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Powers a host from a debug generator off the arm column, so the column stays clear for a blocker.</summary>
    private static void PowerHostAside(TestPair pair, EntityUid host)
    {
        for (var x = 0; x <= 4; x++)
            SpawnAnchored(pair, host, "CableHV", new Vector2i(x, 5));

        SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 4));
        SpawnAnchored(pair, host, "DebugGenerator", new Vector2i(4, 4));
    }

    /// <summary>A solid entity in a crane's arm lane disables it until the lane clears.</summary>
    [Test]
    public async Task BlockedArmLaneDisablesCrane()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var ui = server.System<SharedUserInterfaceSystem>();

        Shipyard yard = default;
        EntityUid ship = default, payer = default, wall = default;

        await server.WaitAssertion(() =>
        {
            yard = SpawnShipyard(pair, map.MapId, powered: false);
            PowerHostAside(pair, yard.Grid);
            ship = CreateDamagedShip(pair, map.MapId);
            payer = CreatePayer(pair, map.MapId);
            Assert.That(drydock.TryGetBerth(yard.Console, em.GetComponent<WFDrydockConsoleComponent>(yard.Console), out var berth), "The host's crane should make a berth.");
            PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
        });

        // The wall sits on the arm column, the first solid tile below the crane's own tile.
        await server.WaitAssertion(() => wall = SpawnAnchored(pair, yard.Grid, "WallSolid", new Vector2i(0, 2)));
        await RunUntil(pair, 100, 10, () => em.GetComponent<WFDrydockCraneComponent>(yard.Crane).Blocked);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(yard.Console);
            var crane = em.GetComponent<WFDrydockCraneComponent>(yard.Crane);
            Assert.That(crane.Blocked, Is.True, "A wall in the arm lane should block the crane.");
            Assert.That(crane.Blocker, Is.EqualTo(em.GetComponent<MetaDataComponent>(wall).EntityName), "The wall should be named as the blocker.");
            Assert.That(drydock.TryStartRepair(yard.Console, comp, ship, payer), Is.False, "A blocked crane should refuse work.");
            Assert.That(em.HasComponent<WFDrydockJobComponent>(yard.Console), Is.False, "No job should start with the arm blocked.");
            Assert.That(ui.TryGetUiState<WFDrydockConsoleState>(yard.Console, WFDrydockConsoleUiKey.Key, out var state), Is.True);
            Assert.That(state!.BlockedCranes, Is.EqualTo(1), "The console should count one blocked crane.");
        });

        await server.WaitAssertion(() => em.DeleteEntity(wall));
        await RunUntil(pair, 100, 10, () => !em.GetComponent<WFDrydockCraneComponent>(yard.Crane).Blocked);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(yard.Console);
            Assert.That(em.GetComponent<WFDrydockCraneComponent>(yard.Crane).Blocked, Is.False, "A cleared lane should unblock the crane.");
            Assert.That(drydock.TryStartRepair(yard.Console, comp, ship, payer), Is.True, "A cleared crane should start work.");
            Assert.That(em.HasComponent<WFDrydockJobComponent>(yard.Console), Is.True, "The job should start.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A solid entity on a crane's boom path stops the boom: points past it are out of reach.</summary>
    [Test]
    public async Task BoomStopsShortOfObstacle()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        Shipyard yard = default;
        await server.WaitAssertion(() =>
        {
            yard = SpawnShipyard(pair, map.MapId);
            // A wall on row 1, between the two points checked below.
            SpawnAnchored(pair, yard.Grid, "WallSolid", new Vector2i(3, 1));
        });

        // The boom's blocker cache refreshes every 2 s.
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(yard.Console);
            Assert.That(drydock.CanReach(comp, yard.Grid, yard.Crane, new Vector2(1.5f, 1.5f)), Is.True,
                "A point before the obstacle on its row should be reachable.");
            Assert.That(drydock.CanReach(comp, yard.Grid, yard.Crane, new Vector2(4.5f, 1.5f)), Is.False,
                "A point past the obstacle on its row should be out of reach.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crane serves the one console it is linked to: linking it to another console moves it.</summary>
    [Test]
    public async Task CraneLinksToOneConsole()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var host = CreateShip(pair, map.MapId, HostPos);
            SetFloor(pair, host, 6);
            var crane = SpawnAnchored(pair, host, "WFDrydockCrane", new Vector2i(0, 5));
            var first = SpawnAnchored(pair, host, "WFComputerDrydock", new Vector2i(5, 0));
            var second = SpawnAnchored(pair, host, "WFComputerDrydock", new Vector2i(5, 1));
            var firstComp = em.GetComponent<WFDrydockConsoleComponent>(first);
            var secondComp = em.GetComponent<WFDrydockConsoleComponent>(second);

            Assert.That(drydock.TryGetBerth(first, firstComp, out _), Is.False, "An unlinked console has no berth.");

            LinkCrane(pair, first, crane);
            Assert.That(drydock.TryGetBerth(first, firstComp, out var firstBerth), "The linked crane should make a berth.");
            Assert.That(firstBerth.Cranes, Has.Count.EqualTo(1), "Console 1 should have its crane.");
            Assert.That(drydock.TryGetBerth(second, secondComp, out _), Is.False, "Console 2 has no crane yet.");

            LinkCrane(pair, second, crane);
            Assert.That(drydock.TryGetBerth(first, firstComp, out _), Is.False, "Relinking should drop the crane from console 1.");
            Assert.That(drydock.TryGetBerth(second, secondComp, out var secondBerth), "Console 2 should now have the crane.");
            Assert.That(secondBerth.Cranes, Has.Count.EqualTo(1), "Console 2 should have one crane.");
        });

        await pair.CleanReturnAsync();
    }
}
