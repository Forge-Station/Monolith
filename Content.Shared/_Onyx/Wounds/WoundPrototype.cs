using Content.Shared.Damage.Prototypes;
using Content.Shared.Alert;
using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared._Onyx.Chemistry.Circulation;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): UI2

namespace Content.Shared._Onyx.Wounds;

[Prototype]
public sealed partial class WoundPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Damage types that can create or heal this wound and their conversion settings.
    /// Multiple wound prototypes may react to the same damage type.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<DamageTypePrototype>, WoundDamageTypeSettings> DamageTypes = new();

    [DataField]
    public WoundMergeMode MergeMode = WoundMergeMode.MergeByPrototype;

    [DataField]
    public FixedPoint2 MaximumSeverity = FixedPoint2.MaxValue;

    [DataField]
    public float HealingMultiplier = 1f;

    /// <summary>
    /// WOLFGATE(Wolfmed): W1: the wound is created only by a Wolfmed wound rule, never by the default per-damage-type
    /// pass. <see cref="DamageTypes"/> still governs how treatment and damage removal heal it.
    /// </summary>
    [DataField]
    public bool RuleOnly;

    [DataField]
    public WoundVisibility Visibility = WoundVisibility.Visible;

    /// <summary>
    /// WOLFGATE(Wolfmed): UI2: which bucket the analyzer's wounds tab files this wound under. Null lets
    /// <see cref="Content.Shared._WF.Wolfmed.Wounds.WolfmedWoundCategories.Resolve"/> derive one from
    /// <see cref="DamageTypes"/>; set it wherever the damage type would mislead.
    /// </summary>
    [DataField]
    public WolfmedWoundCategory? AnalyzerCategory;

    /// <summary>
    /// Optional behavior bricks this wound is assembled from.
    /// </summary>
    [DataField]
    public List<WoundBehavior> Behaviors = new();

    /// <summary>
    /// Severity stages of the wound. Each stage names the severity band above its
    /// threshold; the current stage is the stage with the highest threshold not
    /// exceeding the wound's severity. Thresholds are absolute and wound-specific.
    /// </summary>
    [DataField]
    public Dictionary<string, WoundStageDefinition> Stages = new();

    /// <summary>
    /// Returns the current stage for the given severity, or null if no stages are defined.
    /// </summary>
    public string? GetStage(FixedPoint2 severity)
    {
        string? current = null;
        var threshold = FixedPoint2.Zero;
        foreach (var (id, stage) in Stages)
        {
            if (stage.Severity <= severity && stage.Severity >= threshold)
            {
                threshold = stage.Severity;
                current = id;
            }
        }

        return current;
    }

    /// <summary>
    /// Returns the current stage definition for the given severity, or null if no stage applies.
    /// </summary>
    public WoundStageDefinition? GetStageDefinition(FixedPoint2 severity)
    {
        if (GetStage(severity) is not { } id || !Stages.TryGetValue(id, out var stage))
            return null;

        return stage;
    }

    /// <summary>
    /// Enumerates the behaviors active at the given severity: behaviors of the current stage
    /// first (so they override base behaviors of the same type), then the prototype's base behaviors.
    /// </summary>
    public IEnumerable<WoundBehavior> GetBehaviors(FixedPoint2 severity)
    {
        if (GetStageDefinition(severity) is { } stage)
        {
            foreach (var behavior in stage.Behaviors)
                yield return behavior;
        }

        foreach (var behavior in Behaviors)
            yield return behavior;
    }

    /// <summary>
    /// Severity-aware behavior lookup: prefers a behavior provided by the current stage over the
    /// prototype's base behavior of the same type. Falls back to the base behavior when the stage
    /// does not define one.
    /// </summary>
    public bool TryGetBehavior<T>(FixedPoint2 severity, out T behavior) where T : WoundBehavior
    {
        foreach (var candidate in GetBehaviors(severity))
        {
            if (candidate is T typed)
            {
                behavior = typed;
                return true;
            }
        }

        behavior = null!;
        return false;
    }

}

[DataDefinition]
public sealed partial class WoundDamageTypeSettings
{
    /// <summary>Minimum positive damage in one hit required to reopen a closed or stabilized wound.</summary>
    [DataField]
    public FixedPoint2 ReopenMinimumDamage;

    /// <summary>Wound severity gained or healed per point of damage.</summary>
    [DataField]
    public float SeverityMultiplier = 1f;
}

