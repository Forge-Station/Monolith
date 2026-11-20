using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// The heart has stopped. The body is Critical through an external pressure, the crit heartbeat goes silent,
/// bleeding slows to a trickle and the brain's oxygenation clock runs at its fastest. Only a defibrillator,
/// a working heart with blood behind it, or a rejuvenate takes this off.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedCardiacArrestComponent : Component
{
    /// <summary>When the heart stopped. The analyzer counts from here.</summary>
    [AutoNetworkedField]
    public TimeSpan StartTime;

    /// <summary>Why it stopped, for the admin log and the analyzer's wording.</summary>
    [ViewVariables]
    public string Cause = string.Empty;
}

/// <summary>
/// One step of the curve that says how much a cold body protects its brain. The lowest matching step wins,
/// so the coldest entry is the one that applies.
/// </summary>
[DataDefinition]
public sealed partial class WolfmedBrainColdStep
{
    /// <summary>Body temperature, in kelvin, under which this step applies.</summary>
    [DataField]
    public float Below = 303.15f;

    /// <summary>What the oxygenation drain is multiplied by under that temperature.</summary>
    [DataField]
    public float Factor = 0.5f;
}

/// <summary>
/// Oxygen in the brain organ, 1 down to 0. The one clock that decides whether a body stays revivable: while
/// it is draining the organ takes irreversible damage, and a destroyed brain organ is death.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedBrainComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Oxygenation = 1f;

    /// <summary>Cold protection, coldest step first. 30 C halves the drain, cryo temperature all but stops it.</summary>
    [DataField]
    public List<WolfmedBrainColdStep> ColdSteps = new()
    {
        new WolfmedBrainColdStep { Below = 293.15f, Factor = 0.1f },
        new WolfmedBrainColdStep { Below = 303.15f, Factor = 0.5f },
    };

    /// <summary>Organ health under which the patient blurs and slurs, as a share of the organ's maximum.</summary>
    [DataField]
    public float ConcussionAt = 0.9f;

    /// <summary>Organ health under which the patient also drops things, as a share of the maximum.</summary>
    [DataField]
    public float SevereAt = 0.5f;

    /// <summary>Blur a fully damaged brain is worth, scaled by how far past <see cref="ConcussionAt"/> it is.</summary>
    [DataField]
    public float MaxBlur = 3f;

    /// <summary>Whether the last tick found the organ damaged enough to blur and slur. Edge trigger only.</summary>
    [ViewVariables]
    public bool Concussed;
}

/// <summary>
/// What a repaired brain carries afterwards: the concussion effects, for as long as the trauma lasts. The
/// price of having been dead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedBrainTraumaComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Ends;

    /// <summary>Blur the trauma is worth on its own.</summary>
    [DataField]
    public float Blur = 1.5f;
}

/// <summary>
/// Somebody is doing CPR on this body right now. Slows the oxygenation clock and stands in for both
/// breathing and a little circulation; it never restarts the heart.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedCprComponent : Component
{
    [ViewVariables]
    public TimeSpan Ends;
}

/// <summary>
/// A mechanical body with no power or no pump: the machine analogue of cardiac arrest. Unconscious through
/// an external pressure, with no oxygenation clock behind it and nothing that runs out.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedShutdownComponent : Component
{
    /// <summary>M1a: why the machine stopped, Power or Pump. The HUD, alerts and analyzer name it.</summary>
    [AutoNetworkedField]
    public Content.Shared._WF.Wolfmed.Consciousness.WolfmedCauseSource Reason =
        Content.Shared._WF.Wolfmed.Consciousness.WolfmedCauseSource.Power;
}
