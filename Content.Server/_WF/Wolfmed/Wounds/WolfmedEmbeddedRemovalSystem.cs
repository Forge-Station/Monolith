using Content.Server.Kitchen.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee;
using Robust.Server.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Prying lodged rounds and shrapnel out of a wound. Forceps-class surgical tools do it cleanly; any sharp
/// item works too, slower, and leaves a fresh cut. Self-surgery is allowed at a penalty.
/// </summary>
/// <remarks>
/// Runs last in the interaction chain (AfterInteractUsingEvent) so it never steals a surgery, a butcher or
/// a topical; the target part comes from the user's own Shitmed part selection, like the tourniquet.
/// </remarks>
public sealed class WolfmedEmbeddedRemovalSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private WolfmedInfectionSystem _infection = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundTargetResolver _targeting = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<WoundHostComponent, WolfmedEmbeddedRemovalDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteractUsing(Entity<WoundHostComponent> body, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !TryGetTool(args.Used, out var clean))
            return;

        if (!TryComp(args.User, out TargetingComponent? targeting) ||
            !_targeting.TryResolveExact(body, targeting.Target, out var part))
            return;

        if (_embedded.GetEmbeddedWound(part) is not { } wound)
            return;

        var delay = clean ? wound.Comp.CleanDelay : wound.Comp.SharpDelay;
        if (body.Owner == args.User)
            delay *= wound.Comp.SelfMultiplier;

        _popup.PopupEntity(Loc.GetString(body.Owner == args.User
                ? "wolfmed-embedded-removal-start-self"
                : "wolfmed-embedded-removal-start",
            ("user", args.User), ("target", body.Owner)), body, args.User);
        _audio.PlayPvs(wound.Comp.BeginSound, body);

        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            args.User,
            delay,
            new WolfmedEmbeddedRemovalDoAfterEvent(GetNetEntity(wound.Owner), clean),
            body,
            body,
            args.Used)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnDoAfter(Entity<WoundHostComponent> body, ref WolfmedEmbeddedRemovalDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Wound, out var wound))
            return;

        args.Handled = TryRemoveOne(body, wound.Value, args.User, args.Clean) != null;
    }

    /// <summary>
    /// Pulls one object out of the wound and spawns it at the patient. A dirty tool also cuts the part and
    /// hurts. Returns the spawned item, or null when there was nothing left to take.
    /// </summary>
    public EntityUid? TryRemoveOne(Entity<WoundHostComponent> body, EntityUid wound, EntityUid user, bool clean)
    {
        if (!TryComp(wound, out WolfmedEmbeddedObjectComponent? embedded) ||
            !TryComp(wound, out WoundComponent? core))
            return null;

        // The limb can come off during the do-after; digging in a severed one charges the cut and the
        // contamination to something that is no longer part of the patient.
        var part = core.HoldingPart;
        if (TerminatingOrDeleted(part) || !_body.BodyHasChild(body.Owner, part))
            return null;

        // Validated before the object is consumed: TryTakeOne decrements the count either way, so a bad id
        // in a downstream rule used to eat the fragment and spawn nothing.
        if (!_prototypes.HasIndex(embedded.Item) || !_embedded.TryTakeOne((wound, embedded), out var item))
            return null;

        var spawned = Spawn(item, _transform.GetMapCoordinates(body.Owner));

        // Nothing left in it: the wound stops being "a lodged round" and becomes the hole it always was, at the
        // same severity. Done before the dirty-tool penalties so the contamination lands on the wound that stays.
        if ((CompOrNull<WolfmedEmbeddedObjectComponent>(wound)?.Count ?? 0) <= 0 &&
            _traits.TryGetBehavior(wound, out WolfmedClearedWoundBehavior cleared))
        {
            var severity = core.Severity;
            var old = wound;
            var podMade = HasComp<Content.Shared._WF.Wolfmed.Autodoc.WolfmedPodWoundComponent>(old);
            _wounds.RemoveWound(wound);
            if (_wounds.CreateOrMergeWound(part, cleared.Wound, severity) is { } replacement)
            {
                wound = replacement;
                // Playtest 3 SAM: the hole the round leaves is the patient's wound, not something the pod made.
                RaiseLocalEvent(new WolfmedWoundReplacedEvent(old, replacement, podMade));
            }
        }

        if (!clean)
        {
            if (!embedded.SharpDamage.Empty)
                _routing.TryApplyPartDamage(body, part, embedded.SharpDamage, user);
            if (embedded.SharpPain > FixedPoint2.Zero)
                _pain.ChangePain(part, embedded.SharpPain);

            // W5: a knife in an open wound is the model's one source of dirty treatment.
            _infection.Contaminate(wound);
        }

        _audio.PlayPvs(embedded.EndSound, body);

        var remaining = CompOrNull<WolfmedEmbeddedObjectComponent>(wound)?.Count ?? 0;
        _popup.PopupEntity(Loc.GetString(remaining > 0
                ? "wolfmed-embedded-removal-partial"
                : "wolfmed-embedded-removal-success",
            ("target", body.Owner), ("count", remaining)), body, user);
        return spawned;
    }

    /// <summary>
    /// Whether this item can dig something out, and whether it does so cleanly. Forceps-class surgical
    /// tools are clean; a knife, a scalpel or a glass shard is not.
    /// </summary>
    public bool TryGetTool(EntityUid used, out bool clean)
    {
        clean = HasComp<HemostatComponent>(used) || HasComp<TweezersComponent>(used);
        return clean || HasComp<SharpComponent>(used) || IsSlashing(used);
    }

    private bool IsSlashing(EntityUid used) =>
        TryComp(used, out MeleeWeaponComponent? melee) &&
        melee.Damage.DamageDict.GetValueOrDefault("Slash") > FixedPoint2.Zero;
}

/// <summary>
/// Playtest 3 SAM: broadcast when a lodged object comes out and its wound becomes another one. The pod counts
/// every wound that appears while it works as its own; this tells it the replacement is the old wound.
/// </summary>
public sealed class WolfmedWoundReplacedEvent(EntityUid old, EntityUid replacement, bool podMade) : EntityEventArgs
{
    public readonly EntityUid Old = old;
    public readonly EntityUid Replacement = replacement;

    /// <summary>The old wound was one the pod made, so the replacement is too.</summary>
    public readonly bool PodMade = podMade;
}
