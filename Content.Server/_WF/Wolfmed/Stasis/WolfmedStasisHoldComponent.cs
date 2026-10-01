namespace Content.Server._WF.Wolfmed.Stasis;

/// <summary>A wound host in Avali stasis: every bleed is held while this is on, and its healing is counted here.</summary>
[RegisterComponent]
public sealed partial class WolfmedStasisHoldComponent : Component
{
    /// <summary>Seconds towards the next healing tick.</summary>
    [DataField]
    public float Accumulated;
}
