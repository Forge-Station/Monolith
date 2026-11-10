using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.Bank;
using Content.Server._WF.Drydock;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Rotatable;
using Content.Shared.Wires;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests the ship-buildable mobile drydock: a console that pays in stored materials, and its crane.</summary>
[TestFixture]
public sealed class MobileDrydockTest
{
    private static readonly string[] StockMaterials = { "Steel", "Plasteel", "Glass", "Plastic" };
    private static readonly EntProtoId PlastitaniumWall = "WallPlastitanium";
    private static readonly EntProtoId ShuttleWall = "WallShuttle";

    private static readonly ProtoId<TechnologyPrototype> Shuttlecraft = "Shuttlecraft";
    private static readonly ProtoId<LatheRecipePrototype> ConsoleBoardRecipe = "WFComputerMobileDrydockCircuitboard";
    private static readonly ProtoId<LatheRecipePrototype> CraneBoardRecipe = "WFMobileDrydockCraneMachineCircuitboard";

    /// <summary>Spawns a host grid with a mobile crane, a material console, and MV power from a substation on the HV debug generator.</summary>
    internal static (EntityUid Grid, EntityUid Crane, EntityUid Console) SpawnMobileYard(TestPair pair, MapId map)
    {
        var host = DrydockTest.CreateShip(pair, map, new Vector2(-300f, 0f));
        DrydockTest.SetFloor(pair, host, 6);
        var crane = DrydockTest.SpawnAnchored(pair, host, "WFMobileDrydockCrane", new Vector2i(0, 5));

        // Power comes from the far side of the bay, so the arm column (x = 0) stays clear of solid machines.
        for (var x = 0; x <= 4; x++)
        {
            DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(x, 5));
            DrydockTest.SpawnAnchored(pair, host, "CableMV", new Vector2i(x, 5));
        }

        DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 4));
        DrydockTest.SpawnAnchored(pair, host, "CableHV", new Vector2i(4, 3));
        DrydockTest.SpawnAnchored(pair, host, "DebugGenerator", new Vector2i(4, 3));
        DrydockTest.SpawnAnchored(pair, host, "DebugSubstation", new Vector2i(4, 4));
        DrydockTest.SpawnAnchored(pair, host, "CableMV", new Vector2i(4, 4));
        var console = DrydockTest.SpawnAnchored(pair, host, "WFComputerMobileDrydock", new Vector2i(5, 0));
        DrydockTest.LinkCrane(pair, console, crane);
        return (host, crane, console);
    }

    /// <summary>Builds a mobile yard with a damaged ship in its reach, and a funded payer.</summary>
    private static (EntityUid Console, EntityUid Crane, EntityUid Ship, EntityUid Payer) SetUpMobileRepair(TestPair pair, MapId map)
    {
        var yard = SpawnMobileYard(pair, map);
        var comp = pair.Server.EntMan.GetComponent<WFDrydockConsoleComponent>(yard.Console);
        Assert.That(pair.Server.System<WFDrydockSystem>().TryGetBerth(yard.Console, comp, out var berth), "The crane should make a berth.");

        var ship = DrydockTest.CreateDamagedShip(pair, map);
        DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
        return (yard.Console, yard.Crane, ship, DrydockTest.CreatePayer(pair, map));
    }

    /// <summary>Whether a crane has at least half its draw, the power a job needs to start.</summary>
    private static bool CraneHasPower(TestPair pair, EntityUid crane)
    {
        var consumer = pair.Server.EntMan.GetComponent<PowerConsumerComponent>(crane);
        return consumer.ReceivedPower >= 0.5f * consumer.DrawRate;
    }

    /// <summary>Ticks until a crane has the power a job needs, failing if it never does.</summary>
    internal static async Task WaitForCranePower(TestPair pair, EntityUid crane)
    {
        await DrydockTest.RunUntil(pair, 100, 10, () => CraneHasPower(pair, crane));
        Assert.That(CraneHasPower(pair, crane), Is.True, "The mobile crane should be powered.");
    }

    /// <summary>Fills a material storage with 10,000 of each stock material.</summary>
    internal static void Stock(TestPair pair, EntityUid uid)
    {
        var em = pair.Server.EntMan;
        Assert.That(pair.Server.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount((uid, em.GetComponent<MaterialStorageComponent>(uid)),
            StockMaterials.ToDictionary(m => m, _ => 10_000)), Is.True, $"{em.ToPrettyString(uid)} should take its stock.");
    }

    /// <summary>Spawns a material silo on a yard, off the arm column and clear of the console, always powered so it needs no APC.</summary>
    internal static EntityUid SpawnSilo(TestPair pair, EntityUid host)
    {
        var silo = DrydockTest.SpawnAnchored(pair, host, "MachineMaterialSilo", new Vector2i(5, 2));
        pair.Server.System<SharedPowerReceiverSystem>().SetNeedsPower(silo, false);
        return silo;
    }

    /// <summary>The material bill a console quotes for the repair of the ship in its berth.</summary>
    private static Dictionary<string, int> QuotedBill(TestPair pair, EntityUid console)
    {
        pair.Server.System<WFDrydockSystem>().UpdateUi(console, force: true);
        pair.Server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console, WFDrydockConsoleUiKey.Key, out var state);
        return new Dictionary<string, int>(state!.RepairMaterials);
    }

    /// <summary>A mobile crane holds the sheets a repair's bill is paid in, and the console holds none.</summary>
    [Test]
    public async Task MaterialConsoleChargesMaterials()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        var bill = new Dictionary<string, int>();
        var before = new Dictionary<string, int>();
        var bankBefore = 0;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            Stock(pair, crane);

            bill = QuotedBill(pair, console);
            Assert.That(bill.GetValueOrDefault("Steel"), Is.GreaterThan(0), "The repair should quote steel.");
            Assert.That(bank.TryGetBalance(payer, out bankBefore), Is.True, "The payer should have an account.");
        });

        await WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var storage = em.GetComponent<MaterialStorageComponent>(crane);
            foreach (var id in bill.Keys)
                before[id] = materials.GetMaterialAmount(crane, id, storage, localOnly: true);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start.");
            Assert.Multiple(() =>
            {
                Assert.That(em.TryGetComponent(console, out WFDrydockJobComponent job), Is.True, "The job should exist.");
                Assert.That(job?.PaidInMaterials, Is.True, "The job should be paid in materials.");
                foreach (var (id, amount) in bill)
                {
                    Assert.That(materials.GetMaterialAmount(crane, id, storage, localOnly: true), Is.EqualTo(before[id] - amount),
                        $"The crane's {id} should drop by the quoted bill.");
                }

                Assert.That(em.HasComponent<MaterialStorageComponent>(console), Is.False, "The console should hold no materials.");
                Assert.That(bank.TryGetBalance(payer, out var bankAfter) && bankAfter == bankBefore, Is.True,
                    "The bank balance should be unchanged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// On a material console a missing plastitanium wall is quoted at 2.6% of its construction materials, 1.3 times what
    /// breaking it pays, and a missing thruster at all of its own; the wall is priced well above a shuttle wall in credits.
    /// </summary>
    [Test]
    public async Task PlastitaniumWallBilledInKind()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var (console, _, ship, _) = SetUpMobileRepair(pair, map.MapId);
            var wall = DrydockTest.SpawnAnchored(pair, ship, PlastitaniumWall, new Vector2i(2, 2));
            var thruster = DrydockTest.SpawnAnchored(pair, ship, "Thruster", new Vector2i(2, 3));
            server.System<ShipRepairSystem>().GenerateRepairData(ship);
            em.DeleteEntity(wall);
            em.DeleteEntity(thruster);

            // Nothing caps the bill, so the quote is the wall's and thruster's own, however small the ship.
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            drydock.UpdateUi(console, comp, force: true);
            Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console,
                WFDrydockConsoleUiKey.Key, out var state), Is.True, "The console should have a state.");

            var station = new WFDrydockConsoleComponent();
            Assert.Multiple(() =>
            {
                Assert.That(state!.RepairMaterials, Is.EquivalentTo(new Dictionary<string, int>
                    {
                        ["Plastitanium"] = 11, ["Plasteel"] = 16, ["Steel"] = 6 + 1015,
                    }),
                    "The wall should bill 2.6% of its 4 plastitanium, 6 plasteel and 2 steel sheets, the thruster all its 10.15 steel.");
                Assert.That(drydock.RebuildPrice(station, protoMan.Index(PlastitaniumWall)),
                    Is.GreaterThan(2 * drydock.RebuildPrice(station, protoMan.Index(ShuttleWall))),
                    "A plastitanium wall should cost well over a shuttle wall to rebuild.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mobile crane with no stock of its own draws a repair's bill from a silo it is linked to.</summary>
    [Test]
    public async Task MaterialConsoleDrawsFromSilo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var silos = server.System<SharedOreSiloSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default, silo = default;
        var bill = new Dictionary<string, int>();
        var before = new Dictionary<string, int>();
        var bankBefore = 0;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            silo = SpawnSilo(pair, server.System<SharedTransformSystem>().GetGrid(console)!.Value);
            Stock(pair, silo);

            bill = QuotedBill(pair, console);
            Assert.That(bill.GetValueOrDefault("Steel"), Is.GreaterThan(0), "The repair should quote steel.");
            Assert.That(bank.TryGetBalance(payer, out bankBefore), Is.True, "The payer should have an account.");
        });

        await WaitForCranePower(pair, crane);
        await DrydockTest.RunUntil(pair, 100, 10, () => em.GetComponent<ApcPowerReceiverComponent>(silo).Powered);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(silo).Powered, Is.True, "The silo should be powered.");

            // Link the crane with the silo's toggle message, as its window sends it.
            var siloComp = em.GetComponent<OreSiloComponent>(silo);
            Assert.That(silos.CanTransmitMaterials((silo, siloComp, em.GetComponent<TransformComponent>(silo)), crane), Is.True,
                "A powered silo on the same grid should reach the crane.");
            em.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(em.GetNetEntity(crane)));
            Assert.That(em.GetComponent<OreSiloClientComponent>(crane).Silo, Is.EqualTo(silo), "The crane should link to the silo.");

            var craneStorage = em.GetComponent<MaterialStorageComponent>(crane);
            Assert.That(materials.GetMaterialAmount(crane, "Steel", craneStorage, localOnly: true), Is.EqualTo(0),
                "The crane should hold no steel of its own.");
            Assert.That(materials.GetMaterialAmount(crane, "Steel", craneStorage), Is.GreaterThan(0),
                "The linked silo's steel should count toward the crane.");

            var siloStorage = em.GetComponent<MaterialStorageComponent>(silo);
            foreach (var id in bill.Keys)
                before[id] = materials.GetMaterialAmount(silo, id, siloStorage, localOnly: true);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start.");
            Assert.Multiple(() =>
            {
                Assert.That(em.TryGetComponent(console, out WFDrydockJobComponent job), Is.True, "The job should exist.");
                Assert.That(job?.PaidInMaterials, Is.True, "The job should be paid in materials.");
                foreach (var (id, amount) in bill)
                {
                    Assert.That(materials.GetMaterialAmount(silo, id, siloStorage, localOnly: true), Is.EqualTo(before[id] - amount),
                        $"The silo's {id} should drop by the quoted bill.");
                    Assert.That(materials.GetMaterialAmount(crane, id, craneStorage, localOnly: true), Is.EqualTo(0),
                        $"The crane should stay empty of {id}.");
                }

                Assert.That(bank.TryGetBalance(payer, out var bankAfter) && bankAfter == bankBefore, Is.True,
                    "The bank balance should be unchanged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A mobile crane with no stock refuses a repair, so no job starts and nothing is taken.</summary>
    [Test]
    public async Task MaterialConsoleRefusesWhenShort()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        var bankBefore = 0;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            Assert.That(bank.TryGetBalance(payer, out bankBefore), Is.True, "The payer should have an account.");
        });

        await WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var storage = em.GetComponent<MaterialStorageComponent>(crane);
            var steelBefore = materials.GetMaterialAmount(crane, "Steel", storage, localOnly: true);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.False, "Work should be refused without materials.");
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "No job should start.");
                Assert.That(materials.GetMaterialAmount(crane, "Steel", storage, localOnly: true), Is.EqualTo(steelBefore), "Storage should be unchanged.");
                Assert.That(bank.TryGetBalance(payer, out var bankAfter) && bankAfter == bankBefore, Is.True,
                    "The bank balance should be unchanged.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A crane stocked with everything but plastitanium, on a ship missing a steel and a plastitanium wall, rebuilds the
    /// steel one and leaves the plastitanium wall out.
    /// </summary>
    [Test]
    public async Task MaterialConsoleRepairsWhatStockCovers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            Stock(pair, crane);
            var wall = DrydockTest.SpawnAnchored(pair, ship, PlastitaniumWall, new Vector2i(2, 2));
            var steel = DrydockTest.SpawnAnchored(pair, ship, "WallSolid", new Vector2i(2, 3));
            server.System<ShipRepairSystem>().GenerateRepairData(ship);
            em.DeleteEntity(wall);
            em.DeleteEntity(steel);
        });

        await WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            var items = drydock.BuildRepairItems(ship, comp).Count;
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start short.");
            Assert.That(em.GetComponent<WFDrydockJobComponent>(console).Total, Is.EqualTo(items - 1),
                "The job should leave the wall out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crane holding steel for one of two missing steel walls rebuilds that one and leaves the other.</summary>
    [Test]
    public async Task MaterialConsoleRepairsWhatSteelCovers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            Stock(pair, crane);
            var first = DrydockTest.SpawnAnchored(pair, ship, "WallSolid", new Vector2i(2, 2));
            var second = DrydockTest.SpawnAnchored(pair, ship, "WallSolid", new Vector2i(2, 3));
            server.System<ShipRepairSystem>().GenerateRepairData(ship);
            em.DeleteEntity(first);
            em.DeleteEntity(second);
        });

        await WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            drydock.UpdateUi(console, comp, force: true);
            server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console, WFDrydockConsoleUiKey.Key, out var state);
            var steel = state!.RepairMaterials.GetValueOrDefault("Steel");
            Assert.That(steel, Is.GreaterThanOrEqualTo(2), "Each wall should bill steel.");

            // Steel for one of the two identical walls.
            var materials = server.System<SharedMaterialStorageSystem>();
            materials.TryChangeMaterialAmount(crane, "Steel", steel / 2 - materials.GetMaterialAmount(crane, "Steel", localOnly: true), localOnly: true);

            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start short of steel.");
            Assert.That(em.GetComponent<WFDrydockJobComponent>(console).Total, Is.EqualTo(1), "The job should rebuild one wall.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The mobile crane's board builds it, and the Shuttlecraft tech unlocks both of its boards.</summary>
    [Test]
    public async Task CraneIsConstructible()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ProtoMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var board = em.SpawnEntity("WFMobileDrydockCraneMachineCircuitboard", new MapCoordinates(0, 0, map.MapId));
            Assert.That(em.GetComponent<MachineBoardComponent>(board).Prototype.Id, Is.EqualTo("WFMobileDrydockCrane"),
                "The crane board should build a mobile drydock crane.");
            Assert.That(em.GetComponent<MachineBoardComponent>(board).FrameSize, Is.EqualTo("2x2"),
                "The crane board should only fit the 2x2 frame its crane occupies.");

            var unlocks = protos.Index(Shuttlecraft).RecipeUnlocks;
            Assert.Multiple(() =>
            {
                Assert.That(unlocks, Does.Contain(ConsoleBoardRecipe), "Shuttlecraft should unlock the console board.");
                Assert.That(unlocks, Does.Contain(CraneBoardRecipe), "Shuttlecraft should unlock the crane board.");
                Assert.That(protos.HasIndex(ConsoleBoardRecipe), Is.True, "The console board recipe should exist.");
                Assert.That(protos.HasIndex(CraneBoardRecipe), Is.True, "The crane board recipe should exist.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Raises the walk-in berth light toggle from an actor, as the console's own UI would.</summary>
    private static void ToggleBerthLights(TestPair pair, EntityUid console, EntityUid actor)
    {
        DrydockTest.RaiseUiMessage(pair, console, WFDrydockConsoleUiKey.Key, new WFDrydockToggleBerthLightsMessage(), actor);
    }

    /// <summary>Berth lights frame the console's berth on its grid, toggle off on a second press, and go out when a job starts.</summary>
    [Test]
    public async Task BerthLightsFrameTheBerthUntilAJobStarts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, payer = default;
        WFDrydockBerth berth = default;

        await server.WaitAssertion(() =>
        {
            (console, crane, ship, payer) = SetUpMobileRepair(pair, map.MapId);
            Stock(pair, crane);

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out berth), Is.True, "The console should have a berth.");
        });

        await WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            ToggleBerthLights(pair, console, payer);
            Assert.That(em.TryGetComponent(console, out WFDrydockGuideComponent guide), Is.True, "Lights on should add a guide to the console.");
            Assert.Multiple(() =>
            {
                Assert.That(guide!.Station, Is.EqualTo(berth.Grid), "The guide should sit on the berth's grid.");
                Assert.That(guide.Center, Is.EqualTo(berth.Area.Center), "The frame should center on the berth.");
                Assert.That(guide.Size, Is.EqualTo(berth.Area.Size), "The frame should cover the berth.");
            });

            ToggleBerthLights(pair, console, payer);
            Assert.That(em.HasComponent<WFDrydockGuideComponent>(console), Is.False, "A second press should turn the lights off.");

            ToggleBerthLights(pair, console, payer);
            Assert.That(em.HasComponent<WFDrydockGuideComponent>(console), Is.True, "The lights should come back on.");

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, payer), Is.True, "The repair should start.");
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.True, "The job should exist.");
                Assert.That(em.HasComponent<WFDrydockGuideComponent>(console), Is.False, "Starting a job should turn the lights off.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Mobile crane construction graph: the crane is a 2x2 machine, so its frame is MachineFrame2x2.</summary>
    [Test]
    public async Task MobileCraneUsesMachine2x2Graph()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid crane = default;

        await server.WaitAssertion(() =>
        {
            (_, crane, _) = SpawnMobileYard(pair, map.MapId);
            var construction = em.GetComponent<ConstructionComponent>(crane);
            Assert.Multiple(() =>
            {
                Assert.That(construction.Graph, Is.EqualTo("Machine2x2"), "The crane should use the 2x2 machine graph.");
                Assert.That(construction.Node, Is.EqualTo("machine"), "The crane should start on the machine node.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Breaking the crane down to its frame keeps the four tiles its body covered.</summary>
    [Test]
    public async Task MobileCraneFrameCoversCraneTiles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid crane = default;

        await server.WaitAssertion(() =>
        {
            (_, crane, _) = SpawnMobileYard(pair, map.MapId);
            var craneBox = BodyBox(em, crane);
            // Collision boxes carry a polygon skin of about 0.01 tile, hence the 0.02 tolerance.
            Assert.That(craneBox.Left, Is.EqualTo(-0.5f).Within(0.02f), "The crane body should span the origin tile and the tile to +X.");
            Assert.That(craneBox.Right, Is.EqualTo(1.5f).Within(0.02f));
            Assert.That(craneBox.Bottom, Is.EqualTo(-1.5f).Within(0.02f), "The crane body should span the two tiles below the origin.");
            Assert.That(craneBox.Top, Is.EqualTo(0.5f).Within(0.02f));

            Assert.That(server.System<ConstructionSystem>().ChangeNode(crane, null, "machineFrame"), Is.True,
                "The crane should turn into its frame.");

            var frame = EntityUid.Invalid;
            var query = em.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID == "MachineFrame2x2")
                    frame = uid;
            }

            Assert.That(frame.IsValid(), Is.True, "The crane should leave a MachineFrame2x2.");
            // The frame's box is inset a quarter tile from the crane's, so compare centres and the four tile centres.
            var frameBox = BodyBox(em, frame);
            Assert.Multiple(() =>
            {
                Assert.That(frameBox.Center.X, Is.EqualTo(craneBox.Center.X).Within(0.001f), "The frame should sit on the crane's footprint.");
                Assert.That(frameBox.Center.Y, Is.EqualTo(craneBox.Center.Y).Within(0.001f));
                foreach (var tile in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(1, -1) })
                    Assert.That(frameBox.Contains(tile), Is.True, $"The frame should cover the tile at {tile}.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Mobile crane and console are wrenchable and rotatable, with a wires panel, like a ship machine.</summary>
    [Test]
    public async Task MobileMachinesUnanchorRotateAndOpenPanel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var crane = EntityUid.Invalid;
        var console = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            (_, crane, console) = SpawnMobileYard(pair, map.MapId);
            Assert.Multiple(() =>
            {
                foreach (var uid in new[] { crane, console })
                {
                    Assert.That(em.HasComponent<RotatableComponent>(uid), Is.True, $"{em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID} should rotate.");
#pragma warning disable RA0002
                    Assert.That(em.GetComponent<AnchorableComponent>(uid).Flags.HasFlag(AnchorableFlags.Unanchorable), Is.True,
                        $"{em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID} should be unwrenchable.");
#pragma warning restore RA0002
                    Assert.That(em.HasComponent<WiresPanelComponent>(uid), Is.True, "The machine should have a wires panel for the screwdriver.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Station cranes and the berth and traffic consoles cannot be unwrenched.</summary>
    [Test]
    public async Task StationDrydockMachinesCannotBeUnwrenched()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in new[] { "WFDrydockCrane", "WFDrydockCraneMirrored", "WFComputerDrydock", "WFComputerDrydockTraffic" })
            {
                var uid = em.SpawnEntity(proto, new MapCoordinates(0, 0, map.MapId));
#pragma warning disable RA0002
                var flags = em.GetComponent<AnchorableComponent>(uid).Flags;
#pragma warning restore RA0002
                Assert.Multiple(() =>
                {
                    Assert.That(flags.HasFlag(AnchorableFlags.Anchorable), Is.True, $"{proto} should stay anchorable.");
                    Assert.That(flags.HasFlag(AnchorableFlags.Unanchorable), Is.False, $"{proto} should not be unwrenchable.");
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The mobile crane's berth starts past its gap: nothing in the gap is in reach, but a wall there still blocks the arm.</summary>
    [Test]
    public async Task MobileBerthStartsPastTheGap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        EntityUid host = default, crane = default;

        await server.WaitAssertion(() =>
        {
            (host, crane, var console) = SpawnMobileYard(pair, map.MapId);
            var craneComp = em.GetComponent<WFDrydockCraneComponent>(crane);
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), Is.True, "The crane should make a berth.");

            // The crane on tile (0, 5) has its arm root at grid y 4, and its arm runs toward -Y.
            var gapEnd = 4f - craneComp.BerthGap;
            Assert.Multiple(() =>
            {
                Assert.That(craneComp.BerthGap, Is.EqualTo(3f), "The mobile crane should leave a three-tile gap.");
                Assert.That(berth.Area.Top, Is.EqualTo(gapEnd).Within(0.01f), "The berth should start at the end of the gap.");
                Assert.That(berth.Area.Height, Is.EqualTo(craneComp.MaxArm - craneComp.BerthGap).Within(0.01f), "The berth should run from the gap to the end of the arm.");
                Assert.That(drydock.CanReach(comp, host, crane, new Vector2(1.5f, gapEnd + 1.5f)), Is.False, "A point in the gap should be out of reach.");
                Assert.That(drydock.CanReach(comp, host, crane, new Vector2(1.5f, gapEnd - 1f)), Is.True, "A point past the gap should be in reach.");
            });

            DrydockTest.SpawnAnchored(pair, host, "WallSolid", new Vector2i(0, 2));
        });

        // Arm lanes are rescanned every two seconds.
        await DrydockTest.RunUntil(pair, 20, 10, () => em.GetComponent<WFDrydockCraneComponent>(crane).Blocked);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFDrydockCraneComponent>(crane).Blocked, Is.True, "A wall in the gap should still block the arm.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Wirecutters mirror an unbolted mobile crane with its panel open. Its body keeps its tiles while its arm and berth move
    /// to the other side. Bolted, closed or station cranes refuse.
    /// </summary>
    [Test]
    public async Task WirecuttersMirrorUnboltedMobileCrane()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var interaction = server.System<SharedInteractionSystem>();
        var xforms = server.System<SharedTransformSystem>();
        var wires = server.System<SharedWiresSystem>();
        var cutTicks = pair.SecondsToTicks(3f);

        EntityUid host = default, crane = default, console = default, user = default, cutters = default, station = default;
        Box2 bodyBefore = default;
        WFDrydockBerth berthBefore = default;

        bool Mirrored(EntityUid uid) => em.GetComponent<WFDrydockCraneComponent>(uid).Mirrored;

        async Task Cut(EntityUid target)
        {
            await server.WaitPost(() => interaction.InteractUsing(user, cutters, target, em.GetComponent<TransformComponent>(target).Coordinates));
            await server.WaitRunTicks(cutTicks);
        }

        await server.WaitAssertion(() =>
        {
            (host, crane, console) = SpawnMobileYard(pair, map.MapId);
            station = DrydockTest.SpawnAnchored(pair, host, "WFDrydockCrane", new Vector2i(2, 1));
            user = em.SpawnEntity("MobHuman", new EntityCoordinates(host, 2.5f, 3.5f));
            cutters = em.SpawnEntity("Wirecutter", new EntityCoordinates(host, 2.5f, 3.5f));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, cutters), Is.True, "The user should hold the wirecutters.");

            bodyBefore = BodyBox(em, crane);
            Assert.That(drydock.TryGetBerth(console, em.GetComponent<WFDrydockConsoleComponent>(console), out berthBefore), Is.True);
        });

        await Cut(crane);
        await server.WaitAssertion(() => Assert.That(Mirrored(crane), Is.False, "A closed crane should refuse."));

        await server.WaitPost(() => wires.TogglePanel(crane, em.GetComponent<WiresPanelComponent>(crane), true));
        await Cut(crane);
        await server.WaitAssertion(() => Assert.That(Mirrored(crane), Is.False, "A bolted crane should refuse."));

        await server.WaitPost(() => xforms.Unanchor(crane));
        await Cut(crane);
        await server.WaitAssertion(() =>
        {
            var craneComp = em.GetComponent<WFDrydockCraneComponent>(crane);
            Assert.That(craneComp.Mirrored, Is.True, "An unbolted crane with its panel open should mirror.");
            Assert.That(drydock.TryGetBerth(console, em.GetComponent<WFDrydockConsoleComponent>(console), out var berth), Is.True);

            var body = BodyBox(em, crane);
            Assert.Multiple(() =>
            {
                Assert.That(body.BottomLeft, Is.EqualTo(bodyBefore.BottomLeft), "The body should keep its tiles.");
                Assert.That(body.TopRight, Is.EqualTo(bodyBefore.TopRight), "The body should keep its tiles.");
                Assert.That(craneComp.ArmToLocal(0f, 0f).X, Is.EqualTo(1f), "The arm should leave from the body's other column.");
                Assert.That(berth.Area.Right, Is.EqualTo(1.5f).Within(0.01f), "The berth should end at the arm's new column.");
                Assert.That(berth.Area.Width, Is.EqualTo(berthBefore.Area.Width).Within(0.01f), "The berth should keep its width.");
                Assert.That(berth.Area.Top, Is.EqualTo(berthBefore.Area.Top).Within(0.01f), "The berth should keep its gap.");
            });
        });

        await Cut(crane);
        await server.WaitAssertion(() => Assert.That(Mirrored(crane), Is.False, "A second cut should mirror it back."));

        await server.WaitPost(() => xforms.Unanchor(station));
        await Cut(station);
        await server.WaitAssertion(() => Assert.That(Mirrored(station), Is.False, "Station cranes should not mirror with a tool."));

        await pair.CleanReturnAsync();
    }

    /// <summary>The body's collision box in tile units, relative to the entity's origin.</summary>
    private static Box2 BodyBox(IEntityManager em, EntityUid uid)
    {
        var shape = (PolygonShape) em.GetComponent<FixturesComponent>(uid).Fixtures["fix1"].Shape;
        var box = shape.ComputeAABB(new Robust.Shared.Physics.Transform(Vector2.Zero, 0f), 0);
        return Box2.FromTwoPoints(box.BottomLeft, box.TopRight);
    }
}
