using Robust.Shared.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.BoardingTeleport.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BoardingTeleportPlatformComponent : Component
{
    public const float DefaultReturnWindowSeconds = 300f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string ReceiverPort = "BoardingTeleport";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ActivationRadius = 0.8f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Cooldown = 30f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DepartureDelay = 10f;

    /// <summary>
    /// How long after boarding the return channel stays open.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ReturnWindowSeconds = 300f;

    /// <summary>
    /// Maximum distance from the home platform at which a return jump is allowed.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MaxReturnDistance = 400f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool ExperimentalScatterControl;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool ExperimentalRisk;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId WarmupEffect = "BoardingTeleportWarmupEffect";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntProtoId TeleportEffect = "BoardingTeleportFlashEffect";

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier ActivationSound = new SoundPathSpecifier("/Audio/Effects/Vehicle/ambulancesiren.ogg");

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier CountdownSound = new SoundPathSpecifier("/Audio/_Mono/Effects/Alerts/master_caution.ogg");

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier CountdownFinalSound = new SoundPathSpecifier("/Audio/_Mono/Effects/Alerts/launchwarning.ogg");

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int CountdownFinalThreshold = 3;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier ArrivalSound = new SoundPathSpecifier("/Audio/Effects/Lightning/lightningbolt.ogg");

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public TimeSpan NextUse = TimeSpan.Zero;

    [ViewVariables, AutoNetworkedField]
    public bool DeparturePending;

    [ViewVariables, AutoNetworkedField]
    public uint ChargeToken;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? ActiveChargeUser;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? LinkedConsole;

    [ViewVariables]
    public EntityUid? PendingConsoleUid;

    [ViewVariables]
    public BoardingTeleportInsertionMode PendingMode;

    [ViewVariables]
    public float PendingDistanceScale;

    [ViewVariables]
    public bool PendingEmergencyReturn;

    [ViewVariables]
    public bool PendingReturning;

    [ViewVariables]
    public bool PendingDetectionBlipSpawned;

    [ViewVariables]
    public float PendingLockCheckAccumulator;

    /// <summary>
    /// Landing point locked in when this platform begins charging (per-boarder snapshot).
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? PendingLandingCoordinates;

    [ViewVariables]
    public System.Threading.CancellationTokenSource? PendingCancel;
}
