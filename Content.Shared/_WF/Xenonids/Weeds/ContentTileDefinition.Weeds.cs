// ReSharper disable once CheckNamespace
namespace Content.Shared.Maps;

/// <summary>RMC's per-tile weed rules.</summary>
public sealed partial class ContentTileDefinition
{
    /// <summary>Whether xeno weeds can grow on this tile.</summary>
    [DataField]
    public bool WeedsSpreadable = true;

    /// <summary>Weeds grow here only from sources that spread onto semi-weedable tiles.</summary>
    [DataField]
    public bool SemiWeedable;
}
