using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._HL.Traits.Physical;

/// <summary>
/// Grants shadekins passive healing of specific damage types while in complete darkness (LightExposure == 0).
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ShadekinRegenerationComponent : Component
{
    /// <summary>
    /// Amount healed per second for each configured damage type.
    /// </summary>
    [DataField]
    public float HealPerSecond = 0.18f;

    /// <summary>
    /// Seconds between healing ticks.
    /// </summary>
    [DataField]
    public float IntervalSeconds = 1f;

    /// <summary>
    /// Damage types this component heals.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> HealTypes = new();

    /// <summary>
    /// Multiplier applied to healing while the entity is in critical state.
    /// Values below 1 reduce healing; above 1 increase it.
    /// </summary>
    [DataField]
    public float CritMultiplier = 0.5f;

    [AutoPausedField]
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdate = TimeSpan.Zero;
}
