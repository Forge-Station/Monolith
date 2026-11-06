#nullable enable
using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.Drydock;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WF.Drydock;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>
/// The durasteel plating refit swaps plastitanium walls in place and records them in the repair snapshot, so repairs
/// rebuild durasteel.
/// </summary>
[TestFixture]
[TestOf(typeof(WFHullPlatingRefitSystem))]
public sealed class DrydockDurasteelTest
{
    private static readonly ProtoId<WFShipRefitPrototype> DurasteelRefit = "WFRefitDurasteel";
    private static readonly Vector2i WallTile = new(1, 1);
    private static readonly Vector2i DiagonalTile = new(2, 1);
    private static readonly Vector2i SolidTile = new(3, 3);

    /// <summary>The wall anchored on a grid tile with a prototype, or invalid.</summary>
    private static EntityUid FindAt(TestPair pair, EntityUid grid, string proto, Vector2i tile)
    {
        var em = pair.Server.EntMan;
        var children = em.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            var xform = em.GetComponent<TransformComponent>(uid);
            var pos = xform.LocalPosition;
            if (xform.Anchored && em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == proto &&
                new Vector2i((int) MathF.Floor(pos.X), (int) MathF.Floor(pos.Y)) == tile)
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>
    /// Plastitanium walls are the units and become durasteel in place, turned as they were; other walls are left alone,
    /// and the swapped walls don't read as missing.
    /// </summary>
    [Test]
    public async Task RefitPlatesPlastitaniumInPlace()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var drydock = server.System<WFDrydockSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var durasteel = server.ResolveDependency<IPrototypeManager>().Index(DurasteelRefit);
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        EntityUid wall = default;
        EntityUid diagonal = default;
        EntityUid solid = default;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            wall = DrydockTest.SpawnAnchored(pair, grid, "WallPlastitanium", WallTile);
            diagonal = DrydockTest.SpawnAnchored(pair, grid, "WallPlastitaniumDiagonal", DiagonalTile);
            server.System<SharedTransformSystem>().SetLocalRotation(diagonal, Angle.FromDegrees(90));
            solid = DrydockTest.SpawnAnchored(pair, grid, "WallSolid", SolidTile);
            server.System<ShipRepairSystem>().GenerateRepairData(grid);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var survey = refits.Survey(grid, durasteel, 1);
            Assert.Multiple(() =>
            {
                Assert.That(WFShipRefitSystem.Units(survey), Is.EqualTo(2), "Only the plastitanium walls are units.");
                Assert.That(survey.Below.Select(t => t.Entity), Is.EquivalentTo(new[] { wall, diagonal }));
            });

            foreach (var target in survey.Below)
            {
                refits.Apply(durasteel, target.Entity, 1);
            }
            refits.Apply(durasteel, solid, 1);

            var plated = FindAt(pair, grid, "WFWallDurasteel", WallTile);
            var platedDiagonal = FindAt(pair, grid, "WFWallDurasteelDiagonal", DiagonalTile);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(wall) && em.Deleted(diagonal), "The plastitanium walls should be gone.");
                Assert.That(plated.IsValid() && platedDiagonal.IsValid(), "Each should be durasteel, anchored in place.");
                Assert.That(em.GetComponent<TransformComponent>(platedDiagonal).LocalRotation.Degrees, Is.EqualTo(90).Within(0.01),
                    "The diagonal should keep its turn.");
                Assert.That(em.Deleted(solid), Is.False, "A solid wall is never plated.");
                Assert.That(refits.NextTier(grid, durasteel, out _), Is.EqualTo(0), "A plated ship has nothing left to refit.");
                Assert.That(drydock.BuildRepairItems(grid, console), Is.Empty, "Plated walls should not read as missing.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Anvilhold's consoles and mobile drydocks both sell the plating.</summary>
    [Test]
    public async Task DrydocksSellDurasteel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        Assert.Multiple(() =>
        {
            foreach (var console in new[] { "WFComputerDrydock", "WFComputerMobileDrydock" })
            {
                Assert.That(protoMan.Index<EntityPrototype>(console).TryGetComponent<WFDrydockConsoleComponent>(out var comp, factory), console);
                Assert.That(comp!.Refits, Does.Contain(DurasteelRefit), $"{console} should sell durasteel plating.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The repair snapshot records the durasteel wall, so the ship repair tool and a drydock repair rebuild it as durasteel,
    /// with no refit charge.
    /// </summary>
    [Test]
    public async Task RepairRestoresDurasteel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid;
        var drydock = server.System<WFDrydockSystem>();
        var refits = server.System<WFShipRefitSystem>();
        var durasteel = server.ResolveDependency<IPrototypeManager>().Index(DurasteelRefit);
        var console = new WFDrydockConsoleComponent { Berth = "T" };

        EntityUid wall = default;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            wall = DrydockTest.SpawnAnchored(pair, grid, "WallPlastitanium", WallTile);
            server.System<ShipRepairSystem>().GenerateRepairData(grid);
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            refits.Apply(durasteel, wall, 1);
            refits.SetRefit(grid, DurasteelRefit.Id, 1, 1);
            var data = em.GetComponent<ShipRepairDataComponent>(grid);
            var spec = data.Chunks.Values.SelectMany(c => c.Entities.Values)
                .Single(s => s.OriginalEntity == em.GetNetEntity(FindAt(pair, grid, "WFWallDurasteel", WallTile)));
            Assert.That(data.EntityPalette[spec.ProtoIndex].Id, Is.EqualTo("WFWallDurasteel"),
                "The snapshot should record the durasteel wall, which the repair tool rebuilds.");

            em.DeleteEntity(FindAt(pair, grid, "WFWallDurasteel", WallTile));
            var rebuild = drydock.BuildRepairItems(grid, console).Single(i => i.Kind == WFDrydockWorkKind.Entity);
            Assert.Multiple(() =>
            {
                Assert.That(rebuild.Prototype?.Id, Is.EqualTo("WFWallDurasteel"), "The drydock should rebuild durasteel.");
                Assert.That(rebuild.RefitCost, Is.Zero, "A rebuilt durasteel wall carries no refit charge.");
            });

            drydock.ApplyItem(grid, rebuild);
            Assert.Multiple(() =>
            {
                Assert.That(FindAt(pair, grid, "WFWallDurasteel", WallTile).IsValid(), "The rebuilt wall should come back durasteel.");
                Assert.That(FindAt(pair, grid, "WallPlastitanium", WallTile).IsValid(), Is.False, "No plastitanium should be left under it.");
                Assert.That(drydock.BuildRepairItems(grid, console), Is.Empty, "A repaired ship should have no work left.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
