using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Hud;

/// <summary>
/// One row of the wound-to-readout table the synthetic HUD is built from. A line is keyed either on a wound
/// prototype or on a body/part condition, never on both, so nothing in the readout is a hardcoded string.
/// </summary>
[Prototype("syntheticHudLine")]
public sealed partial class WolfmedSyntheticHudLinePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The wound this line describes. Null on a condition line.</summary>
    [DataField]
    public ProtoId<WoundPrototype>? Wound;

    /// <summary>The condition this line describes. <see cref="WolfmedSyntheticCondition.None"/> on a wound line.</summary>
    [DataField]
    public WolfmedSyntheticCondition Condition = WolfmedSyntheticCondition.None;

    /// <summary>Locale key for the readout text, two or three words in caps.</summary>
    [DataField(required: true)]
    public LocId Line;

    /// <summary>Tag the line carries until <see cref="EscalateAt"/> is reached.</summary>
    [DataField]
    public WolfmedSyntheticSeverity Severity = WolfmedSyntheticSeverity.Warn;

    /// <summary>Wound severity at which the tag becomes <see cref="Escalated"/>. Zero never escalates.</summary>
    [DataField]
    public FixedPoint2 EscalateAt = FixedPoint2.Zero;

    /// <summary>Tag from <see cref="EscalateAt"/> upward.</summary>
    [DataField]
    public WolfmedSyntheticSeverity Escalated = WolfmedSyntheticSeverity.Crit;

    /// <summary>Locale key for the one-line advice the SYSTEM block shows when this is the worst fault.</summary>
    [DataField]
    public LocId Advice;

    /// <summary>Wound severity below which nothing is reported at all.</summary>
    [DataField]
    public FixedPoint2 MinSeverity = FixedPoint2.Zero;
}
