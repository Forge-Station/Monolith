using Content.Server._WF.Lattice;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFLatticeSystem _wfLattice = default!;

    /// <summary>Refuses a jump whose destination lies where FTL drives die, and tells the pilot.</summary>
    private bool WfRefuseLatticeTarget(EntityCoordinates target, EntityUid console, EntityUid actor)
    {
        if (_wfLattice.FtlWorksAt(_transform.ToMapCoordinates(target)))
            return false;

        _popup.PopupEntity(Loc.GetString("wf-lattice-ftl-target-dead"), console, actor, PopupType.Medium);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg"), console);
        return true;
    }
}
