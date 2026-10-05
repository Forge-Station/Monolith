using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Caverns;

/// <summary>Marks a map as the cavern below a planet's ground.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFCavernLayerComponent : Component
{
    /// <summary>The cavern this map was built from.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<WFCavernPrototype> Cavern;

    /// <summary>The ground map above; only set on the server.</summary>
    [ViewVariables]
    public EntityUid Ground;
}
