using System.Collections.Generic;
using System.Linq;
using Content.Shared._Shitmed.Targeting; // WOLFGATE(Wolfmed): D10 — Onyx's TargetingComponent registers as "Targeting", colliding with Shitmed's; use Shitmed's identical field instead
using Content.Shared._WF.Wolfmed.Targeting; // WOLFGATE(Wolfmed): D10 — WoundTargetResolver replaces the absent TargetResolverSystem
using Content.Server._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): W5 — the necrosis clock a tourniquet starts
using Content.Shared._WF.Wolfmed.Wounds; // WOLFGATE(Wolfmed): W2 — arterial bleeds decide where a tourniquet helps
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._Onyx.Medical.Tourniquet;

public sealed partial class TourniquetSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundTargetResolver _targeting = default!; // WOLFGATE(Wolfmed): D10 — TargetResolverSystem is absent; signature-exact TryResolveExact replacement
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!; // WOLFGATE(Wolfmed): W2: which bleeds can be tied off
    [Dependency] private WolfmedNecrosisSystem _necrosis = default!; // WOLFGATE(Wolfmed): W5: a tourniquet left on kills the limb
    [Dependency] private WolfmedTourniquetSlipSystem _slip = default!; // WOLFGATE(Wolfmed): a makeshift strap can slip
    [Dependency] private WoundDamageRoutingSystem _damage = default!;
    [Dependency] private WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TourniquetComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<TourniquetComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<TourniquetComponent, TourniquetDoAfterEvent>(OnDoAfter);
    }

    private void OnUseInHand(Entity<TourniquetComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStart(ent, args.User, args.User);
    }

    private void OnAfterInteract(Entity<TourniquetComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryStart(ent, target, args.User);
    }

    private bool TryStart(Entity<TourniquetComponent> tourniquet, EntityUid body, EntityUid user)
    {
        if (!TryComp(user, out TargetingComponent? targeting) ||
            !_targeting.TryResolveExact(body, targeting.Target, out var part))
        {
            _popup.PopupEntity(Loc.GetString("tourniquet-selected-part-missing"), body, user);
            return false;
        }

        if (!CanApply(body, part))
        {
            // WOLFGATE(Wolfmed): W2: an arterial bleed away from the limbs has nowhere to tie off; say so.
            _popup.PopupEntity(Loc.GetString(_bleeding.GetPartRate(part) > 0f
                ? "wolfmed-tourniquet-nowhere-to-tie"
                : "tourniquet-no-bleeding"), body, user);
            return false;
        }

        _audio.PlayPredicted(tourniquet.Comp.BeginSound, body, user);
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            tourniquet.Comp.Delay,
            new TourniquetDoAfterEvent(GetNetEntity(part)),
            tourniquet,
            body,
            tourniquet)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnDoAfter(Entity<TourniquetComponent> tourniquet, ref TourniquetDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target is not { } body)
            return;

        args.Handled = true;
        var part = GetEntity(args.Part);
        if (!Apply(body, part))
            return;

        _slip.OnApplied(part, tourniquet); // WOLFGATE(Wolfmed): the tied part records how hard a hit knocks a makeshift strap loose
        if (!tourniquet.Comp.Damage.Empty)
            _damage.TryApplyPartDamage(body, part, tourniquet.Comp.Damage, args.Args.User);
        _audio.PlayPredicted(tourniquet.Comp.EndSound, body, args.Args.User);
        _popup.PopupEntity(Loc.GetString("tourniquet-applied"), body, args.Args.User);
        QueueDel(tourniquet); // WOLFGATE(Wolfmed): D13 — class is now Content.Server-only, so Onyx's "if (_net.IsServer)" guard is always true; dropped with the INetManager dependency
    }

    public bool Apply(EntityUid body, EntityUid part)
    {
        if (!CanApply(body, part)) // WOLFGATE(Wolfmed): D13 — class is now Content.Server-only, so Onyx's "!_net.IsServer ||" half of this guard is always false; dropped with the INetManager dependency
            return false;

        var applied = false;
        // WOLFGATE(Wolfmed): playtest 4: the strap ties off everything below it too (a leg's foot, an arm's hand).
        var parts = new List<EntityUid> { part };
        parts.AddRange(_body.GetBodyPartChildren(part).Select(child => child.Id));
        foreach (var tied in parts)
        {
            if (!TryComp(tied, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((tied, woundable)).ToArray())
            {
                if (!TryComp(wound, out WoundBleedingComponent? bleeding) || bleeding.CurrentRate <= 0f ||
                    !_traits.CanTourniquet(wound.Owner, tied)) // WOLFGATE(Wolfmed): W2: a torso or head artery cannot be tied off.
                    continue;

                applied |= _bleeding.SetTreatment(wound.Owner, BleedingTreatment.Clamped);
            }
        }

        if (applied)
            _necrosis.OnTourniquetApplied(part); // WOLFGATE(Wolfmed): W5: the item is consumed, so the part carries the clock.

        return applied;
    }

    private bool CanApply(EntityUid body, EntityUid part) =>
        _body.BodyHasChild(body, part) && HasComp<WoundableComponent>(part) && _bleeding.GetPartRate(part) > 0f &&
        _traits.CanTourniquetPart(part); // WOLFGATE(Wolfmed): W2: nothing to do if every bleed here is untieable.
}
