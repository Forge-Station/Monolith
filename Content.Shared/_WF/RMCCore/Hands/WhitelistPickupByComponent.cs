using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Hands;

[RegisterComponent, NetworkedComponent]
public sealed partial class WhitelistPickupByComponent : Component
{
    [DataField]
    public EntityWhitelist Whitelist = new();
}
