using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Biotype;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Synthetics;

/// <summary>
/// What makes a body a synthetic: the Synthetic biotype, whatever the species. The IPC and Synth species resolve to
/// it natively; any other species gets it from the character's profile. Everything in this module gates on this and
/// never on a species or mob prototype.
/// </summary>
public abstract class SharedWFSyntheticsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>A synthetic-biotype wound host.</summary>
    public bool IsSynthetic(EntityUid body)
    {
        if (!HasComp<WoundHostComponent>(body))
            return false;

        if (TryComp(body, out WFBiotypeComponent? marker))
            return marker.Biotype == WFBiotype.Synthetic;

        if (!TryComp(body, out HumanoidAppearanceComponent? humanoid))
            return false;

        // A mob spawned without a profile carries Default: its species decides.
        return humanoid.Biotype == WFBiotype.Synthetic ||
               humanoid.Biotype == WFBiotype.Default &&
               _prototypes.TryIndex(humanoid.Species, out var species) && species.Biotype == WFBiotype.Synthetic;
    }
}
