using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10 keeps TargetBodyPart under _Shitmed; Onyx's _Onyx.Targeting copy is not ported.
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): W5
using Content.Shared._WF.Wolfmed.Reagents; // WOLFGATE(Wolfmed): CONSC
using Content.Shared._WF.Wolfmed.Life; // WOLFGATE(Wolfmed): M1a: the vitals block
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Medical;

[Serializable, NetSerializable]
public readonly record struct HealthAnalyzerWoundDiagnostic(
    FractureGrade Fracture,
    FractureTreatment FractureTreatment,
    float BleedingRate,
    BleedingTreatment BleedingTreatment,
    ushort ScarCount,
    FixedPoint2 Pain,
    List<HealthAnalyzerVisibleWound> VisibleWounds,
    BodyPartFunctionalityState Functionality,
    float InternalBleedingRate,
    HealthAnalyzerClottingPhase ClottingPhase, // WOLFGATE(Wolfmed): Onyx's last parameter, the additions follow it.
    ushort EmbeddedObjects = 0, // WOLFGATE(Wolfmed): W1: rounds and shrapnel still in the part.
    WolfmedInfectionStage Infection = WolfmedInfectionStage.None, // WOLFGATE(Wolfmed): W5: worst stage on the part.
    bool Necrotic = false, // WOLFGATE(Wolfmed): W5: the part is dead tissue.
    bool NecrosisRisk = false, // WOLFGATE(Wolfmed): W5: a tourniquet or a deep burn is killing it.
    bool Mechanical = false, // WOLFGATE(Wolfmed): W6: a chassis, so the generic labels get their -mechanical variants.
    bool Overheating = false, // WOLFGATE(Wolfmed): W6: the part is running too hot to work properly.
    // WOLFGATE(Wolfmed): UI4: what has already been done here. A clamped, sutured or cauterised wound bleeds at
    // zero, so nothing else in this payload can tell a treated part from an untouched one.
    WolfmedPartTreatments Treatments = WolfmedPartTreatments.None,
    // WOLFGATE(Wolfmed): EVISC: organ slots this part carries that nothing is in, which is how the procedure
    // window can tell a patient whose organs are back in from one still waiting for them.
    ushort MissingOrgans = 0,
    // WOLFGATE(Wolfmed): INFECTION: the part's own infection, which travels towards the torso with no wound on it.
    WolfmedInfectionStage PartInfection = WolfmedInfectionStage.None)
{
    // WOLFGATE(Wolfmed) START: the W1, W5, W6 and INFECTION findings count as findings too.
    // public bool HasFindings =>
    //     Fracture != FractureGrade.None || BleedingRate > 0f || ScarCount > 0 || Pain > FixedPoint2.Zero ||
    //     VisibleWounds.Count > 0 || Functionality != BodyPartFunctionalityState.Functional || InternalBleedingRate > 0f;
    public bool HasFindings =>
        Fracture != FractureGrade.None || BleedingRate > 0f || ScarCount > 0 || Pain > FixedPoint2.Zero ||
        VisibleWounds.Count > 0 || Functionality != BodyPartFunctionalityState.Functional ||
        InternalBleedingRate > 0f || EmbeddedObjects > 0 ||
        Infection != WolfmedInfectionStage.None || Necrotic || NecrosisRisk ||
        Overheating || PartInfection != WolfmedInfectionStage.None;
    // WOLFGATE END
}

[Serializable, NetSerializable]
// WOLFGATE(Wolfmed) START: UI2 and UI3 add the category and the prototype id to Onyx's visible wound.
// public readonly record struct HealthAnalyzerVisibleWound(LocId Name, LocId? StageName, int Count);
public readonly record struct HealthAnalyzerVisibleWound(
    LocId Name,
    LocId? StageName,
    int Count,
    // UI2: the analyzer groups and tints its wound rows by this; resolved from the prototype.
    WolfmedWoundCategory Category = WolfmedWoundCategory.Other,
    // UI3: the wound prototype id, which is what the treatment advice keys are derived from.
    string Prototype = "");
// WOLFGATE END

[Serializable, NetSerializable]
public enum HealthAnalyzerClottingPhase : byte
{
    NotApplicable,
    None,
    InProgress,
    Complete,
    Mixed,
}

[Serializable, NetSerializable]
public sealed class HealthAnalyzerWoundDiagnostics
{
    public readonly Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> Parts;

    // WOLFGATE(Wolfmed) START: W5, CONSC, BRAIN and M1a body-level readouts beside the parts.
    /// <summary>WOLFGATE (W5): systemic infection, 0 to 100. Body-level, so it sits beside the parts.</summary>
    public readonly float Sepsis;

    /// <summary>WOLFGATE (CONSC): strongest painkiller tier in the patient, or None.</summary>
    public readonly WolfmedPainReliefTier PainRelief;

    /// <summary>WOLFGATE (CONSC): seconds of pain relief left.</summary>
    public readonly float PainReliefSeconds;

    /// <summary>WOLFGATE (CONSC): sedation, 0 to 1. Past its threshold the patient stops breathing.</summary>
    public readonly float Sedation;

    /// <summary>WOLFGATE (BRAIN): the heart has stopped. No pulse, and the clock below is running.</summary>
    public readonly bool CardiacArrest;

