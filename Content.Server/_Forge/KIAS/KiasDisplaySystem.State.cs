using System.Linq;
using Content.Shared._Forge.KIAS;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasDisplaySystem
{
    private readonly Dictionary<EntityUid, BoundUserInterfaceState> _sentStates = new();
    private readonly HashSet<EntityUid> _coverageTools = new();

    private void SetState(EntityUid uid, KiasUiKey key, BoundUserInterfaceState state)
    {
        if (_sentStates.TryGetValue(uid, out var previous) && Equivalent(previous, state)) return;
        if (_sentStates.Count >= 256) _sentStates.Clear();
        _sentStates[uid] = state;
        _ui.SetUiState(uid, key, state);
    }

    private void OnDisplayClosed(Entity<KiasDisplayComponent> ent, ref BoundUIClosedEvent args)
    {
        _sentStates.Remove(ent);
        _coverageTools.Remove(ent);
    }
    private void OnLocalClosed(Entity<KiasLocalUiComponent> ent, ref BoundUIClosedEvent args) => _sentStates.Remove(ent);

    private void RefreshCoverageTools(EntityUid grid)
    {
        foreach (var uid in _coverageTools.ToArray())
        {
            if (TerminatingOrDeleted(uid) || !_ui.IsUiOpen(uid, KiasUiKey.Service)) { _coverageTools.Remove(uid); continue; }
            if (TryComp<KiasServiceToolComponent>(uid, out var tool) && tool.Target is { } target && !TerminatingOrDeleted(target)
                && Transform(target).GridUid == grid) Refresh(uid);
        }
    }

    private static bool GeometryEquals(KiasCoverageGeometry? a, KiasCoverageGeometry? b) => ReferenceEquals(a, b)
        || a != null && b != null && a.Target == b.Target && a.Grid == b.Grid && a.Shape == b.Shape && a.Radius == b.Radius
        && a.Arc == b.Arc && a.Large == b.Large && a.Revision == b.Revision && a.Cells.SequenceEqual(b.Cells);

    private static bool Equivalent(BoundUserInterfaceState a, BoundUserInterfaceState b)
    {
        if (a.GetType() != b.GetType()) return false;
        if (a is KiasLocalState firstLocal && b is KiasLocalState secondLocal
            && (firstLocal.Name != secondLocal.Name || firstLocal.Status != secondLocal.Status || firstLocal.Room != secondLocal.Room)) return false;
        return (a, b) switch
        {
            (KiasWallState x, KiasWallState y) => x.Online == y.Online && x.Entities == y.Entities && x.Crew == y.Crew && x.Page == y.Page && x.Details == y.Details,
            (KiasRecorderState x, KiasRecorderState y) => x.Online == y.Online && x.Entries.SequenceEqual(y.Entries),
            (KiasServiceState x, KiasServiceState y) => x.Mode == y.Mode && x.Message == y.Message && x.Group == y.Group && x.CurrentGroup == y.CurrentGroup && x.GroupKind == y.GroupKind
                && x.Source == y.Source && x.Target == y.Target && x.RotaryPositions == y.RotaryPositions && x.RotarySignals.SequenceEqual(y.RotarySignals) && x.SensorRange == y.SensorRange
                && x.Details == y.Details && x.SourceName == y.SourceName && x.TargetName == y.TargetName && GeometryEquals(x.Geometry, y.Geometry),
            (KiasScannerState x, KiasScannerState y) => x.Range == y.Range && x.Modules == y.Modules && GeometryEquals(x.Geometry, y.Geometry),
            (KiasSensorState x, KiasSensorState y) => x.Range == y.Range && x.Arc == y.Arc && GeometryEquals(x.Geometry, y.Geometry),
            (KiasCrewState x, KiasCrewState y) => x.Locked == y.Locked && x.DetectedCrew == y.DetectedCrew && x.Registered.SequenceEqual(y.Registered),
            (KiasSpeakerState x, KiasSpeakerState y) => x.Group == y.Group && x.Message == y.Message,
            (KiasWirelessState x, KiasWirelessState y) => x.Channel == y.Channel && x.Range == y.Range
                && x.TrustedTransmitters.SequenceEqual(y.TrustedTransmitters),
            (KiasLightState x, KiasLightState y) => x.Group == y.Group && x.Color == y.Color && x.Brightness == y.Brightness,
            (KiasResourceState x, KiasResourceState y) => x.Details == y.Details,
            (KiasManagementState x, KiasManagementState y) => x.Online == y.Online && x.Entities == y.Entities && x.Crew == y.Crew
                && x.Devices == y.Devices && x.Log == y.Log && x.Atmos == y.Atmos && x.CrewDetails == y.CrewDetails && x.Power == y.Power
                && x.Defence == y.Defence && x.Navigation == y.Navigation && x.Faults == y.Faults && x.Alert == y.Alert && x.Resources == y.Resources
                && x.Automation == y.Automation
                && (x.Audio?.Notification, x.Audio?.Warning, x.Audio?.Battle, x.Audio?.Emergency) == (y.Audio?.Notification, y.Audio?.Warning, y.Audio?.Battle, y.Audio?.Emergency),
            _ => false,
        };
    }
}
