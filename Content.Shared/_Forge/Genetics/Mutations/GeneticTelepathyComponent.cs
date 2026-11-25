using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Send a silent mental message to a visible target within range.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticTelepathyComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 5f;

    [DataField]
    public EntProtoId Action = "ActionGeneticTelepathy";

    [DataField]
    public EntityUid? ActionEntity;
}
