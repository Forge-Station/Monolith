using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._WF.Drydock;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    /// <summary>Time between scans of every crane's arm lane, and of the boom paths of cranes in a job.</summary>
    private static readonly TimeSpan BlockerCheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>Collision layers that stop a crane's arm or boom.</summary>
    private const CollisionGroup SolidLayers = CollisionGroup.Impassable | CollisionGroup.MidImpassable | CollisionGroup.HighImpassable;

    /// <summary>Sample spacing along the arm and across the boom, in tiles.</summary>
    private const float BlockerStep = 0.25f;

    /// <summary>Per crane, the across distance to the nearest blocker on each integer arm row of its boom; MaxValue where clear.</summary>
    private readonly Dictionary<EntityUid, float[]> _boomBlock = new();

    /// <summary>Solid-entity name per tile for the scan in progress. Cleared before each scan.</summary>
    private readonly Dictionary<Vector2i, string?> _tileMemo = new();

    private TimeSpan _nextBlockerCheck;

    /// <summary>Rescans every crane's arm lane, and the boom paths of cranes in a job, once per interval.</summary>
    private void UpdateBlockers(TimeSpan curTime)
    {
        if (curTime < _nextBlockerCheck)
            return;

        _nextBlockerCheck = curTime + BlockerCheckInterval;
        var changed = false;
        var cranes = EntityQueryEnumerator<WFDrydockCraneComponent>();
        while (cranes.MoveNext(out var uid, out var crane))
        {
            changed |= ScanArmLane(uid, crane);
        }

        var jobs = EntityQueryEnumerator<WFDrydockJobComponent>();
        while (jobs.MoveNext(out _, out var job))
        {
            ScanJobBooms(job);
        }

        if (!changed)
            return;

        var consoles = EntityQueryEnumerator<WFDrydockConsoleComponent>();
        while (consoles.MoveNext(out var console, out var comp))
        {
            UpdateUi(console, comp);
        }
    }

    /// <summary>Rescans the cranes linked to a console, so its start check uses current blockers.</summary>
    private void RefreshCranes(EntityUid console)
    {
        foreach (var uid in LinkedCranes(console))
        {
            ScanArmLane(uid, Comp<WFDrydockCraneComponent>(uid));
        }
    }

    /// <summary>Finds the first solid entity in a crane's arm lane. Returns whether its blocker changed.</summary>
    private bool ScanArmLane(EntityUid uid, WFDrydockCraneComponent crane)
    {
        if (!TryStartScan(uid, out var gridUid, out var grid, out var toGrid))
            return SetBlocker(uid, crane, null);

        string? blocker = null;
        var lane = (int) MathF.Ceiling(crane.MaxArm / BlockerStep);
        for (var i = 0; i <= lane && blocker == null; i++)
        {
            // Five samples across the arm, 0.4 tiles each side of its line.
            for (var j = -2; j <= 2 && blocker == null; j++)
            {
                var tile = ArmTile(crane, toGrid, j * 0.2f, i * BlockerStep);
                blocker = SolidAt(uid, gridUid, grid, tile);
            }
        }

        return SetBlocker(uid, crane, blocker);
    }

    /// <summary>Caches the nearest blocker on each arm row of a crane's boom path, reusing its array when the size matches.</summary>
    private void ScanBoomPaths(EntityUid uid, WFDrydockCraneComponent crane)
    {
        if (!TryStartScan(uid, out var gridUid, out var grid, out var toGrid))
            return;

        var rows = (int) MathF.Ceiling(crane.MaxArm) + 1;
        var samples = (int) MathF.Ceiling(crane.MaxExtension / BlockerStep);
        if (!_boomBlock.TryGetValue(uid, out var block) || block.Length != rows)
        {
            block = new float[rows];
            _boomBlock[uid] = block;
        }

        for (var row = 0; row < rows; row++)
        {
            block[row] = float.MaxValue;
            for (var i = 0; i <= samples; i++)
            {
                var across = i * BlockerStep;
                if (SolidAt(uid, gridUid, grid, ArmTile(crane, toGrid, across, row + 0.5f)) == null)
                    continue;

                block[row] = across;
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a crane's grid and its crane-to-grid transform, and clears the tile memo for a new scan.
    /// Off a grid, drops the crane's cached boom paths and returns false.
    /// </summary>
    private bool TryStartScan(EntityUid uid, out EntityUid gridUid, [NotNullWhen(true)] out MapGridComponent? grid, out Matrix3x2 toGrid)
    {
        var xform = Transform(uid);
        toGrid = Matrix3x2.CreateRotation((float) xform.LocalRotation.Theta) * Matrix3x2.CreateTranslation(xform.LocalPosition);
        gridUid = xform.GridUid ?? EntityUid.Invalid;
        if (!TryComp(gridUid, out grid))
        {
            _boomBlock.Remove(uid);
            return false;
        }

        _tileMemo.Clear();
        return true;
    }

    /// <summary>Caches the boom paths of every crane a job works with.</summary>
    private void ScanJobBooms(WFDrydockJobComponent job)
    {
        foreach (var slot in job.Slots)
        {
            if (slot.Crane is { } craneUid && TryComp<WFDrydockCraneComponent>(craneUid, out var crane))
                ScanBoomPaths(craneUid, crane);
        }
    }

    /// <summary>Stores a crane's blocker, dirtying it only when the blocker changed. Returns whether it changed.</summary>
    private bool SetBlocker(EntityUid uid, WFDrydockCraneComponent crane, string? blocker)
    {
        if (crane.Blocker == blocker)
            return false;

        crane.Blocker = blocker;
        Dirty(uid, crane);
        return true;
    }

    /// <summary>The grid tile under an arm-frame point of a crane.</summary>
    private static Vector2i ArmTile(WFDrydockCraneComponent crane, Matrix3x2 toGrid, float across, float along)
    {
        var point = Vector2.Transform(crane.ArmToLocal(across, along), toGrid);
        return new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));
    }

    /// <summary>The name of a solid entity anchored on a tile, other than the crane, or null. Memoized per scan.</summary>
    private string? SolidAt(EntityUid crane, EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        if (_tileMemo.TryGetValue(tile, out var known))
            return known;

        string? name = null;
        foreach (var ent in _map.GetAnchoredEntities(gridUid, grid, tile))
        {
            if (ent == crane || !IsSolid(ent))
                continue;

            name = MetaData(ent).EntityName;
            break;
        }

        _tileMemo[tile] = name;
        return name;
    }

    /// <summary>Whether an entity has a hard fixture on a layer that stops arms and booms.</summary>
    private bool IsSolid(EntityUid uid)
    {
        if (!TryComp<PhysicsComponent>(uid, out var body) || !body.CanCollide || !TryComp<FixturesComponent>(uid, out var fixtures))
            return false;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & (int) SolidLayers) != 0)
                return true;
        }

        return false;
    }

    /// <summary>Across distance to the nearest blocker on the boom path at an arm row, or MaxValue when clear. Scans on first use outside a job.</summary>
    private float BoomBlockAt(EntityUid uid, WFDrydockCraneComponent crane, float along)
    {
        if (!_boomBlock.ContainsKey(uid))
            ScanBoomPaths(uid, crane);

        if (!_boomBlock.TryGetValue(uid, out var rows))
            return float.MaxValue;

        return rows[Math.Clamp((int) MathF.Floor(along), 0, rows.Length - 1)];
    }

    /// <summary>Shortens a boom so its tip stays short of the nearest blocker on the carriage's row, but not below one section.</summary>
    private float ClampBoom(EntityUid uid, WFDrydockCraneComponent crane, float along, float extension)
    {
        var limit = Math.Max(crane.SectionLength, BoomBlockAt(uid, crane, along) - 0.5f);
        return Math.Min(extension, limit);
    }
}
