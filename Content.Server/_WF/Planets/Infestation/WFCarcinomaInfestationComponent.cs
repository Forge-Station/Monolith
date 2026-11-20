using Robust.Shared.Physics;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._WF.Planets.Infestation;

/// <summary>Carcinoma growth on a hull landed on the carcinoma world.</summary>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
public sealed partial class WFCarcinomaInfestationComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextGrowth;
    public readonly HashSet<EntityUid> Tendrils = new();
    /// <summary>Whether the tendrils have pinned the hull static.</summary>
    public bool Held;
    /// <summary>Body type restored when the hull is released.</summary>
    public BodyType PreviousBodyType;
}

/// <summary>A tendril gripping a landed hull; the hull stays pinned while any remain.</summary>
[RegisterComponent]
public sealed partial class WFCarcinomaTendrilComponent : Component
{
    public EntityUid? Hull;
    [DataField] public SoundSpecifier? DeploySound;
}
