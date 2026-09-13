using Content.Server.Body.Components;
using Content.Server.Polymorph.Components;
using Content.Shared._WF.Genitals.Components;
using Content.Shared.Body.Components;
using Content.Shared.Cloning;
using Content.Shared.Humanoid;
using Content.Shared.Polymorph;

namespace Content.Server._WF.Genitals;

// Cloning, polymorph and gibbing keep anatomy consistent across bodies.
public sealed partial class GenitalOrganSystem
{
    private void InitializeTransfer()
    {
        SubscribeLocalEvent<GenitalsComponent, CloningEvent>(OnCloning);
        SubscribeLocalEvent<GenitalsComponent, PolymorphedEvent>(OnPolymorphed);
        SubscribeLocalEvent<GenitalsComponent, BeforeGibbedEvent>(OnBeforeGibbed);
    }

    /// <summary>A clone grows from the genetic template, so a removed organ grows back once the clone's owner consents.</summary>
    private void OnCloning(Entity<GenitalsComponent> ent, ref CloningEvent args)
    {
        // CloneAppearance has already copied age and species, so this checks the clone's own eligibility.
        var target = args.Target;
        if (!EnsureAnatomy(target))
            return;

        var template = ent.Comp.SourceProfile;
        ApplyTemplate((target, Comp<GenitalsComponent>(target)), template);

        // Normally the mind transfer raises the consent event later; build now if consent is already there.
        // With the kill switch off the template waits; switching it back on builds it.
        if (template != null && _genitals.AnatomyEnabled && _consent.HasMaster(target))
            BuildOrgans(target, template);

        Recompute(target);
    }

    /// <summary>A transformation keeps what the person has now: current organs and runtime state move to the new body.</summary>
    /// <remarks>On revert nothing is needed: the original body kept its organs in the paused map.</remarks>
    private void OnPolymorphed(Entity<GenitalsComponent> ent, ref PolymorphedEvent args)
    {
        var newBody = args.NewEntity;
        if (args.IsRevert
            || !TryComp<PolymorphedEntityComponent>(newBody, out var polymorphed)
            || !polymorphed.Configuration.TransferHumanoidAppearance
            || !HasComp<HumanoidAppearanceComponent>(newBody)
            || !EnsureAnatomy(newBody))
            return;

        var target = Comp<GenitalsComponent>(newBody);
        target.SourceProfile = ent.Comp.SourceProfile;
        target.Arousal = ent.Comp.Arousal;
        target.RevealMode = ent.Comp.RevealMode;
        target.Visibility = ent.Comp.Visibility;
        target.Undergarments = ent.Comp.Undergarments;

        // A template not built yet (no consent so far) is built on the new body once consent arrives. With the kill
        // switch off nothing is copied either; switching it back on builds from the template.
        target.OrgansBuilt = _genitals.AnatomyEnabled && ent.Comp.OrgansBuilt;
        Dirty(newBody, target);

        DeleteOrgans(newBody);
        if (target.OrgansBuilt && TryComp<BodyComponent>(ent.Owner, out var oldBody))
        {
            foreach (var organ in _body.GetBodyOrganEntityComps<GenitalOrganComponent>((ent.Owner, oldBody)))
            {
                TrySpawnOrgan(newBody, organ.Comp1.Slot, organ.Comp1.State, out _);
            }
        }

        Recompute(newBody);
    }

    /// <summary>Organs of a body that is not opted in (opted out, ghosted or never opted in) are deleted before the gib, so none drop.</summary>
    private void OnBeforeGibbed(Entity<GenitalsComponent> ent, ref BeforeGibbedEvent args)
    {
        if (_consent.HasMaster(ent.Owner))
            return;

        DeleteOrgans(ent.Owner);
    }
}
