using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Timing;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.CloakingShuttle;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CloakingShuttleComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool Active;

    [ViewVariables(VVAccess.ReadWrite)]
    public StartEndTime TimeActive;

    [ViewVariables(VVAccess.ReadWrite)]
    public StartEndTime TimeCooldown;

    [ViewVariables(VVAccess.ReadWrite)]
    public float DeviceDuration;

    [ViewVariables(VVAccess.ReadWrite)]
    public float DeviceCooldown;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool DeviceShieldDisabling;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool DeviceRadioSendBlocking;

    [ViewVariables(VVAccess.ReadWrite)]
    public bool DeviceRadioReceiveBlocking;

    [ViewVariables(VVAccess.ReadWrite)]
    public ShuttleCloakingStatus Status = ShuttleCloakingStatus.None;

    [ViewVariables(VVAccess.ReadWrite)]
    public static readonly Color StealthColor = Color.Gray;
}
