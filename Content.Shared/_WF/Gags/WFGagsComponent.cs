using Robust.Shared.GameStates;

namespace Content.Shared._WF.Gags;

/// <summary>
/// An item drawn in recolourable layers, after SS13's greyscale sprites (GAGS). A sprite layer whose state ends in
/// <c>-c&lt;N&gt;</c> is tinted with colour N, on the item, on the wearer and in hand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFGagsComponent : Component
{
    /// <summary>One colour per slot; the prototype's are the defaults.</summary>
    [DataField(required: true), AutoNetworkedField]
    public List<Color> Colors = new();
}
