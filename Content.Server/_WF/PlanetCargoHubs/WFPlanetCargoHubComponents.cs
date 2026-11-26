namespace Content.Server._WF.PlanetCargoHubs;

/// <summary>On a cargo depot's station when the depot stands on a planet.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCargoHubComponent : Component
{
    /// <summary>The depot's index: 0 for depot A, 1 for B.</summary>
    [DataField]
    public int Index;

    /// <summary>The world's ground the hub stands on.</summary>
    [DataField]
    public EntityUid Ground;

    /// <summary>The hub site's origin on the ground.</summary>
    [DataField]
    public Vector2i Origin;

    /// <summary>The world's name, as the announcement and the FTL beacon give it.</summary>
    [DataField]
    public string World = string.Empty;

    /// <summary>Whether the round has been told where the depot stands.</summary>
    [DataField]
    public bool Announced;

    /// <summary>The FTL beacon by the world on the sector map, named after the depot.</summary>
    [DataField]
    public EntityUid? Beacon;
}

/// <summary>On a world's ground: the stations of the cargo hubs built on it, removed with the world.</summary>
[RegisterComponent]
public sealed partial class WFPlanetCargoHubsComponent : Component
{
    [DataField]
    public List<EntityUid> Stations = new();
}
