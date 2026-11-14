using Content.Shared._Onyx.Medical;
using Content.Shared._Onyx.Wounds;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// UI4: what a procedure step can look at to decide it has been done. Every member is answerable from the
/// analyzer payload for one body part plus the two facts the payload does not carry about the part
/// (whether the medic is aiming at it, and whether the finding the window was opened for is still there).
/// </summary>
public enum WolfmedStepCheck : byte
{
    /// <summary>The local player's targeting doll is on this part.</summary>
    PartTargeted,

    /// <summary>The part is no longer losing blood or fluid.</summary>
    BleedingStopped,

    /// <summary>Something is holding the bleeding: gauze, a tourniquet, sutures or a cautery.</summary>
    Dressed,

    /// <summary>The wound is closed, by sutures or by a cautery.</summary>
    Sutured,

    /// <summary>A tourniquet is on the part.</summary>
    Tourniqueted,

    /// <summary>Antiseptic has been on an open wound here and nothing dirty has happened since.</summary>
    Cleaned,

    /// <summary>Nothing is lodged in the part any more.</summary>
    EmbeddedRemoved,

    /// <summary>The bone is set: splinted or mended. A part with no fracture counts.</summary>
    FractureReduced,

    /// <summary>The part reports no fracture at all.</summary>
    FractureMended,

    /// <summary>The part carries no infection, in a wound or (INFECTION) in its own tissue.</summary>
    InfectionCleared,

    /// <summary>The part is no longer bleeding internally.</summary>
    InternalBleedingStopped,

    /// <summary>The part is no longer running hot.</summary>
    Cooled,

    /// <summary>Circulation is no longer failing and the tissue is not dead.</summary>
    NecrosisRiskCleared,

    /// <summary>The part works normally again.</summary>
    FunctionRestored,

    /// <summary>The wound or condition the window was opened for is no longer reported.</summary>
    SubjectGone,

    /// <summary>The body carries no systemic infection.</summary>
    SepsisCleared,

    /// <summary>Blood is back above the analyzer's danger line.</summary>
    BloodRestored,

    /// <summary>Every organ slot on the part is filled again.</summary>
    OrgansRestored,

    /// <summary>The heart is beating again.</summary>
    PulseRestored,

    /// <summary>The brain organ reads some activity, so a defibrillator has something to restart.</summary>
    BrainRepaired,
}

/// <summary>
/// UI4: what has already been done to a body part, as distinct from what is still wrong with it. The rest
/// of the payload cannot answer this: a clamped, sutured or cauterised wound bleeds at zero, so it reports
/// exactly like a part nobody has touched.
/// </summary>
[Flags]
[Serializable, NetSerializable]
public enum WolfmedPartTreatments : byte
{
    None = 0,
    Bandaged = 1 << 0,
    Tourniqueted = 1 << 1,
    Sutured = 1 << 2,
    Cauterized = 1 << 3,

    /// <summary>Antiseptic has reached an open wound here.</summary>
    Cleaned = 1 << 4,
}

/// <summary>Everything a step check may read, in one value, so the evaluator stays pure.</summary>
/// <remarks>
/// <paramref name="Part"/> is null for the two body-level procedures (sepsis, low blood), which are opened
/// from a banner rather than from a part card. Every part-level check reads false without one.
/// </remarks>
public readonly record struct WolfmedProcedureState(
    HealthAnalyzerWoundDiagnostic? Part,
    bool PartTargeted = false,
    bool SubjectPresent = true,
    float Sepsis = 0f,
    float BloodLevel = 1f,
    // BRAIN: the two body-level vitals, for the arrest and brain-death procedures.
    bool CardiacArrest = false,
    float BrainActivity = -1f);

/// <summary>
/// UI4: the pure half of the procedure window. No entities, no IoC, no client types, so the truth table
/// for every check can be unit tested without starting a game.
/// </summary>
public static class WolfmedStepChecks
{
    /// <summary>The blood fraction the analyzer calls dangerous. The wounds tab's banner uses the same line.</summary>
    public const float DangerousBloodLevel = 0.65f;

