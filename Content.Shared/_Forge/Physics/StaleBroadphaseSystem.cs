// Forge-Change-full: drop a stale broadphase id when its entity is deleted, without scanning the map every tick.
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Physics;

namespace Content.Shared._Forge.Physics;

/// <summary>
/// A client that loses an entity from PVS can leave its id in a map broadphase tree after the
/// transform is gone. The engine's contact pass then throws on that id and keeps the moved-grid
/// set, so the same exception is written until the log fills the disk.
/// The id is removed when that entity is deleted. Scanning every tree every tick costs as much
/// as a slice of physics on a full map, and the server never has this PVS bug.
/// </summary>
public sealed class StaleBroadphaseSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        if (!_net.IsClient)
            return;

        EntityManager.EntityDeleted += OnEntityDeleted;
    }

    public override void Shutdown()
    {
        if (_net.IsClient)
            EntityManager.EntityDeleted -= OnEntityDeleted;

        base.Shutdown();
    }

    private void OnEntityDeleted(Entity<MetaDataComponent> entity)
    {
        var uid = entity.Owner;
        var query = EntityQueryEnumerator<BroadphaseComponent>();
        while (query.MoveNext(out var broadphaseUid, out var broadphase))
        {
            if (broadphaseUid == uid)
                continue;

            // An entity lives in at most one sundries tree. A miss is one dictionary lookup.
            if (broadphase.SundriesTree is { Count: > 0 } sundries && sundries.Remove(uid))
                return;

            if (broadphase.StaticSundriesTree is { Count: > 0 } staticTree && staticTree.Remove(uid))
                return;
        }
    }
}
