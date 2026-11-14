using Content.Shared.Mech.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.NpcCrew.Systems;

public sealed partial class WFCrewAlertSystem
{
    /// <summary>How far up the parent chain a weapon or pilot is looked for in a mech.</summary>
    private const int MechDepth = 4;

    /// <summary>A grid, or a piloted, working mech flying free of any grid: what ship crews take for a vessel.</summary>
    public bool IsVessel(EntityUid uid) => HasComp<MapGridComponent>(uid) || IsFreeMech(uid);

    /// <summary>Whether a mech is piloted, working and off any grid.</summary>
    public bool IsFreeMech(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && TryComp<MechComponent>(uid, out var mech) && !mech.Broken
               && mech.PilotSlot.ContainedEntity != null && Transform(uid).GridUid == null;
    }

    /// <summary>The free-flying mech an entity is, rides in or is fitted to, if any.</summary>
    public EntityUid? FreeMechOf(EntityUid? source)
    {
        var current = source;
        for (var depth = 0; depth < MechDepth && current is { } uid && !TerminatingOrDeleted(uid); depth++)
        {
            if (HasComp<MechComponent>(uid))
                return IsFreeMech(uid) ? uid : null;
            var xform = Transform(uid);
            if (xform.GridUid != null)
                return null;
            current = xform.ParentUid;
        }

        return null;
    }
}
