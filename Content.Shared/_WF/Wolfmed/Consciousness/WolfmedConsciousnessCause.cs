using Content.Shared.Alert;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// The input that currently sets a wound host's state (M1a, plan §5.1). Alerts, messages, the synthetic HUD,
/// the analyzer and examine all read it. Later milestones append values; never renumber.
/// </summary>
[Serializable, NetSerializable]
public enum WolfmedCause : byte
{
    None = 0,
    Pain = 1,

    /// <summary>A pain faint: Critical for a fixed time, then Downed.</summary>
    PainFaint = 2,
    Blood = 3,

    /// <summary>The blood input on a mechanical body: hydraulic oil.</summary>
    Oil = 4,
    Hypoxia = 5,
    Sedation = 6,
    Legs = 7,
    Crash = 8,
    Arrest = 9,

    /// <summary>A mechanical body with no power or no pump.</summary>
    Shutdown = 10,

    /// <summary>An outside pressure no cause claims: admin and test keys.</summary>
    Other = 11,

    /// <summary>M3: a heavy blow to the head. A faint: Critical for a fixed few seconds.</summary>
    HeadBlow = 12,

    /// <summary>M3: a brain under a quarter of its health holds the patient Downed.</summary>
    Brain = 13,

    /// <summary>M3: the same for a chassis's positronic core.</summary>
    Core = 14,

    /// <summary>
    /// M4: thermal shutdown. A machine's positronic core past its heat line is losing health; Dying, like arrest.
    /// Numbered 20 so the causes M5 adds in parallel keep 15 upwards.
    /// </summary>
    CoreHeat = 20,

    // M5: 15 is left for M4's CoreHeat.

    /// <summary>M5: systemic Poison past its lines: Downed, then a toxic coma that drains the brain.</summary>
    Toxin = 16,

    /// <summary>M5: radiation sickness. Downed only; it kills through the blood (the marrow route).</summary>
    Radiation = 17,

    /// <summary>M5: a core temperature under the hypothermia lines: Downed, then Unconscious. Arrest is its own trigger.</summary>
    Cold = 18,

    /// <summary>M5: a core temperature over the heat lines: heat exhaustion (Downed), then heat stroke (Unconscious).</summary>
    Heat = 19,

    /// <summary>
    /// Playtest 5: septic shock, sepsis past wolfmed.septic_shock_at. Unconscious until antibiotics pull the sepsis
    /// under the line. Not Dying: only the organ damage that follows kills, through the failing lungs.
    /// </summary>
    SepticShock = 21,
}

/// <summary>One bit per <see cref="WolfmedCause"/>: 1 shifted by the cause's value.</summary>
[Flags, Serializable, NetSerializable]
public enum WolfmedCauseFlags : uint
{
    None = 0,
    Pain = 1u << (int) WolfmedCause.Pain,
    PainFaint = 1u << (int) WolfmedCause.PainFaint,
    Blood = 1u << (int) WolfmedCause.Blood,
    Oil = 1u << (int) WolfmedCause.Oil,
    Hypoxia = 1u << (int) WolfmedCause.Hypoxia,
    Sedation = 1u << (int) WolfmedCause.Sedation,
    Legs = 1u << (int) WolfmedCause.Legs,
    Crash = 1u << (int) WolfmedCause.Crash,
    Arrest = 1u << (int) WolfmedCause.Arrest,
    Shutdown = 1u << (int) WolfmedCause.Shutdown,
    Other = 1u << (int) WolfmedCause.Other,
    HeadBlow = 1u << (int) WolfmedCause.HeadBlow,
    Brain = 1u << (int) WolfmedCause.Brain,
    Core = 1u << (int) WolfmedCause.Core,
    CoreHeat = 1u << (int) WolfmedCause.CoreHeat, // M4

    Toxin = 1u << (int) WolfmedCause.Toxin, // M5
    Radiation = 1u << (int) WolfmedCause.Radiation,
    Cold = 1u << (int) WolfmedCause.Cold,
    Heat = 1u << (int) WolfmedCause.Heat,
    SepticShock = 1u << (int) WolfmedCause.SepticShock, // Playtest 5
}

