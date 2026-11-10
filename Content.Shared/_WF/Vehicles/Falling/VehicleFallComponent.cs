using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Falling;

/// <summary>Lets a grid vehicle drive off an edge where a z-level lies below and drop to it, and fall when its floor goes.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleFallComponent : Component
{
    /// <summary>Whether the vehicle may fall at all.</summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>The share of the footprint with no floor under it at which the vehicle drops.</summary>
    [DataField]
    public float UnsupportedFraction = 0.5f;

    /// <summary>Frame damage on landing as a share of the frame's maximum integrity; null takes the weight class's share.</summary>
    [DataField]
    public float? FrameDamageFraction;

    /// <summary>For a vehicle with no frame integrity: the damage that wrecks it; a landing deals a share of it.</summary>
    [DataField]
    public float BodyHealth = 300f;

    /// <summary>Damage to each wheel on landing; null takes the weight class's amount.</summary>
    [DataField]
    public float? WheelDamage;

    /// <summary>Blunt damage to a mob the vehicle lands on; null takes the weight class's amount.</summary>
    [DataField]
    public float? MobDamage;
}