[Prototype]
public sealed partial class BodyPartProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Damage types that parts using this profile can receive. An empty set allows all damage types.
    /// Whether damage is localized is configured by WoundHostComponent instead.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> AcceptedDamageTypes = new();

    /// <summary>
    /// Wounds that may be created on body parts using this profile.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<WoundPrototype>> SupportedWounds = new();

    [DataField]
    public HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];

    [DataField]
    public ProtoId<CirculatoryStreamPrototype> CirculatoryStream = "Organic";

    /// <summary>
    /// Bleeding multiplier applied to every bleeding wound on parts with this profile.
    /// A non-positive value disables bleeding.
    /// </summary>
    [DataField]
    public float BleedingMultiplier = 1f;

    /// <summary>
    /// If false, wounds on parts with this profile cannot leave scars.
    /// </summary>
    [DataField]
    public bool Scarrable = true;

    [DataField]
    public bool CanFeelPain = true;

    /// <summary>Multiplier for negative damage applied by the entity's PassiveDamageComponent.</summary>
    [DataField]
    public float PassiveRecoveryMultiplier = 1f;

    /// <summary>Multiplier for negative damage applied by HealOnBuckleComponent.</summary>
    [DataField]
    public float BedRecoveryMultiplier = 1f;

    [DataField]
    public OrganDamageRouting OrganDamage = new();
}

/// <summary>
/// Configures whether damage to a part reaches its organs.
/// </summary>
[DataDefinition]
public sealed partial class OrganDamageRouting
{
    /// <summary>Chance an organ is damaged when a part of a given type takes damage.</summary>
    [DataField]
    public Dictionary<BodyPartType, float> Chances = new();

    /// <summary>How many organs are damaged on a successful roll (>= 1).</summary>
    [DataField]
    public int MaxAffected = 1;
}

[Serializable, NetSerializable]
public enum TreatmentCapability : byte
{
    Biological,
    Mechanical,
    Electrical,
}

[Prototype]
public sealed partial class FractureProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Blunt";

    [DataField]
    public ProtoId<WoundPrototype> Wound = "BoneFractureWound";

    [DataField]
    public float SeverityMultiplier = 1f;

    [DataField]
    public bool ResetTreatmentOnDamage = true;

    [DataField]
    public FixedPoint2 WorsenMinimumDamage;

    [DataField]
    public FixedPoint2 MinimumHitDamage;

    [DataField]
    public float AccumulationMultiplier;

    [DataField]
    public FractureGrade ReductionMinimumGrade = FractureGrade.Displaced;

    [DataField]
    public bool RemoveWoundWhenMended = true;

    [DataField]
    public ProtoId<AlertPrototype>? Alert = "BrokenBones";

    [DataField]
    public FractureGrade AlertMinimumGrade = FractureGrade.Simple;

    [DataField]
    public HashSet<FractureTreatment> AlertHiddenTreatments = [FractureTreatment.Mended];

    [DataField]
    public Dictionary<FractureTreatment, float> TreatmentEffectScales = new()
    {
        [FractureTreatment.None] = 1f,
        [FractureTreatment.Reduced] = 0.25f,
        [FractureTreatment.Mended] = 0f,
    };

    [DataField]
    public Dictionary<FractureGrade, FractureGradeSettings> Grades = new()
    {
        [FractureGrade.Hairline] = new(8, 0.9f, 1.1f),
        [FractureGrade.Simple] = new(15, 0.8f, 1.25f),
        [FractureGrade.Displaced] = new(25, 0.6f, 1.5f),
        [FractureGrade.Comminuted] = new(40, 0.4f, 2f),
    };
}

[DataDefinition]
public sealed partial class FractureGradeSettings
{
    [DataField(required: true)]
    public FixedPoint2 Threshold;

    [DataField]
    public float CreationChance = 1f;

    [DataField]
    public float MovementModifier = 1f;

    [DataField]
    public float ManipulationModifier = 1f;

    public FractureGradeSettings()
    {
    }

    public FractureGradeSettings(FixedPoint2 threshold, float movementModifier, float manipulationModifier)
    {
        Threshold = threshold;
        MovementModifier = movementModifier;
        ManipulationModifier = manipulationModifier;
    }
}

[Serializable, NetSerializable]
public enum WoundMergeMode : byte
{
    MergeByPrototype,
    SeparateInstances,
}

[Serializable, NetSerializable]
public enum WoundVisibility : byte
{
    Hidden,
    Visible,
}
