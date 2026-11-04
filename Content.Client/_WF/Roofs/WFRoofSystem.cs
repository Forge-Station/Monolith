using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Doors.Components;
using Content.Shared.Ghost;
using Content.Shared.Light.Components;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client._WF.Roofs;

/// <summary>
/// Works out for <see cref="WFRoofOverlay"/> where roofs show: only on levels below the local player's own, looking down
/// on them. Nobody sees a roof on their own level. Walls, windows and doors are never roofed over, and a ghost sees no
/// roofs.
/// </summary>
public sealed partial class WFRoofSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    /// <summary>Collision layers that together make a structure full height, such as a diagonal wall.</summary>
    private const int FullHeight = (int) (CollisionGroup.HighImpassable | CollisionGroup.MidImpassable | CollisionGroup.LowImpassable);

    /// <summary>Whether the local player sees roofs and roof shade at all: not as a ghost.</summary>
    public bool Enabled => SeesRoofs(_player.LocalEntity);

    /// <summary>Per grid, whether each tile asked about holds a wall, window or door.</summary>
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, bool>> _structures = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeVessels();
        _overlays.AddOverlay(new WFRoofOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WFRoofOverlay>();
    }

    /// <summary>Whether someone sees roofs: anyone with a body. A ghost, an observer or nobody at all sees none.</summary>
    public bool SeesRoofs(EntityUid? viewer)
    {
        return viewer is { } body && !TerminatingOrDeleted(body)
               && !HasComp<GhostComponent>(body) && !HasComp<SpectralComponent>(body);
    }

    /// <summary>
    /// Whether a map is a level below the local player's own in the same z-stack, the only place they see roofs. A
    /// transit map counts by how far its ship has climbed; the player's own level is never below them.
    /// </summary>
    public bool BelowViewer(EntityUid map)
    {
        if (_player.LocalEntity is not { } viewer || !SeesRoofs(viewer) || Transform(viewer).MapUid is not { } own
            || own == map)
            return false;
        return TryLevel(map, out var network, out var level) && TryLevel(own, out var ownNetwork, out var ownLevel)
               && network == ownNetwork && level < ownLevel;
    }

    /// <summary>A map's z-stack and the altitude of its floor in it; a transit map's lies between its two levels.</summary>
    private bool TryLevel(EntityUid map, out EntityUid network, out float altitude)
    {
        network = default;
        altitude = 0f;
        if (TryComp(map, out CEZMapComponent? zMap))
        {
            network = zMap.NetworkUid;
            altitude = zMap.Depth;
            return true;
        }

        if (!TryComp(map, out CEZTransitMapComponent? transit))
            return false;
        var progress = _zLevels.GetTransitProgress(transit);
        if (transit.LowerMap is { } lower && TryComp(lower, out CEZMapComponent? lowerMap))
        {
            network = lowerMap.NetworkUid;
            altitude = lowerMap.Depth + progress;
            return true;
        }
        if (transit.UpperMap is { } upper && TryComp(upper, out CEZMapComponent? upperMap))
        {
            network = upperMap.NetworkUid;
            altitude = upperMap.Depth - 1 + progress;
            return true;
        }
        return false;
    }

    /// <summary>Whether a roof is drawn over a tile of a grid for the local player, as <see cref="WFRoofOverlay"/> draws it.</summary>
    public bool Covers(Entity<MapGridComponent> grid, Vector2i index)
    {
        var implicitRoof = HasComp<ImplicitRoofComponent>(grid);
        TryComp(grid, out RoofComponent? roof);
        if (!implicitRoof && (roof == null || Underground(grid)) || Transform(grid).MapUid is not { } map || !BelowViewer(map))
            return false;
        return Covers(grid, roof, implicitRoof ? Enclosed(grid) : null, index);
    }

    /// <summary>
    /// Whether a roof is drawn over a tile seen from above: roofed (by the grid's roof bits, or as part of a vessel's
    /// <paramref name="enclosed"/> rooms) and not holding a wall, window or door.
    /// </summary>
    public bool Covers(Entity<MapGridComponent> grid, RoofComponent? roof, HashSet<Vector2i>? enclosed, Vector2i index)
    {
        var roofed = enclosed?.Contains(index) ?? roof != null && IsRoofed(roof, index);
        return WFRoofTiles.Covers(roofed, roofed && IsStructure(grid, grid.Comp, index));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        UpdateVessels(frameTime);
    }

    /// <summary>Whether a tile's roof bit is set, without the per-tile entity lookup the roof system falls back on.</summary>
    public static bool IsRoofed(RoofComponent roof, Vector2i index)
    {
        var chunk = SharedMapSystem.GetChunkIndices(index, RoofComponent.ChunkSize);
        if (!roof.Data.TryGetValue(chunk, out var mask))
            return false;
        var relative = SharedMapSystem.GetChunkRelative(index, RoofComponent.ChunkSize);
        var bit = 1UL << (relative.X + relative.Y * RoofComponent.ChunkSize);
        return (mask & bit) != 0;
    }

    /// <summary>
    /// A wall, window, door (open or shut) or other full-height structure anchored on the tile. Remembered until
    /// something is anchored or unanchored there or the tile changes.
    /// </summary>
    public bool IsStructure(EntityUid grid, MapGridComponent gridComp, Vector2i index)
    {
        if (!_structures.TryGetValue(grid, out var tiles))
            _structures[grid] = tiles = new Dictionary<Vector2i, bool>();
        if (tiles.TryGetValue(index, out var known))
            return known;

        var structure = false;
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, gridComp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<DoorComponent>(uid) || TryComp(uid, out PhysicsComponent? body) && WallLike(body))
            {
                structure = true;
                break;
            }
        }
        tiles[index] = structure;
        return structure;
    }

    /// <summary>A hard body on the wall or window layer, or one that blocks at every height.</summary>
    private static bool WallLike(PhysicsComponent body)
    {
        var layer = body.CollisionLayer;
        return body.Hard && ((layer & (int) CollisionGroup.Impassable) != 0 || (layer & FullHeight) == FullHeight);
    }

    /// <summary>A layer under another world layer, such as a cavern: roofed all over by the ground above, not by buildings.</summary>
    public bool Underground(EntityUid grid)
    {
        return HasComp<MapComponent>(grid) && _zLevels.TryMapUp(grid, out var above)
               && HasComp<MapGridComponent>(above.Owner);
    }

    /// <summary>Forgets whether a tile holds a structure, so it's looked at again.</summary>
    private void ForgetStructure(EntityUid grid, Vector2i index)
    {
        if (_structures.TryGetValue(grid, out var tiles))
            tiles.Remove(index);
    }
}
