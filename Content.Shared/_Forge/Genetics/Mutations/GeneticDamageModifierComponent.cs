using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Incoming damage coefficient. Values above 1 are weaknesses, below 1 are resistances.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticDamageModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Coefficient = 1.25f;
}
