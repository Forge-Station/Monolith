using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Added to someone using a jetpack for movement purposes
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class JetpackUserComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Jetpack;

    [DataField, AutoNetworkedField]
    public float WeightlessAcceleration;

    [DataField, AutoNetworkedField]
    public float WeightlessFriction;

    [DataField, AutoNetworkedField]
    public float WeightlessFrictionNoInput;

    [DataField, AutoNetworkedField]
    public float WeightlessModifier;

    // Forge-Change-start
    [DataField, AutoNetworkedField]
    public float BaseWeightlessAcceleration;

    [DataField, AutoNetworkedField]
    public float BaseWeightlessFriction;

    [DataField, AutoNetworkedField]
    public float BaseWeightlessModifier;

    [DataField, AutoNetworkedField]
    public float SuitThrustMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SuitControlMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SuitFuelUsageMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float SuitCombatStabilityBonus;

    [DataField, AutoNetworkedField]
    public float CombatControlPenaltyMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public float CombatModifierPenaltyMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public TimeSpan CombatPenaltyEndTime = TimeSpan.Zero;
    // Forge-Change-end
}
