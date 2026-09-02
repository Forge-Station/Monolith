using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class MovementBodyPartModifierComponent : Component
{
    /// <summary>
    /// Parts with the same group are combined together.
    /// </summary>
    [DataField]
    public string Group = "default";

    [DataField]
    public float WalkModifier = 1f;

    [DataField]
    public float SprintModifier = 1f;
}