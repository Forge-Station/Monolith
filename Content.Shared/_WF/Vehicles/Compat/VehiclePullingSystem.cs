using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._WF.Vehicles.Compat;

/// <summary>Teleports an entity and whatever it pulls, restarting the pull so the joint is not carried across maps.</summary>
public sealed partial class VehiclePullingSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The tiles round the puller's landing spot, sides before corners.</summary>
    private static readonly Vector2i[] Around =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(-1, 1), new(1, -1), new(-1, -1),
    };

    /// <summary>Teleports <paramref name="user"/> and its pulled entity to <paramref name="teleport"/> and resumes the pull.</summary>
    public void HandlePulling(EntityUid user, MapCoordinates teleport)
    {
        if (TryComp(user, out PullableComponent? otherPullable) &&
            otherPullable.Puller != null)
        {
            _pulling.TryStopPull(user, otherPullable, otherPullable.Puller.Value);
        }

        if (TryComp(user, out PullerComponent? puller) &&
            TryComp(puller.Pulling, out PullableComponent? pullable))
        {
            if (TryComp(puller.Pulling, out PullerComponent? otherPullingPuller) &&
                TryComp(otherPullingPuller.Pulling, out PullableComponent? otherPullingPullable))
            {
                _pulling.TryStopPull(otherPullingPuller.Pulling.Value, otherPullingPullable, puller.Pulling);
            }

            var pulling = puller.Pulling.Value;
            _pulling.TryStopPull(pulling, pullable, user);
            _transform.SetMapCoordinates(user, teleport);
            _transform.SetMapCoordinates(pulling, SpotBeside(user, pulling, teleport));
            _pulling.TryStartPull(user, pulling);
        }
        else
        {
            _transform.SetMapCoordinates(user, teleport);
        }
    }

    /// <summary>
    /// A free tile beside where the puller landed, for what they drag: landed on top of them, the two wedge against
    /// whatever is next to the doorway and neither can move. The puller's own spot when nothing round it is free.
    /// </summary>
    private MapCoordinates SpotBeside(EntityUid user, EntityUid pulled, MapCoordinates fallback)
    {
        var xform = Transform(user);
        if (xform.GridUid is not { } gridUid
            || !TryComp<MapGridComponent>(gridUid, out var grid)
            || !TryComp<PhysicsComponent>(pulled, out var body))
            return fallback;

        var centre = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        foreach (var step in Around)
        {
            var tile = centre + step;
            if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty || Blocked(gridUid, grid, tile, pulled, body))
                continue;

            return _transform.ToMapCoordinates(_map.GridTileToLocal(gridUid, grid, tile));
        }

        return fallback;
    }

    /// <summary>Whether anything on the tile would collide with the dragged body, either way round.</summary>
    private bool Blocked(EntityUid gridUid, MapGridComponent grid, Vector2i tile, EntityUid pulled, PhysicsComponent body)
    {
        foreach (var other in _lookup.GetLocalEntitiesIntersecting(gridUid, tile, flags: LookupFlags.Dynamic | LookupFlags.Static, gridComp: grid))
        {
            if (other == pulled || !TryComp<PhysicsComponent>(other, out var otherBody) || !otherBody.CanCollide || !otherBody.Hard)
                continue;

            if ((otherBody.CollisionLayer & body.CollisionMask) != 0 || (otherBody.CollisionMask & body.CollisionLayer) != 0)
                return true;
        }

        return false;
    }
}
