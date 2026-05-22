using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportAnchorComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public NetEntity HomePlatform;

    [ViewVariables, AutoNetworkedField]
    public TimeSpan? ExpiresAt;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [ViewVariables, AutoNetworkedField]
    public TimeSpan? CreatedAt;

    [ViewVariables, AutoNetworkedField]
    public float ReturnWindowSeconds;

    [ViewVariables, AutoNetworkedField]
    public bool EmergencyReturnUsed;

    /// <summary>Server-only cache so return alerts are not refreshed every tick.</summary>
    [ViewVariables]
    public bool? CachedReturnAlertEmergency;
}
