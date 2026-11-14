using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Biotype;

/// <summary>Gates that follow a character's effective biotype instead of its species id.</summary>
public static class WFBiotypeGates
{
    /// <summary>The profile's biotype with Default turned into its species' own.</summary>
    public static WFBiotype Effective(HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return protoManager.TryIndex(profile.Species, out var species)
            ? WFBiotypeRules.Resolve(profile.Biotype, species)
            : profile.Biotype;
    }

    /// <summary>The Fluent key of a biotype's display name.</summary>
    public static string NameKey(WFBiotype biotype)
    {
        return biotype switch
        {
            WFBiotype.Organic => "wf-biotype-organic",
            WFBiotype.Synthetic => "wf-biotype-synthetic",
            _ => "wf-biotype-default",
        };
    }

    /// <summary>Whether the trait lists the profile's effective biotype in its biotype blacklist.</summary>
    public static bool TraitBlocked(TraitPrototype trait, HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return trait.BiotypeBlacklist.Count > 0 && trait.BiotypeBlacklist.Contains(Effective(profile, protoManager));
    }
}
