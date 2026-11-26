using Content.Server.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;

namespace Content.Server._WF.Remade.Conversion;

/// <summary>A Remade blown apart leaves no organs or animal parts on the deck; its own severed limbs are dropped elsewhere.</summary>
public sealed class WFRemadeGibSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRemadeComponent, BeingGibbedEvent>(OnGibbed);
    }

    private void OnGibbed(Entity<WFRemadeComponent> ent, ref BeingGibbedEvent args)
    {
        foreach (var gib in args.GibbedParts)
        {
            if (HasComp<OrganComponent>(gib) || HasComp<BodyPartComponent>(gib))
                QueueDel(gib);
        }
    }
}
