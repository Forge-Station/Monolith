using Content.Shared._CE.ZLevels.Core.Components;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WF.Planets.Flight;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Pow3r;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Content.Shared.Examine;
using Robust.Shared.Map.Components;

namespace Content.Server.Shuttles.Systems;

/// <summary>Ordinary engines lose half their thrust below orbit. Conversions retain their ratings.</summary>
public sealed partial class ThrusterSystem
{
    public const float WfAtmosphereThrustMultiplier = 0.5f;
    // Hover power multiplier for ordinary engines; 1 keeps atmosphere power the same as space.
    public const float WfAtmospherePowerMultiplier = 1f;
    public const float WfStandardGravity = 9.81f;
    private TimeSpan _wfNextAtmosphereUpdate;
    private TimeSpan _wfRestingAt;
    private readonly Dictionary<EntityUid, bool> _wfResting = new();

    private bool WfRestingWreck(EntityUid grid)
    {
        if (_wfRestingAt != _timing.CurTime)
        {
            _wfRestingAt = _timing.CurTime;
            _wfResting.Clear();
        }
        if (!_wfResting.TryGetValue(grid, out var resting))
            _wfResting[grid] = resting = _wfFlightLevels.WfWreckResting(grid);
        return resting;
    }
    [Dependency] private PowerNetSystem _wfPowerNet = default!;
    [Dependency] private CEZLevelsSystem _wfFlightLevels = default!;

    private void WfExamineAtmosphere(EntityUid uid, ExaminedEvent args)
    {
        if (HasComp<WFLandingThrusterComponent>(uid))
            args.PushText(Loc.GetString("wf-thruster-landing-converted"));
        else if (TryComp<WFAtmosphereThrusterComponent>(uid, out var state) && state.Atmospheric)
            args.PushText(Loc.GetString("wf-thruster-atmosphere-mode"));
    }

    /// <summary>True when a grid is on a planet layer below orbit or in a gap above one.</summary>
    public bool WfInAtmosphere(EntityUid grid)
    {
        var map = Transform(grid).MapUid;
        if (map == null || HasComp<WFOrbitLayerComponent>(map))
            return false;
        if (HasComp<WFPlanetLayerComponent>(map))
            return true;
        return TryComp<CEZTransitMapComponent>(map, out var transit)
               && HasComp<WFPlanetLayerComponent>(transit.LowerMap ?? transit.UpperMap);
    }

    private void WfUpdateAtmosphereThrusters()
    {
        if (_timing.CurTime < _wfNextAtmosphereUpdate)
            return;
        _wfNextAtmosphereUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        WfUpdatePowerPulses();
        var query = EntityQueryEnumerator<ThrusterComponent>();
        while (query.MoveNext(out var uid, out var thruster))
        {
            if (thruster.Type == ThrusterType.Linear)
                WfRefreshAtmosphereThruster(uid, thruster);
        }
    }

    /// <summary>Applies or lifts the atmosphere thrust and power penalty on one linear thruster.</summary>
    public void WfRefreshAtmosphereThruster(EntityUid uid, ThrusterComponent thruster)
    {
        if (thruster.Type != ThrusterType.Linear)
            return;
        var xform = Transform(uid);
        var inAtmosphere = xform.GridUid is { } grid && !HasComp<MapComponent>(grid) && WfInAtmosphere(grid);
        if (!TryComp<WFAtmosphereThrusterComponent>(uid, out var state))
        {
            if (!inAtmosphere)
                return;
            state = AddComp<WFAtmosphereThrusterComponent>(uid);
            state.RatedThrust = thruster.Thrust;
            state.RatedLoad = thruster.OriginalLoad;
            state.WasPowered = thruster.IsOn;
        }
        var penalized = inAtmosphere && !HasComp<WFLandingThrusterComponent>(uid);
        var thrust = state.RatedThrust * (penalized ? WfAtmosphereThrustMultiplier : 1f);
        // Adjust the registered directional bank without toggling power, sound or burn fixtures.
        if (thruster.Thrust != thrust)
        {
            if (thruster.IsOn && TryComp<ShuttleComponent>(xform.GridUid, out var shuttle))
            {
                var direction = (int)xform.LocalRotation.GetCardinalDir() / 2;
                shuttle.LinearThrust[direction] += thrust - thruster.Thrust;
            }
            thruster.Thrust = thrust;
        }
        state.Atmospheric = inAtmosphere;
        thruster.OriginalLoad = state.RatedLoad * (penalized ? WfAtmospherePowerMultiplier : 1f);
        if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            receiver.Load = thruster.Enabled && !state.Cooling ? thruster.OriginalLoad : 1f;
        if (state.Cooling)
            DisableThruster(uid, thruster);
        else if (!thruster.IsOn && CanEnable(uid, thruster))
            EnableThruster(uid, thruster);

        state.WasPowered = thruster.IsOn;
    }

