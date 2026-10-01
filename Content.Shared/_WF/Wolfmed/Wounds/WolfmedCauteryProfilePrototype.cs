using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Thresholds and costs for burning a bleed shut. One shipped instance
/// (<see cref="WolfmedCauterySystem.DefaultProfile"/>); the prototype exists so a downstream server can
/// retune searing without a code change.
/// </summary>
[Prototype("wolfmedCauteryProfile")]
public sealed partial class WolfmedCauteryProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Heat in one hit below which nothing is hot enough for long enough to seal anything.</summary>
    [DataField]
    public FixedPoint2 MinIncidentalHeat = FixedPoint2.New(8);

    /// <summary>The burn a sealed bleed leaves behind. Null charges nothing.</summary>
    [DataField]
    public ProtoId<WoundPrototype>? BurnWound = "BurnWound";

    /// <summary>Burn severity per wound sealed by incidental heat.</summary>
    [DataField]
    public FixedPoint2 IncidentalBurn = FixedPoint2.New(5);

    /// <summary>Burn severity per wound sealed by a deliberate cautery, which is held on much longer.</summary>
    [DataField]
    public FixedPoint2 DeliberateBurn = FixedPoint2.New(14);

    /// <summary>Pain a deliberate cautery costs the part, on top of the burn.</summary>
    [DataField]
    public FixedPoint2 DeliberatePain = FixedPoint2.New(20);

    /// <summary>Do-after for holding a hot tool against someone else's wound.</summary>
    [DataField]
    public TimeSpan DeliberateDelay = TimeSpan.FromSeconds(4);

    /// <summary>Delay multiplier when the patient is doing it to themselves.</summary>
    [DataField]
    public float SelfMultiplier = 2f;

    /// <summary>Played at the patient when the hot tool is put against the wound.</summary>
    [DataField]
    public SoundSpecifier? DeliberateBeginSound = new SoundCollectionSpecifier("WFWolfmedCauteryBegin");

    /// <summary>Played when the bleed seals. Incidental heat gets nothing: the hit already made a noise.</summary>
    [DataField]
    public SoundSpecifier? DeliberateEndSound = new SoundCollectionSpecifier("WFWolfmedWoundBurn");
}
