using Content.Server._WF.Planets.Flight;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Power.Pow3r;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ThrusterSystem
{
    public static readonly TimeSpan WfOverloadRest = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan WfOverloadBurst = TimeSpan.FromSeconds(1);

    private bool WfCooling(EntityUid uid) =>
        TryComp<WFAtmosphereThrusterComponent>(uid, out var state) && state.Cooling;

    /// <summary>Snapshot whole electrical networks before changing any loads, including docked engines.</summary>
    private void WfUpdatePowerPulses()
    {
        var groups = new Dictionary<PowerState.Network, List<(EntityUid Uid, ThrusterComponent Engine, ApcPowerReceiverComponent Receiver)>>();
        var query = EntityQueryEnumerator<ThrusterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var engine, out var receiver))
        {
            if (engine.Type != ThrusterType.Linear)
                continue;
            var xform = Transform(uid);
            if (engine.Enabled && xform.Anchored && xform.GridUid is { } grid && WfInAtmosphere(grid) && !WfRestingWreck(grid)
                && receiver.NeedsPower && !receiver.PowerDisabled && receiver.Provider?.Net is { } net)
            {
                if (!groups.TryGetValue(net.NetworkNode, out var group))
                    groups[net.NetworkNode] = group = new();
                group.Add((uid, engine, receiver));
            }
            else if (TryComp<WFAtmosphereThrusterComponent>(uid, out var old))
                WfSetPowerPulse(old, false);
        }
        foreach (var (net, engines) in groups)
        {
            var validNetwork = WfTryGetPowerStatistics(net, out var stats);
            var requested = stats.Consumption;
            foreach (var (uid, engine, receiver) in engines)
            {
                var load = TryComp<WFAtmosphereThrusterComponent>(uid, out var state) ? state.RatedLoad : engine.OriginalLoad;
                requested += load * (HasComp<WFLandingThrusterComponent>(uid) ? 1f : WfAtmospherePowerMultiplier) - receiver.Load;
            }
            var overloaded = !validNetwork || requested > stats.SupplyCurrent + 1f;
            foreach (var (uid, engine, _) in engines)
            {
                if (!TryComp<WFAtmosphereThrusterComponent>(uid, out var state))
                {
                    state = AddComp<WFAtmosphereThrusterComponent>(uid);
                    state.RatedThrust = engine.Thrust;
                    state.RatedLoad = engine.OriginalLoad;
                    state.WasPowered = engine.IsOn;
                }
                WfSetPowerPulse(state, overloaded && !HasComp<WFLandingThrusterComponent>(uid));
            }
        }
    }

    // Destroyed nodes linger until the next reconnect pass, so a missing network means no forecast.
    private bool WfTryGetPowerStatistics(PowerState.Network network, out NetworkPowerStatistics statistics)
    {
        try
        {
            statistics = _wfPowerNet.GetNetworkStatistics(network);
            return true;
        }
        catch (KeyNotFoundException)
        {
            statistics = default;
            return false;
        }
    }

    /// <summary>Cycles an overloaded engine between resting and bursting; clears it once power is sufficient.</summary>
    public void WfSetPowerPulse(WFAtmosphereThrusterComponent state, bool overloaded)
    {
        if (!overloaded)
        {
            state.PowerLimited = false;
            state.Cooling = false;
            state.PulseAt = TimeSpan.Zero;
            return;
        }
        if (!state.PowerLimited)
        {
            state.PowerLimited = true;
            state.Cooling = true;
            state.PulseAt = _timing.CurTime + WfOverloadRest;
        }
        else if (_timing.CurTime >= state.PulseAt)
        {
            state.Cooling = !state.Cooling;
            state.PulseAt = _timing.CurTime + (state.Cooling ? WfOverloadRest : WfOverloadBurst);
        }
    }
}