    /// <summary>Changes the unscaled rating, including while in atmosphere. Also used by fixture builders.</summary>
    public void WfSetRatedThrust(EntityUid uid, float rating)
    {
        var thruster = Comp<ThrusterComponent>(uid);
        var on = thruster.IsOn;
        if (on)
            DisableThruster(uid, thruster);
        thruster.Thrust = rating;
        WfRefreshAtmosphereRating(uid, thruster);
        if (on && CanEnable(uid, thruster))
            EnableThruster(uid, thruster);
    }

    private void WfRefreshAtmosphereRating(EntityUid uid, ThrusterComponent thruster)
    {
        if (TryComp<WFAtmosphereThrusterComponent>(uid, out var state))
            state.RatedThrust = thruster.Thrust;
        WfRefreshAtmosphereThruster(uid, thruster);
    }

    /// <summary>Prospective atmospheric force, even in orbit; ignores unavailable engines.</summary>
    public float WfAtmosphericForce(EntityUid uid, ThrusterComponent thruster)
    {
        if (thruster.Type != ThrusterType.Linear || !thruster.Enabled || !thruster.IsOn)
            return 0f;
        if (TryComp<WFAtmosphereThrusterComponent>(uid, out var state) && state.Cooling)
            return 0f;
        return WfRatedThrust(uid, thruster) * (HasComp<WFLandingThrusterComponent>(uid) ? 1f : WfAtmosphereThrustMultiplier);
    }

    /// <summary>The engine's rating without the atmosphere penalty.</summary>
    public float WfRatedThrust(EntityUid uid, ThrusterComponent thruster)
    {
        return TryComp<WFAtmosphereThrusterComponent>(uid, out var state) ? state.RatedThrust : thruster.Thrust;
    }

    /// <summary>Continuous engine demand and local power-network headroom, not a battery endurance guarantee.</summary>
    public void WfAtmospherePower(IEnumerable<EntityUid> grids, out float demand, out bool deficit)
    {
        WfAtmospherePower(grids, out demand, out deficit, out _);
    }

    /// <inheritdoc cref="WfAtmospherePower(IEnumerable{EntityUid}, out float, out bool)"/>
    /// <param name="shortfall">Watts the networks are short, summed over networks; unlinked engines count their whole demand.</param>
    public void WfAtmospherePower(IEnumerable<EntityUid> grids, out float demand, out bool deficit, out float shortfall)
    {
        demand = 0f;
        deficit = false;
        shortfall = 0f;
        var extraByNet = new Dictionary<PowerState.Network, float>();
        foreach (var grid in grids)
        {
            var children = Transform(grid).ChildEnumerator;
            while (children.MoveNext(out var uid))
            {
                if (!TryComp<ThrusterComponent>(uid, out var thruster) || thruster.Type != ThrusterType.Linear
                    || !thruster.Enabled || !Transform(uid).Anchored
                    || !TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
                    continue;
                var normalLoad = TryComp<WFAtmosphereThrusterComponent>(uid, out var state)
                    ? state.RatedLoad : thruster.OriginalLoad;
                var requested = normalLoad * (HasComp<WFLandingThrusterComponent>(uid) ? 1f : WfAtmospherePowerMultiplier);
                demand += requested;
                if (!receiver.NeedsPower)
                    continue;
                if (receiver.PowerDisabled || receiver.Provider?.Net is not { } net)
                {
                    deficit = true;
                    shortfall += requested;
                    continue;
                }
                extraByNet[net.NetworkNode] = extraByNet.GetValueOrDefault(net.NetworkNode) + requested - receiver.Load;
            }
        }
        foreach (var (net, extra) in extraByNet)
        {
            if (!WfTryGetPowerStatistics(net, out var stats))
            {
                deficit = true;
                shortfall += MathF.Max(extra, 0f);
                continue;
            }
            var missing = stats.Consumption + extra - stats.SupplyCurrent;
            if (missing > 1f)
            {
                deficit = true;
                shortfall += missing;
            }
        }
    }
}
