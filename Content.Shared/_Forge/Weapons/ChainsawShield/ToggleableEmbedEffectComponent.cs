namespace Content.Shared._Forge.Weapons.ChainsawShield;

[RegisterComponent]
public sealed partial class ToggleableEmbedEffectComponent : Component
{
    [DataField]
    public float WalkSpeedModifier = 0.5f;

    [DataField]
    public float SprintSpeedModifier = 0.5f;

    public EntityUid? SlowedTarget;
}
