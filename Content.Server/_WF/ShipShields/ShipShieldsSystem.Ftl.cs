using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private ShuttleSystem _wfShieldFtlShuttles = default!;
    private const FTLState WolfgateShieldFtlStates = FTLState.Starting | FTLState.Travelling | FTLState.Arriving | FTLState.Cooldown;

    /// <summary>Checks this grid's FTL phase or a docked group's successful spoolup.</summary>
    public bool IsWolfgateShieldFtlLocked(EntityUid grid)
    {
        // A planet hop drops the field the same way for as long as it lasts.
        if (HasComp<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>(grid))
            return true;

        if (TryComp<FTLComponent>(grid, out var ftl) &&
            ((ftl.State & WolfgateShieldFtlStates) != 0 || ftl.LinkedShuttle is { } main &&
             TryComp<FTLComponent>(main, out var linked) && (linked.State & WolfgateShieldFtlStates) != 0))
            return true;
        if (!HasComp<ShuttleComponent>(grid))
            return false;
        var starting = EntityQueryEnumerator<FTLComponent>();
        while (starting.MoveNext(out var uid, out var component))
        {
            if ((component.State & FTLState.Starting) == 0 || component.LinkedShuttle != null)
                continue;
            var docked = new HashSet<EntityUid>();
            _wfShieldFtlShuttles.GetAllDockedShuttles(uid, docked);
            if (docked.Contains(grid))
                return true;
        }
        return false;
    }

    /// <summary>Drops a field for FTL without changing its switch, stored damage or recharge state.</summary>
    public void SuppressWolfgateShieldForFtl(EntityUid grid)
    {
        if (!TryComp<ShipShieldedComponent>(grid, out var shielded))
            return;
        var shield = shielded.Shield;
        var source = shielded.Source;
        if (!UnshieldEntity(grid, shielded))
            return;
        if (source is not { } uid || !TryComp<ShipShieldEmitterComponent>(uid, out var emitter))
            return;
        if (emitter.Shield == shield)
        {
            emitter.Shield = null;
            emitter.Shielded = null;
        }
        LogWolfgateShieldTransition(uid, grid, emitter, false, "FTL lockout");
        PlayWolfgateShieldPowerSound(uid, grid, false);
    }

    /// <summary>Enforces direct FTL state changes before emitter recharge ticks.</summary>
    private void UpdateWolfgateFtlShields()
    {
        var hopping = EntityQueryEnumerator<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>();
        while (hopping.MoveNext(out var hopper, out _))
            SuppressWolfgateShieldForFtl(hopper);

        var query = EntityQueryEnumerator<FTLComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if ((component.State & WolfgateShieldFtlStates) == 0 &&
                !(component.LinkedShuttle is { } main && TryComp<FTLComponent>(main, out var linked) &&
                  (linked.State & WolfgateShieldFtlStates) != 0))
                continue;
            SuppressWolfgateShieldForFtl(uid);
            if ((component.State & FTLState.Starting) == 0 || component.LinkedShuttle != null)
                continue;
            var docked = new HashSet<EntityUid>();
            _wfShieldFtlShuttles.GetAllDockedShuttles(uid, docked);
            foreach (var grid in docked)
                SuppressWolfgateShieldForFtl(grid);
        }
    }
}
