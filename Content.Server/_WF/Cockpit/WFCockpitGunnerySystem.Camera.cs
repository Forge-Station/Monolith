using Content.Server._WF.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Shuttles;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.Cockpit;

public sealed partial class WFCockpitGunnerySystem
{
    private readonly Dictionary<EntityUid, CameraSnapshot> _cameras = new();

    private sealed record CameraSnapshot(EntityUid Helm, ShuttleCameraView View, float Zoom, bool LowLight);

    /// <summary>Cockpit camera changes are local to their pilot and never persist on the shared helm.</summary>
    public bool HasCameraSession(EntityUid actor, EntityUid helm) =>
        _cameras.TryGetValue(actor, out var camera) && camera.Helm == helm;

    /// <summary>Preserves the prior camera before cockpit entry requests the external view.</summary>
    private void CaptureCamera(EntityUid actor, EntityUid helm)
    {
        var camera = CompOrNull<ShuttleCameraComponent>(actor);
        _cameras[actor] = new CameraSnapshot(helm, camera?.View ?? ShuttleCameraView.Helm,
            camera?.Zoom ?? Comp<ShuttleConsoleComponent>(helm).Zoom.X, camera?.LowLight ?? false);
    }

    /// <summary>Restores only the departing pilot; another pilot may have changed the shared console settings.</summary>
    private void RestoreCamera(EntityUid actor)
    {
        if (!_cameras.Remove(actor, out var previous) || TerminatingOrDeleted(previous.Helm))
            return;
        EntityManager.System<ShuttleCameraSystem>().WfRestoreCockpitCamera(actor, previous.Helm,
            previous.View, previous.Zoom, previous.LowLight);
    }
}
