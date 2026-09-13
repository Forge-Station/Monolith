using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Prototypes;

/// <summary>Typical anatomy for a species group. Offered by the creator; never applied automatically.</summary>
[Prototype]
public sealed partial class GenitalPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public List<ProtoId<SpeciesPrototype>> Species = new();

    /// <summary>True for the one fallback preset used by species that match no group.</summary>
    [DataField]
    public bool Fallback;

    /// <summary>Species that deliberately use the fallback preset (checked by tests).</summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> ExplicitFallback = new();

    [DataField(required: true)]
    public GenitalProfile Male = default!;

    [DataField(required: true)]
    public GenitalProfile Female = default!;

    /// <summary>Custom colour for the penis shaft and vulva on species whose skin colouration is not HumanToned.</summary>
    [DataField]
    public Color TissueColor = GenitalColorDefaults.DefaultTissueColor;
}
