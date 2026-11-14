using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// A refit line a ship can own, bought tier by tier. Its <see cref="Effect"/> decides what of the ship it covers, in
/// units such as machine parts, and how a unit reaches each tier.
/// </summary>
[Prototype("wfShipRefit")]
public sealed partial class WFShipRefitPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Name shown in the console.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Tiers in order; the first is Mk II.</summary>
    [DataField(required: true)]
    public List<WFShipRefitTier> Tiers = new();

    /// <summary>What the refit does to a ship.</summary>
    [DataField(required: true)]
    public WFShipRefitEffect Effect = default!;

    /// <summary>For a single-tier line, replaces "to Mk II" on its keys, as in "Refit for atmosphere".</summary>
    [DataField]
    public LocId? Mark;

    /// <summary>For a single-tier line, replaces the numeral on its lamp.</summary>
    [DataField]
    public LocId? Lamp;
}

/// <summary>
/// One refit tier's price per unit.
/// </summary>
[DataDefinition]
public sealed partial class WFShipRefitTier
{
    /// <summary>Credits per unit brought to this tier.</summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>Material units per unit brought to this tier, for a console that pays in materials.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Materials = new();
}
