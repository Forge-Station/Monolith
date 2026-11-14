#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.Roofs;
using Content.Server._CE.ZLevels.Core;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Roofs;

/// <summary>
/// A roof shows only from a level above: nobody on a building's own level sees its roof, outside or in, while someone
/// one level up sees its open floor roofed over, never its walls, windows or doors. A ghost sees no roofs at all.
/// </summary>
[TestFixture]
[TestOf(typeof(WFRoofSystem))]
public sealed class WFRoofCoverageTest
{
    // One roofed row: 0 floor, 1 shut airlock, 2 floor, 3 wall, 4 window, 5 table, 6 floor.
    private static readonly (int X, string? Proto)[] Row =
    {
        (0, null), (1, "Airlock"), (2, null), (3, "WallSolid"), (4, "Window"), (5, "Table"), (6, null),
    };

    [Test]
    public async Task RoofsShowOnlyFromALevelAbove()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var ground = await pair.CreateTestMap();
        var upper = await pair.CreateTestMap();
        var elsewhere = await pair.CreateTestMap();
        var sEnt = server.EntMan;

        EntityUid network = default;
        EntityUid wall = default;
        EntityUid inside = default;
        EntityUid outsider = default;
        EntityUid above = default;
        await server.WaitPost(() =>
        {
            var maps = sEnt.System<SharedMapSystem>();
            var roofs = sEnt.System<SharedRoofSystem>();
            var xforms = sEnt.System<SharedTransformSystem>();
            var zLevels = sEnt.System<CEZLevelsSystem>();
            var net = zLevels.CreateMapNetwork();
            network = net.Owner;
            Assert.That(zLevels.TryAddMapsIntoNetwork(net, new Dictionary<EntityUid, int> { [ground.MapUid] = 0 }));
            Assert.That(zLevels.TryAddMapsIntoNetwork(net, new Dictionary<EntityUid, int> { [upper.MapUid] = 1 }));

            var grid = ground.Grid;
            sEnt.RemoveComponent<ImplicitRoofComponent>(grid);
            sEnt.EnsureComponent<RoofComponent>(grid);
            foreach (var (x, proto) in Row)
            {
                var index = new Vector2i(x, 0);
                maps.SetTile(grid, grid.Comp, index, ground.Tile.Tile);
                roofs.SetRoof((grid, grid.Comp, null), index, true);
                if (proto == null)
                    continue;
                var uid = sEnt.SpawnEntity(proto, maps.GridTileToLocal(grid, grid.Comp, index));
                if (!sEnt.GetComponent<TransformComponent>(uid).Anchored)
                    xforms.AnchorEntity(uid);
                if (proto == "WallSolid")
                    wall = uid;
            }

            inside = sEnt.SpawnEntity("MobHuman", maps.GridTileToLocal(grid, grid.Comp, new Vector2i(0, 0)));
            outsider = sEnt.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(3f, 2f), ground.MapId));
            above = sEnt.SpawnEntity("MobHuman", new EntityCoordinates(upper.Grid, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, outsider);
        });
        await pair.RunTicksSync(15);

        var cEnt = client.EntMan;
        var cRoofs = cEnt.System<WFRoofSystem>();
        Entity<MapGridComponent> cGrid = default;
        await client.WaitPost(() =>
        {
            var uid = cEnt.GetEntity(sEnt.GetNetEntity(ground.Grid.Owner));
            cGrid = (uid, cEnt.GetComponent<MapGridComponent>(uid));
        });

        bool Covers(int x) => cRoofs.Covers(cGrid, new Vector2i(x, 0));
        bool AnyCovered() => Covers(0) || Covers(2) || Covers(5) || Covers(6);

        // On the building's own level, outside it: walls and doors, no roof.
        await client.WaitAssertion(() =>
        {
            Assert.That(cRoofs.Enabled, "Someone with a body sees roofs.");
            Assert.That(cRoofs.BelowViewer(ground.CMapUid), Is.False, "Your own level isn't below you.");
            Assert.That(cRoofs.BelowViewer(upper.CMapUid), Is.False, "The level above isn't below you.");
            Assert.That(AnyCovered(), Is.False, "A roof is drawn for someone on the ground next to the building.");
        });

        // Inside it: no roof either.
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, inside));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
            Assert.That(AnyCovered(), Is.False, "A roof is drawn for someone inside the building."));

        // One level up, over it: the open floor is roofed over, the walls, windows and doors show through.
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, above));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            Assert.That(cEnt.GetComponent<TransformComponent>(cEnt.GetEntity(sEnt.GetNetEntity(above))).MapUid,
                Is.EqualTo(upper.CMapUid), "Precondition: the viewer fell off the level above.");
            Assert.That(cRoofs.BelowViewer(ground.CMapUid), "The ground is below the level above it.");
            Assert.That(cRoofs.BelowViewer(upper.CMapUid), Is.False, "Your own level isn't below you.");
            Assert.That(cRoofs.BelowViewer(elsewhere.CMapUid), Is.False, "A map outside your z-stack isn't below you.");
            Assert.That(Covers(0) && Covers(2) && Covers(6), "Open floor under the roof is covered from above.");
            Assert.That(Covers(5), "Furniture inside is roofed over with the floor.");
            Assert.That(Covers(1), Is.False, "An airlock shows through the roof.");
            Assert.That(Covers(3), Is.False, "A wall shows through the roof.");
            Assert.That(Covers(4), Is.False, "A window shows through the roof.");
        });

        // A wall torn down leaves floor to roof over: the remembered structure is forgotten.
        await server.WaitPost(() => sEnt.DeleteEntity(wall));
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() => Assert.That(Covers(3), "A removed wall's tile is roofed again."));

        // Ghosts and admin ghosts see no roofs, from above either.
        foreach (var observer in new[] { "MobObserver", "AdminObserver" })
        {
            EntityUid ghost = default;
            await server.WaitPost(() =>
            {
                ghost = sEnt.SpawnEntity(observer, new EntityCoordinates(upper.Grid, 0.5f, 0.5f));
                server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
            });
            await pair.RunTicksSync(15);
            await client.WaitAssertion(() =>
            {
                Assert.That(cRoofs.Enabled, Is.False, $"A {observer} sees roofs.");
                Assert.That(AnyCovered(), Is.False, $"A roof is drawn for a {observer}.");
            });
            await server.WaitPost(() =>
            {
                server.PlayerMan.SetAttachedEntity(pair.Player!, above);
                sEnt.DeleteEntity(ghost);
            });
            await pair.RunTicksSync(5);
        }

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            sEnt.DeleteEntity(inside);
            sEnt.DeleteEntity(outsider);
            sEnt.DeleteEntity(above);
            sEnt.System<CEZLevelsSystem>().DeleteMapNetwork(network);
        });
        await pair.CleanReturnAsync();
    }
}
