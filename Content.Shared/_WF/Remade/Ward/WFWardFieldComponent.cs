using Robust.Shared.GameStates;

namespace Content.Shared._WF.Remade.Ward;

/// <summary>Marks the ship shield entity that a Monolith's Ward feeds, so clients draw it as the stone's field and not a ship shield.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFWardFieldComponent : Component
{
    /// <summary>The stone's stage, which sets how hard and bright the field looks.</summary>
    [DataField, AutoNetworkedField]
    public WFMonolithStage Stage;

    /// <summary>True while the stone is rooted at the Crescendo and the field can't be dented.</summary>
    [DataField, AutoNetworkedField]
    public bool Rooted;

    /// <summary>Colour the membrane and its glyphs take instead of the stone's red, for fields that borrow its look.</summary>
    [DataField, AutoNetworkedField]
    public Color? Tint;
}
