using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.WolfChem.Narcotics;

/// <summary>
/// The health analyzer's conditions readout: drug levels, status effects something is holding, crashes and
/// dependence, each with what clears it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFAnalyzerConditions
{
    public List<WFAnalyzerDrug> Drugs = new();
    public List<WFAnalyzerHeld> Held = new();
    public List<WFAnalyzerCrash> Crashes = new();
    public List<WFAnalyzerHabit> Habits = new();
}

/// <summary>
/// A reagent behind an effect: how much is in the body, about how long it takes to clear (negative when nothing
/// processes it), and the reagents that remove it faster.
/// </summary>
[Serializable, NetSerializable]
public sealed record WFAnalyzerCause(string Reagent, FixedPoint2 Quantity, float ClearSeconds, bool Stomach,
    List<string> RemovedBy);

/// <summary>A drug level, the reagents raising it and the antidotes that bring its family down.</summary>
[Serializable, NetSerializable]
public sealed record WFAnalyzerDrug(WFDrugFamily Family, WFDrugPhase Phase, List<WFAnalyzerCause> Causes,
    List<string> Antidotes);

/// <summary>What keeps a status effect going besides a reagent.</summary>
[Serializable, NetSerializable]
public enum WFAnalyzerSourceKind : byte
{
    High,
    Crash,
    Withdrawal,
}

[Serializable, NetSerializable]
public readonly record struct WFAnalyzerSource(WFAnalyzerSourceKind Kind, WFDrugFamily Family);

/// <summary>A status effect, its time left, and the reagents and conditions holding it.</summary>
[Serializable, NetSerializable]
public sealed record WFAnalyzerHeld(string Key, float SecondsLeft, List<WFAnalyzerCause> Causes,
    List<WFAnalyzerSource> Sources);

/// <summary>A running crash and its time left.</summary>
[Serializable, NetSerializable]
public readonly record struct WFAnalyzerCrash(WFDrugFamily Family, float SecondsLeft);

/// <summary>
/// Dependence on one family. <see cref="WithdrawalIn"/> is the time until withdrawal without a dose, and
/// <see cref="CleanIn"/> the time clean until the habit is too weak for withdrawal; each is negative when it
/// doesn't apply, and <see cref="CleanIn"/> is infinite when a trait keeps the habit up.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFAnalyzerHabit(WFDrugFamily Family, float Meter, float Floor, float Tolerance,
    int Stage, float WithdrawalIn, float CleanIn);
