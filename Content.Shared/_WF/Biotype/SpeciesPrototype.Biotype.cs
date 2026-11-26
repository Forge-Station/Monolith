using Content.Shared._WF.Biotype;

namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class SpeciesPrototype
{
    /// <summary>The species' native biotype: what its own body and components are.</summary>
    [DataField]
    public WFBiotype Biotype { get; private set; } = WFBiotype.Organic;
}
