namespace Content.Server._Forge.CloakingShuttle;

[RegisterComponent]
public sealed partial class CloakingShuttleDeviceComponent : Component
{
    [DataField("cooldown")]
    public float Cooldown = 15f;

    [DataField("duration")]
    public float Duration = 30f;

    [DataField("shieldDisablingInCloaking")]
    public bool ShieldDisablingInCloaking;
}
