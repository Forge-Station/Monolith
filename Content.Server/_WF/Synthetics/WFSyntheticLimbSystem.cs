using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WF.Synthetics;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// Lego limbs: a chassis limb clicked back onto a synthetic body by hand, no surgery. It comes back loose, with a
/// <c>WFWolfmedLooseCouplingWound</c> that slows it until a cable coil repairs the coupling.
/// </summary>
/// <remarks>
/// The click itself arrives through <see cref="WFChassisBaySystem"/>, which owns the body's InteractUsingEvent.
/// Detached chassis limbs keep indefinitely: WolfmedNecrosisSystem.OnDetached never stamps a mechanical part.
/// </remarks>
public sealed partial class WFSyntheticLimbSystem : SharedWFSyntheticsSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <summary>The wound a hand reattach leaves on the limb.</summary>
    public static readonly ProtoId<WoundPrototype> LooseCouplingWound = "WFWolfmedLooseCouplingWound";

    /// <summary>How loose a limb comes back: two thirds of the wound's maximum.</summary>
    private static readonly FixedPoint2 LooseSeverity = 40;

    private static readonly TimeSpan ReattachDelay = TimeSpan.FromSeconds(4);

    /// <summary>Putting your own limb back on takes this much longer.</summary>
    private const float SelfMultiplier = 2f;

    private static readonly SoundSpecifier ReattachSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFChassisComponent, WFLimbReattachDoAfterEvent>(OnReattachDoAfter);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    /// <summary>
    /// Starts clicking a detached limb back onto a synthetic body. False when the item is not a loose limb at all;
    /// true when it is, whether the attempt started or was refused with a popup.
    /// </summary>
    public bool TryStartReattach(EntityUid body, EntityUid limb, EntityUid user)
    {
        if (!TryComp(limb, out BodyPartComponent? part) || part.Body != null)
            return false;

        if (!IsSynthetic(body))
        {
            _popup.PopupEntity(Loc.GetString("wf-synthetics-not-synthetic"), body, user);
            return true;
        }

        if (!_traits.IsMechanical(limb))
        {
            _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-not-chassis"), body, user);
            return true;
        }

        if (!TryFindSocket(body, (limb, part), out var parent, out var slot))
        {
            _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-no-socket"), body, user);
            return true;
        }

        // NeedHand: a body putting its own arm back on needs a working hand to do it with.
        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            user == body ? ReattachDelay * SelfMultiplier : ReattachDelay,
            new WFLimbReattachDoAfterEvent(GetNetEntity(limb), GetNetEntity(parent), slot),
            body,
            body,
            limb)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (!started)
            return true;

        _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-reattaching", ("limb", limb)), body, user);
        if (user != body)
        {
            _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-reattaching-other",
                ("user", Identity.Entity(user, EntityManager)), ("limb", limb)), body, body);
        }

        return true;
    }

    /// <summary>
    /// The part a limb hangs from and the empty slot it goes in: the torso for an arm, a leg or the head, the arm on
    /// the same side for a hand, the leg on the same side for a foot.
    /// </summary>
    public bool TryFindSocket(EntityUid body, Entity<BodyPartComponent> limb, out EntityUid parent, out string slot)
    {
        parent = default;
        slot = SlotId(limb.Comp);

        BodyPartType? parentType = limb.Comp.PartType switch
        {
            BodyPartType.Arm or BodyPartType.Leg or BodyPartType.Head => BodyPartType.Torso,
            BodyPartType.Hand => BodyPartType.Arm,
            BodyPartType.Foot => BodyPartType.Leg,
            _ => null,
        };

        if (parentType is not { } type)
            return false;

        BodyPartSymmetry? side = type == BodyPartType.Torso ? null : limb.Comp.Symmetry;
        foreach (var (candidate, _) in _body.GetBodyChildrenOfType(body, type, symmetry: side))
        {
            if (_containers.TryGetContainer(candidate, SharedBodySystem.GetPartSlotContainerId(slot), out var container) &&
                container.ContainedEntities.Count > 0)
                continue;

            parent = candidate;
            return true;
        }

        return false;
    }

    private void OnReattachDoAfter(Entity<WFChassisComponent> ent, ref WFLimbReattachDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Limb, out var limb) ||
            !TryGetEntity(args.Parent, out var parent))
            return;

        args.Handled = TryReattach(ent, limb.Value, parent.Value, args.Slot, args.Args.User);
    }

    /// <summary>Attaches the limb, loose, as the surgery's attach step does, without the affix step after it.</summary>
    public bool TryReattach(EntityUid body, EntityUid limb, EntityUid parent, string slot, EntityUid user)
    {
        // Re-read everything: the slot may have been filled, or the parent lost, during the do-after.
        if (!IsSynthetic(body) || !TryComp(limb, out BodyPartComponent? part) || part.Body != null ||
            !_traits.IsMechanical(limb) || !_body.BodyHasChild(body, parent) ||
            !TryFindSocket(body, (limb, part), out var socket, out var socketSlot) ||
            socket != parent || socketSlot != slot)
            return false;

        if (!Comp<BodyPartComponent>(parent).Children.ContainsKey(slot))
            _body.TryCreatePartSlot(parent, slot, part.PartType, out _);

        // AttachPart's BodyPartAddedEvent brings back the sprite and markings (PartAppearance), the hands, and the
        // limb's wounds and closed stump (WolfmedBodyPartLifecycleSystem).
        if (!_body.AttachPart(parent, slot, limb, part: part))
            return false;

        var enable = new BodyPartEnableChangedEvent(true);
        RaiseLocalEvent(limb, ref enable);
        var attached = new BodyPartAttachedEvent((limb, part));
        RaiseLocalEvent(body, ref attached);

        if (_wounds.CreateOrMergeWound(limb, LooseCouplingWound, LooseSeverity) != null)
            EnsureComp<WFLooseLimbComponent>(limb);

        _audio.PlayPvs(ReattachSound, body);
        _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-reattached", ("limb", limb)), body, user);
        return true;
    }

    /// <summary>Says how far each pass of cable got, and seats the limb properly once the coupling is repaired.</summary>
    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Prototype != LooseCouplingWound || !HasComp<WFLooseLimbComponent>(args.Part))
            return;

        var body = CompOrNull<BodyPartComponent>(args.Part)?.Body;
        if (args.Kind == WolfmedWoundLifecycle.Changed)
        {
            var pass = args.OldSeverity - args.Severity;
            if (body != null && pass > FixedPoint2.Zero && args.Severity > FixedPoint2.Zero)
            {
                var left = (int) MathF.Ceiling(args.Severity.Float() / pass.Float());
                _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-coupling-tightened", ("limb", args.Part), ("passes", left)),
                    body.Value, PopupType.Small);
            }

            return;
        }

        if (args.Kind != WolfmedWoundLifecycle.Removed)
            return;

        foreach (var wound in _wounds.GetWounds(args.Part))
        {
            if (wound.Owner != args.Wound && wound.Comp.Prototype == LooseCouplingWound)
                return;
        }

        RemComp<WFLooseLimbComponent>(args.Part);
        if (body != null)
            _popup.PopupEntity(Loc.GetString("wf-synthetic-limb-coupling-seated", ("limb", args.Part)), body.Value);
    }

    /// <summary>One examine line per loose limb on the body. Called from the bay system's examine handler.</summary>
    public void AddLooseExamine(EntityUid body, EntityUid target, ExaminedEvent args)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (HasComp<WFLooseLimbComponent>(part))
                args.PushMarkup(Loc.GetString("wf-synthetic-limb-examine-loose", ("target", target), ("part", part)));
        }
    }

    /// <summary>The body slot id for a part: "left arm", "right foot", "head".</summary>
    private static string SlotId(BodyPartComponent part) =>
        part.Symmetry == BodyPartSymmetry.None
            ? part.PartType.ToString().ToLowerInvariant()
            : $"{part.Symmetry.ToString().ToLowerInvariant()} {part.PartType.ToString().ToLowerInvariant()}";
}
