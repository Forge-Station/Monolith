using System.Linq;
using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WF.Caverns;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Timing;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Moves a climber between a cavern and its ground when the climb finishes: down onto the pad at the climb point, up
/// onto safe ground. Whatever the climber was pulling follows, as it does over stairs.
/// </summary>
public sealed partial class WFCavernClimbSystem : SharedWFCavernClimbSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How far, in tiles, the exit or landing may lie from the climb point in either axis: a 5x5 square.</summary>
    public const int ExitReach = 2;

    /// <summary>How much rope a pull taken up again after a level change has, in tiles: about what one started beside its puller has.</summary>
    public const float CarryRope = 1.15f;

    /// <summary>Exit and landing offsets from the climb point, nearest first.</summary>
    private static readonly Vector2i[] ExitOffsets = BuildExitOffsets();

    /// <summary>Pulls a change of level ended, and the tick it ended them on.</summary>
    private readonly List<(EntityUid Puller, EntityUid Pulled, GameTick Tick)> _ended = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCavernClimbComponent, WFCavernClimbDoAfterEvent>(OnClimbedUp);
        SubscribeLocalEvent<WFCavernShaftComponent, WFCavernClimbDoAfterEvent>(OnClimbedDown);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // A tick late: the hand that held the pull is only free once the tick it ended on is over.
        for (var i = _ended.Count - 1; i >= 0; i--)
        {
            var (puller, pulled, tick) = _ended[i];

            if (tick >= _timing.CurTick)
                continue;

            _ended.RemoveAt(i);
            BringPulled(puller, pulled);
        }
    }

    private void OnClimbedUp(Entity<WFCavernClimbComponent> ent, ref WFCavernClimbDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var result = ClimbUp(args.User, ent);
        if (result == WFCavernClimbResult.Climbed)
            return;

        var key = result == WFCavernClimbResult.Hull ? "wf-cavern-climb-blocked-hull" : "wf-cavern-climb-blocked";
        _popup.PopupEntity(Loc.GetString(key), args.User, args.User);
    }

    private void OnClimbedDown(Entity<WFCavernShaftComponent> ent, ref WFCavernClimbDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!ClimbDown(args.User, ent))
            _popup.PopupEntity(Loc.GetString("wf-cavern-climb-down-blocked"), args.User, args.User);
    }

    /// <summary>Moves a climber at a climb point up onto the nearest safe ground tile, or reports what blocks the way.</summary>
    public WFCavernClimbResult ClimbUp(EntityUid user, EntityUid climbPoint)
    {
        var xform = Transform(climbPoint);

        if (!TryComp<WFCavernLayerComponent>(xform.MapUid, out var layer)
            || Transform(user).MapUid != xform.MapUid
            || !TryComp<WFCavernGroundComponent>(layer.Ground, out var ground)
            || !TryComp<MapGridComponent>(layer.Ground, out var grid))
            return WFCavernClimbResult.Blocked;

        var from = _map.WorldToTile(layer.Ground, grid, _transform.GetWorldPosition(xform));
        var result = FindExit((layer.Ground, ground, grid), from, out var exit);

        if (result != WFCavernClimbResult.Climbed)
            return result;

        var pulled = CompOrNull<PullerComponent>(user)?.Pulling;

        if (!_zLevels.TryMoveUp(user) || Transform(user).MapUid != layer.Ground)
            return WFCavernClimbResult.Blocked;

        // Onto solid ground at rest: the ground height is recached on the move, so nothing falls back or bounces.
        _transform.SetCoordinates(user, _map.GridTileToLocal(layer.Ground, grid, exit));
        _zLevels.SetZPosition(user, 0f);
        _zLevels.SetZVelocity(user, 0f);

        if (pulled != null)
            BringPulledSoon(user, pulled.Value);

        return WFCavernClimbResult.Climbed;
    }

    /// <summary>Moves a climber at a shade down to the cavern, onto the pad at its mouth's climb point, or the nearest free tile to it.</summary>
    // A hole outside any mouth has no climb tile, so the climber goes down the hole itself.
    public bool ClimbDown(EntityUid user, EntityUid shade)
    {
        var xform = Transform(shade);

        if (xform.MapUid is not { } groundUid
            || !TryComp<WFCavernGroundComponent>(groundUid, out var ground)
            || !TryComp<MapGridComponent>(groundUid, out var grid)
            || Transform(user).MapUid != groundUid
            || !_zLevels.TryMapDown(groundUid, out var below)
            || below.Owner != ground.Cavern
            || !TryComp<MapGridComponent>(ground.Cavern, out var cavernGrid))
            return false;

        var hole = _map.TileIndicesFor(groundUid, grid, xform.Coordinates);
        var start = hole;

        foreach (var mouth in ground.Mouths)
        {
            if (!mouth.Contains(hole))
                continue;

            start = mouth.ClimbTile;
            break;
        }

        var pulled = CompOrNull<PullerComponent>(user)?.Pulling;

        if (!TryFindLanding((ground.Cavern, cavernGrid), start, out var landing)
            || !_zLevels.TryMoveDown(user)
            || Transform(user).MapUid != ground.Cavern)
            return false;

        _transform.SetCoordinates(user, _map.GridTileToLocal(ground.Cavern, cavernGrid, landing));
        _zLevels.SetZPosition(user, 0f);
        _zLevels.SetZVelocity(user, 0f);

        if (pulled != null)
            BringPulledSoon(user, pulled.Value);

        return true;
    }

    /// <summary>
    /// Has what a puller was pulling follow it to the level it changed to, a tick from now. A change of level ends
    /// every pull: it clears the joints of whoever changes map.
    /// </summary>
    public void BringPulledSoon(EntityUid puller, EntityUid pulled)
    {
        foreach (var ended in _ended)
        {
            if (ended.Puller == puller && ended.Pulled == pulled)
                return;
        }

        _ended.Add((puller, pulled, _timing.CurTick));
    }

    /// <summary>Puts what was pulled on its puller's spot, a level up or down if need be, and takes hold of it again.</summary>
    public bool BringPulled(EntityUid puller, EntityUid pulled)
    {
        if (TerminatingOrDeleted(puller)
            || TerminatingOrDeleted(pulled)
            || !TryComp<CEZPhysicsComponent>(puller, out var level)
            || !HasComp<CEZPhysicsComponent>(pulled)
            || _container.IsEntityInContainer(pulled))
            return false;

        var pullerXform = Transform(puller);

        if (pullerXform.MapUid is not { } to || Transform(pulled).MapUid is not { } from)
            return false;

        if (to != from)
        {
            int offset;
            if (TryComp<WFCavernLayerComponent>(from, out var below) && below.Ground == to)
                offset = 1;
            else if (TryComp<WFCavernLayerComponent>(to, out var layer) && layer.Ground == from)
                offset = -1;
            else
                return false;

            if (!_zLevels.TryMove(pulled, offset) || Transform(pulled).MapUid != to)
                return false;
        }

        // Onto the puller's own spot, which is known to hold it up: on stairs the tiles behind it don't.
        _transform.SetWorldPosition(pulled, _transform.GetWorldPosition(pullerXform));
        _zLevels.SetZPosition(pulled, level.LocalPosition);
        _zLevels.SetZVelocity(pulled, 0f);

        if (!_pulling.TryStartPull(puller, pulled)
            || !TryComp<PullableComponent>(pulled, out var pullable)
            || !TryComp<JointComponent>(pulled, out var joints))
            return false;

        // A pull's rope is as long as its two ends were apart, which here is nothing.
        var id = pullable.PullJointId;
        if (id != null && joints.GetJoints.TryGetValue(id, out var joint) && joint is DistanceJoint rope)
            rope.MaxLength = CarryRope;

        return true;
    }

    /// <summary>The nearest cavern tile to a climb point that is solid and free of hard anchored entities, such as something built on the pad.</summary>
    public bool TryFindLanding(Entity<MapGridComponent> cavern, Vector2i from, out Vector2i landing)
    {
        foreach (var offset in ExitOffsets)
        {
            var index = from + offset;

            if (!_map.TryGetTileRef(cavern, cavern.Comp, index, out var tile)
                || tile.Tile.IsEmpty
                || HasHardAnchored(cavern, index))
                continue;

            landing = index;
            return true;
        }

        landing = from;
        return false;
    }

    /// <summary>The nearest ground tile to a climb point that is solid, not a hole, under no hull and free of hard anchored entities.</summary>
    /// <returns>Climbed with the exit; else Hull if a hull covered any solid tile that is not a hole, else Blocked.</returns>
    public WFCavernClimbResult FindExit(Entity<WFCavernGroundComponent, MapGridComponent> ground, Vector2i from, out Vector2i exit)
    {
        var mapId = Transform(ground).MapID;
        var underHull = false;

        foreach (var offset in ExitOffsets)
        {
            var index = from + offset;

            if (!_map.TryGetTileRef(ground, ground.Comp2, index, out var tile)
                || tile.Tile.IsEmpty
                || ground.Comp1.Shades.ContainsKey(index))
                continue;

            if (UnderHull(mapId, ground, index))
            {
                underHull = true;
                continue;
            }

            if (HasHardAnchored((ground.Owner, ground.Comp2), index))
                continue;

            exit = index;
            return WFCavernClimbResult.Climbed;
        }

        exit = from;
        return underHull ? WFCavernClimbResult.Hull : WFCavernClimbResult.Blocked;
    }

    /// <summary>Whether a grid other than the map covers a ground tile.</summary>
    private bool UnderHull(MapId mapId, Entity<WFCavernGroundComponent, MapGridComponent> ground, Vector2i index)
    {
        var centre = _map.GridTileToWorldPos(ground, ground.Comp2, index);
        var box = Box2.CenteredAround(centre, new Vector2(0.9f, 0.9f));

        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(mapId, box, ref grids, approx: false, includeMap: false);
        return grids.Count > 0;
    }

    /// <summary>Whether anything anchored on a tile would stop a mob standing there.</summary>
    private bool HasHardAnchored(Entity<MapGridComponent> grid, Vector2i index)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);

        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard)
                return true;
        }

        return false;
    }

    /// <summary>Every offset within <see cref="ExitReach"/> in either axis, nearest first.</summary>
    private static Vector2i[] BuildExitOffsets()
    {
        var offsets = new List<Vector2i>();
        for (var x = -ExitReach; x <= ExitReach; x++)
        for (var y = -ExitReach; y <= ExitReach; y++)
        {
            offsets.Add(new Vector2i(x, y));
        }

        return offsets.OrderBy(offset => offset.LengthSquared).ToArray();
    }
}

/// <summary>How a climb up ended.</summary>
public enum WFCavernClimbResult : byte
{
    /// <summary>The climber is on the ground.</summary>
    Climbed,

    /// <summary>No exit tile: every one is empty, a hole or taken by something anchored.</summary>
    Blocked,

    /// <summary>A hull covers the exit tiles.</summary>
    Hull,
}
