using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// M2 (plan §5.5): why the heart last stopped, kept for the analyzer's "After a restart" line for
/// <c>wolfmed.arrest_cause_memory_seconds</c> after it started again. Server only.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedArrestMemoryComponent : Component
{
    /// <summary>The arrest's cause string: blood, oxygen, heart, sepsis, shock.</summary>
    [ViewVariables]
    public string Cause = string.Empty;

    [ViewVariables]
    public TimeSpan RestartedAt;
}

/// <summary>
/// M2 (OD10): a positronic core put back together by core repair. No brain trauma on a machine; the synthetic HUD
/// shows "CORE RESTORED: DIAGNOSTICS" until <see cref="Ends"/> instead, with no effect on play.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedCoreRestoredComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Ends;
}

/// <summary>
/// M2 (plan §5.2, §2.3): what the explanation card on the unconscious screen needs beyond the cause, pushed by the
/// server: how much time the brain has, as a coarse bar with no seconds, and who is helping.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedCardComponent : Component
{
    /// <summary>Tenths of the untreated rescue window the brain still has, 0 to 10; -1 while nothing counts down.</summary>
    [AutoNetworkedField]
    public sbyte Reserve = -1;

    /// <summary>Somebody is doing CPR on the body.</summary>
    [AutoNetworkedField]
    public bool Cpr;

    /// <summary>An analyzer read the body within the last few seconds.</summary>
    [AutoNetworkedField]
    public bool Examined;

    /// <summary>
    /// Playtest 3: when the running timed faint began and when the body comes round by itself, on the game clock, so
    /// the card counts down and drains its bar every frame. Both null while nothing times the wake.
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan? WakeStart;

    [AutoNetworkedField]
    public TimeSpan? WakeEnd;

    /// <summary>Server: when the last analyzer scan was; null before the first.</summary>
    [ViewVariables]
    public TimeSpan? LastExamined;

    /// <summary>Server: the untreated seconds the brain had when this arrest began, what the bar is a share of.</summary>
    [ViewVariables]
    public float Window;
}

/// <summary>
/// M2 (plan §5.5): the processes making a body worse right now, each with its own first aid. The analyzer lists
/// them; "wait as a ghost" is offered only while there are none.
/// </summary>
[Flags, Serializable, NetSerializable]
public enum WolfmedRoutes : ushort
{
    None = 0,
    Bleeding = 1 << 0,
    InternalBleeding = 1 << 1,
    BurnFluid = 1 << 2,

    /// <summary>No pulse: the brain drains at its fastest.</summary>
    Arrest = 1 << 3,

    /// <summary>Suffocating: no air, or nothing to breathe with.</summary>
    Airway = 1 << 4,
    Lungs = 1 << 5,

    /// <summary>Blood under the line where it starves the brain.</summary>
    Circulation = 1 << 6,
    Sepsis = 1 << 7,

    /// <summary>An overdose depressing the breathing.</summary>
    Sedation = 1 << 8,

    /// <summary>Brain oxygenation under the line where the tissue itself dies.</summary>
    TissueLoss = 1 << 9,

    /// <summary>
    /// M4 (plan §3.11): a machine's positronic core past its heat line, losing health. The top bit, so the routes M5
    /// adds in parallel keep the bits after TissueLoss.
    /// </summary>
    CoreHeat = 1 << 15,

    // M5: bit 10 is left for M4's core heat. Playtest 4 took it, since core heat went to bit 15.

    /// <summary>Playtest 4 (SEPSIS): sepsis past the line where it damages the torso organs.</summary>
    SepsisOrgans = 1 << 10,

    /// <summary>A toxic coma draining the brain.</summary>
    Toxin = 1 << 11,

    /// <summary>Heat stroke draining the brain.</summary>
    HeatStroke = 1 << 12,

    /// <summary>Radiation past the marrow's bleed line: blood lost with nothing making more.</summary>
    Marrow = 1 << 13,

    /// <summary>A hypothermic core still cooling.</summary>
    Hypothermia = 1 << 14,
}
