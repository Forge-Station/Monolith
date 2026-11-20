using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed;

/// <summary>Drives Onyx's part-lifecycle entry points from Wolfgate's body-scoped part events.</summary>
public sealed class WolfmedBodyPartLifecycleSystem : EntitySystem
{
    [Dependency] private WoundDamageProjectionSystem _projection = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!;
    [Dependency] private Wounds.WolfmedNecrosisSystem _necrosis = default!;
    [Dependency] private Damage.WolfmedDegradationVisualsSystem _degradation = default!;
    [Dependency] private Damage.WolfmedTreatmentVisualsSystem _treatments = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private Life.WolfmedLifeSystem _life = default!;
    [Dependency] private Wounds.WolfmedStumpTagSystem _stumps = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // WOLFGATE (D28): <BodyComponent, BodyPart*Event> is owned by Shitmed's
        // SharedBodySystem.PartAppearance.cs:25-26. WoundHostComponent sits on the same entity
        // and scopes the handler to wound hosts, which is what we want anyway.
        SubscribeLocalEvent<WoundHostComponent, BodyPartAddedEvent>(OnPartAdded);
        SubscribeLocalEvent<WoundHostComponent, BodyPartRemovedEvent>(OnPartRemoved);
    }

    /// <summary>Initialises wounds on a newly attached limb and its whole subtree.</summary>
    private void OnPartAdded(Entity<WoundHostComponent> body, ref BodyPartAddedEvent args)
    {
        // Deleting a mob detaches every part on the way down, so this fires mid-termination. Onyx has no
        // caller for these entry points at all; the guard belongs here rather than in the vendored file.
        if (TerminatingOrDeleted(body) || TerminatingOrDeleted(args.Part.Owner))
            return;

        // Onyx initialises every part of the attached subtree, not just the root it was handed.
        foreach (var (part, _) in _body.GetBodyPartChildren(args.Part.Owner, args.Part.Comp))
        {
            _projection.OnPartInserted(part, body);
            // Onyx's BodyInventorySlotSystem:37-39 drives both of these; that system is Nubody glue D8 skips,
            // so without this line a re-attached limb's wounds never rejoin the body's bleed total.
            _bleeding.OnPartInserted(part, body);

            // W5: a limb left on the floor past the grace period comes back as dead tissue.
            _necrosis.OnAttached(part);

            var inserted = new OrganGotInsertedEvent(body);
            RaiseLocalEvent(part, ref inserted);
        }

        // Playtest 5: the stump this limb (or its replacement) was torn off is closed by putting one back.
        _stumps.CloseStumps(body, args.Part.Owner);

        // V3: the limb's wounds now show on the body's sprite instead of on the limb's own.
        _degradation.Refresh(args.Part.Owner);
        _degradation.Refresh(body);
        _treatments.Refresh(body); // G3: whatever the limb came back wearing.
    }

    /// <summary>Re-projects the body and the detached limb when a limb comes off.</summary>
    private void OnPartRemoved(Entity<WoundHostComponent> body, ref BodyPartRemovedEvent args)
    {
        // Ahead of the guard below: a gibbed part leaves its slot while it is being deleted, and a gibbed
        // head is still a lost head.
        if (!TerminatingOrDeleted(body))
            _life.OnPartDetached(body, args.Part.Owner);

        // Same guard as OnPartAdded: RecursiveDeleteEntity detaches every part while the mob terminates, and
        // RefreshDetachedDamage's EnsureComp<PartDamageVisualsComponent> throws on a terminating entity.
        if (TerminatingOrDeleted(body) || TerminatingOrDeleted(args.Part.Owner))
            return;

        _projection.OnPartRemoved(args.Part.Owner, body);
        // Onyx's BodyInventorySlotSystem:49 does the same: the detached limb's wounds must leave the body's
        // bleed total, or BloodstreamComponent.BleedAmount keeps bleeding for a limb that is on the floor.
        _bleeding.OnPartChanged(body);
        ChargeVitalPartLoss(body, args.Part);

        foreach (var (part, _) in _body.GetBodyPartChildren(args.Part.Owner, args.Part.Comp))
        {
            // W5: starts the viability clock the reattachment path checks.
            _necrosis.OnDetached(part);

            var removed = new OrganGotRemovedEvent(body);
            RaiseLocalEvent(part, ref removed);
        }

        // V3: the severed limb carries its own overlay now, and the body has one layer fewer to draw.
        _degradation.Refresh(args.Part.Owner);
        _degradation.Refresh(body);
        _treatments.Refresh(body); // G3: a lost limb takes its dressing with it.
    }

    /// <summary>Keeps a lost vital part's damage on the books; CheckVitalDamage only sums attached parts.</summary>
    private void ChargeVitalPartLoss(Entity<WoundHostComponent> body, Entity<BodyPartComponent> part)
    {
        // P3-D1. Host-gated by this system's own subscription, so non-wound-hosts are structurally unreachable
        // and keep Shitmed's plain VitalDamage behaviour. Shitmed's PartRemoveDamage adds VitalDamage (100) on
        // top of this in the same RemovePart call (SharedBodySystem.Parts.cs:357 raises this event, :362 charges).
        if (!part.Comp.IsVital || _body.GetBodyChildrenOfType(body.Owner, part.Comp.PartType).Any())
            return;

        var lost = _damageable.GetTotalDamage(part.Owner);
        if (lost <= FixedPoint2.Zero)
            return;

        // Bloodloss is the type Shitmed's own PartRemoveDamage uses, and it is absent from
        // WoundHostComponent.LocalizedDamageTypes, so routing keeps it systemic instead of dealing it to
        // another limb - and CheckVitalDamage counts systemic damage.
        _damageable.ChangeDamage(body.Owner,
            new DamageSpecifier(_prototypes.Index<DamageTypePrototype>("Bloodloss"), lost));
    }
}
