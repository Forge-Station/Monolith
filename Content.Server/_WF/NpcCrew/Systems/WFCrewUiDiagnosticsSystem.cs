using Content.Server.Administration.Managers;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Ghost;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ghost;
using Content.Shared.Verbs;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>Opt-in authoritative traces for menu requests and player attachment changes.</summary>
public sealed partial class WFCrewUiDiagnosticsSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminManager _admins = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RequestServerVerbsEvent>((ev, args) => Request("verbs", args.SenderSession, ev.EntityUid));
        SubscribeNetworkEvent<GhostOrbitRequestEvent>((_, args) => Request("ghost", args.SenderSession));
        SubscribeNetworkEvent<WFCrewSetupRequest>((_, args) => Request("crew", args.SenderSession));
        SubscribeLocalEvent<PlayerAttachedEvent>(ev => Attachment("attach", ev.Player));
        SubscribeLocalEvent<PlayerDetachedEvent>(ev => Attachment("detach", ev.Player));
    }

    /// <summary>Records a reply immediately before it enters the normal network path.</summary>
    public void Reply(string kind, ICommonSession session, int count, NetEntity? target = null)
    {
        if (_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
            Log.Info($"UI send-reply kind={kind} target={target} count={count} {State(session)}");
    }

    private void Request(string kind, ICommonSession session, NetEntity? target = null)
    {
        if (_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
            Log.Info($"UI receive kind={kind} target={target} {State(session)}");
    }

    private void Attachment(string kind, ICommonSession session)
    {
        if (_cfg.GetCVar(NpcCrewCVars.UiDiagnostics))
            Log.Info($"UI {kind} {State(session)}");
    }

    private string State(ICommonSession session)
    {
        var attached = session.AttachedEntity;
        var hasActor = TryComp<ActorComponent>(attached, out var actor);
        var netId = TryComp<MetaDataComponent>(attached, out var meta) ? meta.NetEntity.ToString() : $"missing:{attached}";
        return $"tick={_timing.CurTick} player={session.UserId} attached={netId} " +
               $"actor={hasActor} actorMatches={hasActor && actor!.PlayerSession == session} " +
               $"ghost={HasComp<GhostComponent>(attached)} admin={_admins.IsAdmin(session)}";
    }
}
