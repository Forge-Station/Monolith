using Content.Shared._Starlight.Shadekin;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._HL.Traits.Physical;

/// <summary>
/// Modifies shadekin light-exposure burn and slowdown thresholds.
/// For non-shadekin species (MildLightSensitivity), light exposure is computed independently.
/// Burn damage scales as (LightExposure - BurnThreshold + 1) per tick.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class LightSensitivityComponent : Component
{
    /// <summary>
    /// Minimum LightExposure level (1–5, see <see cref="ShadekinState"/>) at which burning starts.
    /// Defaults to 5 (Extreme) so light sensitivity only burns in the brightest light unless overridden.
    /// </summary>
    [DataField]
    public int BurnThreshold = 5;

    /// <summary>
    /// Minimum LightExposure level (1–5, see <see cref="ShadekinState"/>) at which movement slowing starts.
    /// Defaults to 5 (Extreme).
    /// </summary>
    [DataField]
    public int SlowdownThreshold = 5;

    /// <summary>
    /// Speed multiplier applied to both walk and sprint when above SlowdownThreshold.
    /// </summary>
    [DataField]
    public float SpeedMultiplier = 0.9f;

    /// <summary>
    /// Computed light exposure level (1–5, matching <see cref="ShadekinState"/>) for non-shadekin entities.
    /// Updated by LightSensitivitySystem, which discretizes ambient light using the shadekin's own thresholds.
    /// </summary>
    [ViewVariables]
    public float CurrentLightExposure;

    [AutoPausedField]
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    [DataField]
    public TimeSpan UpdateCooldown = TimeSpan.FromSeconds(1f);
}
