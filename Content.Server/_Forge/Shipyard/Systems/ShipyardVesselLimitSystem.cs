using Content.Server._Mono.GameRule.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;

namespace Content.Server._Forge.Shipyard.Systems;

/// <summary>
/// Shared sector limit counting for shipyard vessel purchases.
/// </summary>
public sealed class ShipyardVesselLimitSystem : EntitySystem
{
    [Dependency] private HyperwarRuleSystem _hyperwar = default!;

    public int GetActiveLimit(VesselPrototype vessel)
        => _hyperwar.HyperwarActive ? vessel.HyperwarLimitActive : vessel.LimitActive;

    /// <summary>
    /// Counts shuttles that currently occupy a sector limit slot for this vessel type.
    /// </summary>
    public int CountLimitedVessels(VesselPrototype vessel, EntityUid? exclude = null)
    {
        var count = 0;
        var query = EntityQueryEnumerator<VesselComponent>();

        while (query.MoveNext(out var uid, out var targetVessel))
        {
            if (exclude != null && uid == exclude.Value)
                continue;

            if (targetVessel.VesselId != vessel.ID)
                continue;

            // Only ships idle past the inactivity threshold stop counting.
            if (TryComp<ShipActivityComponent>(uid, out var inactivity) && inactivity.InactivePastThreshold)
                continue;

            count++;
        }

        return count;
    }

    /// <summary>
    /// Returns true when another purchase of this vessel type should be blocked.
    /// </summary>
    public bool IsLimitBlockingPurchase(VesselPrototype vessel, EntityUid purchasingShuttle)
    {
        var limit = GetActiveLimit(vessel);
        if (limit <= 0)
            return false;

        return CountLimitedVessels(vessel, exclude: purchasingShuttle) >= limit;
    }

    public int GetRemainingSlots(VesselPrototype vessel)
    {
        var limit = GetActiveLimit(vessel);
        if (limit <= 0)
            return int.MaxValue;

        return Math.Max(0, limit - CountLimitedVessels(vessel));
    }
}
