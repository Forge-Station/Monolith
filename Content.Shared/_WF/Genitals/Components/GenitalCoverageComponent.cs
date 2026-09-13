namespace Content.Shared._WF.Genitals.Components;

/// <summary>Body regions this clothing covers while worn in an inner or outer clothing slot.</summary>
[RegisterComponent]
public sealed partial class GenitalCoverageComponent : Component
{
    [DataField]
    public GenitalRegion Regions = GenitalRegion.All;

    /// <summary>Regions covered while a FoldableClothing item is folded (e.g. an opened coat). Null means unchanged.</summary>
    [DataField]
    public GenitalRegion? FoldedRegions;
}
