namespace Content.Shared._WF.Xenonids.Fire;

/// <summary>
/// RMC's ignition immunity: this entity can never be set alight, and a fire already on it goes out.
/// </summary>
[RegisterComponent]
public sealed partial class RMCImmuneToIgnitionComponent : Component;

/// <summary>
/// RMC's tile fire immunity. Upstream fire has no tile fires, so this scales the entity's own burn damage instead.
/// </summary>
[RegisterComponent]
public sealed partial class RMCImmuneToFireTileDamageComponent : Component
{
    [DataField]
    public float BurnDamageMultiplier = 0.5f;
}

/// <summary>
/// RMC's modifier for the armour debuff of standing in green fire. That fire isn't ported, so this is only a marker.
/// </summary>
[RegisterComponent]
public sealed partial class RMCFireArmorDebuffModifierComponent : Component
{
    [DataField]
    public float DebuffModifier = 1;
}

/// <summary>
/// Weeds that keep burning until patted out or destroyed, and set the weeds next to them alight.
/// </summary>
[RegisterComponent]
public sealed partial class XenoWeedsBurnComponent : Component
{
    /// <summary>How long these weeds burn before lighting their cardinal neighbours.</summary>
    [DataField]
    public TimeSpan SpreadDelay = TimeSpan.FromSeconds(2);

    [ViewVariables]
    public TimeSpan? SpreadAt;

    [ViewVariables]
    public bool Spread;
}
