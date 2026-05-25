using Content.Server.Cargo.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared.Mind.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Mono.Cleanup;

/// <summary>
///     Deletes entities eligible for deletion.
/// </summary>
public sealed partial class SpaceCleanupSystem : BaseCleanupSystem<PhysicsComponent>
{
    [Dependency] private CleanupHelperSystem _cleanup = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private float _maxDistance;
    private float _maxGridDistance;
    private float _maxPrice;

    private EntityQuery<CleanupImmuneComponent> _immuneQuery;
    private EntityQuery<HTNComponent> _htnQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MindContainerComponent> _mindQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private List<(EntityCoordinates Coord, TimeSpan Time, float Radius, float Aggression)> _sweepQueue = new();
    private HashSet<Entity<PhysicsComponent>> _sweepEnts = new();

    public override void Initialize()
    {
        base.Initialize();

        // this queries over literally everything with PhysicsComponent so has to have big interval
        _cleanupInterval = TimeSpan.FromSeconds(600);

        _immuneQuery = GetEntityQuery<CleanupImmuneComponent>();
        _htnQuery = GetEntityQuery<HTNComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mindQuery = GetEntityQuery<MindContainerComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        Subs.CVar(_cfg, MonoCVars.CleanupMaxGridDistance, val => _maxGridDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.SpaceCleanupDistance, val => _maxDistance = val, true);
        Subs.CVar(_cfg, MonoCVars.SpaceCleanupMaxValue, val => _maxPrice = val, true);
    }

    /// <summary>
    ///     Pre-filter sanity check. The marker is supposed to guarantee these properties,
    ///     but parent-change races or hot reloads can leave the marker stale for a tick;
    ///     verify cheaply before paying the proximity/pricing cost.
    /// </summary>
    protected override bool ShouldEnqueue(EntityUid uid)
    {
        if (_gridQuery.HasComp(uid) || _htnQuery.HasComp(uid) || _immuneQuery.HasComp(uid) || _mindQuery.HasComp(uid))
            return false;

        if (!_xformQuery.TryGetComponent(uid, out var xform) || xform.MapUid == null)
            return false;

        return xform.ParentUid == xform.MapUid;
    }

    protected override bool ShouldEntityCleanup(EntityUid uid)
    {
        return ShouldEntityCleanup(uid, 1f);
    }

    private bool ShouldEntityCleanup(EntityUid uid, float aggression)
    {
        if (!_xformQuery.TryGetComponent(uid, out var xform))
            return false;

        var inSpace = xform.ParentUid == xform.MapUid;
        if (!inSpace && !GetWallStuck((uid, xform)))
            return false;

        // Late, expensive check: price first, then proximity scaled by sqrt(price/max).
        var price = (float)_pricing.GetPrice(uid);
        if (price > _maxPrice)
            return false;

        // Wall-stuck entities skip the proximity scaling - they're already known to be unreachable.
        if (!inSpace)
            return true;

        var scale = aggression * MathF.Sqrt(price / MathF.Max(_maxPrice, 1f));
        return !CleanupHelper.HasNearbyGrids(xform.Coordinates, _maxGridDistance * scale)
            && !CleanupHelper.HasNearbyPlayers(xform.Coordinates, _maxDistance * scale);
    }

    /// <summary>
    ///     Returns true when the entity is wedged inside a wall - has a touching hard contact
    ///     with a static body on its own grid, and the entity's world position is contained
    ///     within that fixture's world AABB.
    /// </summary>
    /// <remarks>
    ///     Forge-Change: replaces the previous reflection-based <c>IManifoldManager.TestOverlap</c>
    ///     call with a public AABB-containment check. Slightly more permissive (AABB vs. exact shape)
    ///     but dramatically cheaper and removes a hidden coupling to engine internals.
    /// </remarks>
    private bool GetWallStuck(Entity<TransformComponent> ent)
    {
        if (ent.Comp.GridUid is not { } gridUid
            || ent.Comp.Anchored
            || ent.Comp.ParentUid != gridUid)
            return false;

        var contacts = _physics.GetContacts(ent.Owner);
        if (contacts == ContactEnumerator.Empty)
            return false;

        // Entity world position - used to test "is the entity center inside a wall fixture".
        var entWorldPos = _transform.GetWorldPosition(ent.Comp);

        while (contacts.MoveNext(out var contact))
        {
            if (!contact.IsTouching || !contact.Hard
                || contact.FixtureA == null || contact.FixtureB == null
                || contact.BodyA == null || contact.BodyB == null)
                continue;

            var weAreA = contact.EntityA == ent.Owner;
            var otherBody = weAreA ? contact.BodyB : contact.BodyA;
            if ((otherBody.BodyType & BodyType.Static) == 0)
                continue;

            var otherFix = weAreA ? contact.FixtureB : contact.FixtureA;
            var otherXform = weAreA ? contact.XformB : contact.XformA;
            var otherEnt = weAreA ? contact.EntityB : contact.EntityA;
            if (otherFix == null || otherXform == null)
                continue;

            var xf = _physics.GetLocalPhysicsTransform(otherEnt, otherXform);
            var childIdx = weAreA ? contact.ChildIndexB : contact.ChildIndexA;
            var aabb = otherFix.Shape.ComputeAABB(xf, childIdx);

            // ComputeAABB returns the AABB in the static body's grid-local frame; convert
            // entity world position into that same frame for containment.
            var localPos = System.Numerics.Vector2.Transform(entWorldPos, otherXform.InvWorldMatrix);
            if (aabb.Contains(localPos))
                return true;
        }
        return false;
    }

    public void QueueSweep(EntityCoordinates coordinates, TimeSpan time, float radius, float aggression)
    {
        _sweepQueue.Add((coordinates, time, radius, aggression));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        for (int i = _sweepQueue.Count - 1; i >= 0; i--)
        {
            var (coord, time, radius, aggression) = _sweepQueue[i];

            if (_timing.CurTime < time)
                continue;

            _sweepQueue.RemoveAt(i);
            if (!coord.IsValid(EntityManager))
                continue;

            _sweepEnts.Clear();
            _lookup.GetEntitiesInRange(_transform.ToMapCoordinates(coord), radius, _sweepEnts, LookupFlags.Dynamic | LookupFlags.Approximate | LookupFlags.Sundries);

            foreach (var (uid, body) in _sweepEnts)
            {
                if (ShouldEntityCleanup(uid, aggression))
                    CleanupEnt(uid);
            }
        }
    }
}
