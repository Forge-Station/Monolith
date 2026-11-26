namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// The last successful shock on this body (M1a, plan §7.1). Holds the grace in which the blood and oxygen
/// arrest triggers wait, and how long ago the restore was, which makes a quick second shock only a restart.
/// Both clocks run on the life tick, so a test that fast-forwards the tick fast-forwards them too.
/// </summary>
[RegisterComponent, Access(typeof(WolfmedRevivalSystem), typeof(WolfmedLifeSystem))]
public sealed partial class WolfmedPostShockComponent : Component
{
    /// <summary>Seconds of grace left. The blood and oxygen arrest triggers hold off while this is above 0.</summary>
    [ViewVariables]
    public float GraceSeconds;

    /// <summary>Seconds since the shock that restored oxygenation and opened the grace.</summary>
    [ViewVariables]
    public float SinceRestore;

    /// <summary>Why the heart had stopped before that shock: blood, oxygen, heart, shock, sepsis, or dead.</summary>
    [ViewVariables]
    public string Cause = string.Empty;
}
