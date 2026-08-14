using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Bss;

/// <summary>
/// Declarative topology used by BSS gates and the shuttle system map.
/// Runtime MapIds are deliberately not stored here because they change every round.
/// Hierarchy: region (Frontier) → sector groups (green/yellow/red/black) → systems (each node/map).
/// </summary>
[Prototype("bssNetwork")]
public sealed partial class BssNetworkPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Region = "frontier";

    [DataField]
    public string RegionName = "Фронтир";

    [DataField]
    public List<BssRegionSectorDefinition> Groups = [];

    /// <summary>
    /// Systems (maps) in this region. Named "sectors" for prototype compatibility.
    /// </summary>
    [DataField]
    public List<BssSectorDefinition> Sectors = [];

    [DataField]
    public List<BssGateLinkDefinition> Links = [];

    public BssRegionSectorDefinition? GetGroup(string id)
        => Groups.Find(group => group.Id == id);

    public BssSectorDefinition? GetSystem(string id)
        => Sectors.Find(system => system.Id == id);

    public Color ResolveColor(BssSectorDefinition system)
        => GetGroup(system.Group)?.Color ?? system.Color;
}

[DataDefinition]
public sealed partial class BssRegionSectorDefinition
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public Color Color = Color.FromHex("#62aee8");
}

[DataDefinition]
public sealed partial class BssSectorDefinition
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Sector group inside the region: green, yellow, red, or black.
    /// </summary>
    [DataField]
    public string Group = "green";

    /// <summary>
    /// UI-space position on the system map.
    /// </summary>
    [DataField]
    public Vector2 Position;

    /// <summary>
    /// Optional stable persistent map key, used for validation and administration.
    /// Defaults to <see cref="Id"/> when omitted.
    /// </summary>
    [DataField]
    public string? MapKey;

    /// <summary>
    /// The round's selected game map is bound to this sector instead of creating an empty map.
    /// Exactly one sector in a network should be primary.
    /// </summary>
    [DataField]
    public bool Primary;

    [DataField]
    public Color Color = Color.FromHex("#62aee8");

    /// <summary>
    /// Parallax prototype applied to the sector map.
    /// </summary>
    [DataField]
    public string? Parallax;

    [DataField]
    public BssSystemAccess Access = BssSystemAccess.Open;

    /// <summary>
    /// WorldgenConfig prototype applied to this system's map. Falls back to the group default.
    /// </summary>
    [DataField]
    public string? Worldgen;
}

public enum BssSystemAccess : byte
{
    Open = 0,
    Hidden = 1,
}

[DataDefinition]
public sealed partial class BssGateLinkDefinition
{
    [DataField(required: true)]
    public BssGateEndpoint From = new();

    [DataField(required: true)]
    public BssGateEndpoint To = new();

    [DataField]
    public bool Bidirectional = true;
}

[DataDefinition]
public sealed partial class BssGateEndpoint
{
    [DataField(required: true)]
    public string Sector = string.Empty;

    [DataField(required: true)]
    public string Gate = string.Empty;
}