/// <summary>
/// What is behind a cause that has more than one route in: the hypoxia drain, the arrest trigger, or the
/// reason a machine shut down.
/// </summary>
[Serializable, NetSerializable]
public enum WolfmedCauseSource : byte
{
    None = 0,

    // Hypoxia: the largest drain on the brain.
    Airway = 1,
    Lungs = 2,
    Circulation = 3,
    Sepsis = 4,
    Sedation = 5,

    // M5: a toxic coma and heat stroke drain the brain too.
    Toxin = 10,
    Heat = 11,

    // Arrest: what stopped the heart.
    ArrestBlood = 20,
    ArrestOxygen = 21,
    ArrestHeart = 22,
    ArrestSepsis = 23,
    ArrestShock = 24,
    ArrestOther = 25,

    // M5: the cold trigger, and the oxygen trigger with a toxic coma or heat stroke behind it.
    ArrestCold = 30,
    ArrestToxin = 31,
    ArrestHeat = 32,

    // Shutdown: why the machine stopped.
    Power = 40,
    Pump = 41,
}

/// <summary>Helpers for <see cref="WolfmedCause"/>: tie order, flags and prototype ids.</summary>
public static class WolfmedCauses
{
    /// <summary>
    /// Which cause names the state when several meet its line (plan §5.1), first wins. Causes from later
    /// milestones slot in where the plan's tie order puts them.
    /// </summary>
    public static readonly WolfmedCause[] Priority =
    {
        WolfmedCause.Arrest,
        WolfmedCause.CoreHeat, // M4
        WolfmedCause.Shutdown,
        WolfmedCause.Blood,
        WolfmedCause.Oil,
        WolfmedCause.Hypoxia,
        WolfmedCause.Sedation,
        WolfmedCause.Toxin, // M5
        WolfmedCause.SepticShock, // Playtest 5
        WolfmedCause.Cold,
        WolfmedCause.Heat,
        WolfmedCause.Radiation,
        WolfmedCause.Other,
        WolfmedCause.Brain,
        WolfmedCause.Core,
        WolfmedCause.HeadBlow,
        WolfmedCause.PainFaint,
        WolfmedCause.Pain,
        WolfmedCause.Legs,
        WolfmedCause.Crash,
    };

    public static WolfmedCauseFlags Flag(WolfmedCause cause) =>
        cause == WolfmedCause.None ? WolfmedCauseFlags.None : (WolfmedCauseFlags) (1u << (int) cause);

    /// <summary>The blockers as causes, in tie order.</summary>
    public static IEnumerable<WolfmedCause> Each(WolfmedCauseFlags flags)
    {
        foreach (var cause in Priority)
        {
            if ((flags & Flag(cause)) != 0)
                yield return cause;
        }
    }

    /// <summary>A faint: unconscious for a fixed time, not for as long as a cause lasts. M3 adds the head blow.</summary>
    public static bool IsFaint(WolfmedCause cause) => cause is WolfmedCause.PainFaint or WolfmedCause.HeadBlow;

    /// <summary>M4 (OD16): the cause prototype a heartless species' arrest reads instead of Arrest's.</summary>
    public const string CirculatoryCollapse = "CirculatoryCollapse";

    /// <summary>
    /// M4: the cause prototype's id. The enum name, except that a heartless body's arrest is circulatory collapse:
    /// the same state and routes, told in its own words.
    /// </summary>
    public static string PrototypeId(WolfmedCause cause, bool heartless) =>
        heartless && cause == WolfmedCause.Arrest ? CirculatoryCollapse : cause.ToString();
}