    /// <summary>WOLFGATE (BRAIN): the brain organ is destroyed. The body is dead and needs surgery first.</summary>
    public readonly bool BrainDead;

    /// <summary>WOLFGATE (BRAIN): brain activity, 0 to 1, or -1 when there is no brain to read.</summary>
    public readonly float BrainActivity;

    /// <summary>WOLFGATE (BRAIN): brain oxygenation, 0 to 1, or -1 when there is no brain to read.</summary>
    public readonly float Oxygenation;

    /// <summary>WOLFGATE (BRAIN): seconds until brain death at the current drain, or -1 when nothing drains.</summary>
    public readonly float BrainDeathSeconds;

    /// <summary>WOLFGATE (BRAIN): a machine body with no power or no pump.</summary>
    public readonly bool Shutdown;

    /// <summary>
    /// WOLFGATE(Wolfmed): M1a: units to transfuse after a successful shock.
    /// Given inside the grace, they keep the heart going (plan §7.1); -1 when there is no post-shock advice to show.
    /// </summary>
    public readonly float PostShockUnits;

    /// <summary>WOLFGATE (M1a): units to the blood line where the brain stops draining.</summary>
    public readonly float PostShockSafeUnits;

    /// <summary>WOLFGATE (M1a): seconds left on the post-shock grace; 0 once it has run out.</summary>
    public readonly float PostShockGraceSeconds;

    /// <summary>WOLFGATE (M1a): that blood line, as a percentage.</summary>
    public readonly float PostShockSafeLine;

    /// <summary>WOLFGATE (M1a): the vitals block: state and cause, breathing, circulation, defib verdict (plan §5.5).</summary>
    public readonly WolfmedVitalsReport? Vitals;

    /// <summary>WOLFGATE(Wolfmed): playtest 5: seconds left on a repaired brain's trauma, or -1 when there is none.</summary>
    public readonly float BrainTraumaSeconds;
    // WOLFGATE END

    // WOLFGATE(Wolfmed) START: W5, CONSC, BRAIN and M1a append optional parameters to the Onyx constructor.
    // public HealthAnalyzerWoundDiagnostics(Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> parts)
    public HealthAnalyzerWoundDiagnostics(
        Dictionary<TargetBodyPart, HealthAnalyzerWoundDiagnostic> parts,
    // WOLFGATE END
        float sepsis = 0f, // WOLFGATE(Wolfmed): W5
        WolfmedPainReliefTier painRelief = WolfmedPainReliefTier.None, // WOLFGATE(Wolfmed): CONSC
        float painReliefSeconds = 0f, // WOLFGATE(Wolfmed): CONSC
        float sedation = 0f, // WOLFGATE(Wolfmed): CONSC
        bool cardiacArrest = false, // WOLFGATE(Wolfmed): BRAIN
        bool brainDead = false, // WOLFGATE(Wolfmed): BRAIN
        float brainActivity = -1f, // WOLFGATE(Wolfmed): BRAIN
        float oxygenation = -1f, // WOLFGATE(Wolfmed): BRAIN
        float brainDeathSeconds = -1f, // WOLFGATE(Wolfmed): BRAIN
        bool shutdown = false, // WOLFGATE(Wolfmed): BRAIN
        float postShockUnits = -1f, // WOLFGATE(Wolfmed): M1a
        float postShockSafeUnits = 0f, // WOLFGATE(Wolfmed): M1a
        float postShockGraceSeconds = 0f, // WOLFGATE(Wolfmed): M1a
        float postShockSafeLine = 0f, // WOLFGATE(Wolfmed): M1a
        WolfmedVitalsReport? vitals = null, // WOLFGATE(Wolfmed): M1a: package D's vitals block
        float brainTraumaSeconds = -1f) // WOLFGATE(Wolfmed): playtest 5, the repaired brain's trauma
    {
        Parts = parts;
        Sepsis = sepsis; // WOLFGATE(Wolfmed): W5
        PainRelief = painRelief; // WOLFGATE(Wolfmed): CONSC
        PainReliefSeconds = painReliefSeconds; // WOLFGATE(Wolfmed): CONSC
        Sedation = sedation; // WOLFGATE(Wolfmed): CONSC
        CardiacArrest = cardiacArrest; // WOLFGATE(Wolfmed): BRAIN
        BrainDead = brainDead; // WOLFGATE(Wolfmed): BRAIN
        BrainActivity = brainActivity; // WOLFGATE(Wolfmed): BRAIN
        Oxygenation = oxygenation; // WOLFGATE(Wolfmed): BRAIN
        BrainDeathSeconds = brainDeathSeconds; // WOLFGATE(Wolfmed): BRAIN
        Shutdown = shutdown; // WOLFGATE(Wolfmed): BRAIN
        PostShockUnits = postShockUnits; // WOLFGATE(Wolfmed): M1a
        PostShockSafeUnits = postShockSafeUnits; // WOLFGATE(Wolfmed): M1a
        PostShockGraceSeconds = postShockGraceSeconds; // WOLFGATE(Wolfmed): M1a
        PostShockSafeLine = postShockSafeLine; // WOLFGATE(Wolfmed): M1a
        Vitals = vitals; // WOLFGATE(Wolfmed): M1a
        BrainTraumaSeconds = brainTraumaSeconds; // WOLFGATE(Wolfmed): playtest 5
    }
}
