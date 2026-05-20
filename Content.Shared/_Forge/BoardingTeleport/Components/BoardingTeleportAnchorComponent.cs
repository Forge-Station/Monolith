using Robust.Shared.GameStates;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportAnchorComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public NetEntity HomePlatform;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan? ExpiresAt;

    [ViewVariables, AutoNetworkedField]
    public bool EmergencyReturnUsed;
}
