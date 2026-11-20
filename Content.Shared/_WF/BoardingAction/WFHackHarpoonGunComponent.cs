using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.BoardingAction;

/// <summary>
/// A ship gun whose harpoon hacks the hull it latches to. FTL drops at once, the rest of the hack lands after
/// <see cref="LockdownDelay"/>, and nothing else on the firing ship can shoot until the cable is gone.
/// </summary>
[RegisterComponent]
public sealed partial class WFHackHarpoonGunComponent : Component
{
    /// <summary>How long after the latch the target is parked and its guns stop bearing on the hacker.</summary>
    [DataField]
    public TimeSpan LockdownDelay = TimeSpan.FromSeconds(8);

    /// <summary>How long a hack lasts before the dart burns out. Zero never burns out.</summary>
    [DataField]
    public TimeSpan MaxDuration = TimeSpan.Zero;

    /// <summary>Cable kept spare over the distance between the two ends, in metres.</summary>
    [DataField]
    public float CableSpare = 6f;

    /// <summary>Time between bursts of sparks off the target's engines.</summary>
    [DataField]
    public TimeSpan ArcInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>Looping arcs that run across each of the target's engines for as long as the hack lasts.</summary>
    [DataField]
    public EntProtoId ArcEffect = "WFEffectHackArc";

    [DataField]
    public EntProtoId SparkEffect = "EffectSparks";

    [DataField]
    public SoundSpecifier ArcSound = new SoundCollectionSpecifier("sparks");

    /// <summary>Played at the target's helms when the dart latches and when the lockdown lands.</summary>
    [DataField]
    public SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/Machines/warning_buzzer.ogg");

    /// <summary>The cable tied to this gun, once a dart bites.</summary>
    [ViewVariables]
    public EntityUid? Cable;

    /// <summary>The dart at the far end of the cable.</summary>
    [ViewVariables]
    public EntityUid? Dart;

    /// <summary>The hull being hacked.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>The ship this gun was on when the hack began.</summary>
    [ViewVariables]
    public EntityUid? Source;

    /// <summary>When the hack began.</summary>
    [ViewVariables]
    public TimeSpan Latched;

    /// <summary>Whether the lockdown has landed.</summary>
    [ViewVariables]
    public bool Lockdown;

    /// <summary>When the engines next throw sparks.</summary>
    [ViewVariables]
    public TimeSpan NextArc;
}
