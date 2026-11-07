using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Mutations;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticMovespeedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float WalkModifier = 1.2f;

    [DataField, AutoNetworkedField]
    public float SprintModifier = 1.2f;
}
