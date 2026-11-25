using Content.Shared.Polymorph;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Slimeperson form with a toggle between humanoid slime and amorphous blob.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticSlimeFormComponent : Component
{
    [DataField, AutoNetworkedField]
    public float TransformSlash = 5f;

    [DataField]
    public EntProtoId Action = "ActionGeneticSlimeTransform";

    [DataField]
    public ProtoId<PolymorphPrototype> BlobPolymorph = "GeneForcedSlimeBlob";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public bool StartupDamageApplied;
}
