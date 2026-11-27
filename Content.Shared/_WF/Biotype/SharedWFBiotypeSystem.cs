using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Biotype;

/// <summary>Applies a profile's biotype to a mob. The server converts the body; the client only mirrors the value.</summary>
public abstract partial class SharedWFBiotypeSystem : EntitySystem
{
    [Dependency] protected IPrototypeManager Proto = default!;

    /// <summary>Applies the profile's biotype, resolved against the profile's species.</summary>
    public void Apply(EntityUid mob, HumanoidCharacterProfile profile, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Proto.TryIndex(profile.Species, out var species))
            return;

        Apply(mob, profile.Biotype, species, humanoid);
    }

    /// <summary>Applies a stored biotype (Default = native) for the given species. Idempotent.</summary>
    public virtual void Apply(EntityUid mob, WFBiotype stored, SpeciesPrototype species, HumanoidAppearanceComponent? humanoid = null)
    {
        var effective = WFBiotypeRules.Resolve(stored, species);
        if (Resolve(mob, ref humanoid, false))
            humanoid.Biotype = effective;
    }

    /// <summary>The overlay prototype for a biotype, if one exists.</summary>
    public WFBiotypePrototype? GetOverlay(WFBiotype biotype)
    {
        foreach (var proto in Proto.EnumeratePrototypes<WFBiotypePrototype>())
        {
            if (proto.Biotype == biotype)
                return proto;
        }

        return null;
    }

    /// <summary>
    /// The mob's applied biotype; before a profile is applied, read off the mob: a machine runs on a Silicon or
    /// SynthBattery power source.
    /// </summary>
    public WFBiotype GetBiotype(EntityUid mob)
    {
        if (TryComp(mob, out WFBiotypeComponent? applied))
            return applied.Biotype;

        return HasComp<SiliconComponent>(mob) || HasComp<SynthBatteryComponent>(mob)
            ? WFBiotype.Synthetic
            : WFBiotype.Organic;
    }
}
