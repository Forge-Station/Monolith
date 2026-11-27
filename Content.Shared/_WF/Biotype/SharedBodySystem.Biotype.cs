using System.Linq;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body.Systems;

public partial class SharedBodySystem
{
    /// <summary>
    /// Replaces a body's parts and organs with a fresh build of another body prototype, exactly as MapInit builds one.
    /// The old tree is taken apart without the events that would read as an injury (organ and part loss, a brain out,
    /// slot contents dropped): it is cut loose from the body first and then deleted. Hands are not touched, so what
    /// they hold stays held where the new body has the same hand. Before MapInit only the prototype is swapped.
    /// </summary>
    public void WgRebuildBody(EntityUid body, ProtoId<BodyPrototype> prototype, BodyComponent? comp = null)
    {
        if (!Resolve(body, ref comp, false) || TerminatingOrDeleted(body) || !Prototypes.HasIndex(prototype))
            return;

        if (LifeStage(body) < EntityLifeStage.MapInitialized)
        {
            comp.Prototype = prototype;
            Dirty(body, comp);
            return;
        }

        var parts = GetBodyChildren(body, comp).ToList();
        var organs = GetBodyOrgans(body, comp).ToList();

        // Components the parts and organs put on the body come off with them, as they would on removal.
        foreach (var (part, partComp) in parts)
        {
            if (partComp.Enabled && (partComp.OnAdd != null || partComp.OnRemove != null))
                RaiseLocalEvent(part, new BodyPartComponentsModifyEvent(body, false));
        }

        foreach (var (organ, organComp) in organs)
        {
            if (organComp.Enabled && (organComp.OnAdd != null || organComp.OnRemove != null))
                RaiseLocalEvent(organ, new OrganComponentsModifyEvent(body, false));
        }

        foreach (var (organ, organComp) in organs)
        {
            organComp.Body = null;
            Dirty(organ, organComp);
        }

        foreach (var (part, partComp) in parts)
        {
            partComp.Body = null;
            Dirty(part, partComp);
        }

        // Without a BodyComponent the root's removal reaches no body handler; the new one builds itself on MapInit.
        var root = comp.RootContainer.ContainedEntity;
        var fresh = new BodyComponent
        {
            Prototype = prototype,
            GibSound = comp.GibSound,
            RequiredLegs = comp.RequiredLegs,
            ThermalVisibility = comp.ThermalVisibility,
        };

        RemComp(body, comp);
        if (root != null)
            Del(root.Value);

        AddComp(body, fresh);
    }
}
