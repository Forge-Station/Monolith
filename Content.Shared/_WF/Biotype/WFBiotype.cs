using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Biotype;

/// <summary>Whether a body is flesh or a machine. Default means "whatever the species is".</summary>
[Serializable, NetSerializable]
public enum WFBiotype : byte
{
    Default = 0,
    Organic = 1,
    Synthetic = 2,
}

/// <summary>Resolves a profile's stored biotype against its species.</summary>
public static class WFBiotypeRules
{
    /// <summary>The effective biotype: Default becomes the species' native one.</summary>
    public static WFBiotype Resolve(WFBiotype stored, SpeciesPrototype species)
    {
        return stored switch
        {
            WFBiotype.Organic => WFBiotype.Organic,
            WFBiotype.Synthetic => WFBiotype.Synthetic,
            _ => species.Biotype,
        };
    }
}
