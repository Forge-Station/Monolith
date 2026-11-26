using Content.Server._WF.BoardingAction.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Dampening under a lockdown, four times park: engines can only push the ship a quarter as fast.</summary>
    private const float WfLockdownDamping = AnchorDampingStrength * 4f;

    /// <summary>Whether a lockdown has this ship's dampening, against every pilot, autopilot and crew.</summary>
    private bool WfLockedDown(EntityUid grid)
    {
        return TryComp<WFHackedGridComponent>(grid, out var hacked) && hacked.LockedOut.Count > 0;
    }

    /// <summary>Clamps a ship's dampening past park for a lockdown. Let go, the ship is left in park.</summary>
    public void WfSetLockdownDamping(EntityUid grid, bool clamped)
    {
        if (TerminatingOrDeleted(grid) || !TryComp<ShuttleComponent>(grid, out var shuttle))
            return;

        shuttle.BodyModifier = clamped ? WfLockdownDamping : AnchorDampingStrength;
        if (shuttle.DampingModifier != 0)
            shuttle.DampingModifier = shuttle.BodyModifier;

        _console.RefreshShuttleConsoles(grid);
    }

    /// <summary>Whether a ship is held from leaving. Every jump passes here, whatever started it.</summary>
    private bool WfDisruptedDeparture(EntityUid uid) => HasComp<WFFtlDisruptedComponent>(uid);

    /// <summary>Stops a jump still spooling up and leaves the ship where it is, ready to fly.</summary>
    public bool WfAbortFtlSpool(EntityUid uid)
    {
        if (!TryComp<FTLComponent>(uid, out var ftl) || ftl.State != FTLState.Starting || ftl.LinkedShuttle != null)
            return false;

        ftl.StartupStream = _audio.Stop(ftl.StartupStream);
        if (TryComp<ShuttleComponent>(uid, out var shuttle))
            _thruster.DisableLinearThrusters(shuttle);

        RemComp<FTLComponent>(uid);
        _console.RefreshShuttleConsoles(uid);
        return true;
    }
}
