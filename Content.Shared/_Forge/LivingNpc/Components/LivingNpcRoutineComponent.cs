using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Forge.LivingNpc.Components;

/// <summary>
/// Job-style routine: work / rest / social cycles around nearby spots.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class LivingNpcRoutineComponent : Component
{
    [DataField]
    public float SpotRange = 24f;

    [DataField]
    public TimeSpan WorkDuration = TimeSpan.FromSeconds(25);

    [DataField]
    public TimeSpan RestDuration = TimeSpan.FromSeconds(12);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextRoutineSwitch;

    [ViewVariables]
    public LivingNpcSpotKind PreferredSpot = LivingNpcSpotKind.Work;
}
