using Robust.Shared.GameStates;

namespace Content.Shared._WF.Synthetics;

/// <summary>
/// A synthetic chassis with an empty cell or none: still up and conscious, but crawling and on one arm. On the body
/// while it lasts; the server adds and removes it as the power comes and goes.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFLowPowerComponent : Component
{
    /// <summary>
    /// Speed on low power, as a share of powered speed. The chassis's own unpowered slowdown (an IPC's charge
    /// thresholds, a synth's battery) is folded into it, so every chassis crawls at the same rate.
    /// </summary>
    [DataField]
    public float Speed = 0.25f;

    /// <summary>The one arm still powered, or null when there is none to power.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? PoweredArm;

    /// <summary>When the powered arm may next be switched.</summary>
    [DataField]
    public TimeSpan NextSwitch;
}
