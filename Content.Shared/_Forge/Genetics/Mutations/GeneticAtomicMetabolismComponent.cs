using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.Genetics.Mutations;

/// <summary>
/// Converts Radiation damage in the body into Brute healing and nutrition; emits a weak rad glow.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class GeneticAtomicMetabolismComponent : Component
{
    /// <summary>
    /// Hit points of Brute healed per point of Radiation consumed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HealPerRad = 3f;

    /// <summary>
    /// Hunger points restored per point of Radiation consumed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float NutritionPerRad = 2f;

    [DataField, AutoNetworkedField]
    public float RadiationIntensity = 0.35f;

    [DataField, AutoNetworkedField]
    public float RadiationSlope = 1.2f;

    [DataField]
    public bool AddedRadiationSource;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextMetabolize;
}
