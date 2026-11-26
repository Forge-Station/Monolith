using System.Numerics;
using Content.Server._WF.BoardingAction.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Tiles;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.BoardingAction;

/// <summary>
/// Holds ships in place: while anything disrupts a ship it can't start a jump, a docked group it is in can't either,
/// and a spool already running is stopped.
/// </summary>
public sealed partial class WFFtlDisruptionSystem : EntitySystem
{
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    private readonly HashSet<EntityUid> _group = new();
    private readonly List<EntityUid> _spooling = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFFtlDisruptedComponent, ConsoleFTLAttemptEvent>(OnFtlAttempt);
    }

    private void OnFtlAttempt(Entity<WFFtlDisruptedComponent> ent, ref ConsoleFTLAttemptEvent args)
    {
        args.Cancelled = true;
        args.Reason = Loc.GetString("wf-boarding-ftl-disrupted");
    }

    /// <summary>Starts holding a ship. <paramref name="source"/> is what holds it, and must be the one to let go.</summary>
    public void Disrupt(EntityUid grid, EntityUid source)
    {
        EnsureComp<WFFtlDisruptedComponent>(grid).Sources.Add(source);
    }

    /// <summary>Lets go of a ship. It can jump again once nothing else holds it.</summary>
    public void Release(EntityUid grid, EntityUid source)
    {
        if (!TryComp<WFFtlDisruptedComponent>(grid, out var disrupted))
            return;

        disrupted.Sources.Remove(source);
        if (disrupted.Sources.Count == 0)
            RemComp<WFFtlDisruptedComponent>(grid);
    }

    /// <summary>Whether a ship or anything docked to it is held, so the group can't jump.</summary>
    public bool IsGroupDisrupted(EntityUid grid)
    {
        if (HasComp<WFFtlDisruptedComponent>(grid))
            return true;

        _group.Clear();
        _shuttle.GetAllDockedShuttles(grid, _group);
        foreach (var docked in _group)
        {
            if (HasComp<WFFtlDisruptedComponent>(docked))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a grid is a ship that can be tackled: free to fly, and neither protected nor docked to something that
    /// is fixed or protected.
    /// </summary>
    public bool IsTackleable(EntityUid grid)
    {
        if (!IsFreeShip(grid))
            return false;

        _group.Clear();
        _shuttle.GetAllDockedShuttlesIgnoringFTLLock(grid, _group);
        foreach (var docked in _group)
        {
            if (!IsFreeShip(docked))
                return false;
        }

        return true;
    }

    /// <summary>Collects a ship and the ships docked to it, without passing through a station to the ships beside it.</summary>
    public void GetDockedShips(EntityUid ship, HashSet<EntityUid> ships)
    {
        if (!ships.Add(ship))
            return;

        foreach (var dock in _docking.GetDocks(ship))
        {
            if (dock.Comp.DockedWith is not { } other)
                continue;

            var grid = _transform.GetParentUid(other);
            if (IsFreeShip(grid))
                GetDockedShips(grid, ships);
        }
    }

    private bool IsFreeShip(EntityUid grid)
    {
        return !TerminatingOrDeleted(grid) && HasComp<ShuttleComponent>(grid) && !HasComp<ProtectedGridComponent>(grid) &&
               TryComp<PhysicsComponent>(grid, out var body) && body.BodyType == BodyType.Dynamic;
    }

    /// <summary>How far a world point is from a ship's bounds, in metres. Zero inside them.</summary>
    public float DistanceToHull(Vector2 point, EntityUid grid)
    {
        if (!TryComp<MapGridComponent>(grid, out var map))
            return Vector2.Distance(point, _transform.GetWorldPosition(grid));

        var local = Vector2.Transform(point, _transform.GetInvWorldMatrix(grid));
        var closest = Vector2.Clamp(local, map.LocalAABB.BottomLeft, map.LocalAABB.TopRight);
        return Vector2.Distance(local, closest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var disrupted = EntityQueryEnumerator<WFFtlDisruptedComponent>();
        if (!disrupted.MoveNext(out _, out _))
            return;

        _spooling.Clear();
        var query = EntityQueryEnumerator<FTLComponent>();
        while (query.MoveNext(out var uid, out var ftl))
        {
            if (ftl.State == FTLState.Starting && ftl.LinkedShuttle == null && IsGroupDisrupted(uid))
                _spooling.Add(uid);
        }

        foreach (var uid in _spooling)
        {
            _shuttle.WfAbortFtlSpool(uid);
        }
    }
}
