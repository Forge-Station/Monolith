namespace Content.Server._WF.AsteroidMining;

/// <summary>
/// An ore magnet that pulls loose ore it can see into its storage. Each ore is locked on for a moment, shown by an arc
/// from the magnet, before it flies in; walls block it, windows don't.
/// </summary>
[RegisterComponent]
public sealed partial class WFOreMagnetComponent : Component
{
    [DataField] public float Range = 30f;

    /// <summary>How often the magnet looks for new ore.</summary>
    [DataField] public TimeSpan ScanInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>Ore it can hold locked at once.</summary>
    [DataField] public int MaxPulls = 6;

    /// <summary>Seconds an ore stays locked before it flies in, plus <see cref="PullTimePerMetre"/> for each metre out.</summary>
    [DataField] public float PullTimeBase = 0.4f;
    [DataField] public float PullTimePerMetre = 0.03f;

    public TimeSpan NextScan;
    public bool Active;
    public readonly HashSet<EntityUid> Pulling = new();
}
