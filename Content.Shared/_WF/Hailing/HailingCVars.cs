using Robust.Shared.Configuration;

namespace Content.Shared._WF.Hailing;

/// <summary>
/// Tuning for ship-to-ship hailing.
/// </summary>
[CVarDefs]
public sealed class HailingCVars
{
    /// <summary>
    /// How far a ship can hail, in metres. A call drops a quarter further out than this.
    /// </summary>
    public static readonly CVarDef<float> Range =
        CVarDef.Create("wf.hailing.range", 3072f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a hail rings before it gives up.
    /// </summary>
    public static readonly CVarDef<float> RingTimeout =
        CVarDef.Create("wf.hailing.ring_timeout", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds a console waits before hailing again after being declined or ignored.
    /// </summary>
    public static readonly CVarDef<float> RetryDelay =
        CVarDef.Create("wf.hailing.retry_delay", 15f, CVar.SERVERONLY);
}
