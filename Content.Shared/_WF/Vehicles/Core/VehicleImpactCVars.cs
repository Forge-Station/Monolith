using Robust.Shared.Configuration;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>How hard crashes hurt vehicles.</summary>
[CVarDefs]
public sealed class VehicleImpactCVars
{
    /// <summary>The share of a frame's integrity that a hit at top speed costs a light vehicle, before its weight class scales it.</summary>
    public static readonly CVarDef<float> FrameFraction =
        CVarDef.Create("wf.vehicle_impact_frame_fraction", 0.18f, CVar.SERVERONLY);

    /// <summary>What a ship's hull hitting a vehicle's tiles deals to it, as a multiple of what any other thing there takes.</summary>
    public static readonly CVarDef<float> HullMultiplier =
        CVarDef.Create("wf.vehicle_hull_impact_multiplier", 3f, CVar.SERVERONLY);
}
