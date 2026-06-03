using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Traits.Physical;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AddictionTraitComponent : Component
{
    [DataField, AutoNetworkedField]
    public string AddictionId { get; set; } = string.Empty;

    [DataField, AutoNetworkedField]
    public float InitialTolerance { get; set; } = 0.1f;
}
