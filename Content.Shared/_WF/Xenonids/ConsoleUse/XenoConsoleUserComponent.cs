using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ConsoleUse;

/// <summary>Lets a xeno open the ship consoles its whitelist names. Gunnery consoles are never allowed.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoConsoleUserComponent : Component
{
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();
}
