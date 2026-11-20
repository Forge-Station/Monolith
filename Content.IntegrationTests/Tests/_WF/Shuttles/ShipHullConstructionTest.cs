using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Construction;
using Content.Server.Construction.Completions;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>Distinguishes completed structural deconstruction from damage and cancelled construction.</summary>
public sealed class ShipHullConstructionTest
{
    [Test]
    public async Task NativeConstructionAndRcdCompletionRemoveOnlyIntentionalHullCapacity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var status = em.System<ShipStatusSystem>();
        EntityUid console = default;
        EntityUid changedWall = default;
        EntityUid rcdWall = default;
        await pair.Server.WaitAssertion(() =>
        {
            console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            foreach (var prototype in new[] { "WallSolid", "Window", "Airlock" })
            {
                var structure = em.SpawnEntity(prototype, map.GridCoords);
                Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
                new DeleteEntity().PerformAction(structure, null, em);
                Assert.That(em.Deleted(structure), Is.True);
                Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                    $"Completed native deletion of {prototype} must leave healthy floor, not permanent structural damage.");
            }
            changedWall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            Assert.That(em.System<ConstructionSystem>().ChangeNode(changedWall, null, "girder"), Is.True);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(changedWall), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Replacing a wall with its girder through the real construction graph must revise its design capacity.");
            rcdWall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            var rcd = em.SpawnEntity("RCD", map.GridCoords);
            var component = em.GetComponent<RCDComponent>(rcd);
            var prototype = pair.Server.ProtoMan.Index<RCDPrototype>("Deconstruct");
            typeof(RCDComponent).GetProperty(nameof(RCDComponent.CachedPrototype))!.SetValue(component, prototype);
            var data = new MapGridData(map.Grid.Owner, map.Grid.Comp, map.GridCoords, map.Tile, Vector2i.Zero);
            // Exercise the native successful-operation branch; tool range and do-after checks are independent.
            typeof(RCDSystem).GetMethod("FinalizeRCDOperation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(em.System<RCDSystem>(), new object[] { rcd, component, data, Direction.South, rcdWall, rcd });
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(rcdWall), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Successful RCD structural removal must use the same intentional-removal accounting.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CancelledAndUnsurveyedRemovalsCannotEraseCombatLoss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var status = em.System<ShipStatusSystem>();
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var wall = em.SpawnEntity("WallSolid", map.GridCoords);
            var secondWall = em.SpawnEntity("WallSolid", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            var cancelled = new ConstructionBeforeDeleteEvent(null);
            em.EventBus.RaiseLocalEvent(wall, cancelled);
            cancelled.Cancel();
            em.DeleteEntity(wall);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.5f).Within(0.001f),
                "A cancelled construction event must not excuse subsequent destruction.");
            new DeleteEntity().PerformAction(secondWall, null, em);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.Zero,
                "Removing one surviving wall intentionally must retain the other wall's combat loss on the same tile.");
            var unsurveyed = em.SpawnEntity("WallSolid", map.GridCoords);
            new DeleteEntity().PerformAction(unsurveyed, null, em);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.Zero,
                "An entity built and removed between surveys must not consume historic capacity it never supplied.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeconstructionOnSplitFragmentDoesNotEditItsSiblingsSurvey()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var maps = em.System<SharedMapSystem>();
            var status = em.System<ShipStatusSystem>();
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 2; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            var left = new EntityCoordinates(map.Grid.Owner, new Vector2(0.5f, 0.5f));
            var console = em.SpawnEntity("ComputerShuttle", left);
            var wall = em.SpawnEntity("WallSolid", left);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            pair.Server.CfgMan.SetCVar(CVars.GridSplitting, true);
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(1, 0), Tile.Empty),
                (new Vector2i(1, 1), Tile.Empty),
            });
            Assert.That(em.GetComponent<TransformComponent>(wall).GridUid, Is.Not.EqualTo(map.Grid.Owner));
            pair.Server.CfgMan.SetCVar(CVars.GridSplitting, false);
            new DeleteEntity().PerformAction(wall, null, em);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.2f).Within(0.001f),
                "Intentional removal before the fragment's first new survey must use its inherited location.");
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, Vector2i.Zero, map.Tile.Tile);
            var originalConsole = em.SpawnEntity("ComputerShuttle",
                new EntityCoordinates(map.Grid.Owner, new Vector2(2.5f, 0.5f)));
            Assert.That(status.GetStatus(originalConsole)!.Summary.HullIntegrity, Is.EqualTo(0.6f).Within(0.001f),
                "Restoring only the original grid's floor cannot restore the wall removed on its sibling fragment.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallShotIntoAGirderStaysALoss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var status = em.System<ShipStatusSystem>();
        EntityUid console = default;
        EntityUid wall = default;
        await pair.Server.WaitAssertion(() =>
        {
            console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            wall = em.SpawnEntity("WallSolidDiagonal", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            // Past the 500 girder threshold but short of the 600 plain destruction one.
            Hit(em, pair.Server.ProtoMan, wall, 550);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(wall), Is.True);
            var girders = 0;
            var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var meta, out var xform))
            {
                if (meta.EntityPrototype?.ID == "Girder" && xform.GridUid == map.Grid.Owner && !em.Deleted(uid))
                    girders++;
            }
            Assert.That(girders, Is.EqualTo(1), "Damage must leave a girder through the construction graph.");
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.LessThan(1f),
                "A wall shot into a girder is combat loss, not a design change.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CurtainsAreNotHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var status = em.System<ShipStatusSystem>();
        EntityUid console = default;
        EntityUid curtains = default;
        EntityUid shotCurtains = default;
        await pair.Server.WaitAssertion(() =>
        {
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            console = em.SpawnEntity("ComputerShuttle", At(0));
            curtains = em.SpawnEntity("CurtainsBlack", At(0));
            shotCurtains = em.SpawnEntity("CurtainsBlack", At(1));
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            new DestroyEntity().PerformAction(curtains, null, em);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(curtains), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Cutting down a curtain must not read as a lost hull location.");
            // Past the 5 damage that destroys a curtain: hull would score this as combat loss.
            Hit(em, pair.Server.ProtoMan, shotCurtains, 10);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(shotCurtains), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "A curtain shot down is not hull, so it is no lost location either.");
        });
        await pair.CleanReturnAsync();
        return;

        EntityCoordinates At(int x) => new(map.Grid.Owner, new Vector2(x + 0.5f, 0.5f));
    }

    [Test]
    public async Task MaterialDoorDeconstructionIsADesignChangeButDamageIsALoss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var status = em.System<ShipStatusSystem>();
        EntityUid console = default;
        EntityUid deconstructed = default;
        EntityUid shot = default;
        await pair.Server.WaitAssertion(() =>
        {
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            console = em.SpawnEntity("ComputerShuttle", At(0));
            deconstructed = em.SpawnEntity("MetalDoor", At(0));
            shot = em.SpawnEntity("MetalDoor", At(1));
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));
            // The door construction graph removes its entity with DestroyEntity, which raises no construction event.
            new DestroyEntity().PerformAction(deconstructed, null, em);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(deconstructed), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Taking a material door apart through its graph must leave healthy floor, not damage.");
            Hit(em, pair.Server.ProtoMan, shot, 5000);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(shot), Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.5f).Within(0.001f),
                "A door destroyed by damage stays a lost location.");
        });
        await pair.CleanReturnAsync();
        return;

        EntityCoordinates At(int x) => new(map.Grid.Owner, new Vector2(x + 0.5f, 0.5f));
    }

    [Test]
    public async Task RcdLatticeRemovalLeavesTheSurveyButOtherFloorLossStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var maps = em.System<SharedMapSystem>();
        var status = em.System<ShipStatusSystem>();
        var lattice = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
        await pair.Server.WaitAssertion(() =>
        {
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), lattice);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0), lattice);
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));

            RcdRemoveTile(new Vector2i(2, 0));
            Assert.That(maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0)).Tile.IsEmpty, Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Trimming lattice with the RCD changes the ship's design, so it is not a hole to repair.");

            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), Tile.Empty);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.5f).Within(0.001f),
                "Floor lost any other way is still a missing location.");
        });
        await pair.CleanReturnAsync();
        return;

        void RcdRemoveTile(Vector2i index)
        {
            var rcd = em.SpawnEntity("RCD", map.GridCoords);
            var component = em.GetComponent<RCDComponent>(rcd);
            var prototype = pair.Server.ProtoMan.Index<RCDPrototype>("Deconstruct");
            typeof(RCDComponent).GetProperty(nameof(RCDComponent.CachedPrototype))!.SetValue(component, prototype);
            var tile = maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, index);
            var data = new MapGridData(map.Grid.Owner, map.Grid.Comp, map.GridCoords, tile, index);
            // Exercise the native successful-operation branch; tool range and do-after checks are independent.
            typeof(RCDSystem).GetMethod("FinalizeRCDOperation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(em.System<RCDSystem>(), new object[] { rcd, component, data, Direction.South, null, rcd });
        }
    }

    [Test]
    public async Task ToolLatticeRemovalLeavesTheSurveyButOtherFloorLossStays()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var maps = em.System<SharedMapSystem>();
        var status = em.System<ShipStatusSystem>();
        var tiles = em.System<TileSystem>();
        var lattice = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Lattice"].TileId);
        await pair.Server.WaitAssertion(() =>
        {
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), lattice);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0), lattice);
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f));

            var cut = maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0));
            Assert.That(tiles.DeconstructTile(cut), Is.True, "Lattice comes up with the tool.");
            Assert.That(maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0)).Tile.IsEmpty, Is.True);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(1f),
                "Cutting lattice away with a tool changes the ship's design, so it is not a hole to repair.");

            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), Tile.Empty);
            Assert.That(status.GetStatus(console)!.Summary.HullIntegrity, Is.EqualTo(0.5f).Within(0.001f),
                "Floor lost any other way is still a missing location.");
        });
        await pair.CleanReturnAsync();
    }

    private static void Hit(IEntityManager em, IPrototypeManager prototypes, EntityUid target, int amount)
    {
        var blunt = prototypes.Index<DamageTypePrototype>("Blunt");
        em.System<DamageableSystem>().TryChangeDamage(target, new DamageSpecifier(blunt, FixedPoint2.New(amount)),
            ignoreResistances: true);
    }
}
