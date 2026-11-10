using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.Audio;

namespace Content.Shared._WF.Vehicles.Grid;

/// <summary>What the mover needs to know to smash one thing; <see cref="GridVehicleMoverSystem.TryGetSmashable"/> supplies it.</summary>
public readonly record struct VehicleSmashSettings(
    bool DeleteOnHit,
    double DamageOnHit,
    float SlowdownMultiplier,
    float SlowdownDuration,
    SoundSpecifier? SmashSound,
    bool RequiresDoorUnpowered,
    VehicleWeightClass? MinDestroyWeightClass,
    VehicleWeightClass? MinContinueWeightClass,
    VehicleWeightClass? SlowdownBelowWeightClass)
{
    /// <summary>The settings a <see cref="VehicleSmashableComponent"/> carries.</summary>
    public static VehicleSmashSettings From(VehicleSmashableComponent smashable)
    {
        return new VehicleSmashSettings(
            smashable.DeleteOnHit,
            smashable.DamageOnHit,
            smashable.SlowdownMultiplier,
            smashable.SlowdownDuration,
            smashable.SmashSound,
            smashable.RequiresDoorUnpowered,
            smashable.MinDestroyWeightClass,
            smashable.MinContinueWeightClass,
            smashable.SlowdownBelowWeightClass);
    }
}
