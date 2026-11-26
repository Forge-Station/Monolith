namespace Content.Shared.NPC.Systems;

public sealed partial class NpcFactionSystem
{
    /// <summary>Takes a target back off an NPC's ignore list, so it may be attacked again.</summary>
    public void UnignoreEntity(Entity<Components.FactionExceptionComponent?> ent, EntityUid target)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.Ignored.Remove(target))
            return;

        if (!ent.Comp.Hostiles.Contains(target) && _trackerQuery.TryGetComponent(target, out var tracker))
            tracker.Entities.Remove(ent);
    }
}
