using System.Diagnostics.CodeAnalysis;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;

namespace Content.Client._WF.StateRecovery;

/// <summary>
/// Removes a PVS-detached entity from its old lookup tree when the engine skips it.
/// </summary>
/// <remarks>
/// When a child leaves view in the same batch as its parent and is handled first, the engine leaves its removal to the
/// parent's recursive removal, but the child is already in null-space by then. Its entry stays in the grid's sundries
/// tree after it is deleted, and every lookup there logs an unresolvable transform.
/// </remarks>
public sealed partial class WFDetachLookupSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;

    // Where each entity detached this tick came from, for its children detached after it.
    private readonly Dictionary<EntityUid, BroadphaseComponent> _detachedFrom = new();
    private readonly List<EntityUid> _deleted = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntParentChangedMessage>(OnParentChanged);
    }

    public override void Update(float frameTime)
    {
        _detachedFrom.Clear();
    }

    /// <summary>
    /// Removes deleted entities from every sundries tree and returns how many it removed.
    /// </summary>
    public int PurgeDeleted()
    {
        var removed = 0;
        var query = AllEntityQuery<BroadphaseComponent>();
        while (query.MoveNext(out var broadphase))
        {
            removed += PurgeDeleted(broadphase.SundriesTree);
            removed += PurgeDeleted(broadphase.StaticSundriesTree);
        }

        return removed;
    }

    private int PurgeDeleted(DynamicTree<EntityUid> tree)
    {
        _deleted.Clear();
        foreach (var uid in tree)
        {
            if (!Exists(uid))
                _deleted.Add(uid);
        }

        foreach (var uid in _deleted)
        {
            tree.Remove(uid);
        }

        return _deleted.Count;
    }

    private void OnParentChanged(ref EntParentChangedMessage args)
    {
        if (args.Transform.ParentUid.IsValid() ||
            args.OldParent is not { } oldParent ||
            !oldParent.IsValid() ||
            (MetaData(args.Entity).Flags & MetaDataFlags.Detached) == 0 ||
            !TryFindOldBroadphase(oldParent, out var broadphase))
        {
            return;
        }

        _detachedFrom[args.Entity] = broadphase;
        broadphase.SundriesTree.Remove(args.Entity);
        broadphase.StaticSundriesTree.Remove(args.Entity);
    }

    // A parent detached earlier in the batch is already in null-space, so use the broadphase it left.
    private bool TryFindOldBroadphase(EntityUid parent, [NotNullWhen(true)] out BroadphaseComponent? broadphase)
    {
        // A grid's children sit in the grid's own tree, not the one the grid itself is on.
        if (TryComp(parent, out broadphase) || _detachedFrom.TryGetValue(parent, out broadphase))
            return true;

        return TryComp(parent, out TransformComponent? parentXform) &&
               _lookup.TryFindBroadphase(parentXform, out broadphase);
    }
}
