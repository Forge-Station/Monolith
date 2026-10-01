using Content.Shared._Onyx.Wounds;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Strips WoundHost from synthetic species that inherit it via BaseMobSpeciesOrganic (D21/D32). RT has no YAML-level component removal, so this is a code-side opt-out instead of a prototype edit.</summary>
public sealed class WolfmedWoundHostExclusionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Abstract ancestor ids whose descendants must not be wound hosts. Add new synthetic species here.</summary>
    // WOLFGATE (P5-D9): protogen is biologically organic (Biological container, OrganicPart limbs, Blood
    // bloodstream, Hunger/Thirst, Respirator, FoodMeatHuman) - exclusion lifted. Add new synthetic species here.
    private static readonly HashSet<string> ExcludedAncestors = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, ComponentInit>(OnWoundHostInit);
    }

    private void OnWoundHostInit(Entity<WoundHostComponent> ent, ref ComponentInit args)
    {
        if (MetaData(ent).EntityPrototype is not { } proto)
            return;

        foreach (var (id, _) in _proto.EnumerateAllParents<EntityPrototype>(proto.ID, true))
        {
            if (!ExcludedAncestors.Contains(id))
                continue;

            // WP9: RemCompDeferred puts the component in EntityManager's _deleteSet while leaving its life
            // stage at Initialized, so StartComponents then trips DebugTools.Assert(!_deleteSet.Contains(...))
            // and every MobProtogen spawn throws. A straight RemComp moves it to Deleted, which that loop skips.
            RemComp<WoundHostComponent>(ent);
            return;
        }
    }
}
