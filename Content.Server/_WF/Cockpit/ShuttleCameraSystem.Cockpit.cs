using Content.Server._WF.Cockpit;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Shuttles.Systems;

public sealed partial class ShuttleCameraSystem
{
    private bool WfCockpitCameraActive(EntityUid actor, EntityUid helm) =>
        EntityManager.System<WFCockpitGunnerySystem>().HasCameraSession(actor, helm);

    /// <summary>Returns a cockpit's camera only to the same connected pilot still operating its helm.</summary>
    public void WfRestoreCockpitCamera(EntityUid actor, EntityUid helm, ShuttleCameraView view, float zoom, bool lowLight)
    {
        if (TerminatingOrDeleted(actor) || !TryComp<PilotComponent>(actor, out var pilot) || pilot.Console != helm ||
            !TryComp<ActorComponent>(actor, out var player) || player.PlayerSession.AttachedEntity != actor)
            return;
        Apply(actor, helm, view, zoom, lowLight);
    }
}
