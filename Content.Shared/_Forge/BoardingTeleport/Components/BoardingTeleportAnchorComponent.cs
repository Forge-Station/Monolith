using Robust.Shared.GameStates;

using Robust.Shared.Serialization;



namespace Content.Shared._Forge.BoardingTeleport.Components;



[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]

public sealed partial class BoardingTeleportAnchorComponent : Component

{

    /// <summary>

    /// Platform that opened the return channel. Return is only valid through this entity.

    /// </summary>

    [ViewVariables, AutoNetworkedField]

    public NetEntity HomePlatform;



    /// <summary>

    /// When the return channel closes. After this, the boarder cannot jump back via the platform.

    /// </summary>

    [ViewVariables, AutoNetworkedField]

    public TimeSpan? ExpiresAt;

}

