using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Forge-Change - Defines how equipped outer clothing modifies jetpack behavior in space.
/// Defines how equipped outer clothing modifies jetpack behavior in space.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SpaceFlightProfileComponent : Component
{
    [DataField("thrustMultiplier")]
    public float ThrustMultiplier = 1f;

    [DataField("controlMultiplier")]
    public float ControlMultiplier = 1f;

    [DataField("fuelUsageMultiplier")]
    public float FuelUsageMultiplier = 1f;

    [DataField("combatStabilityBonus")]
    public float CombatStabilityBonus;
}
