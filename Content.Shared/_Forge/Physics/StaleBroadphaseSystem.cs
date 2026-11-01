// Forge-Change-full: drop stale broadphase ids before physics so a missing transform cannot fill the client log.
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Forge.Physics;

/// <summary>
/// A client that loses an entity from PVS can leave its id in a map broadphase tree after the
/// transform is gone. The engine's contact pass then throws on that id every physics tick and
/// never clears the moved-grid set, so the same exception is written until the log fills the disk.
/// Drop those ids before physics runs.
/// </summary>
public sealed class StaleBroadphaseSystem : EntitySystem
{
    private EntityQuery<TransformComponent> _xformQuery;
    private readonly List<EntityUid> _stale = new();

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
        // SimulateWorld raises this immediately before FindNewContacts.
        SubscribeLocalEvent<PhysicsUpdateBeforeSolveEvent>(OnBeforeSolve);
    }

    public override void Update(float frameTime)
    {
        PurgeAll();
    }

    private void OnBeforeSolve(ref PhysicsUpdateBeforeSolveEvent ev)
    {
        PurgeAll();
    }

    /// <summary>
    /// Prediction reset calls FindNewContacts outside the entity-system update, before this
    /// system's Update. The client entry point calls this in PreEngine so that pass is clean too.
    /// </summary>
    public void PurgeAll()
    {
        var query = EntityQueryEnumerator<BroadphaseComponent>();
        while (query.MoveNext(out _, out var broadphase))
        {
            Purge(broadphase.SundriesTree);
            Purge(broadphase.StaticSundriesTree);
        }
    }

    private void Purge(DynamicTree<EntityUid>? tree)
    {
        if (tree == null || tree.Count == 0)
            return;

        foreach (var uid in tree)
        {
            if (!_xformQuery.HasComponent(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            tree.Remove(uid);
        }

        _stale.Clear();
    }
}
