using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Newbie;

/// <summary>
/// On the body a new player is controlling: draws the learner mark and adds the examine line.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFNewbieComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<SsdIconPrototype> Icon = "WFNewbieIcon";
}

/// <summary>
/// On a ship whose owner is a new player: tags its radar label and hail name.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFNewbieShipComponent : Component;
