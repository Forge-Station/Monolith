using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Every timer, threshold and dose in the infection, sepsis and necrosis model. One shipped instance
/// (<see cref="WolfmedInfectionSystem.DefaultProfile"/>); the CVars scale what is written here rather than
/// replacing it, so an admin tunes speed and a downstream server tunes shape.
/// </summary>
[Prototype("wolfmedInfectionProfile")]
public sealed partial class WolfmedInfectionProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    // Infection progress. One wound's contamination runs 0 to SepsisAt; the stage is read off the same number.

    /// <summary>Progress an untreated wound of risk 1 gains per minute.</summary>
    [DataField]
    public float ProgressPerMinute = 6f;

    /// <summary>Progress at which the wound itself starts to hurt and to widen.</summary>
    [DataField]
    public float LocalAt = 25f;

    /// <summary>Progress at which the infection leaves the wound: fever, and it infects its part.</summary>
    [DataField]
    public float SpreadingAt = 60f;

    /// <summary>The wound's top: Septic, where it infects its part twice as fast.</summary>
    [DataField]
    public float SepsisAt = 100f;

    /// <summary>Progress a cleaned wound sheds per minute while it is still only locally infected.</summary>
    [DataField]
    public float CleanDecayPerMinute = 20f;

    /// <summary>Progress an antibiotic clears per unit metabolised.</summary>
    [DataField]
    public float AntibioticPerUnit = 12f;

    /// <summary>
    /// Progress multiplier per bleeding treatment. A wound that is closed or dressed early is most of the
    /// prevention in the model; anything absent from this table counts as untreated.
    /// </summary>
    [DataField]
    public Dictionary<BleedingTreatment, float> TreatmentMultipliers = new()
    {
        [BleedingTreatment.Bandaged] = 0.15f,
        [BleedingTreatment.Clamped] = 0.5f,
        [BleedingTreatment.Sutured] = 0f,
        [BleedingTreatment.Cauterized] = 0f,
    };

    /// <summary>
    /// M1b (P20): progress multiplier for a wound with no bleeding treatment that is under a burn dressing or a
    /// graft (<see cref="WolfmedDressedComponent"/>). A burn has no bleed to bandage, so it used to count as open.
    /// </summary>
    [DataField]
    public float DressedMultiplier = 1f;

    /// <summary>Progress multiplier for a wound something dirty has been in: a knife digging a round out.</summary>
    [DataField]
    public float ContaminationMultiplier = 2.5f;

    // Local stage.

    /// <summary>Pain the part takes per minute while the wound is locally infected.</summary>
    [DataField]
    public FixedPoint2 LocalPainPerMinute = FixedPoint2.New(4);

    /// <summary>
    /// Severity the wound regains per minute while infected. Expresses "slower healing": treatment has to
    /// outrun the infection instead of being cancelled by it.
    /// </summary>
    [DataField]
    public FixedPoint2 SeverityPerMinute = FixedPoint2.New(1);

    /// <summary>Total severity one infection may ever add back, so a wound cannot grow without bound.</summary>
    [DataField]
    public FixedPoint2 MaxSeverityAdded = FixedPoint2.New(15);

    // Spreading stage.

    /// <summary>Body temperature a fever climbs towards, in kelvin.</summary>
    [DataField]
    public float FeverTemperature = 313f;

    /// <summary>Kelvin per second the fever climbs while it is below that.</summary>
    [DataField]
    public float FeverRise = 0.05f;

    // M5 (OD13): the spreading stage and sepsis dealt Poison here; they no longer do, so both fields are gone.

    // INFECTION: a part's own infection, 0 to 100 (100 is Septic). It travels towards the torso.

    /// <summary>Part progress at which the part itself hurts (the profile's LocalPainPerMinute on it).</summary>
    [DataField]
    public float PartLocalAt = 25f;

    /// <summary>Part progress at which the part runs a fever and infects its parent part.</summary>
    [DataField]
    public float PartSpreadingAt = 60f;

    /// <summary>Part progress per minute from each spreading wound on the part; a septic wound counts twice.</summary>
    [DataField]
    public float PartFromWoundPerMinute = 10f;

    /// <summary>Part progress per minute from each child part (a hand for its arm) at <see cref="PartTransferStage"/> or past it.</summary>
    [DataField]
    public float PartSpreadPerMinute = 8f;

    /// <summary>
    /// Playtest 5: the stage a part has to reach before it infects its parent. Septic, so a limb that is merely
    /// spreading (the first fever on the analyzer) is still safe to deal with and a septic limb is the amputation
    /// decision; a dead limb is pinned at 100 and transfers at once. Sepsis from a torso or head starts at spreading
    /// regardless: that is the bloodstream.
    /// </summary>
    [DataField]
    public WolfmedInfectionStage PartTransferStage = WolfmedInfectionStage.Septic;

    /// <summary>Part progress shed per minute while nothing feeds the part.</summary>
    [DataField]
    public float PartRecoveryPerMinute = 4f;

    /// <summary>Part progress an antibiotic clears per unit metabolised, off every part at once.</summary>
    [DataField]
    public float PartAntibioticPerUnit = 8f;

    // Sepsis.

    /// <summary>Sepsis progress gained per minute per source: a torso or head that is spreading or septic.</summary>
    [DataField]
    public float SepsisPerMinute = 12f;

    /// <summary>Sepsis progress shed per minute once every source is gone.</summary>
    [DataField]
    public float SepsisRecoveryPerMinute = 8f;

    /// <summary>Sepsis progress an antibiotic clears per unit metabolised.</summary>
    [DataField]
    public float SepsisAntibioticPerUnit = 6f;

    /// <summary>
    /// Playtest 4 (SEPSIS): multiplier on wolfmed.sepsis_organ_damage_per_minute per torso organ, keyed by the organ's
    /// slot. The filters go first: sepsis is a blood infection and the kidneys and liver clear it; the heart lasts
    /// longest. An organ whose slot is not listed takes the plain rate.
    /// </summary>
    [DataField]
    public Dictionary<string, float> SepsisOrganWeights = new()
    {
        ["kidneys"] = 1.5f,
        ["liver"] = 1.5f,
        ["lungs"] = 1f,
        ["stomach"] = 1f,
        ["heart"] = 0.75f,
    };

    // Necrosis.

    /// <summary>How long a tourniquet may stay on a limb before the limb under it dies.</summary>
    [DataField]
    public TimeSpan TourniquetOnset = TimeSpan.FromMinutes(10);

    /// <summary>Fraction of the onset at which the patient and the analyzer are warned.</summary>
    [DataField]
    public float WarnFraction = 0.6f;

    /// <summary>How long a severed limb stays viable. Reattached later than this, it is dead tissue.</summary>
    [DataField]
    public TimeSpan ReattachGrace = TimeSpan.FromMinutes(5);

    /// <summary>The wound dead tissue leaves on the part.</summary>
    [DataField]
    public ProtoId<WoundPrototype> NecrosisWound = "WFWolfmedNecrosisWound";

    /// <summary>Severity of that wound.</summary>
    [DataField]
    public FixedPoint2 NecrosisSeverity = FixedPoint2.New(60);
}
