using Content.Shared._WF.Remade.Corruption;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Corruption;

/// <summary>
/// What corruption looks like. Each tile works out which of its eight neighbours join it, walls and windows included, and
/// hands the client that as an appearance mask, so a carpet reads as one piece with a ragged edge. A wall that corruption
/// touches gets a growth entity on it showing which sides, and loses it when the corruption goes.
/// </summary>
public sealed partial class WFCorruptionSystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly EntProtoId WallGrowthProto = "WFCorruptionWall";
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    private struct Probe
    {
        public EntityUid? Corruption;
        public EntityUid? Growth;
        public bool Wall;
        public bool Joins;
    }

    private Probe ProbeTile(Entity<MapGridComponent> grid, Vector2i tile, EntityUid? excluded)
    {
        var probe = new Probe();
        var anchored = _map.GetAnchoredEntities(grid, tile);
        while (anchored.MoveNext(out var found))
        {
            if (found is not { } uid || uid == excluded || TerminatingOrDeleted(uid))
                continue;

            if (HasComp<WFCorruptionComponent>(uid))
            {
                probe.Corruption = uid;
                probe.Joins = true;
            }
            else if (HasComp<WFCorruptionWallComponent>(uid))
            {
                probe.Growth = uid;
            }
            else if (_tag.HasTag(uid, WallTag))
            {
                probe.Wall = true;
                probe.Joins = true;
            }
            else if (_tag.HasTag(uid, WindowTag))
            {
                probe.Joins = true;
            }
        }

        return probe;
    }

    /// <summary>Works out the appearance of the tiles round one that was just grown or lost, and of the walls beside it.</summary>
    private void RefreshAround(EntityUid gridUid, Vector2i center, EntityUid? excluded)
    {
        if (TerminatingOrDeleted(gridUid) || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return;

        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var probes = new Probe[5, 5];
        for (var x = -2; x <= 2; x++)
        for (var y = -2; y <= 2; y++)
            probes[x + 2, y + 2] = ProbeTile(grid, center + new Vector2i(x, y), excluded);

        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            if (probes[x + 2, y + 2].Corruption is { } tile)
                _appearance.SetData(tile, WFCorruptionVisuals.Mask, FloorMask(probes, x, y));
        }

        foreach (var step in Cardinals)
        {
            if (probes[step.X + 2, step.Y + 2].Wall)
                RefreshWall(grid, center + step, probes, step.X, step.Y);
        }
    }

    /// <summary>The 8-bit mask of which neighbours of the tile at (x, y) in the probe window join it.</summary>
    private static int FloorMask(Probe[,] probes, int x, int y)
    {
        bool Joins(int dx, int dy) => probes[x + dx + 2, y + dy + 2].Joins;

        var n = Joins(0, 1);
        var s = Joins(0, -1);
        var e = Joins(1, 0);
        var w = Joins(-1, 0);
        var mask = (n ? 1 : 0) | (s ? 2 : 0) | (e ? 4 : 0) | (w ? 8 : 0);
        if (n && e && Joins(1, 1))
            mask |= 16;
        if (s && e && Joins(1, -1))
            mask |= 32;
        if (s && w && Joins(-1, -1))
            mask |= 64;
        if (n && w && Joins(-1, 1))
            mask |= 128;

        return mask;
    }

    private void RefreshWall(Entity<MapGridComponent> grid, Vector2i wall, Probe[,] probes, int x, int y)
    {
        var mask = (probes[x + 2, y + 3].Corruption != null ? 1 : 0)
                   | (probes[x + 2, y + 1].Corruption != null ? 2 : 0)
                   | (probes[x + 3, y + 2].Corruption != null ? 4 : 0)
                   | (probes[x + 1, y + 2].Corruption != null ? 8 : 0);

        var growth = probes[x + 2, y + 2].Growth;
        if (mask == 0)
        {
            if (growth is { } stale)
                QueueDel(stale);

            return;
        }

        growth ??= Spawn(WallGrowthProto, _map.GridTileToLocal(grid, grid.Comp, wall));
        _appearance.SetData(growth.Value, WFCorruptionVisuals.Mask, mask);
    }

    /// <summary>Drops growth whose wall has gone, say deconstructed, so it can never hang in the air. Checks a quarter of them a tick.</summary>
    private void TickWalls()
    {
        _wallSlice = (_wallSlice + 1) % WallSlices;
        var growths = EntityQueryEnumerator<WFCorruptionWallComponent, TransformComponent>();
        while (growths.MoveNext(out var uid, out _, out var xform))
        {
            if (uid.Id % WallSlices != _wallSlice)
                continue;

            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            {
                QueueDel(uid);
                continue;
            }

            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (ProbeTile((gridUid, grid), tile, uid).Wall)
                continue;

            QueueDel(uid);
            RefreshAround(gridUid, tile, uid);
        }
    }
}
