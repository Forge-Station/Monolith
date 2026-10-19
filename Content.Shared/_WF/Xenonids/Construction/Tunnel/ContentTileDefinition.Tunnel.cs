// ReSharper disable once CheckNamespace
namespace Content.Shared.Maps;

/// <summary>RMC's per-tile tunnel rule.</summary>
public sealed partial class ContentTileDefinition
{
    /// <summary>Whether xenos can dig a tunnel on this tile.</summary>
    [DataField]
    public bool CanPlaceTunnel = true;
}
