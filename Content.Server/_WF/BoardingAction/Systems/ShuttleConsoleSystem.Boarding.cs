using Content.Server._WF.BoardingAction;
using Content.Shared._WF.BoardingAction;
using Content.Shared.Popups;
using Robust.Shared.Audio;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFBoardingHelmSystem _wfBoardingHelm = default!;
    [Dependency] private WFFtlDisruptionSystem _wfDisruption = default!;

    private WFBoardingHelmState? GetWfBoardingState(EntityUid? grid) => _wfBoardingHelm.GetState(grid);

    /// <summary>Tells the pilot why a held ship won't jump.</summary>
    private bool WfRefuseDisruptedFtl(EntityUid shuttle, EntityUid console, EntityUid actor)
    {
        if (!_wfDisruption.IsGroupDisrupted(shuttle))
            return false;

        _popup.PopupEntity(Loc.GetString("wf-boarding-ftl-disrupted"), console, actor, PopupType.Medium);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg"), console);
        return true;
    }
}
