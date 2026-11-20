using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>Straps a splint over a broken limb, reducing the fracture in the field without surgery.</summary>
// Reaches the same FractureTreatment.Reduced a bonesetter reaches on the table, so the penalty drops to a quarter.
// Mending is still surgery or a bone-knitting reagent, and a fresh hard hit resets the treatment through the
// profile's own resetTreatmentOnDamage.
/// <remarks>
/// Server-side for the same reason TourniquetSystem is (D13):
/// <see cref="WoundFractureSystem.TrySetTreatment"/> is a no-op off the server, so a client copy would only
/// ever popup and delete the item on a prediction it cannot make.
/// </remarks>
public sealed class WolfmedSplintSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WoundTargetResolver _targeting = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedSplintComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WolfmedSplintComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WolfmedSplintComponent, WolfmedSplintDoAfterEvent>(OnDoAfter);
    }

    private void OnUseInHand(Entity<WolfmedSplintComponent> splint, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStart(splint, args.User, args.User);
    }

    private void OnAfterInteract(Entity<WolfmedSplintComponent> splint, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryStart(splint, target, args.User);
    }

    private bool TryStart(Entity<WolfmedSplintComponent> splint, EntityUid body, EntityUid user)
    {
        if (!HasComp<WoundHostComponent>(body))
            return false;

        if (!TryResolvePart(splint, body, user, out var part, out var refusal))
        {
            _popup.PopupEntity(Loc.GetString(GetRefusalMessage(refusal)), body, user);
            return true;
        }

        var self = body == user;
        _audio.PlayPvs(splint.Comp.BeginSound, body);
        _popup.PopupEntity(
            Loc.GetString(self ? "wolfmed-splint-start-self" : "wolfmed-splint-start",
                ("user", user), ("target", body)), body, user);

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            self ? splint.Comp.Delay * splint.Comp.SelfMultiplier : splint.Comp.Delay,
            new WolfmedSplintDoAfterEvent(GetNetEntity(part)),
            splint,
            body,
            splint)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private void OnDoAfter(Entity<WolfmedSplintComponent> splint, ref WolfmedSplintDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target is not { } body)
            return;

        args.Handled = TryApply(splint, body, GetEntity(args.Part), args.Args.User);
    }

    /// <summary>
    /// Sets the fracture on that part to Reduced and uses the splint up. Public so a test or a future
    /// surgery step can skip the do-after.
    /// </summary>
    public bool TryApply(Entity<WolfmedSplintComponent> splint, EntityUid body, EntityUid part, EntityUid user)
    {
        // The limb can be blown off during the do-after and still pass CanApply on the floor, which would
        // splint a severed leg and use the item up. TryResolvePart checks this at the start; re-check it here.
        if (TerminatingOrDeleted(part) || !_body.BodyHasChild(body, part))
        {
            _popup.PopupEntity(Loc.GetString(GetRefusalMessage(WolfmedSplintRefusal.NoPart)), body, user);
            return false;
        }

        if (CanApply(splint, part) != WolfmedSplintRefusal.None ||
            _fractures.GetFracture(part) is not { } fracture ||
            !_fractures.TryReduce(fracture.Owner))
            return false;

        // G3: which splint is on the limb, so the overlay can tell a rod and a rag from a medical one.
        EnsureComp<WolfmedSplintMarkComponent>(part).Overlay = splint.Comp.Overlay;

        _audio.PlayPvs(splint.Comp.EndSound, body);
        _popup.PopupEntity(Loc.GetString("wolfmed-splint-success", ("target", body)), body, user);
        if (splint.Comp.Consumed)
            QueueDel(splint);

        return true;
    }

    /// <summary>
    /// Whether this part can take a splint right now, and if not, why. The order of the checks is the
    /// order the popups read in.
    /// </summary>
    public WolfmedSplintRefusal CanApply(Entity<WolfmedSplintComponent> splint, EntityUid part)
    {
        if (!TryComp(part, out BodyPartComponent? bodyPart) || !HasComp<WoundableComponent>(part))
            return WolfmedSplintRefusal.NoPart;

        if (!splint.Comp.Parts.Contains(bodyPart.PartType))
            return WolfmedSplintRefusal.WrongPart;

        if (_fractures.GetFracture(part) is not { } fracture)
            return WolfmedSplintRefusal.NoFracture;

        if (fracture.Comp2.Treatment != FractureTreatment.None)
            return WolfmedSplintRefusal.AlreadyTreated;

        // The same floor a bonesetter works to: below it there is nothing to hold in place.
        if (_fractures.TryGetProfile(part, out var profile) && fracture.Comp2.Grade < profile.ReductionMinimumGrade)
            return WolfmedSplintRefusal.TooSlight;

        return WolfmedSplintRefusal.None;
    }

    private bool TryResolvePart(Entity<WolfmedSplintComponent> splint, EntityUid body, EntityUid user,
        out EntityUid part, out WolfmedSplintRefusal refusal)
    {
        part = default;
        refusal = WolfmedSplintRefusal.NoPart;
        if (!TryComp(user, out TargetingComponent? targeting) ||
            !_targeting.TryResolveExact(body, targeting.Target, out part) ||
            !_body.BodyHasChild(body, part))
            return false;

        refusal = CanApply(splint, part);
        return refusal == WolfmedSplintRefusal.None;
    }

    private static string GetRefusalMessage(WolfmedSplintRefusal refusal) => refusal switch
    {
        WolfmedSplintRefusal.WrongPart => "wolfmed-splint-wrong-part",
        WolfmedSplintRefusal.NoFracture => "wolfmed-splint-no-fracture",
        WolfmedSplintRefusal.TooSlight => "wolfmed-splint-too-slight",
        WolfmedSplintRefusal.AlreadyTreated => "wolfmed-splint-already-treated",
        _ => "wolfmed-splint-no-part",
    };
}
