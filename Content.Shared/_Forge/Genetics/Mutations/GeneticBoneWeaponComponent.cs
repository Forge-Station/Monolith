using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Grow or retract a bone blade in the active hand. Growing costs slash damage.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticBoneWeaponComponent : Component
{
    [DataField, AutoNetworkedField]
    public float GrowSlash = 15f;

    [DataField]
    public EntProtoId Action = "ActionGeneticBoneWeapon";

    [DataField]
    public EntProtoId WeaponPrototype = "GeneBoneBlade";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntityUid? SpawnedWeapon;
}
