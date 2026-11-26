using Content.Shared._WF.Xenonids.Construction.Nest;
using Content.Server.Ghost;

namespace Content.Server._WF.Xenonids.Construction.Nest;

/// <summary>Remembers who ghosted out of a nested body; RMC raises the ghost attempt in shared code, Wolfgate on the server.</summary>
public sealed class XenoNestGhostSystem : EntitySystem
{
    [Dependency] private XenoNestSystem _xenoNest = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GhostAttemptHandleEvent>(OnNestedGhostAttemptHandle);
    }

    private void OnNestedGhostAttemptHandle(GhostAttemptHandleEvent args)
    {
        if (args.Mind.CurrentEntity is not { } ent ||
            !TryComp(ent, out XenoNestedComponent? nested))
        {
            return;
        }

        if (args.Mind.UserId is not { } userId)
            return;

        _xenoNest.SetGhostedId((ent, nested), userId);
    }
}
