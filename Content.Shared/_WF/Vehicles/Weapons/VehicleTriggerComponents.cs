using Robust.Shared.Audio;

namespace Content.Shared._WF.Vehicles.Weapons;

/// <summary>On a gun: the rounds it fires start a trigger timer as they leave. Handled on the server.</summary>
[RegisterComponent]
public sealed partial class OnShootTriggerAmmoTimerComponent : Component
{
    [DataField]
    public float Delay;

    [DataField]
    public float BeepInterval;

    [DataField]
    public float? InitialBeepDelay;

    [DataField]
    public SoundSpecifier? BeepSound;
}

/// <summary>On a round: it triggers when it reaches the end of its fixed distance. Handled on the server.</summary>
[RegisterComponent]
public sealed partial class TriggerOnFixedDistanceStopComponent : Component
{
    [DataField]
    public TimeSpan Delay;
}

/// <summary>A round that has reached the end of its fixed distance and waits to trigger.</summary>
[RegisterComponent]
public sealed partial class ActiveTriggerOnFixedDistanceStopComponent : Component
{
    public TimeSpan TriggerAt;
}
