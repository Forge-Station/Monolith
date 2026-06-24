using Content.Shared.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Server._Forge.Body.Syndromes;

/// <summary>
/// Resolves disease/tolerance targets by <see cref="Organ"/> flag.
/// <see cref="Organ.Body"/> -> <see cref="BodyPhysiologyComponent"/> on the doll.<br/>
/// Any other flag -> matching <see cref="OrganPhysiologyComponent"/> inside the organ.
/// </summary>
public static class OrganResolver
{
    public static List<EntityUid> Resolve(EntityUid bodyUid, Organ target, SharedBodySystem body, IEntityManager entities)
    {
        var result = new List<EntityUid>();
        if (target == Organ.None)
            return result;

        // Body flag: disease lives on the doll component itself
        if (target.HasFlag(Organ.Body) && entities.HasComponent<BodyPhysiologyComponent>(bodyUid))
            result.Add(bodyUid);

        // Only Body (or None): no organ iteration needed
        if (target == Organ.Body || target == Organ.None)
            return result;

        // Find all organs inside this body whose Organ flag matches the target
        foreach (var (organUid, organPhys, _) in body.GetBodyOrganEntityComps<OrganPhysiologyComponent>((bodyUid, null)))
        {
            if (target.HasFlag(organPhys.Organ))
                result.Add(organUid);
        }
        return result;
    }
}
