using Content.Server._Forge.Drones.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.Drones;

/// <summary>
/// Combat drones dump enormous board/gun piles when shredded. Cull everything that leaves
/// a <see cref="DroneSalvageGridComponent"/> hull except whitelisted salvage (core + blackbox),
/// and pull that salvage off the grid before the hull itself is deleted.
/// </summary>
public sealed class DroneSalvageSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<DroneSalvageGridComponent> _salvageGridQuery;
    private EntityQuery<DroneSalvageKeepComponent> _keepQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private readonly List<EntityUid> _extractScratch = new();

    public override void Initialize()
    {
        base.Initialize();

        _salvageGridQuery = GetEntityQuery<DroneSalvageGridComponent>();
        _keepQuery = GetEntityQuery<DroneSalvageKeepComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<DroneSalvageGridComponent, EntityTerminatingEvent>(OnGridTerminating);
        SubscribeLocalEvent<TransformComponent, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnGridTerminating(Entity<DroneSalvageGridComponent> ent, ref EntityTerminatingEvent args)
    {
        ExtractKeepItems(ent.Owner);
    }

    private void OnParentChanged(EntityUid uid, TransformComponent xform, ref EntParentChangedMessage args)
    {
        if (args.OldParent is not { } oldParent || !_salvageGridQuery.HasComp(oldParent))
            return;

        // Grid deletion extracts keep items itself; skip per-child churn while the hull dies.
        if (TerminatingOrDeleted(oldParent) || TerminatingOrDeleted(uid))
            return;

        // Still nested on the same drone hull (crate, rack, etc.).
        if (xform.GridUid == oldParent)
            return;

        if (_keepQuery.HasComp(uid))
        {
            PreserveSalvage(uid, xform);
            return;
        }

        // Picked up into hands / storage / another mob — real loot, leave it.
        if (xform.ParentUid != xform.MapUid && !_gridQuery.HasComp(xform.ParentUid))
            return;

        QueueDel(uid);
    }

    /// <summary>
    /// Reparent whitelist salvage to the map so deletion of the hull does not eat it.
    /// </summary>
    private void ExtractKeepItems(EntityUid grid)
    {
        if (!_xformQuery.TryGetComponent(grid, out var gridXform) || gridXform.MapUid is not { } mapUid)
            return;

        _extractScratch.Clear();
        CollectKeepOnGrid(grid, grid, _extractScratch);

        foreach (var uid in _extractScratch)
        {
            if (TerminatingOrDeleted(uid) || !_xformQuery.TryGetComponent(uid, out var xform))
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            _transform.SetParent(uid, xform, mapUid);
            _transform.SetWorldPosition(uid, worldPos);
            PreserveSalvage(uid, xform);
        }
    }

    private void CollectKeepOnGrid(EntityUid grid, EntityUid parent, List<EntityUid> output)
    {
        if (!_xformQuery.TryGetComponent(parent, out var xform))
            return;

        var child = xform.ChildEnumerator;
        while (child.MoveNext(out var childUid))
        {
            if (_keepQuery.HasComp(childUid))
                output.Add(childUid);

            // Descend into containers / nested parents that are still on this hull.
            if (_xformQuery.TryGetComponent(childUid, out var childXform) && childXform.GridUid == grid)
                CollectKeepOnGrid(grid, childUid, output);
        }
    }

    private void PreserveSalvage(EntityUid uid, TransformComponent xform)
    {
        xform.GridTraversal = false;

        // If grid-traversal already parented the core/blackbox onto a foreign hull, peel it back to the map.
        if (xform.MapUid is { } mapUid
            && xform.ParentUid != mapUid
            && _gridQuery.HasComp(xform.ParentUid))
        {
            var worldPos = _transform.GetWorldPosition(xform);
            _transform.SetParent(uid, xform, mapUid);
            _transform.SetWorldPosition(uid, worldPos);
        }

        Dirty(uid, xform);
    }
}