/// <summary>
/// Everything a cause says to the patient (plan §5.1). One per <see cref="WolfmedCause"/>, with the enum
/// name as its id. Nothing here promises waking or standing unconditionally: <see cref="HelpBlocked"/> is
/// the form shown while something else also holds the body.
/// </summary>
[Prototype("wolfmedConsciousnessCause")]
public sealed partial class WolfmedConsciousnessCausePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Alert while this cause holds the body Downed.</summary>
    [DataField]
    public ProtoId<AlertPrototype>? AlertDowned;

    /// <summary>Alert while this cause holds the body Critical: faint, unconscious, shutdown or arrest.</summary>
    [DataField]
    public ProtoId<AlertPrototype>? AlertOut;

    /// <summary>Downed alert on a mechanical body, when its wording has to differ.</summary>
    [DataField]
    public ProtoId<AlertPrototype>? AlertDownedMechanical;

    /// <summary>Short noun: "blood loss". Used in "Still holding you down: …" and the analyzer's state line.</summary>
    [DataField(required: true)]
    public LocId BlockerName;

    /// <summary>Title for the Downed state: "Downed: blood loss".</summary>
    [DataField]
    public LocId? TitleDowned;

    /// <summary>Title for the Critical state: "Unconscious: blood loss", "Passed out: pain".</summary>
    [DataField]
    public LocId? TitleOut;

    /// <summary>What the patient feels, as a symptom.</summary>
    [DataField]
    public LocId? Symptom;

    /// <summary>What will help while Downed, in a form that makes no unconditional promise.</summary>
    [DataField]
    public LocId? Help;

    /// <summary>The form of <see cref="Help"/> shown while something else also holds the body.</summary>
    [DataField]
    public LocId? HelpBlocked;

    /// <summary>What will help while Critical: what wakes the patient, if anything does.</summary>
    [DataField]
    public LocId? HelpOut;

    /// <summary>The form of <see cref="HelpOut"/> shown while something else also holds the body.</summary>
    [DataField]
    public LocId? HelpOutBlocked;

    /// <summary>
    /// A faint's <see cref="HelpOut"/> with the seconds left (<c>$seconds</c>), shown while nothing else holds the
    /// body. Playtest 2 wrote it for the pain faint; M3 moved it into data for the head blow.
    /// </summary>
    [DataField]
    public LocId? HelpOutTimed;

    /// <summary><see cref="Help"/> on a mechanical body, when it has to differ.</summary>
    [DataField]
    public LocId? HelpMechanical;

    /// <summary>Transition line on going Downed from this cause.</summary>
    [DataField]
    public LocId? Down;

    /// <summary>Transition line on going Critical from this cause.</summary>
    [DataField]
    public LocId? Out;

    /// <summary>Transition line on coming round to Downed with this cause still holding the body.</summary>
    [DataField]
    public LocId? Wake;

    /// <summary>Transition line on standing up when this cause was the one holding the body down.</summary>
    [DataField]
    public LocId? Stand;

    /// <summary>The synthetic HUD's banner line for this cause (IPC). Takes <c>$source</c> when it has one.</summary>
    [DataField]
    public LocId? SyntheticHudLine;

    /// <summary>A cause that ends in death on its own: only these offer Succumb (plan §5.4).</summary>
    [DataField]
    public bool Dying;

    /// <summary>Short names for the sub-sources, for the titles' <c>$source</c>.</summary>
    [DataField]
    public Dictionary<WolfmedCauseSource, LocId> Sources = new();

    /// <summary>
    /// Playtest 3: the explanation card's accent stripe while this cause holds the body. Null takes the card's
    /// default (<see cref="WolfmedExplanationCard.DefaultColour"/>).
    /// </summary>
    [DataField]
    public Color? CardColour;
}

/// <summary>
/// Raised directed on the body when its consciousness state, cause or blockers change. Wolfmed's own event,
/// so readers do not need a second subscription on an engine pair that is already taken.
/// </summary>
[ByRefEvent]
public readonly record struct WolfmedConsciousnessChangedEvent(
    WolfmedConsciousness OldState,
    WolfmedConsciousness NewState,
    WolfmedCause OldCause,
    WolfmedCause NewCause,
    WolfmedCauseFlags OldBlockers,
    WolfmedCauseFlags NewBlockers);

/// <summary>Clicking a condition alert: the full text, blockers included, as a popup and a chat line.</summary>
public sealed partial class WolfmedConditionAlertEvent : BaseAlertEvent;
