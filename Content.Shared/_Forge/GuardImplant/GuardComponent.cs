using Robust.Shared.GameStates;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Guard.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class GuardComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<SecurityIconPrototype> GuardStatusIcon = "GuardIcon";
}
