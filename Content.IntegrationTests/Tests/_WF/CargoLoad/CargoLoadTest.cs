#nullable enable
using System.Numerics;
using Content.Server._WF.CargoLoad;
using Content.Server._WF.Planets.Flight;
using Content.Shared._WF.CargoLoad;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.CargoLoad;

/// <summary>
/// Cargo weight: a grid sums its crates' tonnage, a loaded ship keeps hull / (hull + load) of its thrust down to the
/// floor, planet lift counts the load, and anchors keep their load to themselves.
/// </summary>
[TestFixture]
[TestOf(typeof(WFCargoLoadSystem))]
public sealed class CargoLoadTest
{
    // Metals are Bulk (5t), luxuries Light (1t), in WFCargoLoadDefault.
    private const string BulkCrate = "TradeGoodMetals";
    private const string LightCrate = "TradeGoodLuxuries";
    private static readonly EntProtoId SteelSheet = "SheetSteel1";

    [Test]
    public async Task LoadSlowsTheShipAndCountsAgainstLift()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var cargo = entMan.System<WFCargoLoadSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();

        EntityUid ship = default;
        EntityUid crate = default;

        await server.WaitAssertion(() =>
        {
            var grid = mapSys.CreateGridEntity(map.MapId);
            for (var x = 0; x < 10; x++)
            {
                for (var y = 0; y < 10; y++)
                {
                    mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            ship = grid.Owner;
            physics.SetBodyType(ship, BodyType.Dynamic);
            for (var i = 0; i < 4; i++)
            {
                entMan.SpawnEntity(BulkCrate, new EntityCoordinates(ship, 1.5f + i, 1.5f));
            }

            crate = entMan.SpawnEntity(LightCrate, new EntityCoordinates(ship, 1.5f, 3.5f));
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(21f).Within(0.01f), "Four Bulk and one Light crate weigh 21t.");

            var hull = entMan.GetComponent<PhysicsComponent>(ship).FixturesMass;
            var expected = System.Math.Max(0.4f, hull / (hull + 21f));
            Assert.That(cargo.GetSpaceFactor(ship), Is.EqualTo(expected).Within(0.001f));

            var lift = new WFGridLiftLoadEvent(0f);
            entMan.EventBus.RaiseLocalEvent(ship, ref lift);
            Assert.That(lift.Mass, Is.EqualTo(21f).Within(0.01f), "Planet lift should count the cargo.");

            entMan.DeleteEntity(crate);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(20f).Within(0.01f), "A removed crate should drop the load within a sweep.");

            // A grid that can't fly is an anchor: its set is itself.
            physics.SetBodyType(ship, BodyType.Static);
            Assert.That(cargo.IsAnchor(ship), Is.True);
            Assert.That(cargo.GetCargoSet(ship), Is.EquivalentTo(new[] { ship }));

            // A stack weighs per unit: a full stack of sheets weighs its count times the per-sheet weight.
            var sheets = entMan.SpawnEntity("SheetSteel", new EntityCoordinates(ship, 5.5f, 5.5f));
            var count = entMan.GetComponent<StackComponent>(sheets).Count;
            var perUnit = entMan.GetComponent<WFCargoMassComponent>(sheets).PerUnit;
            Assert.That(perUnit, Is.GreaterThan(0f), "Steel sheets should carry a per-sheet weight.");
            Assert.That(cargo.Weight(sheets), Is.EqualTo(perUnit * count).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StoredMaterialsWeighTheirPackedShare()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var cargo = entMan.System<WFCargoLoadSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var physics = entMan.System<SharedPhysicsSystem>();
        var storage = entMan.System<SharedMaterialStorageSystem>();
        var profile = protos.Index(WFCargoLoadSystem.DefaultProfile);

        EntityUid ship = default;
        EntityUid silo = default;

        await server.WaitAssertion(() =>
        {
            var grid = mapSys.CreateGridEntity(map.MapId);
            for (var x = 0; x < 10; x++)
            {
                for (var y = 0; y < 10; y++)
                {
                    mapSys.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            ship = grid.Owner;
            physics.SetBodyType(ship, BodyType.Dynamic);
            silo = entMan.SpawnEntity("MachineMaterialSilo", new EntityCoordinates(ship, 2.5f, 2.5f));
            Assert.That(storage.TryChangeMaterialAmount(silo, "Steel", 10000), Is.True);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            // 10000 units is 100 sheets of steel; packed, they weigh StoredFactor of the same sheets loose.
            var sheet = protos.Index(SteelSheet);
            Assert.That(sheet.TryComp<WFCargoMassComponent>(out var mass, entMan.ComponentFactory), Is.True);
            var loose = mass!.PerUnit * 100f;
            Assert.That(cargo.MaterialWeight("Steel") * 10000f, Is.EqualTo(loose).Within(0.001f));

            var expected = loose * profile.StoredFactor;
            Assert.That(expected, Is.GreaterThan(0f));
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(expected).Within(0.01f), "A silo's materials should load the ship.");
            Assert.That(cargo.GetSetStored(new[] { ship }), Is.EqualTo(expected).Within(0.01f));
            Assert.That(cargo.GetSpaceFactor(ship), Is.LessThan(1f));

            // The helm's gauge reads the networked readout the sweep keeps on the ship.
            var readout = entMan.GetComponent<WFCargoReadoutComponent>(ship);
            Assert.That(readout.Load, Is.EqualTo(expected).Within(0.1f));
            Assert.That(readout.Stored, Is.EqualTo(expected).Within(0.1f));
            Assert.That(readout.Thrust, Is.EqualTo(cargo.GetSpaceFactor(ship)).Within(0.005f));
            Assert.That(readout.FullScale, Is.GreaterThan(readout.Load), "Full scale is the load that bottoms out thrust.");

            storage.TryChangeMaterialAmount(silo, "Steel", -10000);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(cargo.GetLoad(ship), Is.EqualTo(0f).Within(0.001f), "An emptied silo should weigh nothing.");
            Assert.That(entMan.HasComponent<WFCargoReadoutComponent>(ship), Is.False, "An empty ship shows no cargo gauge.");
        });

        await pair.CleanReturnAsync();
    }
}
