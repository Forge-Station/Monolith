using Content.Server.Station.Components;
using Content.Shared._Forge.Bss;
using Content.Shared._Forge.Persistence;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;

namespace Content.Server._Forge.Persistence;

/// <summary>
/// Decides which grids belong in a weekly world snapshot.
/// Only <see cref="WorldPersistableComponent"/> grids (and their contents) are serialized.
/// Abandoned shuttles, debris, and space junk are omitted.
/// </summary>
public sealed class WorldPersistFilterSystem : EntitySystem
{
    [Dependency] private MapLoaderSystem _loader = default!;

    private EntityUid? _activeMap;

    public override void Initialize()
    {
        base.Initialize();
        _loader.OnIsSerializable += OnIsSerializable;
        SubscribeLocalEvent<PersistentGridComponent, ComponentStartup>(OnPersistentGridStartup);
    }

    public override void Shutdown()
    {
        _loader.OnIsSerializable -= OnIsSerializable;
        base.Shutdown();
    }

    /// <summary>
    /// Tags automatically persistable grids, then filters map serialization to those grids.
    /// </summary>
    public void BeginMapSave(EntityUid map)
    {
        StampMap(map);
        _activeMap = map;
    }

    public void EndMapSave()
    {
        _activeMap = null;
    }

    private void OnPersistentGridStartup(Entity<PersistentGridComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<WorldPersistableComponent>(ent);
    }

    private void StampMap(EntityUid map)
    {
        var query = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid != map)
                continue;

            if (IsAutomaticallyPersistable(uid))
                EnsureComp<WorldPersistableComponent>(uid);
        }
    }

    private bool IsAutomaticallyPersistable(EntityUid grid)
    {
        if (TryComp<BssGateComponent>(grid, out var gate))
            return gate.Persistent;

        return HasComp<WorldPersistableComponent>(grid)
               || HasComp<PersistentGridComponent>(grid)
               || HasComp<BecomesStationComponent>(grid);
    }

    private void OnIsSerializable(Entity<MetaDataComponent> ent, ref bool serializable)
    {
        if (!serializable || _activeMap == null)
            return;

        if (ent.Owner == _activeMap.Value)
            return;

        if (HasComp<MapGridComponent>(ent))
        {
            serializable = HasComp<WorldPersistableComponent>(ent);
            return;
        }

        var xform = Transform(ent);
        if (xform.GridUid is not { Valid: true } grid || grid == _activeMap.Value)
            serializable = false;
    }
}
