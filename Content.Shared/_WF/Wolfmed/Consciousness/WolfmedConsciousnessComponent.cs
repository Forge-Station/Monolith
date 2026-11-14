using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Alert;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>How awake a wound host is. Replaces the damage thresholds' Alive/Critical decision (CONSC).</summary>
public enum WolfmedConsciousness : byte
{
    /// <summary>On their feet, no restrictions.</summary>
    Up = 0,

    /// <summary>Conscious but on the floor: can crawl, talk and use items on themselves only.</summary>
    Downed = 1,

    /// <summary>MobState.Critical. Nothing.</summary>
    Unconscious = 2,
}

/// <summary>
/// Consciousness of a wound host. Ensured on every wound host at startup; the server owns State and Depth,
/// the client reads them for the Downed alert and the dying view.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedConsciousnessComponent : Component
{
    [AutoNetworkedField]
    public WolfmedConsciousness State = WolfmedConsciousness.Up;

    /// <summary>
    /// How far gone the body is, 0 to 1. What the dying view draws instead of a damage ratio: 0.35 to 0.55
    /// across Downed, 0.55 to 1 across Unconscious.
    /// </summary>
    [AutoNetworkedField]
    public float Depth;

    /// <summary>
    /// Levels pushed in from outside the pain, blood and leg inputs; 0 none, 1 unconscious. Keys include
    /// "hypoxia", "sedation", "arrest", "shutdown", "injury" and "coreheat"; the old "airloss" key is gone.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, float> Pressures = new();

    /// <summary>
    /// BRAIN: brain oxygenation, 1 down to 0. Mirrored off the brain organ so the client can fade the view
    /// out as the clock runs without the organ entity being in its PVS.
    /// </summary>
    [AutoNetworkedField]
    public float Oxygenation = 1f;

    /// <summary>M1a: what the chest is doing. Set by the life tick; examine and the analyzer read it.</summary>
    [AutoNetworkedField]
    public WolfmedBreathing Breathing = WolfmedBreathing.Normal;

    /// <summary>M1a: why <see cref="Breathing"/> is not Normal.</summary>
    [AutoNetworkedField]
    public WolfmedBreathingSource BreathingSource = WolfmedBreathingSource.None;

    /// <summary>M1a: circulation as a medic describes it, from blood volume. Set by the life tick.</summary>
    [AutoNetworkedField]
    public WolfmedBloodBand BloodBand = WolfmedBloodBand.Normal;

    /// <summary>M3 (plan §8): the heart is impaired, so the pulse is irregular. Set by the life tick; examine reads it.</summary>
    [AutoNetworkedField]
    public bool PulseIrregular;

    /// <summary>
    /// M4 (OD16): a species built with no heart (Diona, the slimes). Its arrest is circulatory collapse: the same
    /// state and routes, told in other words. Set by the life system when the arrest starts.
    /// </summary>
    [AutoNetworkedField]
    public bool Heartless;

    /// <summary>M1a: the input that sets the current state (plan §5.1). None while Up.</summary>
    [AutoNetworkedField]
    public WolfmedCause Cause = WolfmedCause.None;

    /// <summary>M1a: the sub-source behind <see cref="Cause"/>: the hypoxia drain, arrest trigger or shutdown reason.</summary>
    [AutoNetworkedField]
    public WolfmedCauseSource CauseSource = WolfmedCauseSource.None;

    /// <summary>M1a: every other input that meets the current state's line and so also holds the body.</summary>
    [AutoNetworkedField]
    public WolfmedCauseFlags Blockers = WolfmedCauseFlags.None;

    /// <summary>Server: the running pain faint ends here. Never moved later once set (plan §3.1).</summary>
    [ViewVariables]
    public TimeSpan? PainFaintUntil;

    /// <summary>Server: when the running pain faint began. The faint alert's countdown runs from here to <see cref="PainFaintUntil"/>.</summary>
    [ViewVariables]
    public TimeSpan? PainFaintStart;

    /// <summary>
    /// Server: a crossing of the faint line faints. Cleared by a faint, set again when summed pain falls under
    /// the leave line.
    /// </summary>
    [ViewVariables]
    public bool PainFaintArmed = true;

    /// <summary>Server: summed pain at the moment of waking. A rise of <c>wolfmed.pain_faint_rise</c> over it re-arms.</summary>
    [ViewVariables]
    public float PainFaintBaseline;

    /// <summary>Server: no new faint starts before this, whatever the pain does.</summary>
    [ViewVariables]
    public TimeSpan PainFaintCooldownUntil;

    /// <summary>Server, M3: a head-blow knockout ends here (plan §3.6). Never moved later once set.</summary>
    [ViewVariables]
    public TimeSpan? HeadBlowUntil;

    /// <summary>Server, M3: when the running head-blow knockout began, for the alert's countdown.</summary>
    [ViewVariables]
    public TimeSpan? HeadBlowStart;

    /// <summary>Server, playtest 2: the patient has been told their hands are slowed by their wounds.</summary>
    [ViewVariables]
    public bool HandsPenaltyTold;

    /// <summary>Server, playtest 2: the patient has been told their legs are slowed by their wounds.</summary>
    [ViewVariables]
    public bool LegsPenaltyTold;

    /// <summary>Server: the last condition line the patient was told. Tests and admins read it.</summary>
    [ViewVariables]
    public string LastConditionLine = string.Empty;

    /// <summary>Server: how many condition lines the patient has been told. Tests count spam with it.</summary>
    [ViewVariables]
    public int ConditionLineCount;

    /// <summary>Server: the largest drain behind the hypoxia pressure. Written by the life tick.</summary>
    [ViewVariables]
    public WolfmedCauseSource HypoxiaSource = WolfmedCauseSource.None;

    /// <summary>Blood volume fraction at the bloodstream's last tick. 1 when the body has no bloodstream.</summary>
    [ViewVariables]
    public float BloodFraction = 1f;

    /// <summary>Server: whether the periodic re-evaluation still has anything to watch on this body.</summary>
    [ViewVariables]
    public bool Watching;

    [ViewVariables]
    public TimeSpan NextEvaluation;

    /// <summary>Server: how far past its entry value each state's worst input was, for the hysteresis test.</summary>
    [ViewVariables]
    public float DownLevel;

    [ViewVariables]
    public float OutLevel;

    /// <summary>
    /// Server: the earliest the body may stand back up. A stun landing on the Downed edge used to flip the
    /// state several times a second, and every flip was another body-fall sound.
    /// </summary>
    [ViewVariables]
    public TimeSpan DownedUntil;

    /// <summary>
    /// Server: the body has been on its feet at least once. A body still being assembled has no legs yet,
    /// which reads as both legs gone, and the dwell must not hold a crewman down before they ever stood up.
    /// </summary>
    [ViewVariables]
    public bool WasUp;

    /// <summary>
    /// Server: the body has had a working leg at least once. Until then a missing pair of legs is a body
    /// still being assembled, and downing it played the body-fall sound on every spawn.
    /// </summary>
    [ViewVariables]
    public bool HadLegs;
}

/// <summary>
/// On the floor and restricted to themselves. A separate component so the action blockers subscribe to it
/// rather than testing consciousness on every attempt event.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedDownedComponent : Component
{
    /// <summary>Do-afters take this much longer while Downed: a medipen is slower flat on your back.</summary>
    [DataField]
    public float DoAfterMultiplier = 1.5f;

    /// <summary>The Downed alert when the cause names none of its own (M1a: the condition alert system shows it).</summary>
    [DataField]
    public ProtoId<AlertPrototype> Alert = "WFWolfmedDowned";

    /// <summary>The hands have already let go for this spell on the floor. One drop per Downed, not per tick.</summary>
    [ViewVariables]
    public bool Dropped;
}
