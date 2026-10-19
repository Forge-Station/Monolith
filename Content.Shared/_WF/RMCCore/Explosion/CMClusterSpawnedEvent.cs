namespace Content.Shared._WF.RMCCore.Explosion;

[ByRefEvent]
public readonly record struct CMClusterSpawnedEvent(List<EntityUid> Spawned, List<EntityUid> HitEntities, EntityUid OriginEntity);
