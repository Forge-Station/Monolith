using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Forge.KIAS;
using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.KIAS;
using Content.Shared.Power.Components;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasRoomGridPoseTests
{
    [Test]
    public async Task SavedBriarDoorBoundariesAndScannerCellsSurviveFarRotatedGridRebuilds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var maps = em.System<SharedMapSystem>();
        var topology = em.System<KiasRoomTopologySystem>();
        EntityUid grid = default;
        Dictionary<EntityUid, HashSet<Vector2i>> original = null;
        HashSet<Vector2i> doors = null;
        await pair.Server.WaitAssertion(() =>
        {
            maps.CreateMap(out var mapId, runMapInit: false);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(mapId,
                new ResPath("/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"), out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            maps.InitializeMap(mapId);
            em.System<SharedPhysicsSystem>().SetCanCollide(grid, false);
            var batteries = em.AllEntityQueryEnumerator<BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out var uid, out var battery, out var transform))
                if (transform.GridUid == grid) em.System<BatterySystem>().SetCharge(uid, battery.MaxCharge);
            topology.Invalidate(grid);
        });
        await pair.RunTicksSync(1800);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(topology.Grids[grid].Pending, Is.False);
            original = new();
            var scanners = em.AllEntityQueryEnumerator<KiasRoomScannerComponent, TransformComponent>();
            while (scanners.MoveNext(out var uid, out _, out var transform))
                if (transform.GridUid == grid) original.Add(uid, topology.ScannerCells(uid).ToHashSet());
            Assert.That(original.Count, Is.EqualTo(21));
            doors = topology.Grids[grid].Geometry.DoorTiles.ToHashSet();
        });
        foreach (var radians in new[] { .01, .1, .3, .7, 1.2 })
        {
            await pair.Server.WaitAssertion(() =>
            {
                var transforms = em.System<SharedTransformSystem>();
                transforms.SetWorldPosition(grid, new Vector2(114000, 0));
                transforms.SetWorldRotation(grid, new Angle(radians));
                topology.Invalidate(grid);
            });
            await pair.RunTicksSync(180);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(topology.Grids[grid].Pending, Is.False);
                Assert.That(topology.Grids[grid].Geometry.DoorTiles, Is.EquivalentTo(doors), $"Door footprint changed at {radians} radians");
                foreach (var (scanner, cells) in original)
                    Assert.That(topology.ScannerCells(scanner), Is.EquivalentTo(cells), $"Scanner {scanner} changed at {radians} radians");
            });
        }
        await pair.CleanReturnAsync();
    }
}
