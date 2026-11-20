namespace Content.Server._WF.Wolfmed.Stasis;

/// <summary>
/// A wound host in Avali stasis, or held by an emergency stasis weave: every bleed is held while this is on, and
/// stasis healing is counted here.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedStasisHoldComponent : Component
{
    /// <summary>Seconds towards the next healing tick.</summary>
    [DataField]
    public float Accumulated;

    /// <summary>Seconds this stasis has lasted, against wolfmed.stasis_max_seconds.</summary>
    [DataField]
    public float Elapsed;

    /// <summary>
    /// Set by a stasis weave: the hold lasts until then whether or not the body is in Avali stasis, and the weave
    /// lifts it.
    /// </summary>
    [ViewVariables]
    public TimeSpan? HeldUntil;
}