    /// <summary>A step is done when every one of its checks holds. A step with no checks is never done.</summary>
    public static bool IsDone(List<WolfmedStepCheck> checks, in WolfmedProcedureState state)
    {
        if (checks.Count == 0)
            return false;

        foreach (var check in checks)
        {
            if (!IsDone(check, state))
                return false;
        }

        return true;
    }

    public static bool IsDone(WolfmedStepCheck check, in WolfmedProcedureState state)
    {
        switch (check)
        {
            case WolfmedStepCheck.PartTargeted:
                return state.PartTargeted;
            case WolfmedStepCheck.SubjectGone:
                return !state.SubjectPresent;
            case WolfmedStepCheck.SepsisCleared:
                return state.Sepsis <= 0f;
            case WolfmedStepCheck.BloodRestored:
                return float.IsNaN(state.BloodLevel) || state.BloodLevel >= DangerousBloodLevel;
            case WolfmedStepCheck.PulseRestored:
                return !state.CardiacArrest;
            case WolfmedStepCheck.BrainRepaired:
                return state.BrainActivity > 0f;
        }

        if (state.Part is not { } part)
            return false;

        return check switch
        {
            WolfmedStepCheck.BleedingStopped => part.BleedingRate <= 0f,
            // Gauze leaves the wound bleeding at a quarter, so "dressed" is a treatment being on it, or
            // there being nothing left to dress.
            WolfmedStepCheck.Dressed => part.Treatments != WolfmedPartTreatments.None || part.BleedingRate <= 0f,
            WolfmedStepCheck.Sutured => Has(part, WolfmedPartTreatments.Sutured | WolfmedPartTreatments.Cauterized),
            WolfmedStepCheck.Tourniqueted => Has(part, WolfmedPartTreatments.Tourniqueted),
            WolfmedStepCheck.Cleaned => Has(part, WolfmedPartTreatments.Cleaned),
            WolfmedStepCheck.EmbeddedRemoved => part.EmbeddedObjects == 0,
            WolfmedStepCheck.FractureReduced =>
                part.Fracture == FractureGrade.None || part.FractureTreatment != FractureTreatment.None,
            WolfmedStepCheck.FractureMended => part.Fracture == FractureGrade.None,
            // INFECTION: neither a wound on the part nor the part's own tissue.
            WolfmedStepCheck.InfectionCleared => part.Infection == WolfmedInfectionStage.None &&
                                                 part.PartInfection == WolfmedInfectionStage.None,
            WolfmedStepCheck.InternalBleedingStopped => part.InternalBleedingRate <= 0f,
            WolfmedStepCheck.Cooled => !part.Overheating,
            WolfmedStepCheck.NecrosisRiskCleared => !part.NecrosisRisk && !part.Necrotic,
            WolfmedStepCheck.FunctionRestored => part.Functionality == BodyPartFunctionalityState.Functional,
            WolfmedStepCheck.OrgansRestored => part.MissingOrgans == 0,
            _ => false,
        };
    }

    /// <summary>The flag a bleeding wound's treatment contributes to the part's summary.</summary>
    public static WolfmedPartTreatments Flag(BleedingTreatment treatment) => treatment switch
    {
        BleedingTreatment.Bandaged => WolfmedPartTreatments.Bandaged,
        BleedingTreatment.Clamped => WolfmedPartTreatments.Tourniqueted,
        BleedingTreatment.Sutured => WolfmedPartTreatments.Sutured,
        BleedingTreatment.Cauterized => WolfmedPartTreatments.Cauterized,
        _ => WolfmedPartTreatments.None,
    };

    private static bool Has(in HealthAnalyzerWoundDiagnostic part, WolfmedPartTreatments any) =>
        (part.Treatments & any) != WolfmedPartTreatments.None;
}
