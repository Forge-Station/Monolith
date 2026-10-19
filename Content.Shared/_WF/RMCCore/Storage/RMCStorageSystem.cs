using Content.Shared.Whitelist;

namespace Content.Shared._WF.RMCCore.Storage;

/// <summary>The part of RMC-14's storage system the xeno port needs: entities that refuse to go into some entity storages.</summary>
public sealed partial class RMCStorageSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _entityWhitelist = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BlockEntityStorageComponent, RMCInsertIntoEntityStorageAttemptEvent>(OnBlockInsertIntoEntityStorageAttempt);
    }

    private void OnBlockInsertIntoEntityStorageAttempt(Entity<BlockEntityStorageComponent> ent, ref RMCInsertIntoEntityStorageAttemptEvent args)
    {
        if (_entityWhitelist.IsWhitelistPassOrNull(ent.Comp.Whitelist, args.Container))
            args.Cancelled = true;
    }
}

/// <summary>Raised on an entity about to go into an entity storage, with the storage it goes into.</summary>
[ByRefEvent]
public record struct RMCInsertIntoEntityStorageAttemptEvent(EntityUid Container, bool Cancelled = false);
