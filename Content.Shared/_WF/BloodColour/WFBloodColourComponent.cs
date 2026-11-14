using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.BloodColour;

/// <summary>A character's chosen blood and flesh colour, set from their profile. Absent means the species' own.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WFBloodColourComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;

    /// <summary>The blood reagent the colour was set for; blood of any other reagent keeps its own colour.</summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;
}
