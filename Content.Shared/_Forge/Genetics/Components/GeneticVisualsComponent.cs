using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Genetics.Components;

/// <summary>
/// Networked visual side-effects stacked from active mutations.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GeneticVisualsComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color OverlayColor = Color.Transparent;

    [DataField, AutoNetworkedField]
    public Color GlowColor = Color.Transparent;

    [DataField, AutoNetworkedField]
    public float GlowEnergy;

    [DataField, AutoNetworkedField]
    public float GlowRadius = 1.6f;

    [DataField, AutoNetworkedField]
    public float Scale = 1f;

    [DataField, AutoNetworkedField]
    public bool Flicker;
}
