using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Reagents;

/// <summary>
/// What a painkiller is allowed to do to consciousness. Ordered: a body reports its strongest active tier.
/// </summary>
public enum WolfmedPainReliefTier : byte
{
    None = 0,

    /// <summary>Subtracts pain. Can lift Downed, never Unconscious.</summary>
    Weak = 1,

    /// <summary>
    /// Subtracts more, counts against the unconscious threshold too, masks the wound slowdowns and sedates.
    /// </summary>
    Strong = 2,

    /// <summary>Lifts Downed outright however bad the pain is, and nothing else. No crash.</summary>
    Stimulant = 3,

    /// <summary>Lifts Downed and Unconscious outright for its window, then crashes.</summary>
    Emergency = 4,
}

/// <summary>
/// One reagent's contribution. Doses stack across reagents and are keyed by reagent id. M2: the sedation a dose
/// asks for is a target (its units in the blood times the reagent's sedation per unit), not a rate.
/// </summary>
public record struct WolfmedPainReliefDose(
    WolfmedPainReliefTier Tier,
    float Strength,
    float SedationTarget,
    TimeSpan Ends);

/// <summary>
/// Painkillers in a body. Written by the <c>WolfmedPainRelief</c> metabolism effect, read by consciousness
/// (how much pain to discount and whether a tier may lift a state) and by the analyzer.
/// </summary>
/// <remarks>
/// The tuning below is the whole-body side of the model; the per-reagent side (tier, strength, sedation) is
/// on the reagent's own effect. Listing this component on a mob prototype overrides the tuning for it.
/// </remarks>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedPainReliefComponent : Component
{
    [ViewVariables]
    public Dictionary<string, WolfmedPainReliefDose> Doses = new();

    /// <summary>Strongest tier currently active. What the analyzer names.</summary>
    [AutoNetworkedField]
    public WolfmedPainReliefTier Tier;

    /// <summary>Pain units discounted from the Downed test (every tier).</summary>
    [AutoNetworkedField]
    public float Relief;

    /// <summary>Pain units discounted from the Unconscious test (Strong and up only).</summary>
    [AutoNetworkedField]
    public float StrongRelief;

    /// <summary>When the longest-lasting dose runs out. What the analyzer counts down.</summary>
    [AutoNetworkedField]
    public TimeSpan? Ends;

    /// <summary>End of an emergency window, while which pain decides nothing at all.</summary>
    [AutoNetworkedField]
    public TimeSpan? EmergencyEnds;

    /// <summary>End of the crash that follows an emergency window. Downed for its duration.</summary>
    [AutoNetworkedField]
    public TimeSpan? CrashEnds;

    /// <summary>0 to 1. Slows reactions, and past <see cref="SedationAirlossThreshold"/> depresses breathing.</summary>
    [AutoNetworkedField]
    public float Sedation;

    /// <summary>
    /// How much of every dose past the strongest one still counts. Relief is the strongest single dose plus
    /// this share of the rest, so a second and third painkiller help less and less instead of adding up.
    /// </summary>
    [DataField]
    public float StackShare = 0.35f;

    /// <summary>Sedation lost per second while it is above its target (M2: the target, not "no dose").</summary>
    [DataField]
    public float SedationDecayPerSecond = 0.035f;

    /// <summary>M2: an antagonist holds the sedation target at zero until this time.</summary>
    [ViewVariables]
    public TimeSpan? ReversalEnds;

    /// <summary>M2: the highest sedation warning the patient has been given since sedation last fell under it (0 none, 1-3).</summary>
    [ViewVariables]
    public int WarnedLevel;

    /// <summary>Movement speed multiplier at full sedation.</summary>
    [DataField]
    public float SedationSlowdown = 0.55f;

    /// <summary>Sedation past which breathing is depressed.</summary>
    [DataField]
    public float SedationAirlossThreshold = 0.6f;

    /// <summary>Pain is multiplied by this when an emergency window ends.</summary>
    [DataField]
    public float CrashPainMultiplier = 1.3f;

    /// <summary>How long the crash holds the body Downed, unless the other inputs say worse.</summary>
    [DataField]
    public TimeSpan CrashDuration = TimeSpan.FromSeconds(10);
}
