namespace Content.Server._Forge.CloakingShuttle;

[RegisterComponent]
public sealed partial class CloakingShuttleDeviceComponent : Component
{
    [DataField("duration")]
    public float Duration = 45f;

    [DataField("cooldown")]
    public float Cooldown = 120f;

    [DataField("shieldDisablingInCloaking")]
    public bool ShieldDisablingInCloaking = true;

    [DataField("radioSendBlockingInCloaking")]
    public bool RadioSendBlockingInCloaking = true;

    [DataField("radioReceiveBlockingInCloaking")]
    public bool RadioReceiveBlockingInCloaking = false;
}
