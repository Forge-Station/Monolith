using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>Maps a hit to a wound other than the default Onyx wound for its damage type.</summary>
// Rules run in descending Priority; the first whose filters and chance roll pass wins, and evaluation stops there
// unless it sets Continue. An empty filter matches everything.
[Prototype("wolfmedWoundRule")]
public sealed partial class WolfmedWoundRulePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The wound created when this rule wins. Normally carries <c>ruleOnly: true</c>.</summary>
    [DataField(required: true)]
    public ProtoId<WoundPrototype> Wound;

    /// <summary>Damage types this rule answers for.</summary>
    [DataField]
    public HashSet<ProtoId<DamageTypePrototype>> DamageTypes = new();

    /// <summary>Any one of these cause sets overlapping the hit's causes is a match.</summary>
    [DataField]
    public List<WolfmedWoundCause> Causes = new();

    /// <summary>Per-hit damage of that type, after armour and resistances.</summary>
    [DataField]
    public FixedPoint2 MinDamage = FixedPoint2.Zero;

    [DataField]
    public FixedPoint2? MaxDamage;

    /// <summary>Body part types this rule applies to.</summary>
    [DataField]
    public HashSet<BodyPartType> PartTypes = new();

    /// <summary>Part profile capabilities the part must have: <c>[Biological]</c> for flesh, <c>[Mechanical]</c> for chassis.</summary>
    [DataField]
    public HashSet<TreatmentCapability> Capabilities = new();

    [DataField]
    public float Chance = 1f;

    [DataField]
    public int Priority;

    /// <summary>Wound severity per point of damage, mirroring <see cref="WoundDamageTypeSettings.SeverityMultiplier"/>.</summary>
    [DataField]
    public float SeverityMultiplier = 1f;

    /// <summary>Severity floor, so a graze that scratches still registers.</summary>
    [DataField]
    public FixedPoint2 MinSeverity = FixedPoint2.Zero;

    /// <summary>Whether the default Onyx wound for this damage type is skipped for this hit.</summary>
    [DataField]
    public bool ReplacesDefault = true;

    /// <summary>Keep evaluating lower-priority rules after this one matched, so one hit can make several wounds.</summary>
    [DataField]
    public bool Continue;

    /// <summary>Objects left inside the wound. Removed with forceps or a sharp item.</summary>
    [DataField]
    public WolfmedEmbeddedSpec? Embedded;
}

/// <summary>How many objects a rule leaves in the wound and what they spawn as when pulled out.</summary>
[DataDefinition]
public sealed partial class WolfmedEmbeddedSpec
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public int MinCount = 1;

    [DataField]
    public int MaxCount = 1;

    /// <summary>Cap on what one wound can hold, so repeated hits do not stack forever.</summary>
    [DataField]
    public int MaxTotal = 6;
}
