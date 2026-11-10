using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>On a hardpoint: a plow. While it works, the vehicle damages the walls it is driven front-on into.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VehiclePlowComponent : Component
{
    /// <summary>Multiplier on the vehicle's ram damage against what a plow can break.</summary>
    [DataField]
    public float StructureDamageMultiplier = 1.5f;
}

/// <summary>On a vehicle built around a plow: a further multiplier, and the engine force to grind a wall down without a run-up.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class VehiclePlowChassisComponent : Component
{
    /// <summary>Multiplier on the fitted plow's own.</summary>
    [DataField]
    public float StructureDamageMultiplier = 1f;

    /// <summary>Damage per second while a working plow is held against what it can break.</summary>
    [DataField]
    public float PoweredDemolitionDamagePerSecond;

    /// <summary>Seconds of unbroken contact before the grind begins.</summary>
    [DataField]
    public float PoweredDemolitionWarmup = 0.5f;

    /// <summary>Played while the grind goes on.</summary>
    [DataField]
    public SoundSpecifier? PoweredDemolitionSound = new SoundPathSpecifier("/Audio/Machines/airlock_creaking.ogg");

    /// <summary>Least time between two plays of the sound.</summary>
    [DataField]
    public float PoweredDemolitionSoundCooldown = 2f;
}
