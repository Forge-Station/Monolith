using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Marks a hulk mutation. Skin tint and extra punch damage live here so they can be reverted.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GeneticHulkComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Tint = Color.FromHex("#3D8C40");

    [DataField]
    public Color? OriginalSkin;

    [DataField]
    public bool SkinCaptured;
}
