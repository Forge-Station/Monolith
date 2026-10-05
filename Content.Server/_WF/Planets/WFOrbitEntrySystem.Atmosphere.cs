using System.Diagnostics.CodeAnalysis;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Planets;

/// <summary>Console descent from orbit into the atmosphere, with the lift warning and confirm.</summary>
public sealed partial class WFOrbitEntrySystem
{
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private WFFlightSystem _flight = default!;

    // The readout shows two decimals.
    private const float LiftRatioEpsilon = 0.01f;

    // Below CE's 0.25 settle zone, or a hull with lift would drift straight back into orbit.
    private const float AtmosphereEntryProgress = 0.7f;

    // Levels/s; above CE's ExitTransitMaxSpeed (0.1) so the first settle check doesn't count it as landed.
    private const float AtmosphereEntrySeed = 0.15f;

    /// <summary>Drops the console's hull out of orbit; low lift or power needs <paramref name="confirmed"/>.</summary>
    public bool TryEnterAtmosphere(EntityUid console, bool confirmed, [NotNullWhen(false)] out string? reason)
    {
        if (!TryGetHull(console, out var hull, out reason))
            return false;

        var grid = hull.Value.Owner;

        if (Transform(grid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
        {
            reason = Loc.GetString("wf-orbit-not-in-orbit");
            return false;
        }

        if (!confirmed
            && _zLevels.WfTryGetLiftRatio(grid, out var ratio)
            && ratio < CEZLevelsSystem.WFFullLiftRatio)
        {
            reason = Loc.GetString("wf-flight-lift-warning", ("ratio", ratio.ToString("F2")));
            return false;
        }

        _zLevels.WfGetAtmospherePower(grid, out _, out var powerDeficit);
        if (!confirmed && powerDeficit)
        {
            reason = Loc.GetString("wf-flight-confirm-power-deficit");
            return false;
        }

        return TryDropFromOrbit(grid, out reason);
    }

    /// <summary>Moves a grid into the gap below orbit, entering lift-lost if it can't hold itself up.</summary>
    public bool TryDropFromOrbit(EntityUid grid, [NotNullWhen(false)] out string? reason)
    {
        if (Transform(grid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
        {
            reason = Loc.GetString("wf-orbit-not-in-orbit");
            return false;
        }

        if (HasComp<FTLComponent>(grid))
        {
            reason = Loc.GetString("wf-flight-busy");
            return false;
        }

        if (!TryComp<MapGridComponent>(grid, out var gridComp))
        {
            reason = Loc.GetString("wf-orbit-no-hull");
            return false;
        }

        var hasRatio = _zLevels.WfTryGetLiftRatio(grid, out var ratio);

        // The pooled-lift cache can be stale and would settle the hull straight back into orbit.
        _zLevels.WfInvalidateGravgenCapacity();

        var faller = EnsureComp<CEZGridFallerComponent>(grid);
        faller.Velocity = AtmosphereEntrySeed;
        faller.GravityTime = _timing.CurTime;

        if (!_zLevels.TryEnterTransit((grid, gridComp), AtmosphereEntryProgress))
        {
            reason = Loc.GetString("wf-flight-descent-refused");
            return false;
        }

        // Orbit parking disabled z-gravity.
        _zLevels.WfRearmZGravity(grid);

        if (hasRatio && ratio < CEZLevelsSystem.WFFullLiftRatio)
            _flight.EnterLiftLost(grid, ratio);

        _nextRefresh = TimeSpan.Zero;
        reason = null;
        return true;
    }

    /// <summary>Console request to enter the atmosphere; the actor must pilot this console.</summary>
    private void OnEnterAtmosphereMessage(EntityUid uid, ShuttleConsoleComponent component, WFEnterAtmosphereMessage args)
    {
        if (GetEntity(args.Console) != uid || !IsPilot(args.Actor, uid))
            return;

        if (!TryEnterAtmosphere(uid, args.Confirmed, out var reason))
            _popup.PopupEntity(reason, uid, args.Actor);
    }
}
