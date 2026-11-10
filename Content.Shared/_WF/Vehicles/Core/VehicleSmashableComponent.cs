using System;
using Content.Shared._WF.Vehicles.Grid.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Marks an entity a grid vehicle smashes instead of being stopped by, and how.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleSmashableComponent : Component
{
    /// <summary>Delete the entity after the hit if the damage left it standing.</summary>
    [DataField]
    public bool DeleteOnHit = true;

    /// <summary>Blunt damage of the hit; the server lowers it to what the entity needs to be destroyed.</summary>
    [DataField]
    public double DamageOnHit = 1000;

    /// <summary>Speed multiplier the vehicle gets after smashing it.</summary>
    [DataField]
    public float SlowdownMultiplier = 0.5f;

    /// <summary>Seconds the slowdown lasts.</summary>
    [DataField]
    public float SlowdownDuration = 0f;

    /// <summary>Played when the entity is smashed.</summary>
    [DataField]
    public SoundSpecifier? SmashSound;

    /// <summary>A door is smashed only while unpowered; a powered one opens for a driver with access, or holds.</summary>
    [DataField]
    public bool RequiresDoorUnpowered;

    /// <summary>A vehicle lighter than this is stopped and leaves the entity alone.</summary>
    [DataField]
    public VehicleWeightClass? MinDestroyWeightClass;

    /// <summary>A vehicle lighter than this destroys the entity and is stopped; a heavier one drives on.</summary>
    [DataField]
    public VehicleWeightClass? MinContinueWeightClass;

    /// <summary>Only vehicles lighter than this are slowed.</summary>
    [DataField]
    public VehicleWeightClass? SlowdownBelowWeightClass;
}
