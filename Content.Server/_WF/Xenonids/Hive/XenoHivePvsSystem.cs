using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Hive;
using Robust.Server.GameStates;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenonids.Hive;

/// <summary>
/// Sends the hive entity, which lives in nullspace, to the clients of its xenos so client-side hive checks work.
/// </summary>
public sealed class XenoHivePvsSystem : EntitySystem
{
    [Dependency] private SharedXenoHiveSystem _hive = default!;
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<ActorComponent, HiveChangedEvent>(OnHiveChanged);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (!HasComp<XenoComponent>(args.Entity) || _hive.GetHive(args.Entity) is not { } hive)
            return;

        _pvsOverride.AddForceSend(hive, args.Player);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        if (TerminatingOrDeleted(args.Entity) || !HasComp<XenoComponent>(args.Entity))
            return;

        if (_hive.GetHive(args.Entity) is { } hive)
            _pvsOverride.RemoveForceSend(hive, args.Player);
    }

    private void OnHiveChanged(Entity<ActorComponent> ent, ref HiveChangedEvent args)
    {
        if (!HasComp<XenoComponent>(ent))
            return;

        var session = ent.Comp.PlayerSession;
        if (args.OldHive is { } oldHive)
            _pvsOverride.RemoveForceSend(oldHive, session);

        if (args.Hive is { } newHive)
            _pvsOverride.AddForceSend(newHive, session);
    }
}
