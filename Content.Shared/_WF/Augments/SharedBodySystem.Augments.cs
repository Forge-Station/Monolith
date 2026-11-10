using System.Linq;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;

namespace Content.Shared.Body.Systems;

public partial class SharedBodySystem
{
    /// <summary>While set, a part or organ leaving a part's container isn't treated as lost.</summary>
    protected bool WfCuttingLoose;

    /// <summary>
    /// Takes a part and everything under it off a body without the events that read as an injury (part and organ
    /// loss, a leg gone, slot contents dropped), then deletes it. What the parts and organs put on the body comes off.
    /// Hands stay registered; the caller removes the ones that won't come back.
    /// </summary>
    public void WfCutLoose(EntityUid body, EntityUid part, BodyComponent? comp = null, BodyPartComponent? partComp = null)
    {
        if (!Resolve(body, ref comp, false) || !Resolve(part, ref partComp, false))
            return;

        var legRemoved = false;
        foreach (var (child, childComp) in GetBodyPartChildren(part, partComp).ToList())
        {
            if (childComp.Enabled && (childComp.OnAdd != null || childComp.OnRemove != null))
                RaiseLocalEvent(child, new BodyPartComponentsModifyEvent(body, false));

            foreach (var (organ, organComp) in GetPartOrgans(child, childComp))
                WfCutLooseOrgan(body, organ, organComp);

            legRemoved |= comp.LegEntities.Remove(child);
            childComp.Body = null;
            childComp.ParentSlot = null;
            Dirty(child, childComp);
        }

        // The speed follows the legs left; a replacement leg updates it again when it attaches.
        if (legRemoved)
            UpdateMovementSpeed(body, comp);

        Dirty(body, comp);
        WfCuttingLoose = true;
        try
        {
            Del(part);
        }
        finally
        {
            WfCuttingLoose = false;
        }
    }

    /// <summary>Takes an organ out of a body without the organ-loss events, and deletes it.</summary>
    public void WfDeleteOrgan(EntityUid body, EntityUid organ)
    {
        if (TryComp(organ, out OrganComponent? comp))
            WfCutLooseOrgan(body, organ, comp);

        WfCuttingLoose = true;
        try
        {
            Del(organ);
        }
        finally
        {
            WfCuttingLoose = false;
        }
    }

    private void WfCutLooseOrgan(EntityUid body, EntityUid organ, OrganComponent comp)
    {
        if (comp.Enabled && (comp.OnAdd != null || comp.OnRemove != null))
            RaiseLocalEvent(organ, new OrganComponentsModifyEvent(body, false));

        comp.Body = null;
        Dirty(organ, comp);
    }
}
