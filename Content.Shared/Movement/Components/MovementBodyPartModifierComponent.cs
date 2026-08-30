using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class MovementBodyPartModifierComponent : Component
{
    /// <summary>
    /// Multiplier applied to the body's walk speed.
    /// 1.0 = no change, 0.8 = -20%, 1.2 = +20%.
    /// </summary>
    [DataField]
    public float WalkModifier = 1f;

    /// <summary>
    /// Multiplier applied to the body's sprint speed.
    /// 1.0 = no change, 0.8 = -20%, 1.2 = +20%.
    /// </summary>
    [DataField]
    public float SprintModifier = 1f;
}