namespace Content.Shared._WF.Lattice;

/// <summary>
/// Puts Drift into the heads of people near it, and more into anyone who touches or carries it, wherever it is.
/// </summary>
[RegisterComponent]
public sealed partial class WFDriftSourceComponent : Component
{
    /// <summary>Drift a second for someone right next to it, falling off to nothing at <see cref="Range"/>.</summary>
    [DataField]
    public float Rate = 0.5f;

    /// <summary>How far it reaches, in metres.</summary>
    [DataField]
    public float Range = 5f;

    /// <summary>Drift a second for whoever carries it, however deep in their bags.</summary>
    [DataField]
    public float Carried = 1f;

    /// <summary>Drift added at once when someone touches it or picks it up.</summary>
    [DataField]
    public float Touch = 5f;

    /// <summary>The most Drift it can push someone to.</summary>
    [DataField]
    public float Cap = 100f;

    [DataField]
    public WFDriftFlavour Flavour = WFDriftFlavour.Unmooring;
}
