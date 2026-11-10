namespace Content.Server._WF.Remade.Sector;

/// <summary>
/// Put on every Monolith by the sector systems. Tracks whether it has been disturbed, which is what puts it on every
/// radar, and what has been sent after its carrier.
/// </summary>
[RegisterComponent]
public sealed partial class WFScionSignalComponent : Component
{
    /// <summary>Placed by a site.</summary>
    [ViewVariables]
    public bool Wild;

    /// <summary>The grid it lay on when first seen; leaving it disturbs the Scion.</summary>
    [ViewVariables]
    public EntityUid? Home;

    /// <summary>The site's encounter, for a Scion a site placed.</summary>
    [ViewVariables]
    public EntityUid? Encounter;

    /// <summary>Moved, pulled, unwrenched or drawn on. Once set it stays set.</summary>
    [ViewVariables]
    public bool Disturbed;

    /// <summary>The ship the Choir were last sent after.</summary>
    [ViewVariables]
    public EntityUid? Hunted;

    [ViewVariables]
    public TimeSpan NextHunt;
}
