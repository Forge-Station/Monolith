// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Wounds;

/// <summary>
/// Base class for optional behavior "bricks" a wound may carry.
/// A wound is assembled from these behaviors instead of using flat prototype fields.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class WoundBehavior
{
}

/// <summary>
/// Makes the wound bleed. Rate is per unit of bleeding severity.
/// </summary>
[DataDefinition]
public sealed partial class WoundBleedingBehavior : WoundBehavior
{
    [DataField]
    public float Rate;

    [DataField]
    public FixedPoint2 MinimumSeverity;

    [DataField]
    public float Chance = 1f;

    [DataField]
    public float AwakeMultiplier = 1f;

    [DataField]
    public float ClottingMultiplier = 1f;
}

/// <summary>
/// Makes the wound bleed internally: blood is lost directly from the bloodstream
/// without leaking to the outside (no puddles).
/// </summary>
[DataDefinition]
public sealed partial class WoundInternalBleedingBehavior : WoundBehavior
{
    /// <summary>Blood units lost per unit of severity per second.</summary>
    [DataField]
    public float Rate;

    [DataField]
    public float Chance = 1f;
}

/// <summary>
/// Makes the wound leave a scar once it closes if it peaked above the threshold.
/// Chance is rolled as <c>Chance * surgery.scar_chance</c> CVar; 0 = never, 1 = always when threshold met.
/// Stage behaviors override base, so different stages can have different thresholds/chances.
/// </summary>
[DataDefinition]
public sealed partial class WoundScarBehavior : WoundBehavior
{
    /// <summary>Peak severity required to consider scarring. Below this no scar is attempted.</summary>
    [DataField]
    public FixedPoint2 Threshold;

    /// <summary>Base probability to create a scar when threshold is met, before global CVar multiplier.</summary>
    [DataField]
    public float Chance = 1f;
}

/// <summary>
/// Makes the wound itself generate pain, independent of the part's damage.
/// </summary>
[DataDefinition]
public sealed partial class WoundPainBehavior : WoundBehavior
{
    /// <summary>Pain per unit of severity.</summary>
    [DataField]
    public float PainPerSeverity = 1f;

    /// <summary>Minimum severity before the wound starts producing pain.</summary>
    [DataField]
    public FixedPoint2? MinSeverity;

    /// <summary>
    /// If true, the wound produces a one-time pain spike when its severity increases,
    /// instead of a persistent pain floor while the wound is active.
    /// </summary>
    [DataField]
    public bool OneTime;
}

/// <summary>
/// Applies a status effect to the part's body (or the part itself) while the wound is active.
/// The effect is removed once the wound heals or drops below <see cref="MinSeverity"/>.
/// </summary>
[DataDefinition]
public sealed partial class WoundStatusEffectBehavior : WoundBehavior
{
    /// <summary>Status effect prototype to apply.</summary>
    [DataField(required: true)]
    public EntProtoId<StatusEffectComponent> StatusEffect;

    /// <summary>Minimum severity before the wound applies the effect. Null = always.</summary>
    [DataField]
    public FixedPoint2? MinSeverity;

    /// <summary>Duration of the effect. Null = lasts while the wound is active.</summary>
    [DataField]
    public TimeSpan? Duration;

    /// <summary>Apply to the part itself instead of its body.</summary>
    [DataField]
    public bool ApplyToPart;
}

/// <summary>
/// Declares how an active wound stage affects use of its body part.
/// </summary>
[DataDefinition]
public sealed partial class WoundFunctionalityBehavior : WoundBehavior
{
    [DataField(required: true)]
    public BodyPartFunctionalityState State;
}

/// <summary>
/// A single severity stage of a wound. The wound is in this stage once its severity
/// reaches <see cref="Severity"/>. Stage ordering is defined by these thresholds.
/// </summary>
[DataDefinition]
public sealed partial class WoundStageDefinition
{
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Optional IC observation shown during ordinary health examination.</summary>
    [DataField]
    public LocId? ExamineDescription;

    /// <summary>Severity at which this stage begins.</summary>
    [DataField(required: true)]
    public FixedPoint2 Severity;

    /// <summary>
    /// Behaviors active while the wound is in this stage. They are layered on top of the
    /// prototype's base behaviors; a behavior of the same type on a stage overrides the base one.
    /// This lets stages define their own consequences (bleeding, pain, impairment, ...).
    /// </summary>
    [DataField]
    public List<WoundBehavior> Behaviors = new();
}
