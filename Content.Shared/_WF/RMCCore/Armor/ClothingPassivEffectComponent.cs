using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RMCCore.Armor;

[RegisterComponent, NetworkedComponent]
public sealed partial class ClothingGrantComponentsComponent : Component
{
    [DataField(required: true)]
    public ComponentRegistry Components = new();
}
