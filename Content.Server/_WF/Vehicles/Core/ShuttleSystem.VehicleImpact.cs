using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Robust.Shared.Configuration;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>What a ship's hull impact deals to <paramref name="entity"/> compared with other things: more to a vehicle.</summary>
    public float VehicleHullMultiplier(EntityUid entity)
    {
        return HasComp<RMCVehicleComponent>(entity) ? _cfg.GetCVar(VehicleImpactCVars.HullMultiplier) : 1f;
    }
}
