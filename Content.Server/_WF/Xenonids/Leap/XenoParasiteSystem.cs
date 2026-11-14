using Content.Server.Ghost;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenonids.Leap;

/// <summary>The server's concrete parasite system; keep exactly one subclass of <see cref="SharedXenoParasiteSystem"/>.</summary>
public sealed partial class XenoParasiteSystem : SharedXenoParasiteSystem
{
    [Dependency] private GhostSystem _ghostSystem = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>A player-controlled parasite that hit its host leaves its body as a ghost.</summary>
    protected override void ParasiteLeapHit(Entity<XenoParasiteComponent> parasite)
    {
        if (!TryComp(parasite, out ActorComponent? actor))
            return;

        RemComp<GhostTakeoverAvailableComponent>(parasite);

        var session = actor.PlayerSession;

        Entity<MindComponent> mind;
        if (_mind.TryGetMind(session.UserId, out Entity<MindComponent>? existing))
            mind = existing.Value;
        else
            mind = _mind.CreateMind(session.UserId);

        var ghost = _ghostSystem.SpawnGhost((mind.Owner, mind.Comp), parasite);
        if (ghost == null)
            return;

        EnsureComp<InfectionSuccessComponent>(ghost.Value);
        _popup.PopupEntity(Loc.GetString("rmc-xeno-egg-ghost-bypass-time"), ghost.Value, ghost.Value, PopupType.Medium);
    }
}
